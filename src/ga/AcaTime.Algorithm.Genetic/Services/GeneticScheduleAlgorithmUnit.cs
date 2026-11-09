using AcaTime.Algorithm.Genetic.Models;
using AcaTime.Algorithm.Genetic.Services.Calc;
using AcaTime.Algorithm.Genetic.Utils;
using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScheduleCommon.Models.Constraints;
using AcaTime.ScheduleCommon.Utils;
using AcaTime.ScriptModels;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Design.Serialization;
using System.Net.Security;
using System.Runtime.CompilerServices;
using AcaTime.Algorithm.Genetic.Models.Genetic;


using AcaTime.Algorithm.Genetic.Services.Operations;
namespace AcaTime.Algorithm.Genetic.Services;

/// <summary>
/// Клас для реалізації алгоритму розкладу.
/// </summary>
public class GeneticScheduleAlgorithmUnit
{
    public IReadOnlyList<ScheduleDeltaEvent> AcceptedDeltaEvents { get; private set; } = Array.Empty<ScheduleDeltaEvent>();
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
    private CancellationToken cancelToken;

    // public bool ignoreClassrooms { get; private set; }
    public Dictionary<IScheduleSlot, SlotTracker> Slots { get; internal set; }

    // для заміру часу виконання
    // public DebugData DebugData { get; set; } = new DebugData("none");

    // додатковий кеш для прискорення деяких функцій, клонується в Clone
    internal Dictionary<long, List<SlotTracker>> teacherSlots;
    internal Dictionary<long, List<SlotTracker>> groupsSlots;
    internal List<SlotTracker> FirstTrackers;

    // приватний кеш
    private Dictionary<int, List<SlotTracker>> slotsByStep = new Dictionary<int, List<SlotTracker>>(); // для зберігання слотів по крокам
    internal Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>> assignedSlotsByTeacherDate = new Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>>();
    internal Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>> assignedSlotsByGroupDate = new Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>>();
    // private HashSet<SlotTracker> unassignedFirstSlots;
    private Dictionary<long, List<SlotTracker>> firstSlotsByGroupSubjects;
    private Dictionary<DateTime, Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>>> assignedClassrooms = new Dictionary<DateTime, Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>>>();
    
    
    public AlgorithmResultDTO initialResult = null; // todo можливо переназвати на просто Result...

    /// <summary>
    /// Налаштування алгоритму
    /// </summary>
    /// <param name="root"></param>
    /// <param name="logger"></param>
    /// <param name="userFunctions"></param>
    /// <param name="parameters"></param>
    public void Setup(FacultySeasonDTO root, ILogger logger, UserFunctions userFunctions, AlgorithmParams parameters)
    {
        Root = root;
        this.logger = logger;
        UserFunctions = userFunctions;
        Parameters = parameters;
        _operations ??= new SeriesOperations(this);
    }

    /// <summary>
    /// Materializes a compact genome into this solver state and rebuilds all
    /// indexes used by validation and genetic mutations.
    /// </summary>
        public int ApplyGenome(ScheduleGenome genome, bool applyClassrooms = true)
    {
        ArgumentNullException.ThrowIfNull(genome);

        assignedSlotsByTeacherDate.Clear();
        assignedSlotsByGroupDate.Clear();
        if (applyClassrooms)
            assignedClassrooms.Clear();
        slotsByStep.Clear();

        foreach (var tracker in Slots.Values)
        {
            tracker.IsAssigned = false;
            if (applyClassrooms)
                tracker.ScheduleSlot.Classroom = null;
        }

        var trackers = Slots.Values
            .ToDictionary(x => SlotGeneKey.From(x.ScheduleSlot), x => x);
        var applied = 0;

        foreach (var pair in genome.Genes)
        {
            if (!trackers.TryGetValue(pair.Key, out var tracker))
                continue;

            if (applyClassrooms)
            {
                tracker.ScheduleSlot.Classroom = pair.Value.ClassroomId.HasValue
                    ? Root.Classrooms.FirstOrDefault(x => x.Id == pair.Value.ClassroomId.Value)
                    : null;
            }

            SetSlotAssignedGenetic(tracker, new DomainValue
            {
                Date = pair.Value.Date,
                PairNumber = pair.Value.PairNumber
            });
            applied++;
        }

        if (applied != genome.Count)
            throw new InvalidOperationException($"Genome містить {genome.Count} генів, але матеріалізовано лише {applied}");

        isInit = false;
            return applied;
        }

        public bool TryApplyHgt(IReadOnlyList<ScheduleGenome> donors, int attempts, out int acceptedScore)
        {
            acceptedScore = Estimate();
            if (donors.Count == 0 || attempts <= 0)
                return false;

            var recipient = ScheduleGenome.FromSlots(
                Slots.Values.Where(x => x.IsAssigned).Select(x => x.ScheduleSlot), acceptedScore);
            var bestScore = acceptedScore;

            foreach (var donor in donors
                         .Where(x => !ReferenceEquals(x, recipient))
                         .OrderByDescending(x => x.Fitness ?? int.MinValue)
                         .Take(attempts))
            {
                foreach (var groupSubjectId in donor.Genes.Keys
                             .Select(x => x.GroupSubjectId)
                             .Distinct())
                {
                    var candidate = recipient.Clone();
                    candidate.TransferGroupSubjectFrom(donor, groupSubjectId).Commit();

                    ApplyGenome(candidate, applyClassrooms: false);
                    if (!TryRepairGroupSubjects([groupSubjectId]))
                    {
                        ApplyGenome(recipient, applyClassrooms: false);
                        continue;
                    }

                    var score = Estimate();
                    if (score > bestScore)
                    {
                        bestScore = score;
                        recipient = ScheduleGenome.FromSlots(
                            Slots.Values.Where(x => x.IsAssigned).Select(x => x.ScheduleSlot), score);
                    }
                    else
                    {
                        // HGT follows the same monotonic accept/reject rule as mutations.
                        ApplyGenome(recipient, applyClassrooms: false);
                    }
                }
            }

            if (bestScore == acceptedScore)
                return false;

            acceptedScore = bestScore;
            return true;
        }

    /// <summary>
    /// Reassigns transferred group-subject blocks only when their current
    /// placement violates a hard or user constraint.
    /// </summary>
    public bool TryRepairGroupSubjects(IEnumerable<long> groupSubjectIds)
    {
        ArgumentNullException.ThrowIfNull(groupSubjectIds);

        foreach (var groupSubjectId in groupSubjectIds.Distinct())
        {
            var trackers = Slots.Values
                .Where(x => x.IsAssigned && x.ScheduleSlot.GroupSubject.Id == groupSubjectId)
                .ToList();
            if (trackers.Count == 0 || !GroupSubjectNeedsRepair(trackers))
                continue;

            foreach (var tracker in trackers)
                SetSlotUnAssigned(tracker, clearClassroom: false);

            var series = trackers
                .Where(x => x.IsFirstTrackerInSeries)
                .GroupBy(x => x.SeriesId)
                .Select(x => x.First())
                .OrderBy(x => x.AvailableDomains.Count)
                .ToList();

            foreach (var firstTracker in series)
            {
                var seriesTrackers = trackers
                    .Where(x => x.SeriesId == firstTracker.SeriesId)
                    .ToList();
                if (!TryRepairSeries(firstTracker, seriesTrackers))
                    return false;
            }
        }

        return true;
    }

    private bool GroupSubjectNeedsRepair(List<SlotTracker> trackers)
    {
        foreach (var tracker in trackers)
        {
            var domain = new DomainValue
            {
                Date = tracker.ScheduleSlot.Date,
                PairNumber = tracker.ScheduleSlot.PairNumber
            };

            SetSlotUnAssigned(tracker, clearClassroom: false);
            var valid = ValidateAssignment(tracker, domain, GetAssignedSlots());
            SetSlotAssignedGenetic(tracker, domain);
            if (!valid)
                return true;
        }

        return false;
    }

    private bool TryRepairSeries(SlotTracker firstTracker, List<SlotTracker> seriesTrackers)
    {
        var assignedSlots = GetAssignedSlots();
        foreach (var candidate in firstTracker.AvailableDomains.ToList())
        {
            if (!ValidateAssignment(firstTracker, candidate, assignedSlots))
                continue;

            SetSlotAssignedGenetic(firstTracker, candidate);
            if (ApplySynchronizedDomainPatternGenetic(firstTracker, assignedSlots))
                return true;

            foreach (var tracker in seriesTrackers.Where(x => x.IsAssigned))
                SetSlotUnAssigned(tracker, clearClassroom: false);
        }

        return false;
    }

    #region Генетичний алгоритм
        
        private bool isInit;
        private readonly Random _random = new();
        private IGeneticOperations _operations;
        private readonly double[] operationRewards = new double[13];
        private readonly int[] operationAttempts = new int[13];
        private readonly long[] operationMilliseconds = new long[13];
        
        private void PreparePrivateGeneticCache()
        {
            firstSlotsByGroupSubjects = FirstTrackers
                .Where(x => x.IsAssigned && x.IsFirstTrackerInSeries) // Оскільки працюємо з вже розподіленими, беремо IsAssigned
                .GroupBy(s => s.ScheduleSlot.GroupSubject.Id)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.SeriesId).ToList());

            // unassignedFirstSlots = firstSlotsByGroupSubjects.Values.Select(x => x.First())
            //     .ToHashSet();
            // можливо треба буде також призначати і
            // assignedSlotsByTeacherDate
            // assignedSlotsByGroupDate
            isInit = true;
        }
        
        // todo Коли розберемось з відновленням до попереднього стану у випадку невдалої мутації,
        // todo створити окремий клас та перенести туди.
        // public async Task<SecondScheduleAlgorithmUnit> RunMutations(CancellationToken token, bool ignoreClassrooms, int prevEstimation)
        // {
        //     cancelToken = token;
        //     this.ignoreClassrooms = ignoreClassrooms;
        //     DebugData = new DebugData(algorithmName, true);
        //
        //     Mutations(prevEstimation);
        //
        //     return this;
        // }

        private int strategyChangeIterationCounter = 0;
        private int strategyChangeResultCounter = 0;
        private int strategy = 0;

        private void SelectStrategy(int resultShift)
        {
            strategyChangeIterationCounter++;
            strategyChangeResultCounter += resultShift;

            if(strategyChangeIterationCounter < 3)
                return;
            if (strategyChangeResultCounter > 1000)
            {
                strategyChangeIterationCounter = 0;
                strategyChangeResultCounter = 0;
                return;
            }

            if (strategy >= 3)
                strategy = 0;
            else
                strategy++;
            strategyChangeIterationCounter = 0;
            strategyChangeResultCounter = 0;
        }

        private void GeneticAlgorithm(int iteration, Individual individual, IReadOnlyList<ScheduleGenome>? hgtDonors, List<Individual>? output = null)
        {
            var beforeGenome = ScheduleGenome.FromSlots(
                individual.Slots.Values.Where(x => x.IsAssigned).Select(x => x.ScheduleSlot),
                individual.currentEstimation);
            if (hgtDonors is { Count: > 0 } && iteration % 10 == 0)
            {
                var before = individual.Estimate();
                if (individual.TryApplyHgt(hgtDonors, Parameters.HgtAttempts, out var after))
                    logger.LogInformation($"HGT: прийнято {before} -> {after}");
                return;
            }

            var operation = Parameters.OnlyOperation >= 0
                ? Parameters.OnlyOperation
                : string.Equals(Parameters.Adapter, "itc", StringComparison.OrdinalIgnoreCase)
                    ? 9 + (iteration % (3 + Math.Max(0, Parameters.ExtraItcOperators - 3))) // ITC portfolio; 0/3 preserves 9..11
                    : Parameters.AdaptiveOperationPortfolio
                        ? SelectOperation()
                        : iteration % (9 + Parameters.ExtraItcOperators);
            // паралельні лінії: потомки йдуть у ВЛАСНИЙ список лінії (не в
            // спільне newGeneration — race); sequential шлях лишає field
            output ??= newGeneration;
            var generationStart = output.Count;
            var scoreBeforeOperation = individual.currentEstimation;
            var opStopwatch = System.Diagnostics.Stopwatch.StartNew();

            _operations.Run(operation, individual, output);

            opStopwatch.Stop();
            var scoreAfterOperation = output
                .Skip(generationStart)
                .Select(x => x.currentEstimation)
                .DefaultIfEmpty(scoreBeforeOperation)
                .Max();
            // Слот 7 має ВЛАСНУ статистику (масиви 9) — ремап 7→6 був легаси
            // 7-слотових масивів і ЛАМАВ warm-start SelectOperation:
            // attempts[7] ніколи не інкрементувався → warm-start повертав op 7
            // вічно, і op 8 (hotspot) не отримував жодного прогону
            // (монокультура chain у 114217/121008).
            var trackedOperation = operation;
            operationAttempts[trackedOperation]++;
            operationMilliseconds[trackedOperation] += opStopwatch.ElapsedMilliseconds;
            operationRewards[trackedOperation] += Math.Max(0, scoreAfterOperation - scoreBeforeOperation);
            logger.LogInformation($"OP:{trackedOperation} time:{opStopwatch.ElapsedMilliseconds}ms reward:{Math.Max(0, scoreAfterOperation - scoreBeforeOperation)} attempts:{operationAttempts[trackedOperation]} totalReward:{operationRewards[trackedOperation]}");

            foreach (var offspring in output)
            {
                var afterGenome = ScheduleGenome.FromSlots(
                    offspring.Slots.Values.Where(x => x.IsAssigned).Select(x => x.ScheduleSlot),
                    offspring.currentEstimation);
                var delta = ScheduleDeltaEvent.FromDifference(beforeGenome, afterGenome, "GeneticOperation");
                if (delta != null)
                    offspring.AddDeltaEvent(delta);
            }
        }

        private int SelectOperation()
        {
            for (var operation = 0; operation < operationAttempts.Length; operation++)
            {
                if (operationAttempts[operation] == 0)
                    return operation;
            }

            var totalAttempts = operationAttempts.Sum();
            var totalRewards = operationRewards.Sum();

            // ПЕР-ВИКЛИК (не per-ms!): per-ms фармить дешеві крихти (OP1
            // 7724 виклики, +6030 у 122747) — chain's дорогі структурні перемоги
            // виглядають погано за мс. Mean-per-call = цінність за можливість;
            // частота викликів сама балансує стіну-годинник.
            var scale = Math.Max(1d, totalRewards / Math.Max(1, totalAttempts));

            var bestOperation = 0;
            var bestValue = double.NegativeInfinity;
            for (var operation = 0; operation < operationAttempts.Length; operation++)
            {
                var attempts = operationAttempts[operation];
                var meanReward = operationRewards[operation] / attempts;
                var exploration = 3d * Math.Sqrt(Math.Log(totalAttempts + 1d) / attempts);
                var value = meanReward / scale + exploration;
                if (value > bestValue)
                {
                    bestValue = value;
                    bestOperation = operation;
                }
            }

            return bestOperation;
        }

        public async Task<AlgorithmResultDTO> Run(
            CancellationToken token,
            bool ignoreClassrooms,
            IReadOnlyList<ScheduleGenome>? hgtDonors = null,
            bool kick = false,
            int? iterationsOverride = null)
        {
            cancelToken = token;
            PreparePrivateGeneticCache();
            populationLimitCount = Math.Max(1, Parameters.IntraBranchPopulationSize);
            
            // int baseEstimate = Estimate();
            // int prevEstimate = baseEstimate;
            
            // створити 1у популяцію
            var initialPopulation = this.CloneFromUnit();


            if (kick)
                initialPopulation.Kick(Parameters.KickSeriesCount);
            
            // IndividualMapper mapper = new IndividualMapper();
            // mapper.PrepareIndividual(initialPopulation);
            
            initialPopulation.currentEstimation = initialPopulation.Estimate();
            population.Add(initialPopulation);
            
            // A kick branch is intentionally allowed to start below the original
            // Default score; its result is compared with Default by the caller.
            int baseEstimate = initialPopulation.currentEstimation;
            int bestEstimate = baseEstimate;
            // best — знімок-клон, а не посилання: операції (наприклад
            // SwapTeacherSubjects) мулюють членів популяції на місці
            var best = initialPopulation.clone();
            best.currentEstimation = baseEstimate;
            
            var geneticIterations = iterationsOverride ?? Parameters.GeneticIterations;
            logger.LogInformation($"ПОЧАТОК ГЕН АЛГОРИТМУ. КІЛЬКІСТЬ ІТЕРАЦІЙ {geneticIterations}");
            var stagnation = 0;
            var kicks = 0;

            // ILS: стан пошуку може приймати втрати (kick у гірший басейн);
            // best лишається замороженим і завжди дає фінальний результат.
            var ilsActive = false;
            var ilsIterationsLeft = 0;
            var ilsStartScore = 0;
            var ilsEpisodes = 0;


            // GeneticOperations operations = new GeneticOperations();
            // operations.Setup(logger,initialPopulation);
            // for (var i = 0; i < 50; i++)
            // {
            //     operations.MakeOperation();
            //     foreach (var combinedIndividual in operations.population)
            //     {
            //         var currEst = combinedIndividual.currentEstimation;
            //         logger.LogInformation($"ПІСЛЯ МУТ. №{i} МАЄМО: {currEst} | АБО {currEst - prevEstimate} ВІД НАЙКРАЩОГО РЕЗУЛЬТАТУ");
            //     }
            //     // var currEstimate = Estimate();
            //     
            // }
            
            var operationIndex = 0;
            for (var i = 0; i < geneticIterations && !cancelToken.IsCancellationRequested; i++)
            {
                limit = 5;
                // if (i % 5 == 0)
                //     SwapTeacherSubjects();
                // else
                //     Mutations(prevEstimate);
                // var currEstimate = MutationsForLongSeries(prevEstimate);
                // Swap();

                // SwapTeacherSubjects();
                // робимо операції для кожної популяції
                newGeneration = new List<Individual>();

                // ПАРАЛЕЛЬНІ ЛІНІЇ (Stage 2): кожна лінія — свій потік зі
                // СВОЇМ списком потомків (wrappers пишуть у output); після
                // barrier — серійні sync-точки (селекція/HGT/refill).
                // Воркери: min(лінії, ParallelLineages | ProcessorCount-1).
                var workers = Math.Max(1, Math.Min(population.Count,
                    Parameters.ParallelLineages > 0
                        ? Parameters.ParallelLineages
                        : Math.Max(1, Environment.ProcessorCount - 1)));
                _parallelRun = workers > 1;

                if (_parallelRun)
                {
                    var opIndexLocal = new int[population.Count];
                    var localGens = new List<Individual>[population.Count];
                    // БЕЗ CancellationToken у ParallelOptions: токен, що
                    // спрацював посеред батчу, кидав OCE і ГЛОТАВ увесь
                    // результат рану (130502/130844). Лінії виходять
                    // граціозно: ops самі перевіряють IsCancellationRequested,
                    // а цикл виходить після поточного батчу.
                    Parallel.For(0, population.Count,
                        new ParallelOptions { MaxDegreeOfParallelism = workers },
                        j =>
                        {
                            localGens[j] = new List<Individual>();
                            for (var attempt = 0; attempt < Math.Max(1, Parameters.OperationAttemptsPerIteration); attempt++)
                            {
                                var tagStart = localGens[j].Count;
                                GeneticAlgorithm(opIndexLocal[j]++, population[j], hgtDonors, localGens[j]);
                                // потомок успадковує лінію батька (слоти липкі за лініями)
                                for (var k = tagStart; k < localGens[j].Count; k++)
                                    localGens[j][k].LineageTag = population[j].LineageTag;
                            }
                        });
                    foreach (var list in localGens)
                        newGeneration.AddRange(list);
                }
                else
                {
                    for (var j = 0; j < population.Count; j++)
                    {
                        for (var attempt = 0; attempt < Math.Max(1, Parameters.OperationAttemptsPerIteration); attempt++)
                        {
                            var tagStart = newGeneration.Count;
                            GeneticAlgorithm(operationIndex++, population[j], hgtDonors);
                            // потомок успадковує лінію батька (слоти пула липкі за лініями)
                            for (var k = tagStart; k < newGeneration.Count; k++)
                                newGeneration[k].LineageTag = population[j].LineageTag;
                        }
                    }
                }

                foreach (var newPopulation in newGeneration.Where(IsComplete))
                    population.Add(newPopulation);
                
                // далі робимо перевірку кількості популяцій прибираючі зайві
                EvaluatePopulations();
                if (!ilsActive)
                {
                    foreach (var population in population.ToList())
                    {
                        if (population.currentEstimation < baseEstimate)
                            this.population.Remove(population);
                    }

                    // Oперації мутують членів популяції на місці (swap-операції
                    // тримають гірший стан) — інкубатор може спорожніти
                    if (this.population.Count == 0)
                    {
                        var restored = best.clone();
                        restored.currentEstimation = bestEstimate;
                        this.population.Add(restored);
                    }
                }
                // раз в декілька операцій розмножимо дану мутацію
                // ГЕН-БЛОЧНИЙ HGT: лідер донує блок GroupSubject лягарду; гібрид
                // приймається якщо виживає лінію (>= baseEstimate). Гени, а не
                // покращення — delta-replay був зафальшивлений (0.0%).
                if (population.Count >= 2 &&
                    Parameters.HgtInterval > 0 &&
                    i % Parameters.HgtInterval == 0)
                    PopulationGeneHgt(population[0], population[1], baseEstimate);

                // Острови PHASE 1: клон лідера — лише для ДОПОВНЕННЯ пулу до
                // ліміту (заповідні слоти не витісняються свіжими близнюками —
                // інакше диверговані лінії гинуть щонайменше за 2 ітерації).
                if (populationLimitCount > 1 && i % 2 == 0 && population.Count < populationLimitCount)
                {
                    var cl = population[0].clone();
                    cl.currentEstimation = population[0].currentEstimation;
                    // близнюк-дослідник: інша лінія (дивергенція через власні ops)
                    cl.LineageTag = population[0].LineageTag == 0 ? 1 : 0;
                    // BIRTH-KICK ВІДХИЛЕНО (20260831-041821: +32852 vs +37096):
                    // kick коштує бюджету, і дослідник потім марнує свій на
                    // спуск додому — той самий single-funnel ландшафт.
                    population.Add(cl);
                }
                
                // створимо копію до того як щось змінили
                // var copy = this.CloneWithPrivateCache();

                // SwapGroupSubjects();
                // SwapTeacherSubjects();
                var currEstimate = population[0].currentEstimation;
                
                // if(currEstimate == 0)
                //     currEstimate = Int32.MinValue;
                // var currEstimate = Estimate();
                
                // а потім візьмемо цю копію і застосуємо її
                // todo створити клас "популяція" на основі всіх цих приватних кешів і так далі, і працювати виключно з нею, щоб можна було її множити  

                logger.LogInformation($"ПІСЛЯ МУТ. №{i} МАЄМО: {currEstimate} | АБО {currEstimate - bestEstimate} ВІД НАЙКРАЩОГО РЕЗУЛЬТАТУ");
                // SelectStrategy(currEstimate - prevEstimate);

                if(currEstimate > bestEstimate)
                {
                    bestEstimate = currEstimate;
                    best = population[0].clone();
                    best.currentEstimation = currEstimate;
                    stagnation = 0;
                    if (ilsActive)
                    {
                        ilsActive = false;
                        logger.LogInformation($"ILS: базис відновився вище best: {ilsStartScore} -> {currEstimate}");
                    }
                }
                else if (ilsActive && currEstimate >= bestEstimate)
                {
                    ilsActive = false;
                    stagnation = 0;
                    logger.LogInformation($"ILS: базис досяг рівня best ({currEstimate}) — продовжуємо звичайний пошук");
                }
                else
                {
                    stagnation++;
                }

                if (geneticIterations < Parameters.GeneticIterations &&
                    stagnation >= Math.Max(1, Parameters.PopulationBranchStagnationLimit))
                {
                    logger.LogInformation(
                        $"Гілка завершена достроково після {i + 1} ітерацій без покращення");
                    break;
                }

                if (!kick &&
                    stagnation >= Parameters.KickAfterStagnation &&
                    kicks < Parameters.KickBranches)
                {
                    kicks++;
                    stagnation = 0;
                    if (TryRunKickSearch(
                            population[0],
                            Parameters.KickSeriesCount,
                            Parameters.KickLocalIterations,
                            out var kickResult))
                    {
                        population.Add(kickResult);
                        logger.LogInformation($"Kick search прийнято: {bestEstimate} -> {kickResult.currentEstimation}");
                        EvaluatePopulations();
                    }
                    else
                    {
                        logger.LogInformation("Kick search не знайшов покращення");
                    }
                }

                // ILS: після тривалої стагнації приймаємо обмежену втрату —
                // рандомізований destroy-repair робочого базису (kick цілими
                // серіями падає на -50k..-90k і невідновний); best заморожений.
                if (!ilsActive &&
                    Parameters.IlsStagnationLimit > 0 &&
                    geneticIterations >= Parameters.GeneticIterations &&
                    stagnation >= Parameters.IlsStagnationLimit)
                {
                    ilsEpisodes++;
                    // ILS v2: chain-kick (relocation серії з прийняттям втрати
                    // до IlsChainKickLoss) — "обмежений 2-серійний" примітив,
                    // якого бракувало скелету (док. ILS-AND-SEEDS: TryPerturb
                    // давав або близнюків, або обриви −50k..−90k).
                    var damaged = Parameters.IlsChainKickLoss > 0
                        ? population[0].TryChainPerturb(
                            600,
                            Parameters.IlsChainKickLoss,
                            Math.Max(1, Parameters.IlsChainKickMoves))
                        : population[0].TryPerturb(
                            Math.Max(1, Parameters.IlsKickSeriesCount),
                            600,
                            3,
                            Math.Max(1, Parameters.DestroyRepairMaxAcceptedLoss));
                    if (damaged == null)
                    {
                        stagnation = 0;
                        logger.LogInformation(
                            $"ILS: епізод {ilsEpisodes}: прийнятного збурення не знайдено (поріг {Parameters.DestroyRepairMaxAcceptedLoss}) — пошук триває");
                    }
                    else
                    {
                        ilsStartScore = damaged.currentEstimation;
                        ilsIterationsLeft = Math.Max(1, Parameters.IlsRepairIterations);
                        ilsActive = true;
                        population = new List<Individual> { damaged };
                        stagnation = 0;
                        logger.LogInformation(
                            $"ILS: епізод {ilsEpisodes}: приймаємо втрату {bestEstimate} -> {ilsStartScore} " +
                            (Parameters.IlsChainKickLoss > 0
                                ? $"(chain-kick, втрата {bestEstimate - ilsStartScore}, поріг {Parameters.IlsChainKickLoss})"
                                : $"(perturb {Parameters.IlsKickSeriesCount} серій)") +
                            $", бюджет {Parameters.IlsRepairIterations} іт.");
                    }
                }
                else if (ilsActive && --ilsIterationsLeft <= 0)
                {
                    ilsActive = false;
                    var restored = best.clone();
                    restored.currentEstimation = bestEstimate;
                    population = new List<Individual> { restored };
                    stagnation = 0;
                    logger.LogInformation(
                        $"ILS: епізод {ilsEpisodes} не відновився ({ilsStartScore} -> {currEstimate}), відкат до best {bestEstimate}");
                }
            }

            // ДІАГНОСТИКА Stage 2: чому результат не повертається
            logger.LogInformation(
                $"FINISH-DIAG: bestEstimate={bestEstimate} base={baseEstimate} complete={IsComplete(best)} assigned={best.Slots.Values.Count(x => x.IsAssigned)}/{best.Slots.Count} ilsActive={ilsActive}");
            if (bestEstimate > baseEstimate && IsComplete(best))
            {
                AcceptedDeltaEvents = best.DeltaEvents.ToList();
                logger.LogInformation($"ДО АЛГОРИМУ: {baseEstimate} ПІСЛЯ АЛГОРИТМУ {bestEstimate}");
                if(baseEstimate != 0)
                    logger.LogInformation($"МИ ЗРОБИЛИ КРАЩЕ НА {bestEstimate - baseEstimate}, АБО У: {bestEstimate / (double)baseEstimate} РАЗ");
                logger.LogInformation($"PROFILING ms: {Models.Genetic.Individual.ProfilingSummary()}");
                logger.LogInformation(
                    $"CHEAP-PROFILING: {Services.CheapEval.CheapEvaluationEngine.ProfilingSummary()}");

                var result = new AlgorithmResultDTO();
                
                result.TotalEstimation = bestEstimate;
                
                result.ScheduleSlots = best.Slots.Values.Where(v => v.IsAssigned).Select(x => x.ScheduleSlot).ToList();
                
                // var slots = mapper.RefineIndividualSchedulleSlots(population[0]);
                // result.ScheduleSlots = slots;

                // result.ScheduleSlots = Slots.Values.Where(v => v.IsAssigned).Select(x => x.ScheduleSlot).ToList();
                result.Name = "Genetic";

                return result;
            }
            return null;
        }

        private bool TryRunKickSearch(
            Individual source,
            int kickSeriesCount,
            int localIterations,
            out Individual result)
        {
            var sourceScore = source.currentEstimation;
            var candidate = source.clone();
            candidate.Kick(kickSeriesCount);

            var savedPopulation = population;
            var savedGeneration = newGeneration;
            try
            {
                population = [candidate];
                for (var iteration = 0; iteration < Math.Max(1, localIterations); iteration++)
                {
                    newGeneration = new List<Individual>();
                    GeneticAlgorithm(iteration, candidate, null);

                    var next = newGeneration
                        .Where(IsComplete)
                        .Append(candidate)
                        .OrderByDescending(x => x.currentEstimation)
                        .FirstOrDefault();
                    if (next == null)
                        break;

                    candidate = next;
                    population[0] = candidate;
                }
            }
            finally
            {
                population = savedPopulation;
                newGeneration = savedGeneration;
            }

            result = candidate;
            return candidate.currentEstimation > sourceScore;
        }

        private static bool IsComplete(Individual individual)
        {
            return individual.Slots.Count > 0 && individual.Slots.Values.All(x => x.IsAssigned);
        }
        
        private Dictionary<SlotTracker, int> usedTrackers = new();

        //  Спробуємо використовувати мутації лише для серій в 5+ уроків
        [Obsolete("Метод перенесено до класу Individual")]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public int MutationsForLongSeries(int prevEstimation)
        {
            if(!isInit)
                PreparePrivateGeneticCache();

            var list = FirstTrackers.Select(e => e).Where(e => (!usedTrackers.ContainsKey(e) || usedTrackers[e] < 3) && e is { IsLowDaysDanger: false, ScheduleSlot.LessonSeriesLength: > 4 }).ToList();
            var firstRandomLesson = list.ElementAt(_random.Next(0, list.Count));
            
            // lets add to not repeat it...
            if (!usedTrackers.TryAdd(firstRandomLesson, 1))
            {
                usedTrackers[firstRandomLesson]++;
            }

            var candidateDomain = firstRandomLesson.AvailableDomains;

            var cacheTrackers = firstRandomLesson.ScheduleSlot.GroupSubject.ScheduleSlots
                .Select(slot => Slots[slot])
                .Where(tracker => tracker.SeriesId == firstRandomLesson.SeriesId)
                .OrderBy(tracker => tracker.ScheduleSlot.LessonNumber)
                .ToList();
            List<DomainValue> cacheDomains = new List<DomainValue>();
            foreach (var tracker in cacheTrackers)
            {
                var cachedDomainVal = new DomainValue();
                cachedDomainVal.PairNumber = tracker.ScheduleSlot.PairNumber;
                cachedDomainVal.Date = tracker.ScheduleSlot.Date;
                cacheDomains.Add(cachedDomainVal);
            }
            var cacheSlot = firstRandomLesson.ScheduleSlot;

            var cacheDomain = new DomainValue();
            cacheDomain.PairNumber = cacheSlot.PairNumber;
            cacheDomain.Date = cacheSlot.Date;
            var aSlots = GetAssignedSlots();
            foreach (var domain in candidateDomain)
            {
                bool isVld = ValidateAssignment(firstRandomLesson, domain, aSlots);

                if (isVld)
                {
                    // візьмем трекери для інших занять дисципліни, щоб також перепризначити їх
                    var freeTRackers = firstRandomLesson.ScheduleSlot.GroupSubject.ScheduleSlots
                        .Select(slot => Slots[slot])
                        .Where(tracker => !tracker.IsFirstTrackerInSeries && tracker.SeriesId == firstRandomLesson.SeriesId)
                        .OrderBy(tracker => tracker.ScheduleSlot.LessonNumber)
                        .ToList();
                    freeTRackers.ForEach(x => SetSlotUnAssigned(x));

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
                            
                            // залогуємо наші зміни щоб було легше шукати в excel таблиці різницю з дефолт алгоритмом
                            logger.LogInformation($"БУЛО: ВИКЛАДАЧ:{firstRandomLesson.ScheduleSlot.GroupSubject.Teacher.Name}|ДАТА:{cacheDomain.Date}|НОМЕР:{cacheDomain.PairNumber} СТАЛО:ДАТА:{firstRandomLesson.ScheduleSlot.Date}|НОМЕР:{firstRandomLesson.ScheduleSlot.PairNumber} ");
                            return res;
                        }
                    }
                }
            }
            
            var trackerToRestore = firstRandomLesson.ScheduleSlot.GroupSubject.ScheduleSlots
                .Select(slot => Slots[slot])
                .Where(tracker => tracker.SeriesId == firstRandomLesson.SeriesId)
                .OrderBy(tracker => tracker.ScheduleSlot.LessonNumber)
                .ToList();

            for (int i = 0; i < trackerToRestore.Count; i++)
            {
                SetSlotAssignedGenetic(trackerToRestore[i], cacheDomains[i]);
            }
            return Estimate();
        }


        private int limit = 10;
        
        [Obsolete("Метод перенесено до класу Individual")]
        // todo рідко але іноді буває розклад отриманий після мутацій не зберігається в API, необхідно знайти де могли б втратитись дані.
        // UPD: наче після того як вийшло відновлювати дані такого поки більше не відбувається...
        public int Mutations(int prevEstimation)
        {

            // todo Достатньо буде створити 1 раз, далі скопіюється з інших джерел
            if(!isInit)
                PreparePrivateGeneticCache();
            
            // Перші заняття в серії - наша популяція з якою ми граємось
            
            // беремо випадковий елемент популяції
            // todo вигадати як переробити щоб не брати кожен раз дисципліну випадково з нуля, а, наприклад, брати зі стеку, та заносити в окремий стек які дисципліни мали успішні та неуспішні мутації, бо цікаво погратись яка комбінація в середньому вигідніша
            var list = FirstTrackers.Select(e => e).Where(e => (!usedTrackers.ContainsKey(e) || usedTrackers[e] < 3) && e is { IsLowDaysDanger: false, ScheduleSlot.LessonSeriesLength: < 4 }).ToList();
            // var list = FirstTrackers.Select(e => e).Where(e => !e.IsLowDaysDanger).ToList();
            var firstRandomLesson = list.ElementAt(_random.Next(0, list.Count));
            
            if (!usedTrackers.TryAdd(firstRandomLesson, 1))
            {
                usedTrackers[firstRandomLesson]++;
            }

            // беремо лише той який не стоїть першою можливою датою (тому, що так покращення не дуже очевидне)
            
            // var firstRandomLesson = _population.ElementAt(_random.Next(0,_population.Count));
            
            // відмінити призначення 1 слоту
            // upd не потрібно бо ми робимо ValidateAssignment а потім SetSlotAssignedGenetic
            // firstRandomLesson.IsAssigned = false;
            // SetSlotUnAssigned(firstRandomLesson);
            
            // ResetUnAssignedFirstSlots(firstRandomLesson);
            // SetSlotUnAssigned(firstRandomLesson);
            // і змінюємо його на доступний домен, намагаємось змінити всі інші наступні заняття, перевіряючи констрейнти (is valid)
            var candidateDomain = firstRandomLesson.AvailableDomains;
            
            // скоріш за все для дисциплін з малою кількістю занять і багатьма варіантами,
            // можна обмежити кількість тих доменів що розглядаємо до третини, якщо перша третина не підійшла,
            // мало сенсу розглядати інші
            // if (candidateDomain.Count > 40)
            // {
            //     candidateDomain = new SortedSet<DomainValue>(candidateDomain.Take(40));
            // }

            // збережемо інформацію про всі заняття в цій дисципліні щоб потім відновити назад якщо призначення не відбулось
            var cacheTrackers = firstRandomLesson.ScheduleSlot.GroupSubject.ScheduleSlots
                .Select(slot => Slots[slot])
                .Where(tracker => tracker.SeriesId == firstRandomLesson.SeriesId)
                .OrderBy(tracker => tracker.ScheduleSlot.LessonNumber)
                .ToList();

            List<DomainValue> cacheDomains = new List<DomainValue>();
            foreach (var tracker in cacheTrackers)
            {
                var cachedDomainVal = new DomainValue();
                cachedDomainVal.PairNumber = tracker.ScheduleSlot.PairNumber;
                cachedDomainVal.Date = tracker.ScheduleSlot.Date;
                // var slot = tracker.ScheduleSlot.Clone(firstRandomLesson.ScheduleSlot.GroupSubject);
                cacheDomains.Add(cachedDomainVal);
            }
            
            // cacheTrackers.ForEach(t => t.Clone(t.ScheduleSlot.Clone(firstRandomLesson.ScheduleSlot.GroupSubject)));

            var cacheSlot = firstRandomLesson.ScheduleSlot;

            var cacheDomain = new DomainValue();
            cacheDomain.PairNumber = cacheSlot.PairNumber;
            cacheDomain.Date = cacheSlot.Date;
            
            // var cacheSlotDate = cacheSlot.Date;
            // var cacheSlotLessonNumber = cacheSlot.LessonNumber;
            // var cacheSlotLessonSeriesLength = cacheSlot.LessonSeriesLength;
            // var cacheSlotGroupSubject = cacheSlot.GroupSubject;
            // var lessonShift = firstRandomLesson.WeekShift;
            var aSlots = GetAssignedSlots();
            foreach (var domain in candidateDomain)
            {
                // var currDate = domain.Date;
                // var currPairNum = domain.PairNumber;

                // перевірити чи можемо призначити цей домен
                bool isVld = ValidateAssignment(firstRandomLesson, domain, aSlots);

                if (isVld)
                {
                    // візьмем трекери для інших занять дисципліни, щоб також перепризначити їх
                    // todo подивитись як у розкладі змінюються підгрупи після мутацій, чи всі разом перепризначаються чи окремо
                    var freeTRackers = firstRandomLesson.ScheduleSlot.GroupSubject.ScheduleSlots
                        .Select(slot => Slots[slot])
                        .Where(tracker => !tracker.IsFirstTrackerInSeries && tracker.SeriesId == firstRandomLesson.SeriesId)
                        .OrderBy(tracker => tracker.ScheduleSlot.LessonNumber)
                        .ToList();
                    freeTRackers.ForEach(x => SetSlotUnAssigned(x));

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
                            
                            // якщо вже стало краще - варто зберегти зміни, ніж далі шукати інші варіанти
                            return res;
                            
                            // todo а що якщо ми отримуємо певну кращу оцінку після мутації
                            // ми будемо перевіряти і інші варіанти бо раптом саме для цієї серії є ще щось краще?
                            // тоді можна буде перебрати всі варіанти, і вибрати найкращий з усіх
                        }
                    }
                }
            }
            
            // зараз нам нічого не треба відновлювати
            // але в майбутньому треба буде перепризначати все назад на тих самих даних
            // не залишаючи слідів у випадку якщо призначення було не вигідне / не успішне
            // щоб не відновлювати все копіюванням
            // return Estimate();
            // UPD: зроблено! І навіть наче працює :)
            // поки закоментуємо
            // var before = Estimate();
            
            // todo доробити повернення до попереднього стану
            // UPD: зроблено! І навіть наче працює :)
            
            var trackerToRestore = firstRandomLesson.ScheduleSlot.GroupSubject.ScheduleSlots
                .Select(slot => Slots[slot])
                .Where(tracker => tracker.SeriesId == firstRandomLesson.SeriesId)
                .OrderBy(tracker => tracker.ScheduleSlot.LessonNumber)
                .ToList();

            for (int i = 0; i < trackerToRestore.Count; i++)
            {
                SetSlotAssignedGenetic(trackerToRestore[i], cacheDomains[i]);
            }
            // var after = Estimate();
            // logger.LogInformation($"BEFORE: {before} AFTER {after}");

            // якщо нічого не змінилось, ми просто перезапустимо мутацію.
            // todo це ж може переповнитись стек, треба переробити метод щоб він виконався до раз 10-20
            // а після цього вважати що мутації поки не приносять результату, і виконати інший метод
            if (limit > 0)
                Mutations(prevEstimation);
            // return Mutations(prevEstimation);
            return Estimate();

            SetSlotAssignedGenetic(firstRandomLesson, cacheDomain);
            // firstRandomLesson.IsAssigned = true;
            
            var freeTR = firstRandomLesson.ScheduleSlot.GroupSubject.ScheduleSlots
                .Select(slot => Slots[slot])
                .Where(tracker => !tracker.IsFirstTrackerInSeries && tracker.SeriesId == firstRandomLesson.SeriesId)
                .OrderBy(tracker => tracker.ScheduleSlot.LessonNumber)
                .ToList();
            
            freeTR.ForEach(x => SetSlotUnAssigned(x));
            freeTR.ForEach(e => Slots[e.ScheduleSlot] = e);

            ApplySynchronizedDomainPatternGenetic(firstRandomLesson, aSlots);
            ForwardCheck(firstRandomLesson,firstRandomLesson.AssignStep);
            return Estimate();

        }
        
        private bool ApplySynchronizedDomainPatternGenetic(SlotTracker currentTracker, AssignedSlotsDTO assignedSLots)
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

        
        List<long> swappedTeachers = new List<long>();
        List<int?> swappedSeries = new List<int?>();
        
        // Заміна місцями двох ідентичних дисциплін однієї і тієї самої групи

        private List<Individual> population = new List<Individual>();
        private List<Individual> newGeneration = new List<Individual>();

        internal void PopulationMutationsForLongSeries(Individual individual, List<Individual> output)
        {
            var newPop = TryMutationTournament(
                individual, 4, -1, out var mutatedSeriesDomain);
            // var mutatedSeriesDomain = newPop.MutationsForLongSeries(population.currentEstimation);
            if (newPop == null)
                return;

            if (newPop.currentEstimation >= individual.currentEstimation)
                output.Add(newPop);
            // legacy move-transfer: у parallel РЕЖИМІ ВИМКНЕНО (race на
            // population[0]; та сама ідея, що зафальшивив delta-replay)
            if (!_parallelRun && individual != population[0] &&
                individual.currentEstimation < population[0].currentEstimation &&
                newPop.currentEstimation > individual.currentEstimation &&
                newPop.currentEstimation < population[0].currentEstimation
               )
            {
                logger.LogInformation($"Намагаємось перенести зміни на кращий розклад");
                if (mutatedSeriesDomain != null)
                {
                    population[0].ApplyMutation((KeyValuePair<int, DomainValue>)mutatedSeriesDomain);
                }
            }
        }

        internal void PopulationMutationsForShortSeries(Individual individual, List<Individual> output)
        {
            var clonedIndividual = TryMutationTournament(
                individual, -1, 3, out var mutatedSeriesDomain);
            if (clonedIndividual == null)
                return;

            if (clonedIndividual.currentEstimation >= individual.currentEstimation)
                output.Add(clonedIndividual);
            if (!_parallelRun && individual != population[0] &&
                individual.currentEstimation < population[0].currentEstimation &&
                clonedIndividual.currentEstimation > individual.currentEstimation &&
                clonedIndividual.currentEstimation < population[0].currentEstimation
               )
            {
                logger.LogInformation($"Намагаємось перенести зміни на кращий розклад");
                if (mutatedSeriesDomain != null)
                {
                    population[0].ApplyMutation((KeyValuePair<int, DomainValue>)mutatedSeriesDomain);
                }
            }
        }

        /// <summary>
        /// ITC-оператор: мутація цілого класу (скан доменів першого слоту серії)
        /// без фільтра IsLowDaysDanger, який в ITC-кодуванні (1-заняттєві серії)
        /// відсікав усі класи. Старі оператори не змінюються.
        /// </summary>
        internal void PopulationClassMutationsItc(Individual individual, List<Individual> output)
        {
            var clonedIndividual = TryMutationTournament(individual, -1, 3, out _, itcMode: true);
            if (clonedIndividual == null)
                return;
            if (clonedIndividual.currentEstimation >= individual.currentEstimation)
                output.Add(clonedIndividual);
        }

        /// <summary>
        /// ITC-оператор: два послідовні класові скани (використовується, коли
        /// жоден одиночний хід не покращує оцінку — локальні плато).
        /// </summary>
        /// <summary>
        /// ITC-EVICT-CHAIN: освіта потомка цілочисельним виселенням блокувальників
        /// (потрібні спільні групи в DTO та ExtraItcOperators >= 3).
        /// </summary>
        internal void PopulationEvictChainItc(Individual individual, List<Individual> output)
        {
            if (Parameters.ExtraItcOperators < 3)
            {
                PopulationChainRelocate(individual, output);
                return;
            }
            var clone = individual.TryEvictChainMove(Parameters.EvictChainMaxClasses);
            if (clone == null)
                return;
            if (clone.currentEstimation >= individual.currentEstimation)
                output.Add(clone);
        }

        /// <summary>
        /// Opt-in ITC operator (ExtraItcOperators >= 4): several eviction-chain
        /// targets per generation. The exact room-aware fitness still decides
        /// acceptance; the GA core is unchanged.
        /// </summary>
        internal void PopulationRoomAwareEvictChainItc(Individual individual, List<Individual> output)
        {
            if (Parameters.ExtraItcOperators < 4)
            {
                PopulationEvictChainItc(individual, output);
                return;
            }
            var clone = individual.TryRoomAwareEvictChainMove(
                Parameters.EvictChainMaxClasses,
                Parameters.EvictChainAttempts);
            if (clone == null)
                return;
            if (clone.currentEstimation >= individual.currentEstimation)
                output.Add(clone);
        }

        internal void PopulationDoubleClassMutationItc(Individual individual, List<Individual> output)
        {
            var clone = CloneHelper.clone(individual);
            clone.currentEstimation = individual.currentEstimation;
            var used = new HashSet<int>();
            var first = clone.TryBestDomainMutation(
                clone.currentEstimation, -1, 3, used,
                out _, out _, out _, Parameters.MutationDomainCandidates, itcMode: true);
            if (first == null)
                return;
            var second = clone.TryBestDomainMutation(
                clone.currentEstimation, -1, 3, used,
                out _, out _, out _, Parameters.MutationDomainCandidates, itcMode: true);
            if (second == null)
                return;
            if (clone.currentEstimation >= individual.currentEstimation)
                output.Add(clone);
        }

        private Individual? TryMutationTournament(
            Individual individual,
            int minSeriesLength,
            int maxSeriesLength,
            out KeyValuePair<int, DomainValue>? selectedMutation,
            bool itcMode = false)
        {
            selectedMutation = null;
            Individual? bestCandidate = null;
            var bestScore = individual.currentEstimation;
            var attempts = Math.Max(1, Parameters.MutationTournamentAttempts);
            // виключення серій діє лише в межах однієї операції: серія, що
            // не дала покращення зараз, може стати вигідною після інших змін
            var tournamentUsedSeries = new HashSet<int>();

            for (var attempt = 0; attempt < attempts; attempt++)
            {
                // скан виконується напряму на individual: TryBestDomainMutation
                // сам відновлює стан між спробами доменів, а переможця можна
                // відкочувати через UndoSeriesPlacement. Клон створюється лише
                // для прийнятого кандидата — це знімає один клон з кожної
                // невдалої спроби (у застряглих раундах — з усіх)
                var prevEstimation = individual.currentEstimation;
                var mutated = individual.TryBestDomainMutation(
                    prevEstimation,
                    minSeriesLength,
                    maxSeriesLength,
                    tournamentUsedSeries,
                    out var mutation,
                    out var appliedTrackers,
                    out var appliedOriginalDomains,
                    Parameters.MutationDomainCandidates,
                    itcMode);

                if (mutated == null || mutation == null)
                    continue;

                var score = mutated.currentEstimation;
                tournamentUsedSeries.Add(mutation.Value.Key);

                // строго гірших відкидаємо, рівні приймаємо (дрейф по плато);
                // в обох випадках individual повертається у вихідний стан
                if (score >= bestScore)
                {
                    var candidate = CloneHelper.clone(individual);
                    candidate.currentEstimation = score;
                    bestCandidate = candidate;
                    bestScore = score;
                    selectedMutation = mutation;
                }

                individual.UndoSeriesPlacement(appliedTrackers!, appliedOriginalDomains!);
                individual.currentEstimation = prevEstimation;
            }

            return bestCandidate;
        }
        
        private void PopulationSwapTeacherSubjects(Individual individual)
        {
            var clonedIndividual = individual.clone();
            clonedIndividual.currentEstimation = individual.currentEstimation;
            var swappedSubjects = clonedIndividual.SwapTeacherSubjects();
            if(swappedSubjects == null)
                return;
            logger.LogInformation($"ДОДАЄМО ЦЕ У ПОПУЛЯЦІЮ");
            newGeneration.Add(clonedIndividual);
            
            // якщо в цій популяції щось стало краще, дивимось, чи можемо ми перенести цю зміну у найкращу популяцію
            if (individual != population[0] && 
                individual.currentEstimation < population[0].currentEstimation && 
                clonedIndividual.currentEstimation > individual.currentEstimation &&
                clonedIndividual.currentEstimation < population[0].currentEstimation
                )
            {   
                logger.LogInformation($"ОСЬ ТУТ ТРЕБА ПРОБУВАТИ ПЕРЕНЕСТИ ЗМІНИ!");
                if(swappedSubjects != null)
                    population[0].ApplyTeacherSubjectsSwap((KeyValuePair<int, int>)swappedSubjects);
            }
        }
        
        // особливість цієї стратегії - вона створює альтернативні варіанти розкладу з такою ж оцінкою
        internal void PopulationSwapGroupSubjects(Individual individual, List<Individual> output)
        {
            var clonedIndividual = individual.clone();
            clonedIndividual.currentEstimation = individual.currentEstimation;
            var swappedSubjects = clonedIndividual.SwapGroupSubjects();
            if(swappedSubjects == null)
                return;

            if (!IsComplete(clonedIndividual))
            {
                logger.LogInformation(
                    $"СВАП ГРУП ВІДХИЛЕНО: hard-invalid candidate {individual.currentEstimation} -> {clonedIndividual.currentEstimation}");
                return;
            }

            logger.LogInformation(
                $"ДОДАЄМО СВАП ГРУП ДО ПОПУЛЯЦІЇ: {individual.currentEstimation} -> {clonedIndividual.currentEstimation}");
            output.Add(clonedIndividual);

            if (!_parallelRun && individual != population[0] &&
                individual.currentEstimation < population[0].currentEstimation &&
                clonedIndividual.currentEstimation > individual.currentEstimation &&
                clonedIndividual.currentEstimation < population[0].currentEstimation
               )
            {
                logger.LogInformation($"ОСЬ ТУТ ТРЕБА ПРОБУВАТИ ПЕРЕНЕСТИ ЗМІНИ!");
                if(swappedSubjects != null)
                    population[0].ApplyTeacherSubjectsSwap((KeyValuePair<int, int>)swappedSubjects);
            }
        }

        private bool _parallelRun;

        private int hgtTransplants;
        private int hgtAccepted;

        /// <summary>
        /// ГЕН-БЛОЧНИЙ HGT: лідер (population[0] після сортування) донує
        /// цілий блок GroupSubject у laggard. Приймання = гібрид виживає
        /// лінію пулу (>= baseEstimate); ролі перевертаються наступним
        /// сортуванням, якщо гібрид виявився кращим.
        /// </summary>
        private void PopulationGeneHgt(Individual donor, Individual receiver, int minAcceptableScore)
        {
            var gsList = donor.Root.GroupSubjects;
            var gsId = gsList[_random.Next(gsList.Count)].Id;
            var before = receiver.currentEstimation;
            hgtTransplants++;

            if (receiver.TryTransplantBlockFrom(donor, gsId, minAcceptableScore))
            {
                hgtAccepted++;
                logger.LogInformation(
                    $"HGT-BLOCK: блок {gsId} пересаджено: {before} -> {receiver.currentEstimation} (прийнято {hgtAccepted}/{hgtTransplants})");
            }
            else
            {
                logger.LogInformation(
                    $"HGT-BLOCK: блок {gsId} відкинуто (не виживає лінію) (прийнято {hgtAccepted}/{hgtTransplants})");
            }
        }

        internal void PopulationHotspotRelocate(Individual individual, List<Individual> output)
        {
            // v2: глибший скан (8 гарячих клітинок, 25 доменів) — v1 вичерпував
            // простір за ~5мс і слот майже нічого не давав
            var relocated = individual.TryHotspotRelocate(30, 25);
            if (relocated == null || !IsComplete(relocated))
                return;
            if (relocated.currentEstimation <= individual.currentEstimation)
                return;

            logger.LogInformation(
                $"Hotspot-relocate додано до пошуку: {individual.currentEstimation} -> {relocated.currentEstimation}");
            output.Add(relocated);
        }

        internal void PopulationChainRelocate(Individual individual, List<Individual> output)
        {
            var relocated = Parameters.ChainDirected
                ? individual.TryChainRelocate(600, 8)
                : individual.TryChainRelocateRandom(600, 2);
            if (relocated == null || !IsComplete(relocated))
                return;
            if (relocated.currentEstimation <= individual.currentEstimation)
                return;

            logger.LogInformation(
                $"Chain-relocate додано до пошуку ({(Parameters.ChainDirected ? "directed" : "random")}): {individual.currentEstimation} -> {relocated.currentEstimation}");
            output.Add(relocated);
        }

        internal void PopulationDestroyRepair(Individual individual, List<Individual> output)
        {
            var repaired = individual.TryDestroyRepair(
                Parameters.DestroyRepairSeriesCount,
                Parameters.DestroyRepairMaxMilliseconds,
                Parameters.DestroyRepairAttempts);
            if (repaired == null || !IsComplete(repaired))
                return;

            var scoreShift = repaired.currentEstimation - individual.currentEstimation;
            var maxAcceptedLoss = Math.Max(0, Parameters.DestroyRepairMaxAcceptedLoss);
            if (scoreShift > 0 || (scoreShift < 0 && -scoreShift <= maxAcceptedLoss))
            {
                // Стан після руйнування-відновлення прийнято. Якщо він
                // гірший за початковий (перехід через хребет штрафу),
                // спускаємось з нього легкими мутаціями назад — і лишаємо
                // результат лише якщо він сягнув суворого покращення.
                // Інакше пошкоджений стан просто відкидається.
                var relocalIterations = Math.Max(0, Parameters.DestroyRepairRelocalIterations);
                var best = repaired;
                var relocalSteps = 0;
                for (var i = 0;
                     i < relocalIterations && best.currentEstimation <= individual.currentEstimation;
                     i++)
                {
                    var improved = best.TryQuickImprovement(best.currentEstimation, longSeries: i % 2 == 1);
                    if (improved == null)
                        break;
                    relocalSteps++;
                    best = improved;
                }

                if (relocalIterations > 0 && best.currentEstimation <= individual.currentEstimation)
                {
                    logger.LogInformation(
                        $"Destroy-repair відхилено: {individual.currentEstimation} -> {repaired.currentEstimation}, " +
                        $"спуск {relocalSteps} кроків, досягнуто {best.currentEstimation}");
                    return;
                }

                logger.LogInformation(
                    $"Destroy-repair додано до пошуку: {individual.currentEstimation} -> {best.currentEstimation}");
                output.Add(best);
            }
            else
            {
                logger.LogInformation(
                    $"Destroy-repair без покращення або втрата перевищена: {individual.currentEstimation} -> {repaired.currentEstimation}");
            }
        }

        private int populationLimitCount = 3;
        private void EvaluatePopulations()
        {
            population.Sort((a, b) => a.currentEstimation > b.currentEstimation ? -1 : 1);
            if (populationLimitCount <= 1)
            {
                population = population.Take(1).ToList();
                return;
            }

            // Острови: слот на ЛІНІЮ (тег успадковується потомками). Пул = найкраща
            // особа кожної лінії — потомки лідера не витісняють лінію дослідника,
            // дивергенція виживає, і ген-блочний HGT має між чим обирати.
            var byLineage = new List<Individual>();
            foreach (var p in population)
                if (byLineage.All(x => x.LineageTag != p.LineageTag))
                    byLineage.Add(p);
            population = byLineage.Take(Math.Max(1, populationLimitCount)).ToList();
        }
        
        
        [Obsolete("Метод перенесено до класу Individual")]
        // Заміна місцями двох ідентичних дисциплін одного і того самого викладача
        public int SwapTeacherSubjects()
        {
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

                // var seriesIdsEnumerable = firstRandomLessonTracker.ScheduleSlot.GroupSubject.ScheduleSlots.Select(e => e.LessonSeriesId).Distinct();

                var dicLen = new Dictionary<int, int>();
                
                if(teacherSubjectsTrackers.Count < 2)
                    continue;

                // var slots = teacherSubjectsTrackers.Select(e => e.ScheduleSlot.LessonSeriesLength).Where(e => e.);
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
                            // var aSlots = GetAssignedSlots();
                            var before = Estimate();
                            // замінити місцями.
                            SwapSeriesTrackers(firstSubjectFirstTracker, secondSubjectFirstTracker);
                            var after = Estimate();

                            // якщо ця зміна є негативною, повернути назад
                            if (after <= before)
                            {
                                SwapSeriesTrackers(firstSubjectFirstTracker, secondSubjectFirstTracker);
                            }
                            else
                            {
                                logger.LogInformation($"WE MADE BETTER!: {before} : {after} ");
                                return 0;
                }
            }
        }

                }
                
            }
            
            return 0;
        }

        [Obsolete("Метод перенесено до класу Individual")]
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
        
        [Obsolete("Метод перенесено до класу Individual")]
        public int SwapGroupSubjects()
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

                // var firstGroupSubjects = assignedSlotsByGroupDate[groups[0].Id].Values.ToList();

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

                var before = Estimate();
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
                    
                    List<DomainValue> cacheDomainsFirst = new List<DomainValue>();
                    List<DomainValue> cacheDomainsSecond = new List<DomainValue>();
                    
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
                    
                    freeTRackersFirst.ForEach(x => SetSlotUnAssigned(x));
                    SetSlotAssignedGenetic(firstRandomLessonTracker, DomainSecond);
                    
                    freeTRackersSecond.ForEach(x => SetSlotUnAssigned(x));
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
                        if (res == before)
                        {
                        }
                        if (res >= before)
                        {
                            freeTRackersFirst.ForEach(e => Slots[e.ScheduleSlot] = e);
                            freeTRackersSecond.ForEach(e => Slots[e.ScheduleSlot] = e);

                            return res;
                        }
                    }
                        // повертаємо назад
                        SetSlotAssignedGenetic(firstRandomLessonTracker, DomainFirst);
                        SetSlotAssignedGenetic(subjectTracker, DomainSecond);
                        
                        for (int i = 0; i < freeTRackersFirst.Count; i++)
                        {
                            SetSlotAssignedGenetic(freeTRackersFirst[i], cacheDomainsFirst[i]);
                        }
                        for (int i = 0; i < freeTRackersSecond.Count; i++)
                        {
                            SetSlotAssignedGenetic(freeTRackersSecond[i], cacheDomainsSecond[i]);
                        }
                    
                        // var after = Estimate();
                        // // logger.LogInformation($"BEFORE: {before} AFTER {after}");

                }
            }
            
            return 0;
        }

        public int Estimate()
        {
            int scheduleEstimation = 0;
            foreach (var s in UserFunctions.ScheduleEstimations)
            {
                var extScore = s.Estimate(Root);
                scheduleEstimation += extScore;
            }
            // logger.LogInformation($"Estimate: {scheduleEstimation}");
            return scheduleEstimation;
            
        }
        
        // різниця полягає в тому що нам не потрібно зберігати крок.
        private void SetSlotAssignedGenetic(SlotTracker slot, DomainValue val)
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
        }

        #endregion
        
        
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
                slot.AvailableDomains = new SortedSet<DomainValue>(slot.AvailableDomains
                    .Where(candidate => Validators.ValidateAssignmentArc(slot, candidate, changedSlots)))
                    ;

                // Визначаємо, які доменні значення було вилучено.
                var removed = originalDomains.Except(slot.AvailableDomains).ToList();
                if (removed.Any())
                {
                    if (!slot.RejectedDomains.ContainsKey(currentStep))
                        slot.RejectedDomains[currentStep] = new List<DomainValue>();
                    slot.RejectedDomains[currentStep].AddRange(removed);
                    slotsByStep[currentStep].Add(slot);
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
        private void SetSlotUnAssigned(SlotTracker slot, bool clearClassroom = true)
        {
            if (slot.IsAssigned)
            {
                assignedSlotsByTeacherDate[slot.ScheduleSlot.GroupSubject.Teacher.Id][slot.ScheduleSlot.Date].Remove(slot);
                foreach (var group in slot.ScheduleSlot.GroupSubject.Groups)
                    assignedSlotsByGroupDate[group.Id][slot.ScheduleSlot.Date].Remove(slot);

                if (slot.ScheduleSlot.Classroom != null)
                    assignedClassrooms[slot.ScheduleSlot.Date][slot.ScheduleSlot.PairNumber].Remove(slot.ScheduleSlot.Classroom);

                if (clearClassroom)
                {
                    // Очищаємо аудиторію
                    slot.ScheduleSlot.Classroom = null;
                }
            }
            slot.IsAssigned = false;

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
            slotTracker.ScheduleSlot.Date = domain.Date;
            slotTracker.ScheduleSlot.PairNumber = domain.PairNumber;

            var slotAdapter = GetAdapter(slotTracker.ScheduleSlot);

            var standartValidation = Validators.StandartValidation(slotAdapter, assignedSLots);
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
