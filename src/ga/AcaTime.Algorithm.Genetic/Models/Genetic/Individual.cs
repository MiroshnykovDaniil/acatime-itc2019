using System.Collections.Immutable;
using System.Diagnostics;
using AcaTime.Algorithm.Genetic.Services.Calc;
using AcaTime.Algorithm.Genetic.Utils;
using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScheduleCommon.Models.Constraints;
using AcaTime.ScriptModels;
using Microsoft.Extensions.Logging;

namespace AcaTime.Algorithm.Genetic.Models.Genetic;

public class Individual
{
    /// <summary>
    /// Дані для розкладу
    /// </summary>
    public FacultySeasonDTO Root { get; set; }

    /// <summary>
    /// Оцінки для розкладу
    /// </summary>
    public UserFunctions UserFunctions { get; set; }

    public AlgorithmParams Parameters { get; internal set; }
    // private string algorithmName = $"alg-{Guid.NewGuid()}";
    internal ILogger logger;

    public Dictionary<IScheduleSlot, SlotTracker> Slots { get; internal set; }
    public List<ScheduleDeltaEvent> DeltaEvents { get; } = new();

    /// <summary>
    /// Тег лінії для островів: потомки успадковують тег батька; пул тримає
    /// найкращу особу КОЖНОЇ лінії (слоти липкі за лініями) — дивергенція
    /// виживає, і HGT має між чим обирати.
    /// </summary>
    internal int LineageTag;


    internal void AddDeltaEvent(ScheduleDeltaEvent delta)
    {
        DeltaEvents.Add(delta);
    }

    // додатковий кеш для прискорення деяких функцій, клонується в Clone
    internal Dictionary<long, List<SlotTracker>> teacherSlots;
    internal Dictionary<long, List<SlotTracker>> groupsSlots;
    internal List<SlotTracker> FirstTrackers;

    // приватний кеш
    // private Dictionary<int, List<SlotTracker>> slotsByStep = new Dictionary<int, List<SlotTracker>>(); // для зберігання слотів по крокам
    // FLAT-2a: індекси призначень — ліниві. Клон НЕ копіює їх (~20с
    // thread-time на раунд); при першому доступі властивість ребілдить
    // їх з живого стану трекерів за один O(N) прохід (~50µs).
    private Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>> _assignedSlotsByTeacherDate = new Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>>();
    private Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>> _assignedSlotsByGroupDate = new Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>>();
    internal bool _indexesLazy = false;
    // private HashSet<SlotTracker> unassignedFirstSlots;
    private Dictionary<long, List<SlotTracker>> firstSlotsByGroupSubjects;
    private Dictionary<DateTime, Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>>> _assignedClassrooms = new Dictionary<DateTime, Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>>>();

    internal Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>> assignedSlotsByTeacherDate
    {
        get { EnsureIndexesBuilt(); return _assignedSlotsByTeacherDate; }
        set { _assignedSlotsByTeacherDate = value; }
    }

    internal Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>> assignedSlotsByGroupDate
    {
        get { EnsureIndexesBuilt(); return _assignedSlotsByGroupDate; }
        set { _assignedSlotsByGroupDate = value; }
    }

    private Dictionary<DateTime, Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>>> assignedClassrooms
    {
        get { EnsureIndexesBuilt(); return _assignedClassrooms; }
        set { _assignedClassrooms = value; }
    }

    /// <summary>
    /// Ребілд індексів teacher/group з живого стану (IsAssigned + позиція
    /// DTO). Свіжіший за копію батьківських словників (ті могли застаріти
    /// на moved-while-assigned) — суворо краще для валідації. assignedClassrooms
    /// НЕ ребілдиться: клони й раніше стартували з порожнім (поведінка
    /// незмінна).
    /// </summary>
    private void EnsureIndexesBuilt()
    {
        System.Threading.Interlocked.Increment(ref IdxRebuildCalls);
        if (!_indexesLazy) return;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _indexesLazy = false;
        System.Threading.Interlocked.Increment(ref Services.Calc.FlatBitsets.IndexRebuilds);
        foreach (var t in Slots.Values)
        {
            if (!t.IsAssigned) continue;
            var slot = t.ScheduleSlot;
            var teacherId = slot.GroupSubject.Teacher.Id;
            if (!_assignedSlotsByTeacherDate.TryGetValue(teacherId, out var teacherDates))
                _assignedSlotsByTeacherDate[teacherId] = teacherDates = new Dictionary<DateTime, HashSet<SlotTracker>>();
            if (!teacherDates.TryGetValue(slot.Date, out var teacherSet))
                teacherDates[slot.Date] = teacherSet = new HashSet<SlotTracker>();
            teacherSet.Add(t);
            foreach (var g in slot.GroupSubject.Groups)
            {
                if (!_assignedSlotsByGroupDate.TryGetValue(g.Id, out var groupDates))
                    _assignedSlotsByGroupDate[g.Id] = groupDates = new Dictionary<DateTime, HashSet<SlotTracker>>();
                if (!groupDates.TryGetValue(slot.Date, out var groupSet))
                    groupDates[slot.Date] = groupSet = new HashSet<SlotTracker>();
                groupSet.Add(t);
            }
        }
        TicksIdxRebuild += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }
    
    public AlgorithmResultDTO initialResult = null; // todo можливо переназвати на просто Result...

    public void Setup(FacultySeasonDTO root, ILogger logger, UserFunctions userFunctions, AlgorithmParams parameters)
    {
        Root = root;
        this.logger = logger;
        UserFunctions = userFunctions;
        Parameters = parameters;
    }
    
    
    private bool isInit;
    private readonly Random _random = new();
    private int _diagCount;
    private void PreparePrivateGeneticCache()
    {
        firstSlotsByGroupSubjects = FirstTrackers
            .Where(x => x.IsAssigned && x.IsFirstTrackerInSeries) // Оскільки працюємо з вже розподіленими, беремо IsAssigned
            .GroupBy(s => s.ScheduleSlot.GroupSubject.Id)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.SeriesId).ToList());

        isInit = true;
    }

    public int Estimate()
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            // FLAT-паріті: кожен 256-й Estimate (глобальний лічильник — клони
            // живуть один op, тому статик) повністю перераховує бітсети зі
            // Slots і порівнює з інкрементальним станом (~40µs). Дрейф =
            // warning + вимкнення дзеркала (self-heal, поведінка не змінюється).
            if (flatBits is { Enabled: true } && System.Threading.Interlocked.Increment(ref FlatVerifyTick) >= 256)
            {
                System.Threading.Interlocked.Exchange(ref FlatVerifyTick, 0);
                if (flatBits.VerifyAgainst(Slots.Values, flatTables!, flatTables!.Teachers, flatTables.Groups,
                        msg => logger?.LogWarning("FLAT-DRIFT: {Msg}", msg)) > 0)
                {
                    logger?.LogWarning("FLAT-DRIFT: дзеркало розійшлося — вимкнено");
                    flatBits.Disable("drift");
                }
            }

            if (Parameters.CheapEvaluation)
            {
                if (_cheapEngine == null && !_cheapEngineFailed)
                {
                    var engine = new Services.CheapEval.CheapEvaluationEngine(this, logger);
                    if (engine.EnsureReady())
                        _cheapEngine = engine;
                    else
                        _cheapEngineFailed = true;
                }

                if (_cheapEngine != null)
                    return _cheapEngine.Estimate();
            }

            int scheduleEstimation = 0;
            foreach (var s in UserFunctions.ScheduleEstimations)
            {
                var extScore = s.Estimate(Root);
                scheduleEstimation += extScore;
            }
            return scheduleEstimation;
        }
        finally
        {
            TicksEstimate += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        }
    }

    public int Kick(int seriesCount)
    {
        var before = Estimate();
        currentEstimation = before;

        for (var i = 0; i < Math.Max(1, seriesCount); i++)
        {
            if (SwapGroupSubjects() == null)
                SwapTeacherSubjects();
        }

        currentEstimation = Estimate();
        logger.LogInformation($"Kick: {before} -> {currentEstimation}");
        return currentEstimation;
    }

    public bool TryApplyHgt(IReadOnlyList<ScheduleGenome> donors, int attempts, out int acceptedScore)
    {
        var beforeScore = Estimate();
        var before = ScheduleGenome.FromSlots(
            Slots.Values.Where(x => x.IsAssigned).Select(x => x.ScheduleSlot), beforeScore);
        var trial = CloneHelper.clone(this);

        if (!trial.TryApplyHgtInPlace(donors, attempts, out var trialScore) || trialScore <= beforeScore)
        {
            acceptedScore = beforeScore;
            return false;
        }

        ApplyGenome(ScheduleGenome.FromSlots(
            trial.Slots.Values.Where(x => x.IsAssigned).Select(x => x.ScheduleSlot), trialScore));
        acceptedScore = Estimate();
        if (acceptedScore <= beforeScore)
        {
            ApplyGenome(before);
            acceptedScore = beforeScore;
            return false;
        }

        currentEstimation = acceptedScore;
        return true;
    }

    private bool TryApplyHgtInPlace(IReadOnlyList<ScheduleGenome> donors, int attempts, out int acceptedScore)
    {
        acceptedScore = Estimate();
        if (donors.Count == 0 || attempts <= 0)
            return false;

        var recipient = ScheduleGenome.FromSlots(
            Slots.Values.Where(x => x.IsAssigned).Select(x => x.ScheduleSlot), acceptedScore);
        var improved = false;
        var tried = 0;
        var accepted = 0;

        foreach (var donor in donors.OrderByDescending(x => x.Fitness ?? int.MinValue).Take(attempts))
        {
            foreach (var groupSubjectId in donor.Genes.Keys
                         .Select(x => x.GroupSubjectId)
                         .Distinct()
                         .Take(Math.Max(1, Parameters.HgtBlockCount)))
            {
                var candidate = recipient.Clone();
                candidate.TransferGroupSubjectFrom(donor, groupSubjectId).Commit();
                tried++;
                ApplyGenome(candidate);
                var score = Estimate();
                if (score > acceptedScore)
                {
                    improved = true;
                    accepted++;
                    acceptedScore = score;
                    recipient = ScheduleGenome.FromSlots(
                        Slots.Values.Where(x => x.IsAssigned).Select(x => x.ScheduleSlot), score);
                    logger.LogInformation($"HGT: прийнято {acceptedScore}");
                }
                else
                {
                    ApplyGenome(recipient);
                }
            }
        }

        logger.LogInformation($"HGT: завершено, спроб {tried}, прийнято {accepted}, фінальний score {acceptedScore}");
        return improved;
    }

    private void ApplyGenome(ScheduleGenome genome)
    {
        // FLAT-2a: скидаємо бекінги напряму (без ребілду, якщо ліниві)
        _indexesLazy = false;
        _assignedSlotsByTeacherDate.Clear();
        _assignedSlotsByGroupDate.Clear();
        _assignedClassrooms.Clear();
        // FLAT-дзеркало: повне скидання. FlatCode скидається БЕЗУМОВНО
        // (навіть коли дзеркало вимкнене — гігієна кодів дешева, а засталі
        // коди отруюють майбутні NoteAssigned).
        if (flatBits is { Enabled: true })
            flatBits.ResetAll();
        foreach (var t in Slots.Values)
            t.FlatCode = -1;
        foreach (var tracker in Slots.Values)
        {
            tracker.IsAssigned = false;
            tracker.ScheduleSlot.Classroom = null;
        }

        var trackers = Slots.Values.ToDictionary(x => SlotGeneKey.From(x.ScheduleSlot), x => x);
        foreach (var pair in genome.Genes)
        {
            if (!trackers.TryGetValue(pair.Key, out var tracker))
                continue;

            tracker.ScheduleSlot.Classroom = pair.Value.ClassroomId.HasValue
                ? Root.Classrooms.FirstOrDefault(x => x.Id == pair.Value.ClassroomId.Value)
                : null;
            SetSlotAssignedGenetic(tracker, new DomainValue
            {
                Date = pair.Value.Date,
                PairNumber = pair.Value.PairNumber
            });
        }
    }

    #region Основні операції

    List<int?> swappedSeries = new List<int?>();
    
    private Dictionary<SlotTracker, int> usedTrackers = new();
    
    public KeyValuePair<int, DomainValue>? Mutations(
        int prevEstimation,
        int minSeriesLength,
        int maxSeriesLength,
        HashSet<int> usedSeries,
        bool randomizeDomains = false,
        int? forcedSeriesId = null,
        DomainValue? forcedDomain = null)
    {

        // todo Достатньо буде створити 1 раз, далі скопіюється з інших джерел
        if(!isInit)
            PreparePrivateGeneticCache();
            
            // Перші заняття в серії - наша популяція з якою ми граємось
            
            // беремо випадковий елемент популяції
            // todo вигадати як переробити щоб не брати кожен раз дисципліну випадково з нуля, а, наприклад, брати зі стеку, та заносити в окремий стек які дисципліни мали успішні та неуспішні мутації, бо цікаво погратись яка комбінація в середньому вигідніша
            
            var hasMinSeriesLimit = minSeriesLength != -1;
            var hasMaxSeriesLimit = maxSeriesLength != -1;
            List<SlotTracker>? list = null;
            if (hasMinSeriesLimit && hasMaxSeriesLimit)
            {
                list = FirstTrackers
                    .Select(e => e)
                    .Where(e => 
                        !usedSeries.Contains((int)e.SeriesId) &&
                            // !usedTrackers.ContainsKey(e) || usedTrackers[e] < 3) && 
                        !e.IsLowDaysDanger && 
                        e.ScheduleSlot.LessonSeriesLength <= maxSeriesLength && 
                        e.ScheduleSlot.LessonSeriesLength >= minSeriesLength)
                    .ToList();
            }
            else if (hasMinSeriesLimit)
            {
                list = FirstTrackers
                    .Select(e => e)
                    .Where(e => 
                        !usedSeries.Contains((int)e.SeriesId) &&
                        // (!usedTrackers.ContainsKey(e) || usedTrackers[e] < 3) && 
                        !e.IsLowDaysDanger && 
                        e.ScheduleSlot.LessonSeriesLength >= minSeriesLength)
                    .ToList();
            }
            else
            {
                list = FirstTrackers
                    .Select(e => e)
                    .Where(e =>
                        !usedSeries.Contains((int)e.SeriesId) &&
                        // (!usedTrackers.ContainsKey(e) || usedTrackers[e] < 3) &&
                        !e.IsLowDaysDanger &&
                        e.ScheduleSlot.LessonSeriesLength <= maxSeriesLength)
                    .ToList();
            }

            if (list.Count == 0)
            {
                usedTrackers.Clear();
                return null;
            }
            if (!forcedSeriesId.HasValue && list.Count > 8)
            {
                list = list.OrderBy(x => x.AvailableDomains.Count).ThenByDescending(x => x.ScheduleSlot.LessonSeriesLength).Take(8).ToList();
            }
            var firstRandomLesson = forcedSeriesId.HasValue
                ? list.FirstOrDefault(x => x.SeriesId == forcedSeriesId.Value)
                : list.ElementAt(_random.Next(0, list.Count));
            if (firstRandomLesson == null)
                return null;
            
            // if (!usedTrackers.TryAdd(firstRandomLesson, 1))
            // {
            //     usedTrackers[firstRandomLesson]++;
            // }

            // і змінюємо його на доступний домен, намагаємось змінити всі інші наступні заняття, перевіряючи констрейнти (is valid)
            var candidateDomain = firstRandomLesson.AvailableDomains;
            
            // збережемо інформацію про всі заняття в цій дисципліні щоб потім відновити назад якщо призначення не відбулось
            var cacheTrackers = firstRandomLesson.ScheduleSlot.GroupSubject.ScheduleSlots
                .Select(slot => Slots[slot])
                .Where(tracker => tracker.SeriesId == firstRandomLesson.SeriesId)
                .OrderBy(tracker => tracker.ScheduleSlot.LessonNumber)
                .ToList();

            var cacheDomains = new List<DomainValue>();
            foreach (var tracker in cacheTrackers)
            {
                var cachedDomainVal = new DomainValue();
                cachedDomainVal.PairNumber = tracker.ScheduleSlot.PairNumber;
                cachedDomainVal.Date = tracker.ScheduleSlot.Date;
                // var slot = tracker.ScheduleSlot.Clone(firstRandomLesson.ScheduleSlot.GroupSubject);
                cacheDomains.Add(cachedDomainVal);
            }
            
            var cacheSlot = firstRandomLesson.ScheduleSlot;

            var cacheDomain = new DomainValue();
            cacheDomain.PairNumber = cacheSlot.PairNumber;
            cacheDomain.Date = cacheSlot.Date;
            
            var aSlots = GetAssignedSlots();
            IEnumerable<DomainValue> domains = forcedDomain != null
                ? new[] { forcedDomain }
                : randomizeDomains
                ? candidateDomain.OrderBy(_ => _random.Next()).ToList()
                : candidateDomain;
            foreach (var domain in domains)
            {
                // перевірити чи можемо призначити цей домен
                var isVld = ValidateAssignment(firstRandomLesson, domain, aSlots);

                if (isVld)
                {
                    // візьмем трекери для інших занять дисципліни, щоб також перепризначити їх
                    // todo подивитись як у розкладі змінюються підгрупи після мутацій, чи всі разом перепризначаються чи окремо
                    var freeTRackers = firstRandomLesson.ScheduleSlot.GroupSubject.ScheduleSlots
                        .Select(slot => Slots[slot])
                        .Where(tracker => !tracker.IsFirstTrackerInSeries && tracker.SeriesId == firstRandomLesson.SeriesId)
                        .OrderBy(tracker => tracker.ScheduleSlot.LessonNumber)
                        .ToList();
                    freeTRackers.ForEach(SetSlotUnAssigned);

                    // перепризначити перший слот
                    SetSlotAssignedGenetic(firstRandomLesson, domain);
                    
                    // перепризначити всі інші
                    var syncCheck = ApplySynchronizedDomainPatternGenetic(firstRandomLesson, aSlots);
                    if (syncCheck)
                    {
                        bool fwdcheck = ForwardCheck(firstRandomLesson,firstRandomLesson.AssignStep);
                        // якщо мутація краща, зберігаємо результат
                        var res = Estimate();
                        if (fwdcheck && res > prevEstimation)
                        {
                            freeTRackers.ForEach(e => Slots[e.ScheduleSlot] = e);
                            
                            // todo перевірити, чи ми змінюємо лише одну підгрупу чи всі (наче всі, але варто ще раз подивитись)
                            
                            // залогуємо наші зміни щоб було легше шукати в excel таблиці різницю з дефолт алгоритмом
                            logger.LogInformation($"БУЛО: ВИКЛАДАЧ:{firstRandomLesson.ScheduleSlot.GroupSubject.Teacher.Name}|ДАТА:{cacheDomain.Date}|НОМЕР:{cacheDomain.PairNumber} СТАЛО:ДАТА:{firstRandomLesson.ScheduleSlot.Date}|НОМЕР:{firstRandomLesson.ScheduleSlot.PairNumber} ");
                            
                            currentEstimation = res;
                            if (firstRandomLesson.SeriesId != null)
                            {
                                seriesNewDomain = new KeyValuePair<int, DomainValue>((int)firstRandomLesson.SeriesId, domain);
                            }
                            return seriesNewDomain;
                        }
                    }
                }
            }
            // var trackerToRestore = firstRandomLesson.ScheduleSlot.GroupSubject.ScheduleSlots
            //     .Select(slot => Slots[slot])
            //     .Where(tracker => tracker.SeriesId == firstRandomLesson.SeriesId)
            //     .OrderBy(tracker => tracker.ScheduleSlot.LessonNumber)
            //     .ToList();
            //
            // for (int i = 0; i < trackerToRestore.Count; i++)
            // {
            //     SetSlotAssignedGenetic(trackerToRestore[i], cacheDomains[i]);
            // }
            if (firstRandomLesson.SeriesId != null)
            {
                return new KeyValuePair<int, DomainValue>((int)firstRandomLesson.SeriesId, null);
            }

            return null;
    }

    /// <summary>
    /// Evaluates every feasible domain of one chosen series, restoring the
    /// original placement between trials, and keeps the best result
    /// (best-improvement; sideways moves are allowed for plateau drift).
    /// Returns this individual with the winning domain already applied.
    /// After a non-null return the caller can undo the applied winner via
    /// <see cref="UndoSeriesPlacement"/> using the returned trackers/domains.
    /// </summary>
    public Individual? TryBestDomainMutation(
        int prevEstimation,
        int minSeriesLength,
        int maxSeriesLength,
        HashSet<int> usedSeries,
        out KeyValuePair<int, DomainValue>? selectedMutation,
        out List<SlotTracker>? appliedTrackers,
        out List<DomainValue>? appliedOriginalDomains,
        int maxDomainCandidates = 0,
        bool itcMode = false)
    {
        selectedMutation = null;
        appliedTrackers = null;
        appliedOriginalDomains = null;

        var firstTracker = SelectMutationSeries(minSeriesLength, maxSeriesLength, usedSeries, itcMode);
        if (firstTracker == null || firstTracker.SeriesId == null)
            return null;

        // збережемо інформацію про всі заняття серії щоб відновлювати її
        // між спробами доменів, тоді кожна спроба оцінюється з однієї й тієї
        // ж початкової позиції і argmax має сенс
        var seriesTrackers = firstTracker.ScheduleSlot.GroupSubject.ScheduleSlots
            .Select(slot => Slots[slot])
            .Where(t => t.SeriesId == firstTracker.SeriesId)
            .OrderBy(t => t.ScheduleSlot.LessonNumber)
            .ToList();
        var cacheDomains = seriesTrackers
            .Select(t => new DomainValue { Date = t.ScheduleSlot.Date, PairNumber = t.ScheduleSlot.PairNumber })
            .ToList();
        var freeTrackers = seriesTrackers.Where(t => !t.IsFirstTrackerInSeries).ToList();
        var aSlots = GetAssignedSlots();

        var currentDomain = new DomainValue
        {
            Date = firstTracker.ScheduleSlot.Date,
            PairNumber = firstTracker.ScheduleSlot.PairNumber
        };
        var domains = SelectCandidateDomains(firstTracker, currentDomain, maxDomainCandidates);

        var bestDomains = new List<DomainValue>();
        var bestScore = int.MinValue;
        if (Parameters.MutationDiagnostics) logger?.LogWarning($"MUT-DIAG series={firstTracker.SeriesId} len={firstTracker.ScheduleSlot.LessonSeriesLength} domains={domains.Count()} prev={prevEstimation} cur={currentEstimation}");

        // пробні стани скану — транзієнтні; верифікація на них заборонена
        using var scanGuard = _cheapEngine is null ? null : new ScanVerifyGuard(_cheapEngine);

        foreach (var domain in domains)
        {
            if (!TryPlaceSeries(firstTracker, domain, freeTrackers, aSlots, out var res))
            {
                RestoreSeriesPlacement(seriesTrackers, cacheDomains);
                continue;
            }

            var feasible = ForwardCheck(firstTracker, firstTracker.AssignStep);
            if (Parameters.MutationDiagnostics && _diagCount++ < 10) logger?.LogWarning($"MUT-DIAG trial {firstTracker.ScheduleSlot.Date:MM-dd}/{firstTracker.ScheduleSlot.PairNumber} res={res} feas={feasible}");
            if (feasible && res >= prevEstimation)
            {
                if (res > bestScore)
                {
                    bestScore = res;
                    bestDomains.Clear();
                }
                bestDomains.Add(domain);
            }

            RestoreSeriesPlacement(seriesTrackers, cacheDomains);
        }

        if (bestDomains.Count == 0)
            return null;

        // випадковий вибір між доменами з однаковим score запобігає
        // детермінованим циклам при дрейфі по плато
        var chosenDomain = bestDomains[_random.Next(bestDomains.Count)];

        if (!TryPlaceSeries(firstTracker, chosenDomain, freeTrackers, aSlots, out _))
            return null;

        currentEstimation = bestScore;
        selectedMutation = new KeyValuePair<int, DomainValue>(firstTracker.SeriesId.Value, chosenDomain);
        appliedTrackers = seriesTrackers;
        appliedOriginalDomains = cacheDomains;
        return this;
    }

    /// <summary>
    /// ITC-оператор освіти (eviction chain): вибирає клас, цільовий домен і,
    /// якщо домен заблокований, виселяє класи-блокувальники (спільні групи,
    /// та сама дата), ставить ціль, жадібно повертає виселених. Приймає лише
    /// стан з оцінкою >= поточної. Повертає клон або null (стан не змінено).
    /// Потребує спільних груп у DTO (кластери hard-обмежень).
    /// </summary>
    internal Individual? TryEvictChainMove(int maxEvict = 8)
    {
        if (!isInit)
            PreparePrivateGeneticCache();

        var classTrackers = FirstTrackers.Where(t => t.IsAssigned && t.SeriesId.HasValue).ToList();
        if (classTrackers.Count == 0)
            return null;

        var pick = classTrackers[_random.Next(classTrackers.Count)];
        var gsId = pick.ScheduleSlot.GroupSubject.Id;
        var seriesId = pick.SeriesId;

        var before = currentEstimation == int.MinValue ? Estimate() : currentEstimation;
        var work = CloneHelper.clone(this);
        work.currentEstimation = before;

        // ВАЖЛИВО: після клонування об'єкти Root інші — беремо клас і трекер
        // з клону (work), інакше work.Slots[оригінальний ScheduleSlotDTO] кидає.
        var first = work.Slots.Values.FirstOrDefault(t =>
            t.ScheduleSlot.GroupSubject.Id == gsId && t.SeriesId == seriesId && t.IsFirstTrackerInSeries);
        if (first == null)
            return null;
        var gs = first.ScheduleSlot.GroupSubject;
        if (first.AvailableDomains.Count <= 1)
            return null;

        var domains = first.AvailableDomains.OrderBy(_ => _random.Next()).ToList();
        var target = domains[0];
        if (target.Date.Date == first.ScheduleSlot.Date.Date && target.PairNumber == first.ScheduleSlot.PairNumber && domains.Count > 1)
            target = domains[1];

        // знімок стану для відкату
        var snapshot = work.Slots.Values
            .Where(t => t.IsAssigned)
            .Select(t => (t, new DomainValue { Date = t.ScheduleSlot.Date, PairNumber = t.ScheduleSlot.PairNumber }))
            .ToList();

        List<SlotTracker> SeriesOf(SlotTracker t) => t.ScheduleSlot.GroupSubject.ScheduleSlots
            .Select(s => work.Slots[s])
            .Where(x => x.SeriesId == t.SeriesId)
            .OrderBy(x => x.ScheduleSlot.LessonNumber)
            .ToList();

        void UnassignClass(GroupSubjectDTO g)
        {
            foreach (var s in g.ScheduleSlots)
            {
                var tr = work.Slots[s];
                if (tr.IsAssigned)
                    work.SetSlotUnAssigned(tr);
            }
        }

        bool TryPlace(SlotTracker f, DomainValue dom)
        {
            var series = SeriesOf(f);
            foreach (var t in series.Where(x => !x.IsFirstTrackerInSeries && x != f))
                work.SetSlotUnAssigned(t);
            if (!work.ValidateAssignment(f, dom, work.GetAssignedSlots()))
                return false;
            work.SetSlotAssignedGenetic(f, dom);
            return work.ApplySynchronizedDomainPatternGenetic(f, work.GetAssignedSlots());
        }

        void Restore()
        {
            // повний відкат: знімаємо все і відновлюємо знімок
            foreach (var t in work.Slots.Values)
                if (t.IsAssigned)
                    work.SetSlotUnAssigned(t);
            foreach (var (t, d) in snapshot)
                work.SetSlotAssignedGenetic(t, d);
        }

        // 1) пряма спроба (без виселення)
        if (TryPlace(first, target))
        {
            var est = work.Estimate();
            if (est >= before)
            {
                work.currentEstimation = est;
                return work;
            }
        }
        Restore();

        // 2) виселення блокувальників: класи зі спільними групами на ту саму дату
        var groupIds = new HashSet<long>(gs.Groups.Select(g => g.Id));
        var targetDate = target.Date.Date;
        var blockers = new List<GroupSubjectDTO>();
        var seen = new HashSet<long>();
        foreach (var t in work.Slots.Values)
        {
            if (!t.IsAssigned)
                continue;
            var other = t.ScheduleSlot.GroupSubject;
            if (other.Id == gs.Id)
                continue;
            if (t.ScheduleSlot.Date.Date != targetDate)
                continue;
            if (!other.Groups.Any(g => groupIds.Contains(g.Id)))
                continue;
            if (seen.Add(other.Id))
                blockers.Add(other);
            if (blockers.Count >= maxEvict)
                break;
        }
        if (blockers.Count == 0)
            return null;

        foreach (var b in blockers)
            UnassignClass(b);
        UnassignClass(gs);

        if (!TryPlace(first, target))
        {
            Restore();
            return null;
        }

        // 3) повернення виселених: backtracking з лімітом вузлів (W4)
        var order = blockers.OrderBy(_ => _random.Next()).ToList();
        var nodeCap = Math.Max(10, Parameters.EvictChainNodeCap);
        var nodes = 0;
        bool Reinsert(int idx)
        {
            if (idx >= order.Count)
                return true;
            if (++nodes > nodeCap)
                return false;
            var b = order[idx];
            var bt = b.ScheduleSlots.Select(s => work.Slots[s]).FirstOrDefault(x => x.IsFirstTrackerInSeries);
            if (bt == null)
                return false;
            foreach (var dom in bt.AvailableDomains.OrderBy(_ => _random.Next()).ToList())
            {
                if (TryPlace(bt, dom))
                {
                    if (Reinsert(idx + 1))
                        return true;
                }
                UnassignClass(b);
            }
            return false;
        }
        if (!Reinsert(0))
        {
            Restore();
            return null;
        }

        var after = work.Estimate();
        if (after < before)
            return null;
        work.currentEstimation = after;
        return work;
    }

    /// <summary>
    /// Room-aware ITC variant: try several independent eviction-chain targets
    /// and keep the best accepted state. This is deliberately opt-in; the
    /// original series/ITC operators remain unchanged.
    /// </summary>
    internal Individual? TryRoomAwareEvictChainMove(int maxEvict = 8, int attempts = 4)
    {
        Individual? best = null;
        var bestScore = int.MinValue;
        for (var i = 0; i < Math.Max(1, attempts); i++)
        {
            var candidate = TryEvictChainMove(maxEvict);
            if (candidate == null)
                continue;
            var score = candidate.currentEstimation;
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>
    /// Відкочує серію (трекери) до доменів, зафіксованих до скану мутації:
    /// єдиний спосіб повернути individual у стан після застосованого
    /// переможного домену — той самий патерн, що використовується між
    /// спробами всередині скану.
    /// </summary>
    internal void UndoSeriesPlacement(List<SlotTracker> seriesTrackers, List<DomainValue> cacheDomains)
        => RestoreSeriesPlacement(seriesTrackers, cacheDomains);

    private SlotTracker? SelectMutationSeries(int minSeriesLength, int maxSeriesLength, HashSet<int> usedSeries,
        bool itcMode = false)
    {
        var hasMinSeriesLimit = minSeriesLength != -1;
        var hasMaxSeriesLimit = maxSeriesLength != -1;
        var list = FirstTrackers
            .Where(e =>
                e.SeriesId.HasValue &&
                !usedSeries.Contains(e.SeriesId.Value) &&
                // ITC-режим: 1-заняттєві серії завжди мають IsLowDaysDanger=true
                // (перевірка "неповного останнього тижня" тривіально істинна),
                // тому фільтр low-days ховав від мутацій усі класи ITC-кодування.
                (itcMode || !e.IsLowDaysDanger) &&
                (!itcMode || e.AvailableDomains.Count > 1) &&
                (!hasMinSeriesLimit || e.ScheduleSlot.LessonSeriesLength >= minSeriesLength) &&
                (!hasMaxSeriesLimit || e.ScheduleSlot.LessonSeriesLength <= maxSeriesLength))
            .ToList();

        if (list.Count == 0)
            return null;

        // ВАЖЛИВО: таргетинг тут ШКОДИТЬ (20260831-045409: -6.4k) — мутації =
        // розвідка; експлуатація гарячих клітинок = окремий слот
        // (hotspot-relocate). Змішування ламає баланс розвідка/експлуатація.

        // зміщення до серій з найменшою кількістю доменів: їх скан дешевший,
        // а саме їхні переміщення розблоковують переміщення інших серій
        if (!itcMode && list.Count > 8)
        {
            list = list.OrderBy(x => x.AvailableDomains.Count)
                .ThenByDescending(x => x.ScheduleSlot.LessonSeriesLength)
                .Take(8)
                .ToList();
        }

        return list.ElementAt(_random.Next(0, list.Count));
    }

    private static IEnumerable<DomainValue> SelectCandidateDomains(
        SlotTracker firstTracker,
        DomainValue currentDomain,
        int maxDomainCandidates)
    {
        var pool = firstTracker.AvailableDomains
            .Where(d => !d.Equals(currentDomain))
            .ToList();
        if (maxDomainCandidates <= 0 || pool.Count <= maxDomainCandidates)
            return pool;

        // рівномірна вибірка по діапазону доменів коли задано обмеження
        return Enumerable.Range(0, maxDomainCandidates)
            .Select(index => pool[index * (pool.Count - 1) / Math.Max(1, maxDomainCandidates - 1)])
            .Distinct();
    }

    private bool TryPlaceSeries(
        SlotTracker firstTracker,
        DomainValue domain,
        List<SlotTracker> freeTrackers,
        AssignedSlotsDTO aSlots,
        out int score)
    {
        score = int.MinValue;
        if (!ValidateAssignment(firstTracker, domain, aSlots))
            return false;

        freeTrackers.ForEach(SetSlotUnAssigned);
        SetSlotAssignedGenetic(firstTracker, domain);

        if (!ApplySynchronizedDomainPatternGenetic(firstTracker, aSlots))
            return false;

        score = Estimate();
        return true;
    }

    private void RestoreSeriesPlacement(List<SlotTracker> seriesTrackers, List<DomainValue> cacheDomains)
    {
        foreach (var tracker in seriesTrackers)
            SetSlotUnAssigned(tracker);
        for (var i = 0; i < seriesTrackers.Count; i++)
            SetSlotAssignedGenetic(seriesTrackers[i], cacheDomains[i]);
    }

    /// <summary>
    /// Легка first-improvement мутація: перепризначає одну серію на перший
    /// домен з score не гіршим за поточний. Значно дешевша за турнір
    /// best-improvement; використовується для повторного спуску після
    /// destroy-repair з пошкодженого стану.
    /// </summary>
    public Individual? TryQuickImprovement(int prevEstimation, bool longSeries)
    {
        var firstTracker = SelectMutationSeries(longSeries ? 4 : -1, longSeries ? -1 : 3, new HashSet<int>());
        if (firstTracker == null || firstTracker.SeriesId == null)
            return null;

        var seriesTrackers = firstTracker.ScheduleSlot.GroupSubject.ScheduleSlots
            .Select(slot => Slots[slot])
            .Where(t => t.SeriesId == firstTracker.SeriesId)
            .OrderBy(t => t.ScheduleSlot.LessonNumber)
            .ToList();
        var cacheDomains = seriesTrackers
            .Select(t => new DomainValue { Date = t.ScheduleSlot.Date, PairNumber = t.ScheduleSlot.PairNumber })
            .ToList();
        var freeTrackers = seriesTrackers.Where(t => !t.IsFirstTrackerInSeries).ToList();
        var aSlots = GetAssignedSlots();

        var currentDomain = new DomainValue
        {
            Date = firstTracker.ScheduleSlot.Date,
            PairNumber = firstTracker.ScheduleSlot.PairNumber
        };

        foreach (var domain in firstTracker.AvailableDomains)
        {
            if (domain.Equals(currentDomain))
                continue;

            if (!TryPlaceSeries(firstTracker, domain, freeTrackers, aSlots, out var res))
            {
                RestoreSeriesPlacement(seriesTrackers, cacheDomains);
                continue;
            }

            // перше прийнятне переміщення — достатньо для спуску
            if (res >= prevEstimation)
            {
                currentEstimation = res;
                return this;
            }

            RestoreSeriesPlacement(seriesTrackers, cacheDomains);
        }

        return null;
    }

    private KeyValuePair<int, DomainValue> seriesNewDomain = new KeyValuePair<int, DomainValue>();

    public void ApplyMutation(KeyValuePair<int, DomainValue> pair)
    {
        var tracker = FirstTrackers
            .Select(e => e)
            .First(e => e.SeriesId == pair.Key);
        var aSlots = GetAssignedSlots();
        
        var cacheTrackers = tracker.ScheduleSlot.GroupSubject.ScheduleSlots
            .Select(slot => Slots[slot])
            .Where(t => t.SeriesId == tracker.SeriesId)
            .OrderBy(t => tracker.ScheduleSlot.LessonNumber)
            .ToList();

        var cacheDomains = new List<DomainValue>();
        foreach (var t in cacheTrackers)
        {
                var cachedDomainVal = new DomainValue();
                cachedDomainVal.PairNumber = t.ScheduleSlot.PairNumber;
                cachedDomainVal.Date = t.ScheduleSlot.Date;
                cacheDomains.Add(cachedDomainVal);
        }
        
        var cacheSlot = tracker.ScheduleSlot;
        var cacheDomain = new DomainValue();
        cacheDomain.PairNumber = cacheSlot.PairNumber;
        cacheDomain.Date = cacheSlot.Date;

        var candidateDomain = tracker.AvailableDomains;

        foreach (var domain in candidateDomain)
        {
            bool isVld = ValidateAssignment(tracker, domain, aSlots);

            if (isVld)
            {
                // візьмем трекери для інших занять дисципліни, щоб також перепризначити їх
                var freeTRackers = tracker.ScheduleSlot.GroupSubject.ScheduleSlots
                    .Select(slot => Slots[slot])
                    .Where(t => !t.IsFirstTrackerInSeries && t.SeriesId == tracker.SeriesId)
                    .OrderBy(t => t.ScheduleSlot.LessonNumber)
                    .ToList();
                freeTRackers.ForEach(SetSlotUnAssigned);

                // перепризначити перший слот
                SetSlotAssignedGenetic(tracker, domain);
                    
                // перепризначити всі інші
                var syncCheck = ApplySynchronizedDomainPatternGenetic(tracker, aSlots);
                if (syncCheck)
                {
                    bool fwdcheck = ForwardCheck(tracker,tracker.AssignStep);
                    // якщо мутація краща, зберігаємо результат
                    var res = Estimate();
                    if (fwdcheck && res > currentEstimation)
                    {
                        freeTRackers.ForEach(e => Slots[e.ScheduleSlot] = e);
                            
                        // залогуємо наші зміни щоб було легше шукати в excel таблиці різницю з дефолт алгоритмом
                        logger.LogInformation($"ПЕРЕНОС ДАВ РЕЗУЛЬТАТ З {currentEstimation} НА {res}");
                        logger.LogInformation($"БУЛО: ВИКЛАДАЧ:{tracker.ScheduleSlot.GroupSubject.Teacher.Name}|ДАТА:{cacheDomain.Date}|НОМЕР:{cacheDomain.PairNumber} СТАЛО:ДАТА:{tracker.ScheduleSlot.Date}|НОМЕР:{tracker.ScheduleSlot.PairNumber} ");

                        this.currentEstimation = res;
                        // if (tracker.SeriesId != null)
                        // {
                        //     seriesNewDomain = new KeyValuePair<int, DomainValue>((int)firstRandomLesson.SeriesId, domain);
                        // }
                        return;
                    }
                }
            }
        }
            
        // var isVld = ValidateAssignment(tracker, pair.Value, aSlots);
        // if (isVld)
        // {
        //     // візьмем трекери для інших занять дисципліни, щоб також перепризначити їх
        //     var freeTRackers = tracker.ScheduleSlot.GroupSubject.ScheduleSlots
        //         .Select(slot => Slots[slot])
        //         .Where(t =>  t.SeriesId == tracker.SeriesId)
        //         .OrderBy(t => t.ScheduleSlot.LessonNumber)
        //         .ToList();
        //     freeTRackers.ForEach(SetSlotUnAssigned);
        //
        //     // перепризначити перший слот
        //     SetSlotAssignedGenetic(tracker, pair.Value);
        //     var syncCheck = ApplySynchronizedDomainPatternGenetic(tracker, aSlots);
        //     if (syncCheck)
        //     {
        //         bool fwdcheck = ForwardCheck(tracker, tracker.AssignStep);
        //         // якщо мутація краща, зберігаємо результат
        //         var res = Estimate();
        //         if (fwdcheck && res > currentEstimation)
        //         {
        //             freeTRackers.ForEach(e => Slots[e.ScheduleSlot] = e);
        //
        //             logger.LogInformation($"ВСЕ ВИЙШЛО!");
        //
        //             // залогуємо наші зміни щоб було легше шукати в excel таблиці різницю з дефолт алгоритмом
        //             logger.LogInformation(
        //                 $"БУЛО: ВИКЛАДАЧ:{tracker.ScheduleSlot.GroupSubject.Teacher.Name}|ДАТА:{pair.Value.Date}|НОМЕР:{pair.Value.PairNumber} СТАЛО:ДАТА:{tracker.ScheduleSlot.Date}|НОМЕР:{tracker.ScheduleSlot.PairNumber} ");
        //
        //             currentEstimation = res;
        //             return;
        //         }
        //     }
        // }
        var trackerToRestore = tracker.ScheduleSlot.GroupSubject.ScheduleSlots
            .Select(slot => Slots[slot])
            .Where(t => t.SeriesId == tracker.SeriesId)
            .OrderBy(t => t.ScheduleSlot.LessonNumber)
            .ToList();

        for (int i = 0; i < trackerToRestore.Count; i++)
        {
            SetSlotAssignedGenetic(trackerToRestore[i], cacheDomains[i]);
        }
    }

    public KeyValuePair<int, int>? SwapTeacherSubjects()
    {
        if (!isInit)
            PreparePrivateGeneticCache();
        // беремо предмети які можна змінювати
        // var list = FirstTrackers.Select(e => e).Where(e => !e.IsLowDaysDanger && e.ScheduleSlot.GroupSubject.Groups.Count > 1).ToList();
        var list = FirstTrackers.Select(e => e).Where(e => !e.IsLowDaysDanger && e.SeriesLength > 3 && !swappedSeries.Contains(e.SeriesId)).ToList();
            
        // цикл, оскільки вірогідність з 1го разу натрапити на такий предмет що має ідентичний у того ж викладача не висока
        int stop = 100;
        while (true)
        {
            if(stop == 0)
                break;
            stop--;
            // перестворимо swappedSeries якщо list пустий
            if (list.Count == 0)
            {
                swappedSeries = new List<int?>();
                break;
            }
            var firstRandomLessonTracker = list.ElementAt(_random.Next(0, list.Count));
            // якщо нещодавно вже намагались замінити цей предмет, спробуємо інший
            if (swappedSeries.Contains(firstRandomLessonTracker.SeriesId))
            {
                continue;
            }
            swappedSeries.Add(firstRandomLessonTracker.SeriesId);
            var teacher = firstRandomLessonTracker.ScheduleSlot.GroupSubject.Teacher.Id;
                
            var teacherSubjectsList = assignedSlotsByTeacherDate[teacher].Values.ToList();
            if(teacherSubjectsList.Count < 2)
                continue;

            // всі предмети викладача
            var teacherSubjectsTrackers = FirstTrackers.Select(e => e).Where(e => !e.IsLowDaysDanger && e.ScheduleSlot.GroupSubject.Teacher.Id == firstRandomLessonTracker.ScheduleSlot.GroupSubject.Teacher.Id).ToList();
            
            var dicLen = new Dictionary<int, int>();
                
            if(teacherSubjectsTrackers.Count < 2)
                continue;

            for (var i = 0; i < teacherSubjectsTrackers.Count(); i++)
            {
                int l = teacherSubjectsTrackers[i].ScheduleSlot.LessonSeriesLength;
                if (!dicLen.ContainsKey(l))
                {
                    dicLen.Add(l, i);
                }
                else
                {
                    // тобто ці 2 предмети мають однакову довжину і кількість можливих доменів для першого слоту (а відповідно і для всіх інших теж)
                    if (teacherSubjectsTrackers[i].AvailableDomains.Count ==
                        teacherSubjectsTrackers[dicLen[l]].AvailableDomains.Count)
                    {
                        // тоді пробуємо замінити їх місцями
                        var firstSubjectFirstTracker = teacherSubjectsTrackers[i];
                        var secondSubjectFirstTracker = teacherSubjectsTrackers[dicLen[l]];
                        // var before = Estimate();
                        var before = currentEstimation;
                        // замінити місцями.
                        SwapSeriesTrackers(firstSubjectFirstTracker, secondSubjectFirstTracker);
                        // SwapSeriesTrackers(firstSubjectFirstTracker, secondSubjectFirstTracker);
                        var after = Estimate();
                        
                        // якщо ця зміна є негативною, повернути назад
                        if (after < before)
                        {
                            // не потрібно повертати, популяція просто потім видалиться
                            currentEstimation = after;
                            // SwapSeriesTrackers(firstSubjectFirstTracker, secondSubjectFirstTracker);
                            // якщо стає гірше не повертаємо нічого,
                            // нову зберігаємо лише коли вона не гірше за поточну
                            return null;
                        }
                        // зберігаємо оцінку нової популяції
                        currentEstimation = after;
                        
                        logger.LogInformation($"ЗРОБИЛИ КРАЩЕ!: {before} : {after} ");
                        if(firstSubjectFirstTracker.SeriesId != null && secondSubjectFirstTracker.SeriesId != null)
                            return new KeyValuePair<int, int>((int)firstSubjectFirstTracker.SeriesId, (int)secondSubjectFirstTracker.SeriesId);
                    }
                }
            }
        }
        return null;
    }
    
    public KeyValuePair<int, int>? SwapGroupSubjects()
    {
            // беремо предмети які можна змінювати
            // var list = FirstTrackers.Select(e => e).Where(e => !e.IsLowDaysDanger && e.ScheduleSlot.GroupSubject.Groups.Count > 1).ToList();
            var list = FirstTrackers.Select(e => e).Where(e => !e.IsLowDaysDanger && e.SeriesLength > 3 && !swappedSeries.Contains(e.SeriesId)).ToList();
            
            // цикл, оскільки вірогідність з 1го разу натрапити на такий предмет що має ідентичні групи не висока
            int stop = 5;
            while (true)
            {
                if(stop == 0)
                    break;
                stop--;
                // перестворимо swappedSeries якщо list пустий
                if (list.Count == 0)
                {
                    swappedSeries = new List<int?>();
                    break;
                }
                var firstRandomLessonTracker = list.ElementAt(_random.Next(0, list.Count));
                // якщо нещодавно вже намагались замінити цей предмет, спробуємо інший
                if (swappedSeries.Contains(firstRandomLessonTracker.SeriesId))
                {
                    continue;
                }
                swappedSeries.Add(firstRandomLessonTracker.SeriesId);

                // ми можемо замінити лише якщо ті самі групи
                // додаємо у список всі групи що беруть участь в цій дисципліні
                var groups = firstRandomLessonTracker.ScheduleSlot.GroupSubject.Groups;
                
                // дивимось по першій групі, чи є ще дисципліна в якій задіяні лише групи з цього списку
                var slots = groupsSlots[groups[0].Id]
                    .Select(s => s)
                    .Where(s => s.IsFirstTrackerInSeries)
                    .ToList();
                
                var potentialSubjectToSwap = new List<SlotTracker>();

                foreach (var slot in slots)
                {
                    // пропускаємо ту серію що змінюємо та слоти які не є першими у серії
                    if(slot.SeriesId == firstRandomLessonTracker.SeriesId || !slot.IsFirstTrackerInSeries)
                        continue;
                    
                    // якщо у якоїсь з груп немає такої дисципліни, то вона нам не підходить, дивимось інші дисципліни
                    var subjectGroups = slot.ScheduleSlot.GroupSubject.Groups;
                    var isPresent = true;
                    // дивимось як ті що є в даній дисципліні яку ми змінюємо
                    foreach (var group in subjectGroups)
                    {
                        if (!groups.Select(g => g.Id).Contains(group.Id))
                        {
                            isPresent = false;
                            break;
                        }
                    }
                    if(!isPresent)
                        continue;
                    // дивимось і ті що є в іншій дисципліні з якою ми змінюємо
                    foreach (var group in groups)
                    {
                        if (!subjectGroups.Select(g => g.Id).Contains(group.Id))
                        {
                            isPresent = false;
                            break;
                        }
                    }
                    if(!isPresent)
                        continue;
                    potentialSubjectToSwap.Add(slot);
                }
                
                // якщо немає дисциплін для свапу з цією, шукаємо для іншої дисципліни
                if (potentialSubjectToSwap.Count == 0)
                    continue;
                
                // пробуваємо перепризначити місцями 2 дисципліни

                var before = currentEstimation;
                var aSlots = GetAssignedSlots();
                
                foreach (var subjectTracker in potentialSubjectToSwap)
                {
                    var DomainFirst = new DomainValue()
                    {
                        Date = firstRandomLessonTracker.ScheduleSlot.Date,
                        PairNumber = firstRandomLessonTracker.ScheduleSlot.PairNumber,
                    };
                    var DomainSecond = new DomainValue()
                    {
                        Date = subjectTracker.ScheduleSlot.Date,
                        PairNumber = subjectTracker.ScheduleSlot.PairNumber,
                    };
                    
                    var cacheDomainsFirst = new List<DomainValue>();
                    var cacheDomainsSecond = new List<DomainValue>();
                    
                    if(!Slots.ContainsKey(firstRandomLessonTracker.ScheduleSlot))
                        continue;
                    
                    var freeTRackersFirst = firstRandomLessonTracker.ScheduleSlot.GroupSubject.ScheduleSlots
                        .Select(slot => Slots[slot])
                        .Where(tracker => tracker.SeriesId == firstRandomLessonTracker.SeriesId)
                        .OrderBy(tracker => tracker.ScheduleSlot.LessonNumber)
                        .ToList();
                    
                    var freeTRackersSecond = subjectTracker.ScheduleSlot.GroupSubject.ScheduleSlots
                        .Select(slot => Slots[slot])
                        .Where(tracker => tracker.SeriesId == subjectTracker.SeriesId)
                        .OrderBy(tracker => tracker.ScheduleSlot.LessonNumber)
                        .ToList();

                    // A series swap must preserve the complete schedule. Without
                    // equal series lengths, synchronized reassignment can leave
                    // one or more slots unassigned and inflate the score.
                    if (freeTRackersFirst.Count != freeTRackersSecond.Count)
                        continue;

                    var cacheClassroomsFirst = freeTRackersFirst
                        .Select(x => x.ScheduleSlot.Classroom)
                        .ToList();
                    var cacheClassroomsSecond = freeTRackersSecond
                        .Select(x => x.ScheduleSlot.Classroom)
                        .ToList();
                    
                    foreach (var tracker in freeTRackersFirst)
                    {
                        var cachedDomainVal = new DomainValue();
                        cachedDomainVal.PairNumber = tracker.ScheduleSlot.PairNumber;
                        cachedDomainVal.Date = tracker.ScheduleSlot.Date;
                        cacheDomainsFirst.Add(cachedDomainVal);
                    }
                    foreach (var tracker in freeTRackersSecond)
                    {
                        var cachedDomainVal = new DomainValue();
                        cachedDomainVal.PairNumber = tracker.ScheduleSlot.PairNumber;
                        cachedDomainVal.Date = tracker.ScheduleSlot.Date;
                        cacheDomainsSecond.Add(cachedDomainVal);
                    }
                    
                    freeTRackersFirst.ForEach(SetSlotUnAssigned);
                    SetSlotAssignedGenetic(firstRandomLessonTracker, DomainSecond);
                    
                    freeTRackersSecond.ForEach(SetSlotUnAssigned);
                    SetSlotAssignedGenetic(subjectTracker, DomainFirst);
                    
                    var syncCheck = ApplySynchronizedDomainPatternGenetic(firstRandomLessonTracker, aSlots);
                    syncCheck = syncCheck && ApplySynchronizedDomainPatternGenetic(subjectTracker, aSlots);

                    if (syncCheck)
                    {
                        var res = Estimate();
                        // якщо стає не гірше - зберігаємо
                        // якщо замінити не строге >= на строге >=, шукає набагато довше
                        // тобто простіше знаходити ідентичні розклади, можна тоді зберігати 2 варіанти замість 1го
                        // і використовувати 2 популяції, ніби ми їх так множимо.
                        if (res > before && Slots.Values.All(tracker => tracker.IsAssigned))
                        {
                            freeTRackersFirst.ForEach(e => Slots[e.ScheduleSlot] = e);
                            freeTRackersSecond.ForEach(e => Slots[e.ScheduleSlot] = e);
                            
                            currentEstimation = res;
                            logger.LogInformation($"ЗРОБИЛИ КРАЩЕ СВАПОМ ГРУП");

                            if(firstRandomLessonTracker.SeriesId != null && subjectTracker.SeriesId != null)
                                return new KeyValuePair<int, int>((int)firstRandomLessonTracker.SeriesId, (int)subjectTracker.SeriesId);
                            return null;
                        }

                        // A rejected swap must restore both complete series. Keeping
                        // the mutated slots while restoring only the score corrupts
                        // the individual for all following operations.
                        foreach (var tracker in freeTRackersFirst.Concat(freeTRackersSecond))
                            SetSlotUnAssigned(tracker);
                        for (var index = 0; index < freeTRackersFirst.Count; index++)
                        {
                            freeTRackersFirst[index].ScheduleSlot.Classroom = cacheClassroomsFirst[index];
                            SetSlotAssignedGenetic(freeTRackersFirst[index], cacheDomainsFirst[index]);
                        }
                        for (var index = 0; index < freeTRackersSecond.Count; index++)
                        {
                            freeTRackersSecond[index].ScheduleSlot.Classroom = cacheClassroomsSecond[index];
                            SetSlotAssignedGenetic(freeTRackersSecond[index], cacheDomainsSecond[index]);
                        }
                        currentEstimation = before;
                    }
                }

            }
            return null;
    }

    public void ApplyTeacherSubjectsSwap(KeyValuePair<int, int> subjectsForSwap)
    {
        var firstSubject = FirstTrackers
            .Select(e => e)
            .First(e => e.SeriesId == subjectsForSwap.Key);
        var secondSubject = FirstTrackers
            .Select(e => e)
            .First(e => e.SeriesId == subjectsForSwap.Value);
        var before = currentEstimation;
        SwapSeriesTrackers(firstSubject, secondSubject);
        var after = Estimate();

        if (after <= before)
        {
            SwapSeriesTrackers(firstSubject, secondSubject);
        }
        else
            currentEstimation = after;
    } 

    #endregion

    public Individual SetSlotAssignedGeneticClone(SlotTracker slot, DomainValue val)
    {
        var population = this.clone();
        population.SetSlotAssignedGenetic(slot, val);
        return population;
    }
    
    // різниця полягає в тому що нам не потрібно зберігати крок.
    public void SetSlotAssignedGenetic(SlotTracker slot, DomainValue val)
    {
        Slots[slot.ScheduleSlot] = slot; // let's try this?
        slot.SetDomain(val);
        slot.IsAssigned = true;

        if (!assignedSlotsByTeacherDate.ContainsKey(slot.ScheduleSlot.GroupSubject.Teacher.Id))
            assignedSlotsByTeacherDate[slot.ScheduleSlot.GroupSubject.Teacher.Id] = new Dictionary<DateTime, HashSet<SlotTracker>>();

        if (!assignedSlotsByTeacherDate[slot.ScheduleSlot.GroupSubject.Teacher.Id].ContainsKey(val.Date))
            assignedSlotsByTeacherDate[slot.ScheduleSlot.GroupSubject.Teacher.Id][val.Date] = new HashSet<SlotTracker>();

        assignedSlotsByTeacherDate[slot.ScheduleSlot.GroupSubject.Teacher.Id][val.Date].Add(slot);

        foreach (var group in slot.ScheduleSlot.GroupSubject.Groups)
        {
            if (!assignedSlotsByGroupDate.ContainsKey(group.Id))
                assignedSlotsByGroupDate[group.Id] = new Dictionary<DateTime, HashSet<SlotTracker>>();

            if (!assignedSlotsByGroupDate[group.Id].ContainsKey(val.Date))
                assignedSlotsByGroupDate[group.Id][val.Date] = new HashSet<SlotTracker>();

            assignedSlotsByGroupDate[group.Id][val.Date].Add(slot);
        }

        if (slot.ScheduleSlot.Classroom != null) 
        { 
            if (!assignedClassrooms.ContainsKey(val.Date))
                assignedClassrooms[val.Date] = new Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>>();

            if (!assignedClassrooms[val.Date].ContainsKey(val.PairNumber))
                assignedClassrooms[val.Date][val.PairNumber] = new Dictionary<ClassroomDTO, ScheduleSlotDTO>();

            if (assignedClassrooms[val.Date][val.PairNumber].ContainsKey(slot.ScheduleSlot.Classroom))
                throw new Exception($"Аудиторія {slot.ScheduleSlot.Classroom.Name} вже зайнята на {val.Date.ToShortDateString()} {val.PairNumber} парі");

            assignedClassrooms[val.Date][slot.ScheduleSlot.PairNumber][slot.ScheduleSlot.Classroom] = slot.ScheduleSlot;
        }

        // FLAT-дзеркало: встановити біти вчителя/груп у живій позиції.
        // NoteAssigned повертає false на засталому FlatCode (re-assign без
        // чистого unassign) — повний ребілд при наступному використанні.
        if (flatBits is { Enabled: true } && !flatBits.NoteAssigned(slot))
        {
            System.Threading.Interlocked.Increment(ref Services.Calc.FlatBitsets.StaleRebuilds);
            flatBits = null;
            flatTables = null;
        }
    }

    internal int currentEstimation = Int32.MinValue;

    // Діагностика Amdahl: сумарні тики у гарячих місцях (ланки виконуються
    // послідовно; скидаються у Run). Сайти не вкладаються один в одного.
    internal static long TicksEstimate, TicksForwardCheck, TicksValidate, TicksUnassign, TicksPattern, TicksClone;
    internal static long TicksCloneRoot, TicksCloneGroups, TicksCloneTrackers, TicksCloneDomains;
    internal static long TicksCloneMaps, TicksCloneDeltas, TicksCloneIndexes;
    // діагностика 2a: куди пішов unassign
    internal static long TicksUnassignDicts, TicksUnassignClassrooms, TicksUnassignMirror, TicksIdxRebuild;
    internal static long UnassignCalls, IdxRebuildCalls;
    internal static string ProfilingSummary() =>
        $"estimate={MsOf(TicksEstimate)} forwardCheck={MsOf(TicksForwardCheck)} validate={MsOf(TicksValidate)} unassign={MsOf(TicksUnassign)} (dicts={MsOf(TicksUnassignDicts)} cls={MsOf(TicksUnassignClassrooms)} mir={MsOf(TicksUnassignMirror)} n={UnassignCalls}) pattern={MsOf(TicksPattern)} clone={MsOf(TicksClone)} (root={MsOf(TicksCloneRoot)} groups={MsOf(TicksCloneGroups)} trackers={MsOf(TicksCloneTrackers)} domains={MsOf(TicksCloneDomains)} maps={MsOf(TicksCloneMaps)} deltas={MsOf(TicksCloneDeltas)} indexes={MsOf(TicksCloneIndexes)} idxrb={MsOf(TicksIdxRebuild)} rbCalls={IdxRebuildCalls}) | {Services.Calc.FlatBitsets.Telemetry()}";
    private static string MsOf(long ticks) => ((int)(ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency)).ToString();

    internal Services.CheapEval.CheapEvaluationEngine? _cheapEngine;
    private bool _cheapEngineFailed;

    // FLAT-дзеркало (фаза 1a — телеметрія): бітсети TRUE-стану генома.
    internal Services.Calc.SeasonFlatTables? flatTables;
    internal Services.Calc.FlatBitsets? flatBits;
    private int _flatVerifyTick;
    internal static int DiffMismatchLog;
    private static int FlatVerifyTick;
    private static int FlatShadowTick;

    /// <summary>
    /// Лінива побудова дзеркала з поточного стану. Таблиці ПОВНІСТЮ
    /// передреєстровуються (всі вчителі/групи Root + ВСІ дати з доменів і
    /// поточних позицій) і заморожуються — далі лише читання, безпечне для
    /// паралельних ліній. Нові дати після фризу = баг (домени сідовані до
    /// генетики; призначення беруть дати лише з доменів).
    /// </summary>
    private void EnsureFlat()
    {
        if (flatBits != null) return;
        var tables = new Services.Calc.SeasonFlatTables();
        foreach (var gs in Root.GroupSubjects)
        {
            tables.TeacherCode(gs.Teacher.Id);
            foreach (var g in gs.Groups)
                tables.GroupCode(g.Id);
        }
        foreach (var t in Slots.Values)
        {
            tables.DateCode(t.ScheduleSlot.Date, 0);
            foreach (var d in t.AvailableDomains)
                tables.DateCode(d.Date, 0);
        }
        tables.Freeze();

        var fb = new Services.Calc.FlatBitsets();
        fb.EnsureEntities(tables, tables.Teachers, tables.Groups);
        foreach (var t in Slots.Values)
            if (t.IsAssigned)
                fb.NoteAssigned(t);
        flatTables = tables;
        flatBits = fb;
    }
    
    public Individual SwapSeriesTrackersClone(SlotTracker first, SlotTracker second)
    {
        var population = this.clone();
        population.SwapSeriesTrackers(first, second);
        
        return population;
    }
    
    // Міняє серії місцями, при цьому залишаючи лише кращий.
    // Виконується лише якщо кількість занять є однаковою.
    private void SwapSeriesTrackers(SlotTracker first, SlotTracker second)
    {
        var freeTrackersFirst = first.ScheduleSlot.GroupSubject.ScheduleSlots
            .Select(slot => Slots[slot])
            .Where(tracker => tracker.SeriesId == first.SeriesId)
            .OrderBy(tracker => tracker.ScheduleSlot.LessonNumber)
            .ToList();
        var freeTrackersSecond = second.ScheduleSlot.GroupSubject.ScheduleSlots
            .Select(slot => Slots[slot])
            .Where(tracker => tracker.SeriesId == second.SeriesId)
            .OrderBy(tracker => tracker.ScheduleSlot.LessonNumber)
            .ToList();
            
        // Якщо кількість занять не однакова, неможливо просто замінити серії місцями
        // todo подумати, можливо
        // просто необхідно доповнити цей метод для більшого числа занять,
        // при цьому використовуючи не SetSlotAssignedGenetic а ApplySynchronizedDomainPatternGenetic
        if(freeTrackersFirst.Count != freeTrackersSecond.Count)
            return;

        for (int j = 0; j < freeTrackersFirst.Count; j++)
        {
            var domainFirst = new DomainValue()
            {
                Date = freeTrackersFirst[j].ScheduleSlot.Date,
                PairNumber = freeTrackersFirst[j].ScheduleSlot.PairNumber,
            };
            var domainSecond = new DomainValue()
            {
                Date = freeTrackersSecond[j].ScheduleSlot.Date,
                PairNumber = freeTrackersSecond[j].ScheduleSlot.PairNumber,
            };    
            SetSlotAssignedGenetic(freeTrackersFirst[j], domainSecond);
            SetSlotAssignedGenetic(freeTrackersSecond[j], domainFirst);
        }
    }
    
    private bool ApplySynchronizedDomainPatternGenetic(SlotTracker currentTracker, AssignedSlotsDTO assignedSLots)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return ApplySynchronizedDomainPatternGeneticInner(currentTracker, assignedSLots);
        }
        finally
        {
            TicksPattern += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        }
    }

    private bool ApplySynchronizedDomainPatternGeneticInner(SlotTracker currentTracker, AssignedSlotsDTO assignedSLots)
    {
        // Отримуємо предмет із поточного трекера
        var subject = currentTracker.ScheduleSlot.GroupSubject;

        // Отримуємо всі слот-трекери для цього предмету через список слотів GroupSubject.
        // Використовуємо лише ще не призначені з серії. 
        var freeTRackers = subject.ScheduleSlots
            .Select(slot => Slots[slot])
            .Where(tracker => !tracker.IsAssigned && tracker.SeriesId == currentTracker.SeriesId)
            .OrderBy(tracker => tracker.ScheduleSlot.LessonNumber)
            .ToList();

        if (!freeTRackers.Any()) return true;

        DateTime minAvailable = currentTracker.ScheduleSlot.Date;

        var nextDate = new DomainValue
        {
            Date = minAvailable.AddDays(currentTracker.WeekShift * 7),
            PairNumber = currentTracker.ScheduleSlot.PairNumber
        };

        var maxAvailable = freeTRackers.Select(x => x.AvailableDomains.Max()).Max();

        while (nextDate <= maxAvailable)
        {
            var nextTracker = freeTRackers.FirstOrDefault(tracker => !tracker.IsAssigned
                                                                     && tracker.AvailableDomains.Contains(nextDate)
                                                                     && ValidateAssignment(tracker, nextDate, assignedSLots)
            );

            if (nextTracker != null)
            {
                SetSlotAssignedGenetic(nextTracker, nextDate);
                freeTRackers.Remove(nextTracker);
            }

            nextDate.Date = nextDate.Date.AddDays(currentTracker.WeekShift * 7);
        }

        var res = !freeTRackers.Any(tracker => !tracker.IsAssigned);
        return res;
    }

    /// <summary>
    /// Removes complete series from a clone and rebuilds them through the
    /// constraint-aware series search. The original individual is untouched.
    /// </summary>
    public Individual? TryDestroyRepair(int seriesCount = 1, int maxMilliseconds = 300, int attempts = 3)
    {
        var stopwatch = Stopwatch.StartNew();
        Individual? bestCandidate = null;
        var attemptCount = Math.Max(1, attempts);

        for (var attempt = 0; attempt < attemptCount; attempt++)
        {
            var remainingMilliseconds = maxMilliseconds - (int)stopwatch.ElapsedMilliseconds;
            if (remainingMilliseconds <= 0)
                break;

            var remainingAttempts = attemptCount - attempt;
            var attemptMilliseconds = Math.Max(1, remainingMilliseconds / remainingAttempts);
            var requestedSeriesCount = Math.Max(1, seriesCount - (attempt % Math.Max(1, seriesCount)));
            var candidate = TryDestroyRepairOnce(requestedSeriesCount, attemptMilliseconds);

            if (candidate != null &&
                (bestCandidate == null || candidate.currentEstimation > bestCandidate.currentEstimation))
                bestCandidate = candidate;
        }

        return bestCandidate;
    }

    /// <summary>
    /// ILS-збурення: знищує N пов'язаних серій і відновлює їх у випадкові
    /// валідні домени — гарантовано повний розклад в іншому басейні.
    /// Повертає кандидата, якщо втрата в межах maxAcceptedLoss, інакше null.
    /// Сам індивід не змінюється (робота йде на клоні).
    /// </summary>
    public Individual? TryPerturb(int seriesCount, int maxMilliseconds, int attempts, int maxAcceptedLoss)
    {
        var stopwatch = Stopwatch.StartNew();
        var attemptCount = Math.Max(1, attempts);
        var lossBound = Math.Max(1, maxAcceptedLoss);

        for (var attempt = 0; attempt < attemptCount; attempt++)
        {
            var remainingMilliseconds = maxMilliseconds - (int)stopwatch.ElapsedMilliseconds;
            if (remainingMilliseconds <= 0)
                break;

            var remainingAttempts = attemptCount - attempt;
            var attemptMilliseconds = Math.Max(1, remainingMilliseconds / remainingAttempts);
            var candidate = TryDestroyRepairOnce(Math.Max(1, seriesCount), attemptMilliseconds, randomized: true);

            if (candidate != null &&
                candidate.Slots.Count > 0 &&
                candidate.Slots.Values.All(x => x.IsAssigned) &&
                candidate.currentEstimation >= currentEstimation - lossBound)
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Chain-relocate: серія A переїжджає у випадковий валідний домен,
    /// серія B (спільний викладач або група з A) стає на місце, яке A
    /// звільнила. Розширює окіл на двосерійні ходи, недосяжні для
    /// односерійних мутацій. Робота на клоні; повертає покращеного
    /// повного індивіда або null. Сам індивід не змінюється.
    /// </summary>
    /// <summary>
    /// Directed (турнірна) версія chain-relocate: скан багатьох комбінацій
    /// (серія A × домен × серія B) на оригіналі з self-restore, переможця
    /// застосовуємо до одного клону. Приймається найкраща ДЕЛЬТА, а не
    /// перше покращення — як у TryBestDomainMutation.
    /// </summary>
    public Individual? TryChainRelocate(int maxMilliseconds = 600, int maxTrials = 24)
    {
        var stopwatch = Stopwatch.StartNew();

        var seriesFirsts = FirstTrackers
            .Where(x => x.IsAssigned && x.IsFirstTrackerInSeries && x.SeriesId.HasValue && !x.IsLowDaysDanger)
            .GroupBy(x => x.SeriesId)
            .Select(g => g.First())
            .ToList();
        if (seriesFirsts.Count == 0)
            return null;
        // Випадковий вибір серій: pressure-сортування (найменш доступу —
        // першими) тут шкодить — затиснуті серії найгірші кандидати на
        // relocation. Random sampling дав +3890 у солід-тесті.
        Shuffle(_random, seriesFirsts);

        var before = currentEstimation;
        using var scanGuard = _cheapEngine is null ? null : new ScanVerifyGuard(_cheapEngine);
        int? bestAId = null, bestBId = null;
        DomainValue bestADomain = null, bestVacancy = null;
        var bestScore = before;
        var trials = 0;

        foreach (var a in seriesFirsts)
        {
            if (trials >= maxTrials || stopwatch.ElapsedMilliseconds >= maxMilliseconds)
                break;
            trials++;

            var aOriginal = CacheSeries(this, a.SeriesId!.Value);
            var aTrackers = aOriginal.Select(x => x.Tracker).ToList();
            foreach (var tracker in aTrackers)
                SetSlotUnAssigned(tracker);

            var aDomains = a.AvailableDomains.ToList();
            Shuffle(_random, aDomains);
            if (aDomains.Count > 8)
                aDomains = aDomains.Take(8).ToList();
            var vacancy = new DomainValue
            {
                Date = aOriginal[0].Domain.Date,
                PairNumber = aOriginal[0].Domain.PairNumber
            };
            var aTeacher = a.ScheduleSlot.GroupSubject.Teacher.Id;
            var aGroupIds = a.ScheduleSlot.GroupSubject.Groups.Select(g => g.Id).ToHashSet();

            foreach (var domain in aDomains)
            {
                if (stopwatch.ElapsedMilliseconds >= maxMilliseconds)
                    break;
                if (!ValidateAssignment(a, domain, GetAssignedSlots()))
                    continue;

                SetSlotAssignedGenetic(a, domain);
                if (!ApplySynchronizedDomainPatternGenetic(a, GetAssignedSlots()))
                {
                    RestoreSeries(this, aOriginal);
                    continue;
                }

                // A-only варіант теж кандидат у переможці
                var aOnlyScore = Estimate();
                if (aOnlyScore > bestScore && Slots.Values.All(x => x.IsAssigned))
                {
                    bestScore = aOnlyScore;
                    bestAId = a.SeriesId;
                    bestADomain = domain;
                    bestVacancy = vacancy;
                    bestBId = null;
                }

                var bCandidates = seriesFirsts
                    .Where(x => x.SeriesId != a.SeriesId)
                    .Where(x => x.ScheduleSlot.GroupSubject.Teacher.Id == aTeacher ||
                                x.ScheduleSlot.GroupSubject.Groups.Any(g => aGroupIds.Contains(g.Id)))
                    .ToList();
                Shuffle(_random, bCandidates);

                foreach (var b in bCandidates.Take(2))
                {
                    if (stopwatch.ElapsedMilliseconds >= maxMilliseconds)
                        break;

                    var bOriginal = CacheSeries(this, b.SeriesId!.Value);
                    if (ValidateAssignment(b, vacancy, GetAssignedSlots()))
                    {
                        SetSlotAssignedGenetic(b, vacancy);
                        if (ApplySynchronizedDomainPatternGenetic(b, GetAssignedSlots()))
                        {
                            var res = Estimate();
                            if (res > bestScore && Slots.Values.All(x => x.IsAssigned))
                            {
                                bestScore = res;
                                bestAId = a.SeriesId;
                                bestADomain = domain;
                                bestVacancy = vacancy;
                                bestBId = b.SeriesId;
                            }
                        }
                    }
                    RestoreSeries(this, bOriginal);
                }

                RestoreSeries(this, aOriginal);
            }
        }

        // Сеттл: скан лишив engine з неконсумованими переходами останнього
        // restore — клон-переможець успадкував би стале (stale) стан клітинок
        // і його Estimate-гейт відхилив би все (chainWins=0 у 20260830-230242).
        // Оригінал лишається в популяції — консистентний стан потрібен завжди.
        Estimate();

        if (bestAId == null)
            return null;

        // Відтворюємо переможну комбінацію на одному клоні
        var winner = CloneHelper.clone(this);
        winner.currentEstimation = before;
        if (!winner.isInit)
            winner.PreparePrivateGeneticCache();

        var wAFirst = winner.FirstTrackers.First(x => x.SeriesId == bestAId && x.IsFirstTrackerInSeries);
        var wAOriginal = CacheSeries(winner, bestAId.Value);
        foreach (var tracker in wAOriginal.Select(x => x.Tracker))
            winner.SetSlotUnAssigned(tracker);
        if (!winner.ValidateAssignment(wAFirst, bestADomain, winner.GetAssignedSlots()))
            return null;
        winner.SetSlotAssignedGenetic(wAFirst, bestADomain);
        if (!winner.ApplySynchronizedDomainPatternGenetic(wAFirst, winner.GetAssignedSlots()))
            return null;

        if (bestBId != null)
        {
            var wBFirst = winner.FirstTrackers.First(x => x.SeriesId == bestBId && x.IsFirstTrackerInSeries);
            var wBOriginal = CacheSeries(winner, bestBId.Value);
            foreach (var tracker in wBOriginal.Select(x => x.Tracker))
                winner.SetSlotUnAssigned(tracker);
            if (!winner.ValidateAssignment(wBFirst, bestVacancy, winner.GetAssignedSlots()))
                return null;
            winner.SetSlotAssignedGenetic(wBFirst, bestVacancy);
            if (!winner.ApplySynchronizedDomainPatternGenetic(wBFirst, winner.GetAssignedSlots()))
                return null;
        }

        var finalScore = winner.Estimate();
        if (finalScore <= before || !winner.Slots.Values.All(x => x.IsAssigned))
            return null;
        winner.currentEstimation = finalScore;
        return winner;
    }

    /// <summary>
    /// Легаси-режим (ChainDirected=false): 2 незалежні спроби з випадковим
    /// вибором серії і першим знайденим покращенням.
    /// </summary>
    public Individual? TryChainRelocateRandom(int maxMilliseconds = 600, int attempts = 2)
    {
        var stopwatch = Stopwatch.StartNew();
        Individual? best = null;
        var attemptCount = Math.Max(1, attempts);

        for (var attempt = 0; attempt < attemptCount; attempt++)
        {
            var remainingMilliseconds = maxMilliseconds - (int)stopwatch.ElapsedMilliseconds;
            if (remainingMilliseconds <= 0)
                break;

            var candidate = TryChainCore(remainingMilliseconds, 0, requireImprovement: true);
            if (candidate != null &&
                (best == null || candidate.currentEstimation > best.currentEstimation))
                best = candidate;
        }

        return best;
    }

    /// <summary>
    /// HOTSPOT-RELOCATE: перший ЦІЛЬОВИЙ op (усі інші — сліпі). Рушій повідомляє
    /// найгарячіші клітинки (|значення - baseline| через декомпозицію — generic,
    /// без знань про правила); op сканує серії цих клітинок (турнір, найкраща
    /// дельта, self-restore на оригіналі) і застосовує переможця до одного клону.
    /// </summary>
    public Individual? TryHotspotRelocate(int maxMilliseconds = 400, int maxDomainCandidates = 10)
    {
        var stopwatch = Stopwatch.StartNew();
        var hotspotGroups = _cheapEngine?.GetHotspots(8);
        if (hotspotGroups == null || hotspotGroups.Count == 0)
            return null;

        var before = currentEstimation;
        using var scanGuard = _cheapEngine is null ? null : new ScanVerifyGuard(_cheapEngine);

        var candidateFirsts = new List<SlotTracker>();
        var seenSeries = new HashSet<int?>();
        foreach (var slots in hotspotGroups)
            foreach (var dto in slots)
            {
                if (!Slots.TryGetValue(dto, out var t))
                    continue;
                if (!t.IsAssigned || !t.IsFirstTrackerInSeries || t.SeriesId == null || t.IsLowDaysDanger)
                    continue;
                if (seenSeries.Add(t.SeriesId))
                    candidateFirsts.Add(t);
            }
        if (candidateFirsts.Count == 0)
            return null;
        Shuffle(_random, candidateFirsts);

        int? bestSeriesId = null;
        DomainValue bestDomain = null;
        var bestScore = before;

        foreach (var first in candidateFirsts)
        {
            if (stopwatch.ElapsedMilliseconds >= maxMilliseconds)
                break;

            var seriesTrackers = first.ScheduleSlot.GroupSubject.ScheduleSlots
                .Select(slot => Slots[slot])
                .Where(t => t.SeriesId == first.SeriesId)
                .OrderBy(t => t.ScheduleSlot.LessonNumber)
                .ToList();
            var cacheDomains = seriesTrackers
                .Select(t => new DomainValue { Date = t.ScheduleSlot.Date, PairNumber = t.ScheduleSlot.PairNumber })
                .ToList();
            var freeTrackers = seriesTrackers.Where(t => !t.IsFirstTrackerInSeries).ToList();
            var aSlots = GetAssignedSlots();
            var currentDomain = new DomainValue
            {
                Date = first.ScheduleSlot.Date,
                PairNumber = first.ScheduleSlot.PairNumber
            };

            foreach (var domain in SelectCandidateDomains(first, currentDomain, maxDomainCandidates))
            {
                if (stopwatch.ElapsedMilliseconds >= maxMilliseconds)
                    break;
                if (!TryPlaceSeries(first, domain, freeTrackers, aSlots, out var res))
                {
                    RestoreSeriesPlacement(seriesTrackers, cacheDomains);
                    continue;
                }

                var feasible = ForwardCheck(first, first.AssignStep);
                if (feasible && res > bestScore)
                {
                    bestScore = res;
                    bestSeriesId = first.SeriesId;
                    bestDomain = domain;
                }

                RestoreSeriesPlacement(seriesTrackers, cacheDomains);
            }
        }

        if (bestSeriesId == null)
            return null;

        // відтворюємо переможця на одному клоні (контракт mutation-op)
        var winner = CloneHelper.clone(this);
        winner.currentEstimation = before;
        if (!winner.isInit)
            winner.PreparePrivateGeneticCache();

        var wFirst = winner.Slots.Values
            .First(t => t.SeriesId == bestSeriesId && t.IsFirstTrackerInSeries);
        var wSeriesTrackers = wFirst.ScheduleSlot.GroupSubject.ScheduleSlots
            .Select(slot => winner.Slots[slot])
            .Where(t => t.SeriesId == bestSeriesId)
            .OrderBy(t => t.ScheduleSlot.LessonNumber)
            .ToList();
        var wFree = wSeriesTrackers.Where(t => !t.IsFirstTrackerInSeries).ToList();

        if (!winner.TryPlaceSeries(wFirst, bestDomain, wFree, winner.GetAssignedSlots(), out _))
            return null;
        if (!winner.ForwardCheck(wFirst, wFirst.AssignStep))
            return null;

        var finalScore = winner.Estimate();
        if (finalScore <= before || !winner.Slots.Values.All(x => x.IsAssigned))
            return null;
        winner.currentEstimation = finalScore;
        return winner;
    }

    /// <summary>
    /// ГЕН-БЛОЧНИЙ HGT (видіння користувача): пересадка ЦІЛОГО блоку
    /// GroupSubject з донора. Кожна серія блока саджається на позицію донора
    /// (fallback — будь-який валідний домен: feasibility-repair конфліктів).
    /// Гібрид приймається ТІЛЬКИ якщо виживає лінію пулу
    /// (>= minAcceptableScore), інакше повний відкат; прийнятий гібрид далі
    /// перетравлюється власним локальним пошуком лінії. Це перенесення ГЕНІВ
    /// (структурного матеріалу), а не покращень — на відміну від
    /// зафальшивленого delta-replay.
    /// </summary>
    public bool TryTransplantBlockFrom(Individual donor, long gsId, int minAcceptableScore)
    {
        var before = currentEstimation;

        var donorSeries = donor.Slots.Values
            .Where(t => t.IsAssigned && t.SeriesId.HasValue &&
                        t.IsFirstTrackerInSeries &&
                        t.ScheduleSlot.GroupSubject.Id == gsId)
            .GroupBy(t => t.SeriesId!.Value)
            .ToList();
        if (donorSeries.Count == 0)
            return false;
        Shuffle(_random, donorSeries);

        var transplanted = new List<List<(SlotTracker Tracker, DomainValue Domain, ClassroomDTO? Classroom)>>();
        var placed = 0;

        foreach (var seriesGroup in donorSeries)
        {
            var seriesId = seriesGroup.Key;
            var donorFirst = seriesGroup.First();

            var receiverTrackers = Slots.Values
                .Where(t => t.SeriesId == seriesId)
                .OrderBy(t => t.ScheduleSlot.LessonNumber)
                .ToList();
            if (receiverTrackers.Count == 0)
                continue;

            var original = CacheSeries(this, seriesId);
            foreach (var t in receiverTrackers)
                SetSlotUnAssigned(t);

            var anchor = receiverTrackers.FirstOrDefault(t => t.IsFirstTrackerInSeries) ?? receiverTrackers[0];
            var donorDomain = new DomainValue
            {
                Date = donorFirst.ScheduleSlot.Date,
                PairNumber = donorFirst.ScheduleSlot.PairNumber
            };

            var placedOk = false;
            if (ValidateAssignment(anchor, donorDomain, GetAssignedSlots()))
            {
                SetSlotAssignedGenetic(anchor, donorDomain);
                if (ApplySynchronizedDomainPatternGenetic(anchor, GetAssignedSlots()))
                    placedOk = true;
                else
                    foreach (var t in receiverTrackers.Where(x => x.IsAssigned))
                        SetSlotUnAssigned(t);
            }

            // feasibility-repair: конфлікт із контекстом отримувача —
            // серія їде в будь-який вільний валідний домен (без врахування скору)
            if (!placedOk)
            {
                var domains = anchor.AvailableDomains.ToList();
                Shuffle(_random, domains);
                foreach (var domain in domains)
                {
                    if (!ValidateAssignment(anchor, domain, GetAssignedSlots()))
                        continue;
                    SetSlotAssignedGenetic(anchor, domain);
                    if (ApplySynchronizedDomainPatternGenetic(anchor, GetAssignedSlots()))
                    {
                        placedOk = true;
                        break;
                    }
                    foreach (var t in receiverTrackers.Where(x => x.IsAssigned))
                        SetSlotUnAssigned(t);
                }
            }

            if (placedOk)
            {
                placed++;
                transplanted.Add(original);
            }
            else
            {
                RestoreSeries(this, original);
            }
        }

        if (placed == 0)
            return false;

        var res = Estimate();
        if (res >= minAcceptableScore && Slots.Values.All(x => x.IsAssigned))
        {
            currentEstimation = res;
            return true;
        }

        // гібрид не виживає лінію пулу — повний відкат трансплантата
        foreach (var original in transplanted)
            RestoreSeries(this, original);
        currentEstimation = before;
        return false;
    }

    /// <summary>
    /// ILS-збурення: до `moves` послідовних relocation серій (з
    /// синхронізованим патерном) з прийняттям кумулятивної втрати до
    /// lossBudget. Один хід = надто мілко (спуск повертається в той самий
    /// басейн); стек глибших ходів лишається обмеженим бюджетом.
    /// </summary>
    public Individual? TryChainPerturb(
        int maxMilliseconds = 600, int lossBudget = 3000, int moves = 1)
    {
        var stopwatch = Stopwatch.StartNew();
        var startScore = currentEstimation;
        var current = this;
        for (var move = 0; move < Math.Max(1, moves); move++)
        {
            var remainingMilliseconds = maxMilliseconds - (int)stopwatch.ElapsedMilliseconds;
            if (remainingMilliseconds <= 0)
                break;

            var next = current.TryChainCore(remainingMilliseconds, lossBudget, requireImprovement: false);
            if (next == null)
                break;

            // кумулятивний ліміт: відкидаємо хід, що вивів за бюджет
            if (next.currentEstimation < startScore - lossBudget)
                break;

            current = next;
        }

        return ReferenceEquals(current, this) ? null : current;
    }

    private Individual? TryChainCore(int maxMilliseconds, int lossBudget, bool requireImprovement)
    {
        var stopwatch = Stopwatch.StartNew();
        var candidate = CloneHelper.clone(this);
        candidate.currentEstimation = currentEstimation;
        var before = currentEstimation;
        if (!candidate.isInit)
            candidate.PreparePrivateGeneticCache();

        var seriesFirsts = candidate.FirstTrackers
            .Where(x => x.IsAssigned && x.IsFirstTrackerInSeries && x.SeriesId.HasValue && !x.IsLowDaysDanger)
            .GroupBy(x => x.SeriesId)
            .Select(g => g.First())
            .ToList();
        if (seriesFirsts.Count == 0)
            return null;

        var a = seriesFirsts[candidate._random.Next(seriesFirsts.Count)];
        var aTeacher = a.ScheduleSlot.GroupSubject.Teacher.Id;
        var aGroupIds = a.ScheduleSlot.GroupSubject.Groups.Select(g => g.Id).ToHashSet();

        var aOriginal = CacheSeries(candidate, a.SeriesId!.Value);
        var aTrackers = aOriginal.Select(x => x.Tracker).ToList();

        // Крок 1: знімаємо A і ставимо у випадковий валідний домен
        foreach (var tracker in aTrackers)
            candidate.SetSlotUnAssigned(tracker);
        var aDomains = a.AvailableDomains.ToList();
        Shuffle(candidate._random, aDomains);
        var aPlaced = false;
        foreach (var domain in aDomains)
        {
            if (stopwatch.ElapsedMilliseconds >= maxMilliseconds)
                break;
            if (!candidate.ValidateAssignment(a, domain, candidate.GetAssignedSlots()))
                continue;

            candidate.SetSlotAssignedGenetic(a, domain);
            if (candidate.ApplySynchronizedDomainPatternGenetic(a, candidate.GetAssignedSlots()))
            {
                aPlaced = true;
                break;
            }

            foreach (var tracker in aTrackers.Where(x => x.IsAssigned))
                candidate.SetSlotUnAssigned(tracker);
        }

        if (!aPlaced)
        {
            RestoreSeries(candidate, aOriginal);
            return null;
        }

        // Крок 2: серія B (спільний викладач або група) стає на звільнене
        // місце A (дата+пара першого слота), далі свій синхронізований патерн
        var vacancy = new DomainValue
        {
            Date = aOriginal[0].Domain.Date,
            PairNumber = aOriginal[0].Domain.PairNumber
        };
        var bCandidates = seriesFirsts
            .Where(x => x.SeriesId != a.SeriesId)
            .Where(x => x.ScheduleSlot.GroupSubject.Teacher.Id == aTeacher ||
                        x.ScheduleSlot.GroupSubject.Groups.Any(g => aGroupIds.Contains(g.Id)))
            .ToList();
        Shuffle(candidate._random, bCandidates);

        foreach (var b in bCandidates.Take(3))
        {
            if (stopwatch.ElapsedMilliseconds >= maxMilliseconds)
                break;

            var bOriginal = CacheSeries(candidate, b.SeriesId!.Value);
            var bTrackers = bOriginal.Select(x => x.Tracker).ToList();
            foreach (var tracker in bTrackers)
                candidate.SetSlotUnAssigned(tracker);

            var bPlaced = false;
            if (candidate.ValidateAssignment(b, vacancy, candidate.GetAssignedSlots()))
            {
                candidate.SetSlotAssignedGenetic(b, vacancy);
                if (candidate.ApplySynchronizedDomainPatternGenetic(b, candidate.GetAssignedSlots()))
                    bPlaced = true;
                else
                    foreach (var tracker in bTrackers.Where(x => x.IsAssigned))
                        candidate.SetSlotUnAssigned(tracker);
            }

            if (!bPlaced)
            {
                RestoreSeries(candidate, bOriginal);
                continue;
            }

            var res = candidate.Estimate();
            var acceptable = requireImprovement
                ? res > before
                : res >= before - lossBudget;
            if (acceptable && candidate.Slots.Values.All(x => x.IsAssigned))
            {
                candidate.currentEstimation = res;
                return candidate;
            }

            // B на місці A не дав покращення — відновлюємо B і пробуємо наступного
            RestoreSeries(candidate, bOriginal);
        }

        // perturb-режим: A-без-B теж прийнятний kick (менше збурення)
        if (!requireImprovement)
        {
            var resA = candidate.Estimate();
            if (resA >= before - lossBudget && candidate.Slots.Values.All(x => x.IsAssigned))
            {
                candidate.currentEstimation = resA;
                return candidate;
            }
        }

        // жоден B не дав покращення — повертаємо A на місце
        RestoreSeries(candidate, aOriginal);
        candidate.currentEstimation = before;
        return null;
    }

    private static List<(SlotTracker Tracker, DomainValue Domain, ClassroomDTO? Classroom)> CacheSeries(
        Individual individual, int seriesId)
    {
        return individual.Slots.Values
            .Where(t => t.SeriesId == seriesId)
            .OrderBy(t => t.ScheduleSlot.LessonNumber)
            .Select(t => (t, new DomainValue { Date = t.ScheduleSlot.Date, PairNumber = t.ScheduleSlot.PairNumber }, t.ScheduleSlot.Classroom))
            .ToList();
    }

    private static void RestoreSeries(
        Individual individual,
        List<(SlotTracker Tracker, DomainValue Domain, ClassroomDTO? Classroom)> original)
    {
        foreach (var (tracker, domain, classroom) in original)
        {
            if (tracker.IsAssigned)
                individual.SetSlotUnAssigned(tracker);
            tracker.ScheduleSlot.Classroom = classroom;
            individual.SetSlotAssignedGenetic(tracker, domain);
        }
    }

    /// <summary>
    /// Гард транзієнтних сканів: придкує VerifyAll cheap-двигуна на час
    /// застосувань/відкатів пробних станів (док. §7: верифікація транзитів
    /// перманентно вимикає правила → fallback-шторм).
    /// </summary>
    private sealed class ScanVerifyGuard : IDisposable
    {
        private readonly Services.CheapEval.CheapEvaluationEngine _engine;

        public ScanVerifyGuard(Services.CheapEval.CheapEvaluationEngine engine)
        {
            _engine = engine;
            _engine.BeginTransientScan();
        }

        public void Dispose() => _engine.EndTransientScan();
    }

    private static void Shuffle<T>(Random random, List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private Individual? TryDestroyRepairOnce(int seriesCount, int maxMilliseconds, bool randomized = false)
    {
        var stopwatch = Stopwatch.StartNew();
        var candidate = this.clone();
        // проміжні стани ремонту — транзієнтні; верифікація тільки після
        using var scanGuard = candidate._cheapEngine is null
            ? null
            : new ScanVerifyGuard(candidate._cheapEngine);
        // таргетинг тут ШКОДИТЬ (20260831-045409): pressure-order знаходить
        // ЗАТИСКУВАНІ серії (найкращі кандидати на reshuffle), гарячі —
        // гарячі через структуру, repair їх просто повертає
        var pressuredSeries = candidate.FirstTrackers
            .Where(x => x.IsAssigned && x.SeriesId.HasValue)
            .OrderBy(x => x.AvailableDomains.Count)
            .ThenByDescending(x => x.ScheduleSlot.LessonSeriesLength)
            .Take(Math.Max(8, seriesCount * 8))
            .ToList();
        if (pressuredSeries.Count == 0)
            return null;

        var requestedSeriesCount = Math.Max(1, seriesCount);
        var anchor = pressuredSeries[candidate._random.Next(pressuredSeries.Count)];
        var anchorGroupIds = anchor.ScheduleSlot.GroupSubject.Groups
            .Select(group => group.Id)
            .ToHashSet();
        var relatedSeries = pressuredSeries
            .Where(x => x.SeriesId != anchor.SeriesId)
            .Where(x => x.ScheduleSlot.GroupSubject.Teacher.Id == anchor.ScheduleSlot.GroupSubject.Teacher.Id ||
                        x.ScheduleSlot.GroupSubject.Groups.Any(group => anchorGroupIds.Contains(group.Id)))
            .OrderBy(_ => candidate._random.Next())
            .ToList();
        var series = new[] { anchor }
            .Concat(relatedSeries)
            .Concat(pressuredSeries
                .Where(x => x.SeriesId != anchor.SeriesId &&
                            relatedSeries.All(related => related.SeriesId != x.SeriesId))
                .OrderBy(_ => candidate._random.Next()))
            .Take(requestedSeriesCount)
            .ToList();

        foreach (var firstTracker in series)
        {
            if (stopwatch.ElapsedMilliseconds >= maxMilliseconds)
                return null;

            var seriesTrackers = candidate.FirstTrackers
                .Where(x => x.SeriesId == firstTracker.SeriesId)
                .ToList();
            foreach (var tracker in seriesTrackers)
                candidate.SetSlotUnAssigned(tracker);

            var assignedSlots = candidate.GetAssignedSlots();
            if (!candidate.TryAssignSeries(firstTracker, seriesTrackers, assignedSlots, stopwatch, maxMilliseconds, randomized))
                return null;
        }

        candidate.currentEstimation = candidate.Estimate();
        return candidate;
    }

    private bool TryAssignSeries(
        SlotTracker firstTracker,
        List<SlotTracker> seriesTrackers,
        AssignedSlotsDTO assignedSlots,
        Stopwatch stopwatch,
        int maxMilliseconds,
        bool randomized = false)
    {
        var domains = firstTracker.AvailableDomains.ToList();
        if (randomized)
        {
            // перший валідний домен буде випадковим, а не першим за порядком
            // дат — так серія потрапляє в інший басейн
            Shuffle(_random, domains);
        }

        foreach (var domain in domains)
        {
            if (stopwatch.ElapsedMilliseconds >= maxMilliseconds)
                return false;

            if (!ValidateAssignment(firstTracker, domain, assignedSlots))
                continue;

            SetSlotAssignedGenetic(firstTracker, domain);
            if (ApplySynchronizedDomainPatternGenetic(firstTracker, assignedSlots))
                return true;

            foreach (var tracker in seriesTrackers.Where(x => x.IsAssigned))
                SetSlotUnAssigned(tracker);
        }

        return false;
    }
    
    #region Необхідні нам дефолтні методи
    
    /// <summary>
    /// Forward checking: оновлює домени для всіх незаповнених слотів, видаляючи кандидати,
    /// які вже не відповідають констрейнтам. Вилучені значення записуються в RejectedDomains для поточного кроку.
    /// </summary>
    /// <param name="assignedSlot">Слот, для якого зроблено останнє призначення.</param>
    /// <param name="currentStep">Поточний крок пошуку.</param>
    /// <returns>True, якщо для всіх слотів залишається хоча б один кандидат; інакше false.</returns>
    private bool ForwardCheck(SlotTracker assignedSlot, int currentStep)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return ForwardCheckInner(assignedSlot, currentStep);
        }
        finally
        {
            TicksForwardCheck += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        }
    }

    private bool ForwardCheckInner(SlotTracker assignedSlot, int currentStep)
    {
        var changedSlots = assignedSlot.ScheduleSlot.GroupSubject.ScheduleSlots.Select(x => Slots[x]).Where(x => x.IsAssigned && x.AssignStep == currentStep).ToList();

        HashSet<SlotTracker> forwardSlots = new HashSet<SlotTracker>(teacherSlots[assignedSlot.ScheduleSlot.GroupSubject.Teacher.Id].Where(s => !s.IsAssigned && s.IsFirstTrackerInSeries));
        foreach (var grId in assignedSlot.ScheduleSlot.GroupSubject.Groups.Select(g => g.Id))
        foreach (var sl in groupsSlots[grId].Where(s => !s.IsAssigned && s.IsFirstTrackerInSeries))
            forwardSlots.Add(sl);

        foreach (var slot in forwardSlots)
        {
            // Зберігаємо поточний список доступних доменів.
            var originalDomains = new List<DomainValue>(slot.AvailableDomains);

            // Оновлюємо домени: залишаємо лише ті, що відповідають обмеженням.
            // Заміна посилання (а не мутація) — старий сет лишається спільним
            // для індивідуумів-сиблінгів, новий ексклюзивний для цього слота.
            slot.AvailableDomains = new SortedSet<DomainValue>(slot.AvailableDomains
                    .Where(candidate => Validators.ValidateAssignmentArc(slot, candidate, changedSlots)))
                ;
            slot.DomainsOwned = true;

            // Визначаємо, які доменні значення було вилучено.
            var removed = originalDomains.Except(slot.AvailableDomains).ToList();
            if (removed.Any())
            {
                slot.AddRejectedDomains(currentStep, removed);
                // slotsByStep[currentStep].Add(slot);
            }

            // Якщо домен став порожнім, повертаємо false.
            if (!slot.AvailableDomains.Any())
            {
                return false;
            }
        }
        return true;
    }
                
    /// <summary>
    /// Відміна призначення слоту. Оновлюємо кеші.
    /// </summary>
    /// <param name="slot"></param>
    private void SetSlotUnAssigned(SlotTracker slot)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            SetSlotUnAssignedInner(slot);
        }
        finally
        {
            TicksUnassign += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        }
    }

    private void SetSlotUnAssignedInner(SlotTracker slot)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        System.Threading.Interlocked.Increment(ref UnassignCalls);
        if (slot.IsAssigned)
        {
            if (assignedSlotsByTeacherDate.TryGetValue(slot.ScheduleSlot.GroupSubject.Teacher.Id, out var teacherDates))
            {
                foreach (var slots in teacherDates.Values)
                    slots.Remove(slot);
            }

            foreach (var group in slot.ScheduleSlot.GroupSubject.Groups)
            {
                if (!assignedSlotsByGroupDate.TryGetValue(group.Id, out var groupDates))
                    continue;

                foreach (var slots in groupDates.Values)
                    slots.Remove(slot);
            }
            TicksUnassignDicts += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            var t1 = System.Diagnostics.Stopwatch.GetTimestamp();

            if (slot.ScheduleSlot.Classroom != null)
            {
                foreach (var date in assignedClassrooms.Values)
                foreach (var pair in date.Values)
                {
                    var classroomKeys = pair
                        .Where(x => ReferenceEquals(x.Value, slot.ScheduleSlot))
                        .Select(x => x.Key)
                        .ToList();
                    foreach (var classroom in classroomKeys)
                        pair.Remove(classroom);
                }
            }

            // Очищаємо аудиторію
            slot.ScheduleSlot.Classroom = null;
            TicksUnassignClassrooms += System.Diagnostics.Stopwatch.GetTimestamp() - t1;
        }

        // FLAT-дзеркало: зняти біти з позиції, де вони були встановлені
        var t2 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (flatBits is { Enabled: true })
            flatBits.NoteUnassigned(slot);

        slot.IsAssigned = false;
        TicksUnassignMirror += System.Diagnostics.Stopwatch.GetTimestamp() - t2;

    }

    /// <summary>
    /// Перевірка призначення для слоту в контексті вже призначених слотів.
    /// </summary>
    /// <param name="slotTracker"></param>
    /// <param name="domain"></param>
    /// <param name="assignedSLots"></param>
    /// <returns></returns>
    private bool ValidateAssignment(SlotTracker slotTracker, DomainValue domain, IAssignedSlots assignedSLots)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return ValidateAssignmentInner(slotTracker, domain, assignedSLots);
        }
        finally
        {
            TicksValidate += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        }
    }

    private bool ValidateAssignmentInner(SlotTracker slotTracker, DomainValue domain, IAssignedSlots assignedSLots)
    {
        slotTracker.SetDomainRaw(domain.Date, domain.PairNumber);

        var slotAdapter = GetAdapter(slotTracker.ScheduleSlot);

        // FLAT-фаза 1a: ТЕЛЕМЕТРІЯ — диференціал бітсет-відповіді (TRUE-стан)
        // проти словникової відповіді ( membérship × жива пара). НУЛЬ змін
        // поведінки: і бітсет, і словник обчислюються, порівнюються, лічильники.
        if (flatBits == null && Parameters.FlatIndex)
            EnsureFlat();
        if (flatBits is { Enabled: true })
        {
            System.Threading.Interlocked.Increment(ref Services.Calc.FlatBitsets.DiffChecks);
            var bitBusy = flatBits.TeacherBusyAt(slotTracker);
            var dictBusy = assignedSLots
                .GetSlotsByTeacherAndDate(slotTracker.ScheduleSlot.GroupSubject.Teacher.Id, slotTracker.ScheduleSlot.Date.Date)
                .Any(s => s.PairNumber == slotTracker.ScheduleSlot.PairNumber);
            if (bitBusy != dictBusy)
            {
                System.Threading.Interlocked.Increment(ref Services.Calc.FlatBitsets.DiffMismatch);
                if (DiffMismatchLog++ < 5)
                {
                    logger?.LogWarning("FLAT-DIFF: bit={Bit} dict={Dict} teacher={T} slot={D}/{P}",
                        bitBusy, dictBusy, slotTracker.ScheduleSlot.GroupSubject.Teacher.Id,
                        slotTracker.ScheduleSlot.Date.Date, slotTracker.ScheduleSlot.PairNumber);
                    // діагностика: ХТО сидить у словнику під цією датою?
                    foreach (var s in assignedSLots
                        .GetSlotsByTeacherAndDate(slotTracker.ScheduleSlot.GroupSubject.Teacher.Id, slotTracker.ScheduleSlot.Date.Date)
                        .Where(s => s.PairNumber == slotTracker.ScheduleSlot.PairNumber))
                    {
                        var st = Slots[s];
                        logger?.LogWarning("FLAT-DIFF-OFFENDER: assigned={A} liveDate={LD} livePair={LP} dictDate={DD} series={S} flatCode={FC}",
                            st.IsAssigned, st.ScheduleSlot.Date.Date, st.ScheduleSlot.PairNumber,
                            slotTracker.ScheduleSlot.Date.Date, st.SeriesId, st.FlatCode);
                    }
                }
            }
        }

        // FLAT-фаза 1b: PRECHECK — вільна позиція (вчитель вільний + усі групи
        // вільні) ⇒ StandartValidation гарантовано true (вчителева перевірка —
        // чиста екзистенція; групова — цикл по порожній множині). Кожен 64-й
        // вільний кейс все одно прогоняє StandartValidation (shadow: ловить
        // гіпотетичну розбіжність і вимикає precheck). Зайняті кейси (вчитель
        // біт стоїть) — exact, але для безпеки падають у повний шлях
        // (підгрупові винятки в групах все одно вимагають повного прогону).
        var skipStandart = false;
        if (flatBits is { Enabled: true } && Parameters.FlatIndexPrecheck)
        {
            var teacherBusy = flatBits.TeacherBusyAt(slotTracker);
            if (teacherBusy)
            {
                System.Threading.Interlocked.Increment(ref Services.Calc.FlatBitsets.PrecheckTeacherBusy);
                skipStandart = false; // повний шлях (exact безпечно)
            }
            else if (!flatBits.AnyGroupBusyAt(slotTracker))
            {
                // вільно: StandartValidation гарантовано true — пропустити,
                // крім кожного 64-го (shadow-прогін для моніторингу)
                if (System.Threading.Interlocked.Increment(ref FlatShadowTick) % 64 == 0)
                {
                    if (!Validators.StandartValidation(slotAdapter, assignedSLots))
                    {
                        System.Threading.Interlocked.Increment(ref Services.Calc.FlatBitsets.ShadowFailed);
                        logger?.LogWarning("FLAT-SHADOW-FAIL: precheck сказав 'вільно', StandartValidation відхилив — precheck вимкнено");
                        flatBits.Disable("shadow-fail");
                    }
                    else
                        System.Threading.Interlocked.Increment(ref Services.Calc.FlatBitsets.PrecheckShadowOk);
                }
                else
                {
                    skipStandart = true;
                    System.Threading.Interlocked.Increment(ref Services.Calc.FlatBitsets.PrecheckSkipped);
                }
            }
            else
                System.Threading.Interlocked.Increment(ref Services.Calc.FlatBitsets.PrecheckGroupBusy);
        }

        var standartValidation = !skipStandart && Validators.StandartValidation(slotAdapter, assignedSLots);
        if (!standartValidation)
            return false;

        // // Перевірка на аудиторії, якщо алгоритм має враховувати їх
        // if (!ignoreClassrooms && !slotTracker.ScheduleSlot.GroupSubject.Subject.NoClassroom)
        // {
        //     var classroomValidation = ValidateAndSelectClassroom(slotTracker.ScheduleSlot, domain, assignedSLots);
        //     if (!classroomValidation)
        //         return false;
        // }

        foreach (var validator in UserFunctions.SlotValidators)
        {
            var userValidation = validator.Validate(slotAdapter, assignedSLots);
            if (!userValidation)
                return false;
        }

        return true;
    }
    
    /// <summary>
    /// Отримання адаптера слоту за слотом.
    /// </summary>
    /// <param name="slot">Слот, для якого потрібно отримати адаптер.</param>
    /// <returns>Адаптер слоту.</returns>
    /// <exception cref="KeyNotFoundException">Виникає, якщо адаптер для слоту не знайдено.</exception>
    public IScheduleSlot GetAdapter(ScheduleSlotDTO slot)
    {
        return slot;
    }
    
    /// <summary>
    /// Отримання списку призначених слотів.
    /// </summary>
    /// <returns></returns>
    private AssignedSlotsDTO GetAssignedSlots()
    {

        var res = new AssignedSlotsDTO(
            slotFactory: () => Slots.Values.Where(s => s.IsAssigned).Select(s => s.ScheduleSlot),
            slotsByTeacherFactory: getAssignedByTeacher,
            slotsByGroupFactory: getAssignedByGroup,
            slotsByTeacherAndDateFactory: getAssignedByTeacherAndDate,
            slotsByGroupAndDateFactory: getAssignedByGroupAndDate
        );

        return res;
    }
    private IEnumerable<IScheduleSlot> getAssignedByGroupAndDate(long groupId, DateTime date)
    {
        IEnumerable<IScheduleSlot> res = assignedSlotsByGroupDate.ContainsKey(groupId) && assignedSlotsByGroupDate[groupId].ContainsKey(date)
            ? assignedSlotsByGroupDate[groupId][date].Select(s => s.ScheduleSlot)
            : new List<IScheduleSlot>();
        return res;
    }
    private IEnumerable<IScheduleSlot> getAssignedByTeacherAndDate(long teacherId, DateTime date)
    {
        IEnumerable<IScheduleSlot> res = assignedSlotsByTeacherDate.ContainsKey(teacherId) && assignedSlotsByTeacherDate[teacherId].ContainsKey(date)
            ? assignedSlotsByTeacherDate[teacherId][date].Select(s => s.ScheduleSlot)
            : new List<IScheduleSlot>();
        return res;
    }
    private IEnumerable<IScheduleSlot> getAssignedByGroup(long groupId)
    {
        IEnumerable<IScheduleSlot> res2 = assignedSlotsByGroupDate.ContainsKey(groupId)
            ? assignedSlotsByGroupDate[groupId].Values.SelectMany(x => x.Select(s => s.ScheduleSlot as IScheduleSlot))
            : new List<IScheduleSlot>();
        return res2;
    }
    private IEnumerable<IScheduleSlot> getAssignedByTeacher(long teacherId)
    {
        List<IScheduleSlot> res2 = assignedSlotsByTeacherDate.ContainsKey(teacherId)
            ? assignedSlotsByTeacherDate[teacherId].Values.SelectMany(x => x.Select(s => s.ScheduleSlot as IScheduleSlot)).ToList()
            : new List<IScheduleSlot>();
        return res2;
    }
        
    #endregion
}
