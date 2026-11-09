using System.Collections.Concurrent;
using AcaTime.Algorithm.Genetic.Models;
using AcaTime.Algorithm.Genetic.Models.Genetic;
using AcaTime.Algorithm.Genetic.Utils;
using AcaTime.ScheduleCommon.Abstract;
using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScheduleCommon.Models.Constraints;
using AcaTime.ScriptModels;
using Microsoft.Extensions.Logging;

namespace AcaTime.Algorithm.Genetic.Services
{
    /// <summary>
    /// Алгоритм побудови розкладу
    /// </summary>
    public class GeneticScheduleAlgorithm : IScheduleAlgorithm
    {
        public AlgorithmParams RunParameters { get; private set; }
        public DateTime StartTime { get; private set; }

        // GeneticScheduleAlgorithmUnit defaultUnit;
        DefaultScheduleAlgorithmUnit defaultUnit;
        
        private ILogger logger;
        private AlgorithmStatistics statistics = new AlgorithmStatistics();

        private DefaultScheduleAlgorithmUnit savedUnit;

        /// <summary>
        /// Compact start population produced by independent Default runs.
        /// Full solver states are intentionally not retained here.
        /// </summary>
        public IReadOnlyList<ScheduleGenome> InitialGenomes { get; private set; } = Array.Empty<ScheduleGenome>();

        public async Task<List<AlgorithmResultDTO>> Run(FacultySeasonDTO root, UserFunctions userFunctions, Dictionary<string, string> parameters, bool ignoreClassrooms, ILogger logger, CancellationToken cancellationToken = default)
        {
            this.logger = logger;

            var runParameters = new AlgorithmParams(parameters);

            this.RunParameters = runParameters;
            this.StartTime = DateTime.Now;

            // defaultUnit = new GeneticScheduleAlgorithmUnit();
            // defaultUnit.Setup(root, logger, userFunctions, runParameters);
            
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

            // time for one iteration
            var initialPopulationSize = Math.Max(1, runParameters.InitialPopulationSize);

            // Паралельні сіди: кожен сід = власний клон стану (незалежні
            // об'єктні графи — Root.Clone на кожного). Бюджет потоків =
            // ProcessorCount - 1 (cgroup-aware у .NET 6+: Docker/K8s ліміти
            // враховуються) з override ParallelLineages — не перевищуємо те,
            // що виділив адмін.
            var seedWorkers = Math.Max(1, Math.Min(initialPopulationSize,
                runParameters.ParallelLineages > 0
                    ? runParameters.ParallelLineages
                    : Math.Max(1, Environment.ProcessorCount - 1)));

            // Сіди ПОСЛІДОВНО: паралельні сіди НЕ thread-safe (20260831-131416:
            // 3/5 ранів KeyNotFoundException у ForwardCheck — shared state між
            // клонами Default-юніта; вимагає аудиту Default-кодбази).
            // Lineages (Genetic) паралельні БЕЗПЕЧНО — кожен має власний граф.
            ParallelOptions parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 1,
                CancellationToken = linkedCts.Token
            };

            // per-seed cap: стіна фази сідів = N × timeOneSec (послідовно)
            var timeOneSec = runParameters.TimeoutInSeconds / initialPopulationSize;

            // КЛОНУВАННЯ СІДІВ ПОСЛІДОВНЕ (Clone не thread-safe — конкурентні
            // клони дали KeyNotFoundException у ForwardCheck, 131113), тільки
            // RUN-и паралельні: кожен юніт — власний об'єктний граф.
            var seedUnits = new List<DefaultScheduleAlgorithmUnit>(initialPopulationSize);
            for (var i = 0; i < initialPopulationSize; i++)
                seedUnits.Add(defaultUnit.Clone());

            statistics = new AlgorithmStatistics();
            var initialGenomes = new ConcurrentBag<ScheduleGenome>();

            try
            {
                // Запускаємо паралельні обчислення

                logger.LogInformation($"Початок розрахунку. Кількість ітерацій: {runParameters.MaxIterations}. Кількість паралельних обчислень: {parallelOptions.MaxDegreeOfParallelism}");
                await Parallel.ForEachAsync(
                    Enumerable.Range(0, initialPopulationSize),
                    parallelOptions,
                    async (i, token) =>
                    {
                        // var unit = defaultUnit.Clone();
                        var unit = seedUnits[i];

                        // set timeout for one iteration
                        var timeoutOneCts = new CancellationTokenSource();
                        var linkedOneCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutOneCts.Token, token);

                        timeoutOneCts.CancelAfter(TimeSpan.FromSeconds(timeOneSec));

                        // var res = await unit.Run(root, userFunctions, null, ignoreClassrooms, logger, linkedOneCts.Token);
                        // var result = res.Count != 0 ? res[0] : null;

                        
                        var result = await unit.Run(linkedOneCts.Token, ignoreClassrooms).ConfigureAwait(false);
                        
                          if (result != null)
                          {
                                   lock (results)
                                   {
                                statistics.Success++;
                                result.Name = "Default";
                                initialGenomes.Add(ScheduleGenome.FromResult(result));
                                if (savedUnit == null)
                                {
                                    savedUnit = unit;
                                }

                                if (result.TotalEstimation > savedUnit.Estimate())
                                {
                                    savedUnit = unit;
                                }
                                statistics.BestResult = Math.Max(statistics.BestResult, result.TotalEstimation);
                            }
                        }
                        else
                        {
                            statistics.Failed++;
                        }
                    });
            }
            catch (OperationCanceledException)
            {
                // Операція була скасована через таймаут або токен скасування
                logger.LogInformation("Обчислення алгоритму було перервано через таймаут або зовнішнє скасування");
            }

            logger.LogInformation($"Завершено розрахунку. Кількість успішних результатів: {statistics.Success}, Найкращий результат: {statistics.BestResult}");
            InitialGenomes = initialGenomes.ToList();

            // Keep only one full solver state during the initial population phase.
            // The remaining candidates are represented by compact genomes.
            if (savedUnit != null)
            {
                results.Add(new AlgorithmResultDTO
                {
                    TotalEstimation = savedUnit.Estimate(),
                    ScheduleSlots = savedUnit.Slots.Values.Where(v => v.IsAssigned).Select(x => x.ScheduleSlot).ToList(),
                    Name = "Default"
                });
            }
            
            // Повертаємо найкращі результати
            var res = results
                .OrderByDescending(x => x.TotalEstimation)
                .Take(runParameters.ResultsCount)
                .ToList();

            if (res.Count > 0)
            {
                var defaultResult = res[0];
                
                // logger.LogInformation($"ПОЧИНАЄМО РАХУВАТИ. DEFAULT RESULT: {defaultResult.TotalEstimation}");
                // logger.LogInformation($"ПОЧИНАЄМО РАХУВАТИ. GENETIC START: {unit.initialResult.TotalEstimation}");

                var before = defaultResult.TotalEstimation;
                
                // Calculate(unit);
                 // Default generation and Genetic branches have independent budgets.
                 // The initial population phase can otherwise consume the shared token
                 // before the short kick branches get a chance to run.
                 var algoRes = await Cal(RunParameters, ignoreClassrooms);
                // var r = algoRes.Select(r => r).OrderBy(r => r.TotalEstimation).First();
                // Never expose a Genetic result that is worse than the Default
                // result; Default remains the safe production baseline.
                res.AddRange(algoRes
                    .Where(r => r.TotalEstimation > before)
                    .OrderBy(r => r.TotalEstimation));
                res = res
                    .OrderByDescending(r => r.TotalEstimation)
                    .Take(runParameters.ResultsCount)
                    .ToList();

                // The genetic unit used by Cal owns the actual result. The
                // separate compatibility field initialResult is not populated
                // by this path and must not be used for diagnostics.
                var after = res
                    .Select(x => x.TotalEstimation)
                    .DefaultIfEmpty(before)
                    .Max();
                logger.LogInformation($"БУЛО: {before}");
                logger.LogInformation($"СТАЛО: {after}");

                // if (after > before)
                // {
                //     res.Insert(0, unit.initialResult);
                // }
            }

            return res;
        }

        private async Task<ConcurrentBag<AlgorithmResultDTO>> Cal(AlgorithmParams runParameters, bool ignoreClassrooms, CancellationToken cancellationToken = default)
        {
            // Створюємо джерело токенів скасування, яке можна використовувати для обмеження часу виконання
            using var timeoutCts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

            ParallelOptions parallelOptions = new ParallelOptions
            {
                // Each lane owns a large mutable solver state.
                MaxDegreeOfParallelism = Math.Max(1, runParameters.MaxParallelBranches),
                CancellationToken = linkedCts.Token
            };

            var rankedSeeds = InitialGenomes
                .OrderByDescending(x => x.Fitness ?? int.MinValue)
                .ToList();
            var bestSeedScore = rankedSeeds.FirstOrDefault()?.Fitness ?? int.MinValue;
            var minimumBranchScore = bestSeedScore == int.MinValue
                ? int.MinValue
                : (int)(bestSeedScore * runParameters.PopulationBranchMinimumScoreRatio);
            var populationSeeds = rankedSeeds
                .Where(x => (x.Fitness ?? int.MinValue) >= minimumBranchScore)
                .Take(Math.Max(1, runParameters.PopulationBranches))
                .ToList();
            logger.LogInformation(
                $"Population seeds: {populationSeeds.Count}/{rankedSeeds.Count}, " +
                $"мінімальний score гілки {minimumBranchScore}");
            statistics = new AlgorithmStatistics();
            var results = new ConcurrentBag<AlgorithmResultDTO>();
            var branchOutcomes = new ConcurrentBag<(AlgorithmResultDTO Result, IReadOnlyList<ScheduleDeltaEvent> Deltas)>();
            var branchCount = populationSeeds.Count;

            try
            {
                // Запускаємо паралельні обчислення

                 logger.LogInformation($"Початок розрахунку. Кількість гілок: {branchCount}. Кількість ітерацій: {runParameters.MaxIterations}. Кількість паралельних обчислень: {parallelOptions.MaxDegreeOfParallelism}");
                 await Parallel.ForEachAsync(
                     Enumerable.Range(0, branchCount),
                     parallelOptions,
                     async (i, token) =>
                     {
                         var seed = populationSeeds[i];
                        // var unit = defaultUnit.Clone();
                        // var unit = defaultUnit.Clone();
                        
                         GeneticScheduleAlgorithmUnit unit = savedUnit.CloneFromDefault();

                             // Classroom placement is a shared resource and is
                             // repaired separately after the time genes transfer.
                              unit.ApplyGenome(seed, applyClassrooms: false);
                              var laneName = i == 0 ? "population-best" : $"population-{i + 1}";
                              logger.LogInformation($"Genetic lane: {laneName}, стартовий score {unit.Estimate()}");

                          // set timeout for one iteration
                         var timeoutOneCts = new CancellationTokenSource();
                          var linkedOneCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutOneCts.Token, token);

                         var timeoutSeconds = i == 0
                             ? runParameters.TimeoutInSeconds
                             : runParameters.KickTimeoutInSeconds;
                         timeoutOneCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                        // var res = await unit.Run(root, userFunctions, null, ignoreClassrooms, logger, linkedOneCts.Token);
                        // var result = res.Count != 0 ? res[0] : null;
                        
                          var result = await unit.Run(
                              linkedOneCts.Token,
                              ignoreClassrooms,
                              hgtDonors: null,
                              kick: false,
                              iterationsOverride: i == 0
                                  ? runParameters.GeneticIterations
                                  : runParameters.PopulationBranchIterations).ConfigureAwait(false);
                        
                         if (result != null)
                         {
                                  lock (results)
                                  {
                                      statistics.Success++;
                                   result.Name = GetName();
                                   results.Add(result);
                                   branchOutcomes.Add((result, unit.AcceptedDeltaEvents));
                                 // Сортуємо та обмежуємо кількість результатів при необхідності
                                // if (results.Count > runParameters.ResultsCount)
                                // {
                                //     var sortedResults = results.OrderByDescending(x => x.TotalEstimation).Take(runParameters.ResultsCount).ToList();
                                //     results.Clear();
                                //     foreach (var sortedResult in sortedResults)
                                //     {
                                //         results.Add(sortedResult);
                                //     }                                    
                                // }

                                      statistics.BestResult = results.Max(x => x.TotalEstimation);
                                   }
                          }
                          else
                          {
                              lock (results)
                              {
                                  statistics.Failed++;
                                  // A branch can still be a delta recipient even when it
                                  // did not improve its own starting score.
                                  branchOutcomes.Add((
                                      new AlgorithmResultDTO
                                      {
                                          Name = GetName(),
                                          TotalEstimation = unit.Estimate(),
                                          ScheduleSlots = unit.Slots.Values
                                              .Where(x => x.IsAssigned)
                                              .Select(x => x.ScheduleSlot)
                                              .ToList()
                                      },
                                      unit.AcceptedDeltaEvents));
                              }
                          }
                      });

                 await TryTransferAcceptedDelta(
                     branchOutcomes.ToList(),
                     runParameters,
                     ignoreClassrooms,
                     linkedCts.Token,
                     results).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Операція була скасована через таймаут або токен скасування
                logger.LogInformation("Обчислення алгоритму було перервано через таймаут або зовнішнє скасування");
            }

            return results;
        }

        private async Task TryTransferAcceptedDelta(
            IReadOnlyList<(AlgorithmResultDTO Result, IReadOnlyList<ScheduleDeltaEvent> Deltas)> outcomes,
            AlgorithmParams runParameters,
            bool ignoreClassrooms,
            CancellationToken cancellationToken,
            ConcurrentBag<AlgorithmResultDTO> results)
        {
            if (outcomes.Count < 2)
                return;

            var recipient = outcomes.OrderByDescending(x => x.Result.TotalEstimation).First();
            var positiveDeltas = outcomes
                .Where(x => !ReferenceEquals(x.Result, recipient.Result))
                .SelectMany(x => x.Deltas)
                .Where(x => x.ScoreDelta > 0)
                .OrderByDescending(x => x.ScoreDelta)
                .DistinctBy(x => string.Join(
                    ";",
                    x.Changes
                        .OrderBy(change => change.Key.GroupSubjectId)
                        .ThenBy(change => change.Key.SlotId)
                        .ThenBy(change => change.Key.LessonNumber)
                        .Select(change =>
                            $"{change.Key.GroupSubjectId}:{change.Key.SlotId}:{change.Key.LessonNumber}:" +
                            $"{change.Value.Date.Ticks}:{change.Value.PairNumber}:{change.Value.ClassroomId}")))
                .ToList();
            if (positiveDeltas.Count == 0)
            {
                logger.LogInformation("Delta transfer: немає позитивної delta для переносу");
                return;
            }

            var recombinations = positiveDeltas
                .Take(5)
                .SelectMany((first, index) => positiveDeltas
                    .Skip(index + 1)
                    .Take(5)
                    .Select(second => ScheduleDeltaEvent.Combine(first, second)))
                .Where(x => x != null)
                .Cast<ScheduleDeltaEvent>();
            var deltas = positiveDeltas
                .Concat(recombinations)
                .OrderByDescending(x => x.ScoreDelta)
                .Take(Math.Max(1, runParameters.OperationAttemptsPerIteration))
                .ToList();

            logger.LogInformation($"Delta transfer: відібрано {deltas.Count} унікальних delta");

            var transferResults = new ConcurrentBag<AlgorithmResultDTO>();
            await Parallel.ForEachAsync(
                deltas,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, runParameters.MaxParallelBranches),
                    CancellationToken = cancellationToken
                },
                async (delta, token) =>
                {
                    var candidate = ScheduleGenome.FromResult(recipient.Result);
                    delta.ApplyTo(candidate);
                    var unit = savedUnit.CloneFromDefault();
                    unit.ApplyGenome(candidate, applyClassrooms: false);
                    var startScore = unit.Estimate();
                    logger.LogInformation(
                        $"Delta transfer: recipient {recipient.Result.TotalEstimation}, " +
                        $"delta {delta.ScoreDelta}, стартовий score {startScore}");

                    var transferredResult = startScore > recipient.Result.TotalEstimation
                        ? new AlgorithmResultDTO
                        {
                            TotalEstimation = startScore,
                            ScheduleSlots = unit.Slots.Values
                                .Where(x => x.IsAssigned)
                                .Select(x => x.ScheduleSlot)
                                .ToList()
                        }
                        : null;

                    using var timeoutCts = new CancellationTokenSource();
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(runParameters.KickTimeoutInSeconds));
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, token);
                    var result = await unit.Run(
                        linked.Token,
                        ignoreClassrooms,
                        hgtDonors: null,
                        kick: false,
                        iterationsOverride: runParameters.DeltaTransferIterations).ConfigureAwait(false);

                    var candidateResult = result != null && (transferredResult == null || result.TotalEstimation > transferredResult.TotalEstimation)
                        ? result
                        : transferredResult;
                    if (candidateResult != null)
                        transferResults.Add(candidateResult);
                }).ConfigureAwait(false);

            var bestTransfer = transferResults
                .OrderByDescending(x => x.TotalEstimation)
                .FirstOrDefault();

            if (bestTransfer != null && bestTransfer.TotalEstimation > recipient.Result.TotalEstimation)
            {
                bestTransfer.Name = GetName();
                results.Add(bestTransfer);
                logger.LogInformation($"Delta transfer прийнято: {recipient.Result.TotalEstimation} -> {bestTransfer.TotalEstimation}");
            }
            else
                logger.LogInformation("Delta transfer відхилено");
        }

        private ScheduleGenome? SelectHgtSeed(out List<long> transferredBlockIds)
        {
            transferredBlockIds = new List<long>();
            var recipient = InitialGenomes
                .OrderByDescending(x => x.Fitness ?? int.MinValue)
                .FirstOrDefault();
            if (recipient == null)
                return null;

            if (RunParameters.HgtAttempts <= 0 || RunParameters.HgtBlockCount <= 0)
            {
                logger.LogInformation("HGT: вимкнено параметрами");
                return recipient;
            }

            var donor = InitialGenomes
                .Where(x => !ReferenceEquals(x, recipient))
                .OrderByDescending(x => x.Fitness ?? int.MinValue)
                .FirstOrDefault();
            if (donor == null)
                return recipient;

            var candidates = InitialGenomes
                .Where(x => !ReferenceEquals(x, recipient))
                .OrderByDescending(x => x.Fitness ?? int.MinValue)
                .Take(Math.Max(1, RunParameters.HgtAttempts))
                .SelectMany(donorGenome => donorGenome.Genes.Keys
                    .Select(x => x.GroupSubjectId)
                    .Distinct()
                    .Select(id => new { Donor = donorGenome, Id = id, Differences = CountBlockDifferences(recipient, donorGenome, id) }))
                .Where(x => x.Differences > 0)
                .OrderByDescending(x => (x.Donor.Fitness ?? int.MinValue, x.Differences))
                .Take(Math.Max(1, RunParameters.HgtAttempts))
                .ToList();

            ScheduleGenome? bestHgt = null;
            var bestHgtScore = int.MinValue;
            foreach (var candidate in candidates)
            {
                var child = recipient.Clone();
                var changes = child.TransferGroupSubjectFrom(candidate.Donor, candidate.Id);
                changes.Commit();

                var evaluated = EvaluateHgtCandidate(child, candidate.Id, out var repairedGenome, out var rawScore, out var repairedScore, out var repairSucceeded);
                logger.LogInformation(
                    $"HGT: донор {candidate.Donor.Fitness}, реципієнт {recipient.Fitness}, " +
                    $"GroupSubject {candidate.Id}, генів змінено {candidate.Differences}, " +
                    $"score {rawScore} -> repair {repairedScore}, repair {(repairSucceeded ? "успішний" : "невдалий")}");

                if (evaluated && repairedScore > bestHgtScore)
                {
                    bestHgt = repairedGenome;
                    bestHgtScore = repairedScore;
                    transferredBlockIds = [candidate.Id];
                }
            }

            logger.LogInformation($"HGT: найкращий offspring score {bestHgtScore}, спроб {candidates.Count}");
            return bestHgt ?? recipient;
        }

        private bool EvaluateHgtCandidate(
            ScheduleGenome candidate,
            long blockId,
            out ScheduleGenome repairedGenome,
            out int rawScore,
            out int repairedScore,
            out bool repairSucceeded)
        {
            var unit = savedUnit.CloneFromDefault();
            unit.ApplyGenome(candidate, applyClassrooms: false);
            rawScore = unit.Estimate();
            repairSucceeded = unit.TryRepairGroupSubjects([blockId]);
            repairedScore = unit.Estimate();
            repairedGenome = ScheduleGenome.FromSlots(
                unit.Slots.Values.Where(x => x.IsAssigned).Select(x => x.ScheduleSlot),
                repairedScore);
            return repairSucceeded;
        }

        private static int CountDifferences(ScheduleGenome left, ScheduleGenome right)
        {
            return left.Genes.Keys
                .Union(right.Genes.Keys)
                .Count(key => !left.Genes.TryGetValue(key, out var leftGene) ||
                             !right.Genes.TryGetValue(key, out var rightGene) ||
                             leftGene != rightGene);
        }

        private static int CountBlockDifferences(ScheduleGenome recipient, ScheduleGenome donor, long blockId)
        {
            return donor.Genes
                .Where(x => x.Key.GroupSubjectId == blockId)
                .Count(x => !recipient.Genes.TryGetValue(x.Key, out var gene) || gene != x.Value);
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
                    int weekShift = series.SplitType == AcaTime.ScriptModels.SubjectSeriesSplitType.Weekly ? 1 : 2;
                    
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
           return "Genetic";
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
                },
                // для ген алгоритму
                new AlgorithmParameterDTO
                {
                    Name = "GeneticIterations",
                    Description = "Кількість ітерацій",
                    DataType = AlgorithmParameterType.Decimal,
                    DefaultValue = "100",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "InitialPopulationSize",
                    Description = "Кількість незалежних стартових Default-рішень",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "1",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "HgtAttempts",
                    Description = "Кількість спроб HGT",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "1",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "HgtBlockCount",
                    Description = "Кількість блоків HGT",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "1",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "KickAfterStagnation",
                    Description = "Кількість ітерацій без покращення до kick",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "6",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "KickSeriesCount",
                    Description = "Кількість серій у kick",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "2",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "KickLocalIterations",
                    Description = "Кількість ітерацій короткої kick-гілки",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "8",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "KickBranches",
                    Description = "Кількість незалежних kick-гілок",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "0",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "KickTimeoutInSeconds",
                    Description = "Таймаут однієї kick-гілки",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "20",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "PopulationBranches",
                    Description = "Кількість незалежних population-гілок",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "1",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "PopulationBranchIterations",
                    Description = "Кількість ітерацій додаткової population-гілки",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "25",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "MaxParallelBranches",
                    Description = "Максимальна кількість одночасних population-гілок",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "1",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "IntraBranchPopulationSize",
                    Description = "Кількість повних Individual усередині однієї гілки",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "1",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "OperationAttemptsPerIteration",
                    Description = "Кількість Genetic-операцій на одного індивідуума за ітерацію",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "1",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "MutationTournamentAttempts",
                    Description = "Кількість спроб вибору серії у mutation tournament",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "3",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "MutationDomainCandidates",
                    Description = "Максимальна кількість доменів для перевірки на одну серію",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "8",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "DeltaTransferIterations",
                    Description = "Кількість ітерацій Genetic для delta transfer",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "8",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "DestroyRepairSeriesCount",
                    Description = "Кількість серій у destroy-repair",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "2",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "DestroyRepairMaxMilliseconds",
                    Description = "Ліміт часу destroy-repair",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "300",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "DestroyRepairMaxAcceptedLoss",
                    Description = "Максимальне тимчасове погіршення destroy-repair",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "1000",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "DestroyRepairAttempts",
                    Description = "Кількість спроб destroy-repair",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "3",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "DestroyRepairRelocalIterations",
                    Description = "Кількість легких мутацій для спуску після прийнятої втрати destroy-repair (0 = вимкнено)",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "0",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "AdaptiveOperationPortfolio",
                    Description = "Адаптивний вибір Genetic-операцій",
                    DataType = AlgorithmParameterType.Boolean,
                    DefaultValue = "true",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "IlsStagnationLimit",
                    Description = "Скільки ітерацій без покращення запускають ILS-епізод (kick робочого базису в гірший басейн; 0 = вимкнено)",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "12",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "IlsRepairIterations",
                    Description = "Бюджет ітерацій на відновлення після ILS-kick",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "20",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "IlsKickSeriesCount",
                    Description = "Кількість серій у ILS-kick (розмір збурення)",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "2",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "IlsChainKickLoss",
                    Description = "Втрата, прийнятна для ILS-kick relocation серії (chain-relocate); 0 = старий TryPerturb-режим",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "0",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "IlsChainKickMoves",
                    Description = "Кількість послідовних chain-kick ходів в одному ILS-збуренні",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "1",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "ChainDirected",
                    Description = "Chain-relocate: турнірний скан (серія × домен × B, найкраща дельта) замість випадкового першого покращення",
                    DataType = AlgorithmParameterType.Boolean,
                    DefaultValue = "true",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "HgtInterval",
                    Description = "Інтервал HGT-міграції (ітерацій): прийняті події лідера реплаються на laggard; 0 = вимкнено",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "0",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "ParallelLineages",
                    Description = "Бюджет паралельних потоків: 0 = авто (ProcessorCount-1, cgroup-aware), N = жорсткий ліміт",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "0",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "OnlyOperation",
                    Description = "Діагностика: -1 = звичайний цикл операцій; 0..6 = виконувати лише цю операцію",
                    DataType = AlgorithmParameterType.Integer,
                    DefaultValue = "-1",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "CheapEvaluation",
                    Description = "Дешева інкрементальна оцінка правил (чорна скірня з верифікацією; відкат на повну оцінку при розбіжності)",
                    DataType = AlgorithmParameterType.Boolean,
                    DefaultValue = "false",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "FlatIndex",
                    Description = "FLAT-дзеркало генома (бітсети зайнятості): фаза 1a — телеметрія, нуль змін поведінки",
                    DataType = AlgorithmParameterType.Boolean,
                    DefaultValue = "false",
                    IsRequired = false
                },
                new AlgorithmParameterDTO
                {
                    Name = "FlatIndexPrecheck",
                    Description = "FLAT-фаза 1b: precheck валідації з дзеркала (skip StandartValidation на вільних позиціях, shadow-контроль кожен 64-й)",
                    DataType = AlgorithmParameterType.Boolean,
                    DefaultValue = "false",
                    IsRequired = false
                }
            };
        }

        #region genetic
        
        // todo: Переробити для паралельного виконання
        // або можливо не варто виконувати весь алгоритм паралельно,
        // оскільки юніт = популяція, можна викликати самі операції паралельно і дивитись яка з них показує кращий результат,
        // зберігаючи найкращий юніт
        private void Calculate(GeneticScheduleAlgorithmUnit unit)
        {
            // Збережемо 
            // var cacheRoot = unit.CloneWithPrivateCache();
            
            // на 100 ітерацій, більшість успішних мутацій в сумі дадуть +1-2%, але в середньому є 2-3 такі мутації що дадуть +5-6%.
            // коли зможемо прискорити виконання, можна буде використати більшу кількість ітерацій
            // +краще поки не ставити більше 100-120, бо при 100+ буває що не зберігається розклад (подивитись в чому може бути проблема, можливо при перевірці на валідність нового призначення не врахувалось закінчення семестру?)
            // upd наче розібрався?
            // UDP на 150-200 ітерацій виходить навіть +10-15%
            // Це добре що номільнально оцінка стає краще, але треба детально роздивлятись excel файли. Роздивившись, можу сказати що десь ці зміни мають певний сенс, як такий альтернативний погляд, люфт
            var maxGenerations = 100;
            // var maxGenerations = 150;
            // var maxGenerations = RunParameters.GeneticIterations;
            logger.LogInformation($"ПОЧАТОК ГЕН АЛГОРИТМУ. КІЛЬКІСТЬ ІТЕРАЦІЙ {maxGenerations}");

            var initEstimate = unit.Estimate();
            
            var baseEstimate = unit.Estimate();

            for (var gen = 0; gen < maxGenerations; gen++)
            {
                var estimation = Int32.MinValue;
                var e = unit.Estimate();
                
                // unit.Swap();

                // estimation = unit.Mutations(e);
                
                // for (var i = 0; i < 1; i++)
                // {
                //     unit.Mutations(e);
                //     unit.MutationsForLongSeries(e);
                // }
                //     estimation = unit.MutationsForLongSeries(e);
                
                // for(var i = 0; i < 1; i++)
                //     estimation = unit.Mutations(e);
                // Зараз виконуємо лише мутації, оскільки мутації вже виконуються правильно і дають результат,
                // Треба буде зробити і свап теж, тільки вигадати що з чим можна буде свапати.
                // По ідеї, наприклад, для однієї і тієї самої групи дивитись чи є дисципліни зі схожими параметрами
                // +можна дивитись по підгрупам, поміняти їх місцями (таким чином може ми трошки збалансуємо розклад)
                // +можна дивитись по викладачам, але напевно варто дивитись лише по тим у кого багато дисциплін

                // var estimation = unit.Estimate();
                logger.LogInformation($"ПІСЛЯ МУТ. №{gen} МАЄМО: {estimation} | АБО {estimation - baseEstimate} ВІД НАЙКРАЩОГО РЕЗУЛЬТАТУ");

                if (estimation > baseEstimate)
                {
                    baseEstimate = estimation; // Як же я довго шукав чому не зберігало кращі варіанти...
                }
            }

            // виявлено що мутації для дисциплін з малою кількістю занять мають багато варіантів, тож виконуються дещо повільніше, тому обмежимо парою десятків
            // for (var gen = 0; gen < 30; gen++)
            // {
            //     var estimation = 0;
            //     var e = unit.Estimate();
            //     estimation = unit.Mutations(e);
            //     logger.LogInformation($"ПІСЛЯ МУТ. №{gen} МАЄМО: {estimation} | АБО {estimation - baseEstimate} ВІД НАЙКРАЩОГО РЕЗУЛЬТАТУ");
            //     if (estimation > baseEstimate)
            //     {
            //         baseEstimate = estimation; // Як же я довго шукав чому не зберігало кращі варіанти...
            //     }
            // }

            
            
            var res = unit.Estimate();
            // var resCache = Estimate(cacheRoot);
            logger.LogInformation($"ДО АЛГОРИМУ: {initEstimate} ПІСЛЯ АЛГОРИТМУ {res}");
            if(initEstimate != 0)
                logger.LogInformation($"МИ ЗРОБИЛИ КРАЩЕ НА {res - initEstimate}, АБО У: {res / (double)initEstimate} РАЗ");
            if (res > initEstimate)
            {
                unit.initialResult.TotalEstimation = res; // todo?

                var result = new AlgorithmResultDTO();
                
                result.TotalEstimation = res;
                result.ScheduleSlots = unit.Slots.Values.Where(v => v.IsAssigned).Select(x => x.ScheduleSlot).ToList();
                result.Name = GetName();

                unit.initialResult = result;
            }
        }
        
        #endregion
    }
    
    
}
