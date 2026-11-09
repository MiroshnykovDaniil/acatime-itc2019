using AcaTime.Algorithm.Default.Models;
using AcaTime.Algorithm.Default.Services.Statictics;
using AcaTime.Algorithm.Default.Utils;
using AcaTime.ScheduleCommon.Abstract;
using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScheduleCommon.Models.Constraints;
using AcaTime.ScriptModels;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AcaTime.Algorithm.Default.Services
{
    /// <summary>
    /// Алгоритм побудови розкладу
    /// </summary>
    public class DefaultScheduleAlgorithm : IScheduleAlgorithm
    {
        public AlgorithmParams RunParameters { get; private set; }
        public DateTime StartTime { get; private set; }

        DefaultScheduleAlgorithmUnit defaultUnit;
        private ILogger logger;
        private AlgorithmStatistics statistics = new AlgorithmStatistics();
        private readonly IScheduleStatisticsService scheduleStatisticsService;

        public DefaultScheduleAlgorithm(IScheduleStatisticsService scheduleStatisticsService)
        {
            this.scheduleStatisticsService = scheduleStatisticsService;
        }

        public async Task<List<AlgorithmResultDTO>> Run(FacultySeasonDTO root, UserFunctions userFunctions, Dictionary<string, string> parameters, bool ignoreClassrooms, ILogger logger, CancellationToken cancellationToken = default)
        {
          
            this.logger = logger;

            var runParameters = new AlgorithmParams(parameters);
            logger.LogInformation($"Запуск алгоритму {this.GetName()} з параметрами: {string.Join(", ", parameters.Select(kv => $"{kv.Key}={kv.Value}"))}, ignoreClassrooms={ignoreClassrooms}");

            this.RunParameters = runParameters;
            this.StartTime = DateTime.Now;

            defaultUnit = new DefaultScheduleAlgorithmUnit();
            defaultUnit.Setup(root, logger, userFunctions, runParameters);

            await Load();

            // Створюємо джерело токенів скасування, яке можна використовувати для обмеження часу виконання
            using var timeoutCts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

            // Встановлюємо таймаут, якщо він вказаний в параметрах
            if (runParameters.TimeoutInSeconds > 0)
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(runParameters.TimeoutInSeconds));
            }

            // Створюємо завдання для паралельного обчислення
            var tasks = new List<Task<AlgorithmResultDTO>>();
            var results = new ConcurrentBag<AlgorithmResultDTO>();
            ParallelOptions parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount > 4 ? Environment.ProcessorCount / 2 : 1,
                CancellationToken = linkedCts.Token
            };

            // time for one itteration
            var timeOneSec = runParameters.TimeoutInSeconds * parallelOptions.MaxDegreeOfParallelism / runParameters.MaxIterations;

            statistics = new AlgorithmStatistics();

            try
            {
                // Запускаємо паралельні обчислення

                logger.LogInformation($"Початок розрахунку. Кількість ітерацій: {runParameters.MaxIterations}. Кількість паралельних обчислень: {parallelOptions.MaxDegreeOfParallelism}");
                await Parallel.ForEachAsync(
                    Enumerable.Range(0, runParameters.MaxIterations),
                    // Enumerable.Range(0, runParameters.MaxIterations),
                    parallelOptions,
                    async (i, token) =>
                    {
                        var unit = defaultUnit.Clone();

                        // set timeout for one itteration
                        var timeoutOneCts = new CancellationTokenSource();
                        var linkedOneCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutOneCts.Token, token);

                        timeoutOneCts.CancelAfter(TimeSpan.FromSeconds(timeOneSec));

                        var result = await unit.Run(linkedOneCts.Token, ignoreClassrooms).ConfigureAwait(false);

                        

                        if (result != null)
                        {
                            lock (results)
                            {
                                statistics.Success++;
                                result.Name = this.GetName();
                                results.Add(result);
                                // Сортуємо та обмежуємо кількість результатів при необхідності
                                if (results.Count > runParameters.ResultsCount)
                                {
                                    var sortedResults = results.OrderByDescending(x => x.TotalEstimation).Take(runParameters.ResultsCount).ToList();
                                    results.Clear();
                                    foreach (var sortedResult in sortedResults)
                                    {
                                        results.Add(sortedResult);
                                    }                                    
                                }

                                statistics.BestResult = results.Max(x => x.TotalEstimation);
                            }
                        }
                        else
                        {
                            statistics.Failed++;
                        }

                        statistics.ScheduleStatistics.Add(new ScheduleStatisticsModel
                        {
                            MaxExecutionTimeSec = runParameters.TimeoutInSeconds,
                            Deterministic = runParameters.SlotsTopK<2 && runParameters.DomainsTopK<2,
                            ParallelCount = parallelOptions.MaxDegreeOfParallelism,
                            MaxIterations = runParameters.MaxIterations,
                            SlotsTopK = runParameters.SlotsTopK,
                            SlotsTemperature = runParameters.SlotsTemperature,
                            DomainsTopK = runParameters.DomainsTopK,
                            DomainsTemperature = runParameters.DomainsTemperature,
                            LastStep = 0,
                            ResultScore = result?.TotalEstimation ,
                            ExecutionTimeMs = unit.DebugData.TotalMs
                        });
                    });
            }
            catch (OperationCanceledException)
            {
                // Операція була скасована через таймаут або токен скасування
                logger.LogInformation("Обчислення алгоритму було перервано через таймаут або зовнішнє скасування");
            }

            logger.LogInformation($"Завершено розрахунку. Кількість успішних результатів: {statistics.Success}, Найкращий результат: {statistics.BestResult}");

            // Повертаємо найкращі результати
            var res = results
                .OrderByDescending(x => x.TotalEstimation)
                .Take(runParameters.ResultsCount)
                .ToList();

            await this.scheduleStatisticsService.SaveStatisticsAsync(statistics.ScheduleStatistics).ConfigureAwait(false);

            return res;
        }      


        /// <summary>
        /// Отримує статистику роботи алгоритму
        /// </summary>
        /// <returns></returns>
        public string GetStatistics()
        {
            if (RunParameters != null)
                return $"Знайдено рішень: {statistics.Success}, Знято по таймауту: {statistics.Failed}, Найкращий результат: {statistics.BestResult}, Час роботи: {(int)DateTime.Now.Subtract(StartTime).TotalSeconds} секунд, Залишок часу: {RunParameters.TimeoutInSeconds - (int)DateTime.Now.Subtract(StartTime).TotalSeconds} секунд";
            else
                return "Підготовка";
        }

        /// <summary>
        /// Генерує доступні доменні значення для всіх слотів розкладу
        /// </summary>
        /// <returns></returns>
        private List<DomainValue> GenerateAvailableDomainValues()
        {
            var slots = new List<DomainValue>();
            for (var date = defaultUnit.Root.BeginSeason; date <= defaultUnit.Root.EndSeason; date = date.AddDays(1))
            {
                for (int pair = 1; pair <= defaultUnit.Root.MaxLessonsPerDay; pair++)
                {
                    slots.Add(new DomainValue
                    {
                        Date = date,
                        PairNumber = pair
                    });
                }
            }
            return slots;
        }

        /// <summary>
        /// Виконуємо крокі які спільні для всіх юнітів. Підготавлюємо кеш.
        /// </summary>
        private async Task Load()
        {
            var domains = GenerateAvailableDomainValues();
            defaultUnit.Slots = defaultUnit.Root.GroupSubjects.SelectMany(x => x.ScheduleSlots).ToDictionary(x => x as IScheduleSlot, x => new SlotTracker { ScheduleSlot = x, AvailableDomains = new SortedSet<DomainValue>(domains) });

            // перевірка обмежень на одиничні слоти
            foreach (var a in defaultUnit.UserFunctions.UnitaryConstraints)
            {
                var sl = a.Select(defaultUnit.Root);
                foreach (var v in sl)
                {
                    var tracker = defaultUnit.Slots[v];
                    var domainsToRemove = new HashSet<DomainValue>();
                    foreach (var d in tracker.AvailableDomains)
                    {
                        tracker.SetDomain(d, 0);
                        if (!a.Check(defaultUnit.GetAdapter(tracker.ScheduleSlot)))
                        {
                            domainsToRemove.Add(d);
                        }
                    }

                    foreach (var item in tracker.AvailableDomains.Where(x => domainsToRemove.Contains(x)).ToList())
                    {
                        tracker.AvailableDomains.Remove(item);
                    }
                }

            }

            defaultUnit.teacherSlots = defaultUnit.Slots.Values.GroupBy(s => s.ScheduleSlot.GroupSubject.Teacher.Id, s => s).ToDictionary(x => x.Key, x => x.ToList());
            defaultUnit.groupsSlots = new Dictionary<long, List<SlotTracker>>();
            foreach (var sl in defaultUnit.Slots.Values)
            {
                foreach (var id in sl.ScheduleSlot.GroupSubject.Groups.Select(g => g.Id))
                {
                    if (!defaultUnit.groupsSlots.ContainsKey(id))
                        defaultUnit.groupsSlots.Add(id, new List<SlotTracker>());
                    defaultUnit.groupsSlots[id].Add(sl);
                }
            }

            // можливо декілька підгруп однієї групи на один слот - тоді будуть дублі
            foreach (var k in defaultUnit.groupsSlots.Keys)
            {
                defaultUnit.groupsSlots[k] = defaultUnit.groupsSlots[k].Distinct().ToList();
            }

            // групуємо слоти по серіям
            GroupAndFilterSeries();

            // зберігаємо перші слоти серій
            defaultUnit.FirstTrackers = defaultUnit.Slots.Values.Where(x => x.IsFirstTrackerInSeries).ToList();
        }


        /// <summary>
        /// Групує слот‑трекери одного предмету (GroupSubject) у серії та обмежує доступні доменні значення для кожного слоту так,
        /// щоб вони покривали лише початковий період (наприклад, перший тиждень для щотижневого розкладу або перші два тижні для бітижневого).
        /// Серії - це послідовність слотів, які мають однаковий предмет і мають строгу періодичність занять через тиждень або два тижні.
        /// </summary>
        private void GroupAndFilterSeries()
        {
            int currentSeriesId = 1;

            // для предметів з визначеними серіями потрібно визначити для кожного слоту серію та номер уроку в серії
            foreach (var subject in defaultUnit.Root.GroupSubjects.Where(x => x.Subject.DefinedSeries != null && x.Subject.DefinedSeries.Count > 0))
            {
                // Отримуємо всі слот‑трекери для предмету
                var trackers = subject.ScheduleSlots
                    .Select(slot => defaultUnit.Slots[slot])
                    .OrderBy(t => t.ScheduleSlot.LessonNumber)
                    .ToList();

                if (trackers.Count == 0)
                    continue;

                // Розбиваємо слоти по серіям відповідно до визначених серій предмету
                var definedSeries = subject.Subject.DefinedSeries.OrderBy(s => s.SeriesNumber).ToList();
                int trackerIndex = 0;

                foreach (var series in definedSeries.OrderByDescending(x => x.NumberOfLessons))
                {
                    if (trackerIndex >= trackers.Count)
                        break;

                    // Визначаємо скільки слотів ми можемо включити в цю серію
                    int slotsToTake = Math.Min(series.NumberOfLessons, trackers.Count - trackerIndex);
                    
                    if (slotsToTake <= 0)
                        continue;

                    // Визначаємо тип розбиття (щотижневий або через тиждень)
                    int weekShift = series.SplitType == ScriptModels.SubjectSeriesSplitType.Weekly ? 1 : 2;
                    
                    // Додаємо слоти до серії
                    var currentSeries = trackers.GetRange(trackerIndex, slotsToTake);
                    var firstTracker = currentSeries.First();
                    var lastTracker = currentSeries.Last();

                    // Перевіряємо, що довжина серії дозволяє включити всі заняття в період навчання
                    DateTime minDate = firstTracker.AvailableDomains.Min().Date;
                    var maxDate = minDate.AddDays((currentSeries.Count-1) * 7 * weekShift);
                    if (maxDate > lastTracker.AvailableDomains.Max().Date  )
                    {
                        throw new Exception($"Недостатньо тижнів для розміщення серії '{subject.Subject.Name}' номер серії {series.SeriesNumber}.");
                    }

                    // перевірка чи останній тиждень серії має достатньо днів
                    var isLowDaysDanger = maxDate > lastTracker.AvailableDomains.Max().Date.AddDays(-7);
                    
                    // Встановлюємо параметри для всіх слотів серії
                    foreach (var tracker in currentSeries)
                    {
                        tracker.SeriesId = currentSeriesId;
                        tracker.SeriesLength = slotsToTake;
                        tracker.WeekShift = weekShift;
                        tracker.IsLowDaysDanger = isLowDaysDanger;
                    }

                    // Перший слот в серії позначаємо особливо і обмежуємо доменні значення
                    firstTracker.IsFirstTrackerInSeries = true;


                    // вираховуємо останній день на який може  припасти перший слот серії, щоб влізла вся серія
                    DateTime lastDayForFirstSlot;
                    if (series.StartInAnyWeek)
                    {
                        lastDayForFirstSlot = lastTracker.AvailableDomains.Max().Date.AddDays(-(currentSeries.Count-1) * 7 * weekShift);;
                    }
                    else
                    {
                        lastDayForFirstSlot = firstTracker.AvailableDomains.Min().Date.AddDays(7 * weekShift - 1);
                    }                    
              
                    // Обмежуємо доменні значення першого слота серії, щоб вони покривали лише перший тиждень або два
                     var rejectsForTracker = firstTracker.AvailableDomains
                        .Where(x => x.Date > lastDayForFirstSlot)
                        .ToList();

                    foreach (var ad in rejectsForTracker)
                        firstTracker.AvailableDomains.Remove(ad);

                    // Переходимо до наступної серії
                    trackerIndex += slotsToTake;
                    currentSeriesId++;
                }

                // Перевіряємо, чи всі слоти розподілені
                if (trackerIndex < trackers.Count)
                {
                    throw new Exception($"Не вдалося розподілити всі слоти для предмету '{subject.Subject.Name}'. Сума визначених серій ({definedSeries.Sum(s => s.NumberOfLessons)}) менша за кількість слотів ({trackers.Count}).");
                }
            }

            foreach (var subject in defaultUnit.Root.GroupSubjects.Where(x => x.Subject.DefinedSeries == null || x.Subject.DefinedSeries.Count == 0))
            {
                // Отримуємо всі слот‑трекери для предмету (за даними GroupSubject.ScheduleSlots)
                var trackers = subject.ScheduleSlots
                    .Select(slot => defaultUnit.Slots[slot])
                    .OrderBy(t => t.ScheduleSlot.LessonNumber)
                    .ToList();


                // Проста логіка групування: послідовні слоти для одного предмету вважаємо однією серією.
                // (Більш складна евристика може враховувати перетин доменних значень тощо.)
                foreach (var tracker in trackers)
                {
                    if (!tracker.SeriesId.HasValue)
                    {
                        tracker.SeriesId = currentSeriesId;
                        tracker.IsFirstTrackerInSeries = true;

                        var freeTrackers = trackers.Where(t => !t.SeriesId.HasValue).Union([tracker]).OrderBy(t => t.ScheduleSlot.LessonNumber).ToList();

                        // перевірка що є доступні доменні значення
                        if (tracker.AvailableDomains.Count == 0)
                            throw new Exception($"Груповий предмет {subject.Subject.Name} для групи {subject.Groups.First().Name} не має доступних доменних значень. Перевірте обмеження.");

                        // Визначаємо загальний період доступності: мінімальна і максимальна дата
                        var minAvailable = tracker.AvailableDomains.Min();
                        var maxAvailable = freeTrackers.Select(d => d.AvailableDomains.Max()).Max();

                        var td = (maxAvailable.Date - minAvailable.Date).TotalDays + 1;
                        double totalWeeks = td / 7.0;
                        var lastWeekDays = td % 7;

                        // Визначення що на останній тиждень попадає не повна кількість днів
                        bool lastLowDays = lastWeekDays > 0 && freeTrackers.SelectMany(t => t.AvailableDomains).Count(d => d.Date > maxAvailable.Date.AddDays(-7)) >
                            freeTrackers.SelectMany(t => t.AvailableDomains).Count(d => d.Date > maxAvailable.Date.AddDays(-lastWeekDays));

                        int roundedWeeks = (int)Math.Ceiling(totalWeeks);

                        bool isEven = roundedWeeks % 2 == 0;
                        var maxWeeksFor2WeekDistrib = isEven ? roundedWeeks : roundedWeeks + 1;

                        // якщо кількість слотів дорівнює кількості тижнів, то всі слоти призначаються на цей тиждень
                        if (freeTrackers.Count == roundedWeeks)
                        {
                            foreach (var t in freeTrackers)
                            {
                                t.SeriesLength = roundedWeeks;
                                t.SeriesId = currentSeriesId;
                                t.WeekShift = 1;
                                t.IsLowDaysDanger = lastLowDays;
                            }

                            var rejectsForTracker = tracker.AvailableDomains.Where(x => x.Date >= minAvailable.Date.AddDays(7)).ToList();

                            foreach (var ad in rejectsForTracker)
                                tracker.AvailableDomains.Remove(ad);

                        }
                        else if (freeTrackers.Count < roundedWeeks) // якщо кількість слотів менша за кількість тижнів, то всі слоти призначаються на цей тиждень
                        {
                            int ws = (roundedWeeks - 1) / freeTrackers.Count;
                            if (ws > 2) // незрозумілий варіант 
                            {
                                foreach (var t in freeTrackers)
                                {
                                    t.SeriesLength = freeTrackers.Count;
                                    t.SeriesId = currentSeriesId;
                                    t.WeekShift = 1;
                                    t.IsLowDaysDanger = false;
                                }

                                var rejectsForTracker = tracker.AvailableDomains.Where(x => x.Date >= minAvailable.Date.AddDays(7 * (roundedWeeks + 1 - freeTrackers.Count))).ToList();
                                foreach (var ad in rejectsForTracker)
                                    tracker.AvailableDomains.Remove(ad);
                            }
                            else if (ws == 2) // через два тижні
                            {
                                foreach (var t in freeTrackers)
                                {
                                    t.SeriesLength = freeTrackers.Count;
                                    t.SeriesId = currentSeriesId;
                                    t.WeekShift = 2;
                                    t.IsLowDaysDanger = false;
                                }

                                var rejectsForTracker = tracker.AvailableDomains.Where(x => x.Date >= minAvailable.Date.AddDays(14)).ToList();

                                foreach (var ad in rejectsForTracker)
                                    tracker.AvailableDomains.Remove(ad);
                            }
                            else
                            {
                                if (maxWeeksFor2WeekDistrib / freeTrackers.Count == 2)
                                {
                                    foreach (var t in freeTrackers)
                                    {
                                        t.SeriesLength = freeTrackers.Count;
                                        t.SeriesId = currentSeriesId;
                                        t.WeekShift = 2;
                                        t.IsLowDaysDanger = lastLowDays;
                                    }
                                    var rejectsForTracker = tracker.AvailableDomains.Where(x => x.Date >= minAvailable.Date.AddDays(14)).ToList();
                                    foreach (var ad in rejectsForTracker)
                                        tracker.AvailableDomains.Remove(ad);
                                }
                                else
                                {
                                    foreach (var t in freeTrackers)
                                    {
                                        t.SeriesLength = freeTrackers.Count;
                                        t.SeriesId = currentSeriesId;
                                        t.WeekShift = 1;
                                        t.IsLowDaysDanger = false;
                                    }
                                    var rejectsForTracker = tracker.AvailableDomains.Where(x => x.Date >= minAvailable.Date.AddDays(7)).ToList();
                                    foreach (var ad in rejectsForTracker)
                                        tracker.AvailableDomains.Remove(ad);
                                }
                            }
                        }
                        else // якщо кількість слотів більша за кількість тижнів
                        {
                            if (roundedWeeks == 1 || roundedWeeks == 0) // специфіка для одного тижня. консультації наприклад
                            {
                                foreach (var t in freeTrackers)
                                {
                                    t.SeriesLength = 1;
                                    t.SeriesId = currentSeriesId++;
                                    t.WeekShift = 1;
                                    t.IsLowDaysDanger = false;
                                    t.IsFirstTrackerInSeries = true;
                                }
                            }
                            else
                            {
                                // Кількість слотів на тиждень для рівномірного розподілу
                                int slotsPerWeek = (int)Math.Ceiling((double)freeTrackers.Count / roundedWeeks);

                                // Кількість серій, яка потрібна для розподілу всіх слотів
                              //  int requiredSeries = (int)Math.Ceiling((double)freeTrackers.Count / slotsPerWeek);

                                if (slotsPerWeek <= 1)
                                    throw new Exception("slotsPerWeek <= 1");

                                List<int> seriesLengths = new List<int>();

                                for (int i = 0; i < slotsPerWeek - 1; i++)
                                {
                                    seriesLengths.Add(roundedWeeks);
                                }

                                var remains = freeTrackers.Count - seriesLengths.Sum();

                                if (remains < 0)
                                    throw new Exception("remains < 0");

                                // остання серія не може бути більшою за розподіл раз на 2 тижні
                                var remainsFor2Week = remains <= maxWeeksFor2WeekDistrib / 2;

                                if (remains > 0)
                                {
                                    // остання серія не може бути більшою за розподіл раз на 2 тижні
                                    if (remainsFor2Week)
                                    {
                                        for (int i = seriesLengths.Count * roundedWeeks - 1; i >= 0; i--)
                                        {
                                            var seriesIndex = i % seriesLengths.Count;
                                            seriesLengths[seriesIndex]--;
                                            remains++;



                                            if (remains > maxWeeksFor2WeekDistrib / 2 || remains * 2 > seriesLengths[0])
                                            {
                                                // undo
                                                seriesLengths[seriesIndex]++;
                                                remains--;

                                                break;
                                            }

                                        }
                                    }
                                    else
                                    {
                                        // остання серія розподіл потижнево
                                        for (int i = seriesLengths.Count * roundedWeeks - 1; i >= 0; i--)
                                        {
                                            var seriesIndex = i % seriesLengths.Count;
                                            seriesLengths[seriesIndex]--;
                                            remains++;

                                            if (remains > seriesLengths[0])
                                            {
                                                // undo
                                                seriesLengths[seriesIndex]++;
                                                remains--;

                                                break;
                                            }

                                        }
                                    }
                                }


                                // заповнюємо слоти інформацією про серії
                                int trackerIndex = 0;
                                for (int i = 0; i < seriesLengths.Count; i++)
                                {
                                    int seriesLength = seriesLengths[i];
                                    if (seriesLength <= 0 || trackerIndex >= freeTrackers.Count)
                                        continue;

                                    // Визначаємо кількість слотів для поточної серії
                                    int slotsToTake = Math.Min(seriesLength, freeTrackers.Count - trackerIndex);

                                    if (slotsToTake < seriesLength)
                                        throw new Exception("slotsToTake < seriesLength");

                                    // Додаємо слоти до серії
                                    var currentSeries = freeTrackers.GetRange(trackerIndex, slotsToTake);

                                    foreach (var t in currentSeries)
                                    {
                                        t.SeriesLength = slotsToTake;
                                        t.SeriesId = currentSeriesId;
                                        t.WeekShift = 1;
                                        t.IsLowDaysDanger = lastLowDays && slotsToTake == roundedWeeks;
                                    }

                                    var firstTracker = currentSeries.First();
                                    firstTracker.IsFirstTrackerInSeries = true;

                                    var rejectsForTracker = firstTracker.AvailableDomains.Where(x => x.Date >= minAvailable.Date.AddDays(7)).ToList();
                                    foreach (var ad in rejectsForTracker)
                                        firstTracker.AvailableDomains.Remove(ad);
                                    // Оновлюємо індекс для наступної серії
                                    trackerIndex += slotsToTake;
                                    currentSeriesId++;
                                }

                                // заповнюємо останню серію
                                if (remains > 0)
                                {
                                    int slotsToTake = Math.Min(remains, freeTrackers.Count - trackerIndex);

                                    if (slotsToTake < remains)
                                        throw new Exception("slotsToTake < remains");

                                    // Додаємо слоти до серії
                                    var currentSeries = freeTrackers.GetRange(trackerIndex, slotsToTake);

                                    foreach (var t in currentSeries)
                                    {
                                        t.SeriesLength = slotsToTake;
                                        t.SeriesId = currentSeriesId;
                                        t.WeekShift = remainsFor2Week ? 2 : 1;
                                        t.IsLowDaysDanger = lastLowDays;
                                    }

                                    var firstTracker = currentSeries.First();
                                    firstTracker.IsFirstTrackerInSeries = true;

                                    var rejectsForTracker = firstTracker.AvailableDomains.Where(x => x.Date >= minAvailable.Date.AddDays(remainsFor2Week ? 14 : 7)).ToList();
                                    foreach (var ad in rejectsForTracker)
                                        firstTracker.AvailableDomains.Remove(ad);
                                }
                            }
                        }

                        currentSeriesId++;
                    }
                }
            }
        }

        public string GetName()
        {
           return "Default";
        }

        /// <summary>
        /// Отримує список параметрів, які використовує алгоритм
        /// </summary>
        /// <returns>Список параметрів з описом, типом та значенням за замовчуванням</returns>
        public List<AlgorithmParameterDTO> GetParameters()
        {
            return new List<AlgorithmParameterDTO>
            {
                new AlgorithmParameterDTO
                {
                    Name = "ResultsCount",
                    Description = "Кількість результатів, які потрібно знайти",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "1",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "MaxIterations",
                    Description = "Максимальна кількість ітерацій",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "10",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "TimeoutInSeconds",
                    Description = "Максимальний час роботи алгоритму в секундах",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "600",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "SlotsTopK",
                    Description = "Кількість кращих слотів для вибору",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "3",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "DomainsTopK",
                    Description = "Кількість кращих доменів для вибору",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "1",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "SlotsTemperature",
                    Description = "Температура для вибору слотів",
                    DataType = AlgorithmParameterType.Decimal,
                    DefaultValue = "1",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "DomainsTemperature",
                    Description = "Температура для вибору доменів",
                    DataType = AlgorithmParameterType.Decimal,
                    DefaultValue = "1",
                    IsRequired = false
                }
            };
        }
    }
}