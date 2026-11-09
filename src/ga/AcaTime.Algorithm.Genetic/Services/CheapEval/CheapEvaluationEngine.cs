using System.Diagnostics;
using AcaTime.Algorithm.Genetic.Models;
using AcaTime.Algorithm.Genetic.Models.Genetic;
using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScriptModels;
using AcaTime.ScheduleCommon.Models.Constraints;
using Microsoft.Extensions.Logging;

namespace AcaTime.Algorithm.Genetic.Services.CheapEval;

/// <summary>
/// Дешева (інкрементальна) оцінка індивідуума через чорно-скірнову декомпозицію
/// користувацьких правил: те самі скомпільовані скрипти запускаються на малих
/// підмножинах сезону (unit × date), значення клітинок кешуються, при мутації
/// перераховуються лише зачеплені клітинки.
///
/// Гарантії точності (1-в-1 з повною оцінкою):
///  1. статична верифікація декомпозиції при побудові (predicted == full);
///  2. верифікаційні пробні мутації (probes) перед активацією сімейства;
///  3. періодична звірка RunningTotal з повним запуском кожного правила;
///  4. фінальний score завжди береться з повної оцінки (unit.Estimate() поза рушієм).
/// При будь-якій невдачі правило автоматично падає назад на повну оцінку.
/// </summary>
public class CheapEvaluationEngine
{
    private const int VerifyEveryNthEstimate = 40;

    /// <summary>
    /// Memo-кеш клітинок ВИМКНЕНО (20260831-015714): композит хешів кошиків
    /// не покриває все, що скрипти читають через сирі DTO-навігації
    /// (slot.GroupSubject.* поза клітинкою) — правила 107/108 (pair-залежні)
    /// давали 492 heal-и за ран (сталі клітинки між верифікаціями). Точність
    /// не страждала (VerifyAll ловив), але накладні витрати > виграшу.
    /// Повернення до цієї ідеї потребує: хеш сирого-DTO-стану АБО view-чисті
    /// скрипти (док. ALGORITHM-NOTES-20260831-ISLANDS-HGT §memoization).
    /// </summary>
    private const bool EnableCellMemo = false;
    private const int ProbeCount = 12;

    // Діагностика: сумарні тики/лічильники по всім двигунам за раунд
    // (ланки послідовні; скидаються у Run). script — час викликів скриптів
    // на гарячому шляху (RecomputeCells); повні прогони рахуються окремо.
    internal static long TicksBuild, TicksCloneFor, TicksProcessDirty, TicksConsumeDirty, TicksRecompute, TicksScript, TicksVerify, TicksTotal;
    internal static long NEstimates, NScriptCalls, NFullRuns, NCloneFor;
    internal static string ProfilingSummary()
    {
        string Ms(long t) => ((int)(t * 1000.0 / Stopwatch.Frequency)).ToString();
        return $"estimates={NEstimates} scriptCalls={NScriptCalls} fullRuns={NFullRuns} | ms: build={Ms(TicksBuild)} cloneFor={Ms(TicksCloneFor)} processDirty={Ms(TicksProcessDirty)} (consume={Ms(TicksConsumeDirty)} recompute={Ms(TicksRecompute)} script={Ms(TicksScript)}) verify={Ms(TicksVerify)} total={Ms(TicksTotal)}";
    }

    private readonly Individual _owner;
    private readonly ILogger _logger;
    private readonly SeasonSliceView _view;
    private readonly List<RuleDeltaState> _rules = new();
    private readonly List<SlotTracker> _dirty = new();
    private readonly Dictionary<long, (long gen, int value)> _fallbackCache = new();
    private readonly Random _random;

    private long _stateGeneration;
    private int _estimatesSinceVerify;

    /// <summary>
    /// Придушення VerifyAll на час транзієнтних станів скану (турнірні
    /// застосування/відкати на оригіналі): проміжний стан може не
    /// декомпозуватися, хоча відновлений — декомпозований; верифікація
    /// транзитів назавжди вимикає правила і породжує fallback-шторм.
    /// Вкладено-безпечно (лічильник); після EndScan стан на поліцейському
    /// обліку як завжди.
    /// </summary>
    internal int _verifySuppressCount;

    public bool Ready { get; private set; }

    public CheapEvaluationEngine(Individual owner, ILogger logger)
    {
        _owner = owner;
        _logger = logger;
        _view = new SeasonSliceView(owner.Root);
        _random = new Random((int)(owner.Root.Id % int.MaxValue) ^ 0x5f3759df);
    }

    #region ініціалізація

    public bool EnsureReady()
    {
        if (Ready)
            return true;

        try
        {
            var t0 = Stopwatch.GetTimestamp();
            var started = Stopwatch.StartNew();
            BuildIndex();
            AttachHooks(); // до пошуку сімейств: пробні мутації мають фіксуватись

            foreach (var est in _owner.UserFunctions.ScheduleEstimations)
            {
                if (est.Func == null)
                    throw new InvalidOperationException($"Правило {est.Id} не скомпільоване");

                var state = new RuleDeltaState
                {
                    RuleId = est.Id ?? 0,
                    Name = est.Name,
                    Script = est.Func
                };
                int full = est.Func(_owner.Root);
                NFullRuns++;

                state.Family = FindFamily(state, full);
                if (state.Family != null)
                {
                    BuildTable(state);
                    state.RunningTotal = state.AllEmpty;
                    foreach (var kv in state.Cells)
                        state.RunningTotal += kv.Value - state.Baselines[kv.Key.Item1];
                    _logger.LogInformation(
                        $"CheapEval: правило {state.RuleId} '{state.Name}' -> сімейство '{state.Family.Name}', клітинок {state.Cells.Count}");
                }
                else
                {
                    _logger.LogInformation(
                        $"CheapEval: правило {state.RuleId} '{state.Name}' не декомпонується — завжди повна оцінка");
                }

                _rules.Add(state);
            }

            Ready = true;
            TicksBuild += Stopwatch.GetTimestamp() - t0;
            _logger.LogInformation(
                $"CheapEval: готово за {started.ElapsedMilliseconds}мс; " +
                string.Join(", ", _rules.Select(r => r.Enabled ? $"{r.RuleId}={r.Family!.Name}" : $"{r.RuleId}=FULL")));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheapEval: ініціалізація не вдалась — відкат на повну оцінку");
            Ready = false;
            return false;
        }
    }

    /// <summary>
    /// Копія стану для клону індивідуума (той самий стан розкладу).
    /// trackerMap — відповідність трекерів джерела трекерам клону: разом з
    /// прапорцями CheapDirty (копіюються у SlotTracker.Clone) переносимо і
    /// список незпотріблених dirty-змін, інакше клон стартує з клітинками
    /// попереднього стану і без маркерів — тихе розходження оцінки.
    /// </summary>
    public CheapEvaluationEngine CloneFor(Individual newOwner, Dictionary<SlotTracker, SlotTracker> trackerMap)
    {
        NCloneFor++;
        var t0 = Stopwatch.GetTimestamp();
        try
        {
            return CloneForInner(newOwner, trackerMap);
        }
        finally
        {
            TicksCloneFor += Stopwatch.GetTimestamp() - t0;
        }
    }

    private CheapEvaluationEngine CloneForInner(Individual newOwner, Dictionary<SlotTracker, SlotTracker> trackerMap)
    {
        var clone = new CheapEvaluationEngine(newOwner, _logger)
        {
            _stateGeneration = _stateGeneration,
            _estimatesSinceVerify = _estimatesSinceVerify
        };
        clone.BuildIndex();
        foreach (var rule in _rules)
        {
            clone._rules.Add(new RuleDeltaState
            {
                RuleId = rule.RuleId,
                Name = rule.Name,
                Script = rule.Script,
                Family = rule.Family,
                AllEmpty = rule.AllEmpty,
                Baselines = rule.Baselines, // стан-незалежні (статичні дані сезону) — спільні
                Cells = rule.Enabled ? new Dictionary<(long, DateTime), int>(rule.Cells) : new Dictionary<(long, DateTime), int>(),
                RunningTotal = rule.RunningTotal,
                // memo валідний для клону: ключі — композити КОНТЕНТУ кошиків,
                // а стан клона ідентичний донору на момент клону
                Memo = rule.Enabled && rule.Memo != null
                    ? new Dictionary<(long, DateTime), (ulong, int)>(rule.Memo)
                    : null
            });
        }
        clone._fallbackCache.Clear();
        foreach (var kv in _fallbackCache)
            clone._fallbackCache[kv.Key] = kv.Value;
        clone._dirty.Clear();
        foreach (var tracker in _dirty)
            if (trackerMap.TryGetValue(tracker, out var mapped))
                clone._dirty.Add(mapped);
        clone.AttachHooks();
        clone.Ready = true;
        return clone;
    }

    private void AttachHooks()
    {
        foreach (var tracker in _owner.Slots.Values)
            tracker.CheapEngine = this;
    }

    private void BuildIndex()
    {
        var index = new Dictionary<long, Dictionary<DateTime, List<ScheduleSlotDTO>>>();
        foreach (var gs in _owner.Root.GroupSubjects)
        {
            var byDate = new Dictionary<DateTime, List<ScheduleSlotDTO>>();
            foreach (var slot in gs.ScheduleSlots)
            {
                var date = slot.Date.Date;
                if (!byDate.TryGetValue(date, out var bucket))
                    byDate[date] = bucket = new List<ScheduleSlotDTO>();
                bucket.Add(slot);
            }
            index[gs.Id] = byDate;
        }
        _view.Index = index;
        _view.RebuildBucketHashes();
    }

    #endregion

    #region гарячий шлях

    internal void NoteDirty(SlotTracker tracker) => _dirty.Add(tracker);

    internal void BeginTransientScan() => _verifySuppressCount++;
    internal void EndTransientScan() => _verifySuppressCount = Math.Max(0, _verifySuppressCount - 1);

    /// <summary>
    /// FLAT-дзеркало: форвард живого переїзду слота (SetDomainRaw) у бітсети
    /// власника. No-op для не-призначених слотів (усередині NoteMoved).
    /// </summary>
    internal void FlatMoved(SlotTracker t, DateTime prevDate, int prevPair)
        => _owner.flatBits?.NoteMoved(t, prevDate, prevPair);

    /// <summary>
    /// Дешева оцінка поточного стану: перераховує зачеплені клітинки і повертає суму.
    /// Інваріант: результат == повна оцінка, поки всі правила Enabled.
    /// </summary>
    public int Estimate()
    {
        NEstimates++;
        var t0 = Stopwatch.GetTimestamp();
        ProcessDirty();
        TicksProcessDirty += Stopwatch.GetTimestamp() - t0;
        // Лічильник росте і на транзієнтних оцінках, але верифікація
        // відкладається до першої НЕ-транзієнтної: скани майже монополізували
        // Estimate, і "кожна 40-а" всередині suppression не спрацьовувала б
        // ніколи (verify=0 у run 20260830-225157). Відкладена верифікація
        // гарантовано виконується на персистентному стані.
        if (++_estimatesSinceVerify >= VerifyEveryNthEstimate && _verifySuppressCount == 0)
        {
            var tv = Stopwatch.GetTimestamp();
            VerifyAll();
            TicksVerify += Stopwatch.GetTimestamp() - tv;
            _estimatesSinceVerify = 0;
        }
        var tt = Stopwatch.GetTimestamp();
        var total = TotalScore();
        TicksTotal += Stopwatch.GetTimestamp() - tt;
        return total;
    }

    private void ProcessDirty()
    {
        if (_dirty.Count == 0)
            return;

        var t0 = Stopwatch.GetTimestamp();
        var affected = ConsumeDirtyToIndex();
        TicksConsumeDirty += Stopwatch.GetTimestamp() - t0;
        if (affected.Count == 0)
            return;

        var t1 = Stopwatch.GetTimestamp();
        foreach (var rule in _rules)
            RecomputeCells(rule, affected);
        TicksRecompute += Stopwatch.GetTimestamp() - t1;
    }

    /// <summary>Оновлює per-(gs,date) індекс під збруді слоти; повертає map gsId -> зачеплені дати.</summary>
    private Dictionary<long, HashSet<DateTime>> ConsumeDirtyToIndex()
    {
        var affected = new Dictionary<long, HashSet<DateTime>>();
        foreach (var tracker in _dirty)
        {
            tracker.CheapDirty = false;
            var slot = tracker.ScheduleSlot;
            var curDate = slot.Date.Date;
            var prevDate = tracker.CheapPrevDate.Date;

            if (prevDate == curDate && tracker.CheapPrevPair == slot.PairNumber)
                continue; // повернулись у синхронізований стан

            var gsId = slot.GroupSubject.Id;
            if (!_view.Index.TryGetValue(gsId, out var byDate))
                continue;

            if (prevDate != curDate)
            {
                if (byDate.TryGetValue(prevDate, out var prevBucket))
                {
                    if (prevBucket.Remove(slot))
                        _view.BucketHashes[(gsId, prevDate)] =
                            _view.BucketHash(gsId, prevDate) ^ SeasonSliceView.SlotHashWithPair(slot, tracker.CheapPrevPair);
                }
                if (!byDate.TryGetValue(curDate, out var curBucket))
                    byDate[curDate] = curBucket = new List<ScheduleSlotDTO>();
                // ідемпотентність: клон будує індекс з поточних дат і успадковує
                // незпотріблені dirty-зміни — перехід може посилатись на стан,
                // якого індекс вже не містить; дублікати в кошику ламають клітинку
                if (!curBucket.Contains(slot))
                {
                    curBucket.Add(slot);
                    _view.BucketHashes[(gsId, curDate)] =
                        _view.BucketHash(gsId, curDate) ^ SeasonSliceView.SlotHash(slot);
                }
            }
            else if (tracker.CheapPrevPair != slot.PairNumber)
            {
                // та сама дата, інша пара: склад кошика незмінний, але внесок
                // члена змінився (prev -> cur)
                _view.BucketHashes[(gsId, curDate)] =
                    _view.BucketHash(gsId, curDate)
                    ^ SeasonSliceView.SlotHashWithPair(slot, tracker.CheapPrevPair)
                    ^ SeasonSliceView.SlotHash(slot);
            }

            if (!affected.TryGetValue(gsId, out var dates))
                affected[gsId] = dates = new HashSet<DateTime>();
            dates.Add(prevDate);
            dates.Add(curDate);
        }
        _dirty.Clear();

        if (affected.Count > 0)
            _stateGeneration++;
        return affected;
    }

    private void RecomputeCells(RuleDeltaState state, Dictionary<long, HashSet<DateTime>> affected)
    {
        if (!state.Enabled || affected.Count == 0)
            return;

        var units = new HashSet<long>();
        foreach (var gsId in affected.Keys)
            if (state.Family!.GsUnits.TryGetValue(gsId, out var gsUnits))
                foreach (var unit in gsUnits)
                    units.Add(unit);
        if (units.Count == 0)
            return;

        var dates = affected.Values.SelectMany(x => x).Distinct().ToList();

        var memo = EnableCellMemo ? state.Memo : null;
        foreach (var unit in units)
        {
            var baseline = GetBaseline(state, unit);
            _view.UseSubset(state.Family!, unit);

            // композит хешів годівників клітинки (unit, date) — повний видимий
            // вхід скрипта (view фільтрує до members[unit], bucket = (gs, date))
            var members = state.Family!.Members[unit];
            foreach (var date in dates)
            {
                ulong composite = 0;
                foreach (var (_, bucketHash) in members.Select(gsId => (gsId, _view.BucketHash(gsId, date))))
                    composite ^= bucketHash;

                if (memo != null && memo.TryGetValue((unit, date), out var cached) && cached.hash == composite)
                {
                    // undo-черт сканів: контент клітинки ідентичний — скрипт не викликаємо
                    int oldCell = state.Cells.TryGetValue((unit, date), out var existing) ? existing : baseline;
                    if (cached.value != oldCell)
                    {
                        state.RunningTotal += cached.value - oldCell;
                        state.Cells[(unit, date)] = cached.value;
                    }
                    continue;
                }

                _view.Filter = date;
                var ts = Stopwatch.GetTimestamp();
                int newCell = state.Script(_view);
                TicksScript += Stopwatch.GetTimestamp() - ts;
                NScriptCalls++;
                int old = state.Cells.TryGetValue((unit, date), out var existing2) ? existing2 : baseline;
                if (newCell != old)
                {
                    state.RunningTotal += newCell - old;
                    state.Cells[(unit, date)] = newCell;
                }
                if (memo != null)
                    memo[(unit, date)] = (composite, newCell);
            }
        }
    }

    private int GetBaseline(RuleDeltaState state, long unit)
    {
        if (state.Baselines.TryGetValue(unit, out var baseline))
            return baseline;
        _view.UseSubset(state.Family!, unit);
        _view.Filter = DateTime.MinValue;
        baseline = state.Script(_view);
        state.Baselines[unit] = baseline;
        return baseline;
    }

    /// <summary>
    /// ГАРЯЧІ КЛІТИНКИ: |значення клітинки - baseline| — найбільший вплив
    /// клітинки на скор (знак не важливий: штраф чи бонус — таргетинг це
    /// ПРОПОЗИЦІЯ, прийняття все одно скорове). Повертає слоти топ-K клітинок
    /// (generic: через Families/Cells рушія, без знань про конкретні правила).
    /// </summary>
    internal List<List<ScheduleSlotDTO>> GetHotspots(int maxCells)
    {
        var hot = new List<(double weight, RuleDeltaState rule, long unit, DateTime date)>();
        foreach (var rule in _rules)
        {
            if (!rule.Enabled || rule.Family == null)
                continue;
            foreach (var kv in rule.Cells)
            {
                if (!rule.Baselines.TryGetValue(kv.Key.unit, out var baseline))
                    continue;
                var weight = Math.Abs((double)kv.Value - baseline);
                if (weight > 0)
                    hot.Add((weight, rule, kv.Key.unit, kv.Key.date));
            }
        }

        hot.Sort((a, b) => b.weight.CompareTo(a.weight));

        var result = new List<List<ScheduleSlotDTO>>();
        var seen = new HashSet<(long gsId, DateTime date)>();
        foreach (var (_, rule, unit, date) in hot)
        {
            if (result.Count >= maxCells)
                break;

            var slots = new List<ScheduleSlotDTO>();
            var fresh = false;
            foreach (var gsId in rule.Family.Members[unit])
            {
                if (!_view.Index.TryGetValue(gsId, out var byDate) ||
                    !byDate.TryGetValue(date, out var bucket))
                    continue;
                if (seen.Add((gsId, date)))
                {
                    slots.AddRange(bucket);
                    fresh = true;
                }
            }

            if (fresh && slots.Count > 0)
                result.Add(slots);
        }

        return result;
    }

    private int TotalScore()
    {
        int total = 0;
        foreach (var rule in _rules)
            total += rule.Enabled ? (int)rule.RunningTotal : GetFullFallback(rule);
        return total;
    }

    private int GetFullFallback(RuleDeltaState rule)
    {
        if (_fallbackCache.TryGetValue(rule.RuleId, out var cached) && cached.gen == _stateGeneration)
            return cached.value;
        var value = rule.Script(_owner.Root);
        NFullRuns++;
        _fallbackCache[rule.RuleId] = (_stateGeneration, value);
        return value;
    }

    /// <summary>Періодична звірка з повним запуском; розбіжність => правило вимикається.</summary>
    private void VerifyAll()
    {
        foreach (var rule in _rules)
        {
            if (!rule.Enabled)
                continue;
            int full = rule.Script(_owner.Root);
            NFullRuns++;
            if (full == rule.RunningTotal)
                continue;

            // самолікування: розбіжність майже завжди означає застарілі
            // клітинки, а не непридатну сім'ю — перебудовуємо таблицю на
            // поточному стані (статична перевірка проти повного значення).
            // Сім'я вже була верифікована пробами при активації, тому повторні
            // проби тут не потрібні. Невдача => відкат на повну оцінку.
            if (!TryHeal(rule, full))
            {
                _logger.LogWarning(
                    $"CheapEval: правило {rule.RuleId} '{rule.Name}' втратило декомпозицію (cheap={rule.RunningTotal}, full={full}) — відкат на повну оцінку");
                rule.Family = null;
                rule.RunningTotal = full;
                _fallbackCache[rule.RuleId] = (_stateGeneration, full);
            }
        }
    }

    private bool TryHeal(RuleDeltaState rule, int full)
    {
        try
        {
            if (rule.Family == null)
                return false;
            var healed = new RuleDeltaState
            {
                RuleId = rule.RuleId,
                Name = rule.Name,
                Script = rule.Script,
                Family = rule.Family
            };
            BuildTable(healed);
            long predicted = healed.AllEmpty;
            foreach (var kv in healed.Cells)
                predicted += kv.Value - healed.Baselines[kv.Key.Item1];
            if (predicted != full)
                return false;

            rule.AllEmpty = healed.AllEmpty;
            rule.Baselines = healed.Baselines;
            rule.Cells = healed.Cells;
            rule.RunningTotal = full;
            _fallbackCache[rule.RuleId] = (_stateGeneration, full);
            _logger.LogInformation(
                $"CheapEval: правило {rule.RuleId} '{rule.Name}' перебудовано на поточному стані — декомпозицію відновлено");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, $"CheapEval: спроба відновлення правила {rule.RuleId} не вдалась");
            return false;
        }
    }

    #endregion

    #region пошук сімейства + верифікація

    private RuleFamily? FindFamily(RuleDeltaState state, int fullScore)
    {
        foreach (var family in RuleFamily.BuildDefault(_owner.Root))
        {
            var occupancy = Occupancy(family);
            if (occupancy.Count == 0)
                continue;

            var (allEmpty, baselines, cells) = BuildTempTable(state.Script, family, occupancy);
            long predicted = allEmpty;
            foreach (var kv in cells)
                predicted += kv.Value - baselines[kv.Key.Item1];
            if (predicted != fullScore)
            {
                _logger.LogDebug(
                    $"CheapEval: [{state.RuleId}] fam={family.Name} static FAIL predicted={predicted} full={fullScore} diff={fullScore - predicted} cells={cells.Count}");
                continue;
            }

            var temp = new RuleDeltaState
            {
                RuleId = state.RuleId,
                Name = state.Name,
                Script = state.Script,
                Family = family,
                AllEmpty = allEmpty,
                Baselines = baselines,
                Cells = cells,
                RunningTotal = predicted
            };
            if (!ProbesPass(temp, fullScore))
            {
                _logger.LogDebug($"CheapEval: [{state.RuleId}] fam={family.Name} static OK але PROBES FAILED — відхилено");
                continue;
            }

            state.AllEmpty = allEmpty;
            state.Baselines = baselines;
            state.Cells = cells;
            return family;
        }
        return null;
    }

    private Dictionary<long, HashSet<DateTime>> Occupancy(RuleFamily family)
    {
        var occupancy = new Dictionary<long, HashSet<DateTime>>();
        foreach (var gs in _owner.Root.GroupSubjects)
        {
            if (!family.GsUnits.TryGetValue(gs.Id, out var units))
                continue;
            foreach (var date in _view.Index[gs.Id].Keys)
                foreach (var unit in units)
                {
                    if (!occupancy.TryGetValue(unit, out var set))
                        occupancy[unit] = set = new HashSet<DateTime>();
                    set.Add(date);
                }
        }
        return occupancy;
    }

    private (int allEmpty, Dictionary<long, int> baselines, Dictionary<(long, DateTime), int> cells) BuildTempTable(
        Func<IFacultySeason, int> script, RuleFamily family, Dictionary<long, HashSet<DateTime>> occupancy)
    {
        _view.UseAll();
        _view.Filter = DateTime.MinValue;
        int allEmpty = script(_view);

        var baselines = new Dictionary<long, int>();
        var cells = new Dictionary<(long, DateTime), int>();
        foreach (var (unit, dates) in occupancy)
        {
            _view.UseSubset(family, unit);
            _view.Filter = DateTime.MinValue;
            int baseline = script(_view);
            baselines[unit] = baseline;

            foreach (var date in dates)
            {
                _view.Filter = date;
                cells[(unit, date)] = script(_view);
            }
        }
        return (allEmpty, baselines, cells);
    }

    /// <summary>
    /// Пробні випадкові мутації: дешева оцінка має збігатися з повною
    /// до і після відкату. Ловить "вироджені" сімейства, які проходять
    /// статичну перевірку на вдалих даних.
    /// </summary>
    private bool ProbesPass(RuleDeltaState temp, int originalFullScore)
    {
        var trackers = _owner.Slots.Values.Where(t => t.IsAssigned).ToList();
        if (trackers.Count == 0)
            return true;

        for (var probe = 0; probe < ProbeCount; probe++)
        {
            var tracker = trackers[_random.Next(trackers.Count)];
            var candidates = tracker.AvailableDomains
                .Where(d => d.Date != tracker.ScheduleSlot.Date || d.PairNumber != tracker.ScheduleSlot.PairNumber)
                .ToList();
            if (candidates.Count == 0)
                continue;

            var newDomain = candidates[_random.Next(candidates.Count)];
            var prevDate = tracker.ScheduleSlot.Date;
            var prevPair = tracker.ScheduleSlot.PairNumber;

            tracker.SetDomainRaw(newDomain.Date, newDomain.PairNumber);
            var affected = ConsumeDirtyToIndex();
            RecomputeCells(temp, affected);
            int fullAfterMove = temp.Script(_owner.Root);
            if (TotalOfTemp(temp) != fullAfterMove)
            {
                RollBackProbe(tracker, prevDate, prevPair, temp, originalFullScore);
                return false;
            }

            if (!RollBackProbe(tracker, prevDate, prevPair, temp, originalFullScore))
                return false;
        }
        return true;
    }

    private bool RollBackProbe(SlotTracker tracker, DateTime prevDate, int prevPair, RuleDeltaState temp, int originalFullScore)
    {
        tracker.SetDomainRaw(prevDate, prevPair);
        var affected = ConsumeDirtyToIndex();
        RecomputeCells(temp, affected);
        if (TotalOfTemp(temp) != originalFullScore)
            return false;
        return true;
    }

    private int TotalOfTemp(RuleDeltaState temp)
    {
        long total = temp.AllEmpty;
        foreach (var kv in temp.Cells)
            total += kv.Value - temp.Baselines[kv.Key.Item1];
        return (int)total;
    }

    private void BuildTable(RuleDeltaState state)
    {
        var occupancy = Occupancy(state.Family!);
        var (allEmpty, baselines, cells) = BuildTempTable(state.Script, state.Family!, occupancy);
        state.AllEmpty = allEmpty;
        state.Baselines = baselines;
        state.Cells = cells;
        state.Memo = new Dictionary<(long unit, DateTime date), (ulong hash, int value)>();
    }

    #endregion
}
