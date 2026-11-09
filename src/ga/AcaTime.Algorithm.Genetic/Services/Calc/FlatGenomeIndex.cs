using AcaTime.Algorithm.Genetic.Models;
using System.Runtime.CompilerServices;

namespace AcaTime.Algorithm.Genetic.Services.Calc;

/// <summary>
/// Спільні (пер-сезонні) таблиці кодування: DateTime→індекс дня,
/// teacherId/groupIds→індекс сутності. ЗАМОРОЖЕНІ після повної
/// передреєстрації (EnsureFlat): далі лише читання — безпечне для
/// паралельних ліній (2 потоки читають один об'єкт без мутацій).
/// </summary>
internal sealed class SeasonFlatTables
{
    /// <summary>Реальні пари 1..7; кап 16 з запасом. Понад кап — flat вимикається.</summary>
    internal const int PairsCap = 16;

    private readonly Dictionary<DateTime, int> DateIdx = new();
    private readonly Dictionary<long, int> TeacherIdx = new();
    private readonly Dictionary<long, int> GroupIdx = new();
    internal bool Frozen;

    /// <summary>Під час побудови: GetOrAdd. Після Freeze: лише читання (-1 = невідомо = баг дзеркала).</summary>
    internal int DateCode(DateTime date, int pair)
    {
        if (Frozen)
            return DateIdx.TryGetValue(date.Date, out var d) ? d * PairsCap + pair : -1;
        if (!DateIdx.TryGetValue(date.Date, out var di))
        {
            di = DateIdx.Count;
            DateIdx[date.Date] = di;
        }
        return di * PairsCap + pair;
    }

    internal int TeacherCode(long id)
    {
        if (Frozen)
            return TeacherIdx.TryGetValue(id, out var i) ? i : -1;
        if (!TeacherIdx.TryGetValue(id, out var ii))
        {
            ii = TeacherIdx.Count;
            TeacherIdx[id] = ii;
        }
        return ii;
    }

    internal int GroupCode(long id)
    {
        if (Frozen)
            return GroupIdx.TryGetValue(id, out var i) ? i : -1;
        if (!GroupIdx.TryGetValue(id, out var ii))
        {
            ii = GroupIdx.Count;
            GroupIdx[id] = ii;
        }
        return ii;
    }

    internal int Teachers => TeacherIdx.Count;
    internal int Groups => GroupIdx.Count;

    internal void Freeze() => Frozen = true;
}

/// <summary>
/// Дзеркало TRUE-стану генома (IsAssigned + жива позиція DTO) у бітсетах
/// (teacher|group × (day,pair)). Оновлюється виключно в чокпоінтах
/// (SetSlotAssignedGenetic / SetSlotUnAssignedInner / SetDomainRaw-через-ruшій)
/// і клонується memcpy-ом. Фаза 1a: ТЕЛЕМЕТРІЯ — диференціал з словниковою
/// відповіддю ValidateAssignment, нуль змін поведінки. Перемикач precheck —
/// ПІСЛЯ того, як батчі доведуть drift=0.
/// </summary>
internal sealed class FlatBitsets
{
    private ulong[][] _teacher = Array.Empty<ulong[]>();
    // Групи — ЛІЧИЛЬНИКИ (не біти): підгрупи можуть ЛЕГАЛЬНО спів-займати
    // (group, date, pair) — біт втратив би спів-зайнятість при unassign одного
    // з двох. Вчитель — біт (спів-зайнятість заборонена валідацією).
    private int[][] _group = Array.Empty<int[]>();
    internal SeasonFlatTables Tables = null!;

    internal bool Enabled = true;
    internal bool _capacityExceeded;

    // --- телеметрія (static: агрегат по рану)
    internal static long DiffChecks, DiffMismatch, VerifyRuns, VerifyDrifts;
    internal static long PrecheckSkipped, PrecheckTeacherBusy, PrecheckGroupBusy, PrecheckShadowOk, ShadowFailed;
    internal static long StaleRebuilds, IndexRebuilds;

    internal static string Telemetry() =>
        $"flat: diffChecks={DiffChecks} diffMismatch={DiffMismatch} verifyRuns={VerifyRuns} verifyDrifts={VerifyDrifts} " +
        $"pre: skip={PrecheckSkipped} tBusy={PrecheckTeacherBusy} gBusy={PrecheckGroupBusy} shadowOk={PrecheckShadowOk} shadowFail={ShadowFailed} " +
        $"staleRebuilds={StaleRebuilds} idxRebuilds={IndexRebuilds}";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetBit(ulong[][] rows, int entity, int code)
    {
        var w = code >> 6;
        if (entity >= rows.Length) return; // неможливо: idx зростає монотонно до виклику
        if (w >= rows[entity].Length)
        {
            var grown = new ulong[w + 1];
            rows[entity].AsSpan().CopyTo(grown);
            rows[entity] = grown;
        }
        rows[entity][w] |= 1ul << (code & 63);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ClearBit(ulong[][] rows, int entity, int code)
    {
        var w = code >> 6;
        if (entity >= rows.Length || w >= rows[entity].Length) return;
        rows[entity][w] &= ~(1ul << (code & 63));
    }

    // --- Діагностика дрейфу: журнал 1→0 очищень учительських бітів. ---
    // Записуємо кожне реальне стирання (хто: IsAssigned/FlatCode/серія
    // порушника). При MISSING-дрейфі верифікатор виводить записи по
    // (teacher, code) жертви після її останнього SET — рядок журналу
    // прямо називає механізм (спіймано: series=935 assigned@code стер
    // спільний з series=934 біт — транзієнтна співвідніманість клітинки
    // одного вчителя двома серіями; виправлення — лічильники, окремо).
    internal static long MirrorSeq;
    private const int RingCap = 4096;
    private static readonly long[] RingSeq = new long[RingCap];
    private static readonly int[] RingTeacher = new int[RingCap];
    private static readonly int[] RingCode = new int[RingCap];
    private static readonly int[] RingOffAssigned = new int[RingCap];
    private static readonly int[] RingOffFlat = new int[RingCap];
    private static readonly int[] RingOffSeries = new int[RingCap];
    private static int RingPos;

    private void ClearTeacherBitLogged(SlotTracker t, int teacherIdx, int code)
    {
        var w = code >> 6;
        var wasSet = teacherIdx < _teacher.Length && w < _teacher[teacherIdx].Length
                     && (_teacher[teacherIdx][w] & (1ul << (code & 63))) != 0;
        ClearBit(_teacher, teacherIdx, code);
        if (!wasSet) return;
        var s = System.Threading.Interlocked.Increment(ref MirrorSeq);
        var p = RingPos;
        RingSeq[p] = s;
        RingTeacher[p] = teacherIdx;
        RingCode[p] = code;
        RingOffAssigned[p] = t.IsAssigned ? 1 : 0;
        RingOffFlat[p] = t.FlatCode;
        RingOffSeries[p] = t.SeriesId ?? -1;
        RingPos = (p + 1) & (RingCap - 1);
    }

    internal static string DumpRingFor(int teacherIdx, int code, long afterSeq)
    {
        var lines = new System.Collections.Generic.List<string>();
        for (var i = 0; i < RingCap; i++)
        {
            if (RingSeq[i] <= afterSeq || RingTeacher[i] != teacherIdx || RingCode[i] != code) continue;
            lines.Add($"clear-log: seq={RingSeq[i]} offAssigned={RingOffAssigned[i]} offFlatCode={RingOffFlat[i]} offSeries={RingOffSeries[i]}");
        }
        return lines.Count == 0 ? "clear-log: (порожньо)" : string.Join(" | ", lines);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IncrementCount(int[][] rows, int entity, int code)
    {
        var w = code >> 6;
        if (entity >= rows.Length) return;
        if (w >= rows[entity].Length)
        {
            var grown = new int[w + 1];
            rows[entity].AsSpan().CopyTo(grown);
            rows[entity] = grown;
        }
        rows[entity][w]++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DecrementCount(int[][] rows, int entity, int code)
    {
        var w = code >> 6;
        if (entity >= rows.Length || w >= rows[entity].Length) return;
        if (rows[entity][w] > 0) rows[entity][w]--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool GetBit(ulong[][] rows, int entity, int code)
    {
        var w = code >> 6;
        if (entity >= rows.Length || w >= rows[entity].Length) return false;
        return (rows[entity][w] >> (code & 63) & 1) != 0;
    }

    internal void EnsureEntities(SeasonFlatTables tables, int teachers, int groups)
    {
        Tables = tables;
        if (_teacher.Length < teachers) Array.Resize(ref _teacher, teachers);
        if (_group.Length < groups) Array.Resize(ref _group, groups);
        for (var i = 0; i < _teacher.Length; i++) _teacher[i] ??= Array.Empty<ulong>();
        for (var i = 0; i < _group.Length; i++) _group[i] ??= Array.Empty<int>();
    }

    /// <summary>
    /// Встановлює біти призначеного слота в його ЖИВІЙ позиції.
    /// Повертає false якщо FlatCode був засталий (re-assign без чистого
    /// unassign) — дзеркало потребує повного ребілду (викликач скидає).
    /// </summary>
    internal bool NoteAssigned(SlotTracker t)
    {
        if (!Enabled) return true;
        if (t.FlatCode >= 0)
            return false; // засталий FlatCode: біти не відповідають реальності → ребілд
        if (t.ScheduleSlot.PairNumber is < 0 or >= SeasonFlatTables.PairsCap)
        {
            Disable("pair>cap");
            return true;
        }
        var code = Tables.DateCode(t.ScheduleSlot.Date, t.ScheduleSlot.PairNumber);
        if (code < 0) { Disable("unknown-date-assign"); return true; }
        SetBit(_teacher, Tables.TeacherCode(t.ScheduleSlot.GroupSubject.Teacher.Id), code);
        foreach (var g in t.ScheduleSlot.GroupSubject.Groups)
            IncrementCount(_group, Tables.GroupCode(g.Id), code);
        t.FlatCode = code;
        t.FlatSetSeq = System.Threading.Interlocked.Increment(ref MirrorSeq);
        return true;
    }

    /// <summary>Знімає біти слота з позиції, де вони були встановлені (FlatCode).</summary>
    internal void NoteUnassigned(SlotTracker t)
    {
        if (!Enabled || t.FlatCode < 0) return;
        ClearTeacherBitLogged(t, Tables.TeacherCode(t.ScheduleSlot.GroupSubject.Teacher.Id), t.FlatCode);
        foreach (var g in t.ScheduleSlot.GroupSubject.Groups)
            DecrementCount(_group, Tables.GroupCode(g.Id), t.FlatCode);
        t.FlatCode = -1;
    }

    /// <summary>Живий слот переїхав (SetDomainRaw): перепoint бітів. 0 витрат для не-assign.</summary>
    internal void NoteMoved(SlotTracker t, DateTime oldDate, int oldPair)
    {
        if (!Enabled || t.FlatCode < 0) return; // не призначений — бітів немає
        if (t.ScheduleSlot.PairNumber is < 0 or >= SeasonFlatTables.PairsCap
            || oldPair is < 0 or >= SeasonFlatTables.PairsCap)
        {
            Disable("pair>cap-move");
            return;
        }
        var newCode = Tables.DateCode(t.ScheduleSlot.Date, t.ScheduleSlot.PairNumber);
        if (newCode < 0) { Disable("unknown-date-move"); return; }
        if (newCode == t.FlatCode) return;
        ClearTeacherBitLogged(t, Tables.TeacherCode(t.ScheduleSlot.GroupSubject.Teacher.Id), t.FlatCode);
        foreach (var g in t.ScheduleSlot.GroupSubject.Groups)
            DecrementCount(_group, Tables.GroupCode(g.Id), t.FlatCode);
        SetBit(_teacher, Tables.TeacherCode(t.ScheduleSlot.GroupSubject.Teacher.Id), newCode);
        foreach (var g in t.ScheduleSlot.GroupSubject.Groups)
            IncrementCount(_group, Tables.GroupCode(g.Id), newCode);
        t.FlatCode = newCode;
        t.FlatSetSeq = System.Threading.Interlocked.Increment(ref MirrorSeq);
    }

    internal void ResetAll()
    {
        if (!Enabled) return;
        for (var i = 0; i < _teacher.Length; i++) if (_teacher[i] != null) Array.Clear(_teacher[i]);
        for (var i = 0; i < _group.Length; i++) if (_group[i] != null) Array.Clear(_group[i]);
    }

    /// <summary>Вчитель зайнятий у живій позиції слота (тією, що в DTO зараз).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TeacherBusyAt(SlotTracker t)
    {
        if (t.ScheduleSlot.PairNumber is < 0 or >= SeasonFlatTables.PairsCap) return false;
        var code = Tables.DateCode(t.ScheduleSlot.Date, t.ScheduleSlot.PairNumber);
        if (code < 0) return false;
        return GetBit(_teacher, Tables.TeacherCode(t.ScheduleSlot.GroupSubject.Teacher.Id), code);
    }

    /// <summary>Хоча б одна група слота зайнята в живій позиції.</summary>
    internal bool AnyGroupBusyAt(SlotTracker t)
    {
        if (t.ScheduleSlot.PairNumber is < 0 or >= SeasonFlatTables.PairsCap) return false;
        var code = Tables.DateCode(t.ScheduleSlot.Date, t.ScheduleSlot.PairNumber);
        if (code < 0) return false;
        foreach (var g in t.ScheduleSlot.GroupSubject.Groups)
        {
            var gi = Tables.GroupCode(g.Id);
            var w = code >> 6;
            if (gi < _group.Length && w < _group[gi].Length && _group[gi][w] > 0)
                return true;
        }
        return false;
    }

    internal void Disable(string reason)
    {
        if (!Enabled) return;
        Enabled = false;
        _capacityExceeded = true;
    }

    /// <summary>Memcpy для клону (стан клона == стан джерела в момент клонування).</summary>
    internal FlatBitsets CloneArrays()
    {
        var copy = new FlatBitsets
        {
            Tables = Tables,
            Enabled = Enabled,
            _capacityExceeded = _capacityExceeded
        };
        copy._teacher = new ulong[_teacher.Length][];
        for (var i = 0; i < _teacher.Length; i++)
            copy._teacher[i] = (ulong[])(_teacher[i] ?? Array.Empty<ulong>()).Clone();
        copy._group = new int[_group.Length][];
        for (var i = 0; i < _group.Length; i++)
            copy._group[i] = (int[])(_group[i] ?? Array.Empty<int>()).Clone();
        return copy;
    }

    /// <summary>
    /// Паріті-гейт: перерахунок бітсетів зі Slots (IsAssigned + жива позиція)
    /// і порівняння з інкрементальним станом. Логує перших 3 винуватців
    /// (трекер + очікуваний/фактичний біт) — точна діагностика дірок хуків.
    /// </summary>
    internal int VerifyAgainst(IEnumerable<SlotTracker> slots, SeasonFlatTables tables, int teachers, int groups, Action<string>? logOffender = null)
    {
        var drift = 0;
        var logged = 0;
        // форвард-перевірка: біт кожного призначеного трекера має стояти
        foreach (var t in slots)
        {
            if (!t.IsAssigned) continue;
            if (t.ScheduleSlot.PairNumber is < 0 or >= SeasonFlatTables.PairsCap) continue;
            var code = tables.DateCode(t.ScheduleSlot.Date, t.ScheduleSlot.PairNumber);
            if (code < 0) continue;
            var ti = tables.TeacherCode(t.ScheduleSlot.GroupSubject.Teacher.Id);
            if (!GetBit(_teacher, ti, code))
            {
                drift++;
                if (logOffender != null && logged++ < 3)
                    logOffender($"teacher-bit MISSING: series={t.SeriesId} live={t.ScheduleSlot.Date.Date}/{t.ScheduleSlot.PairNumber} flatCode={t.FlatCode} expectedCode={code} setSeq={t.FlatSetSeq} :: {DumpRingFor(ti, code, t.FlatSetSeq)}");
            }
            foreach (var g in t.ScheduleSlot.GroupSubject.Groups)
            {
                var gi = Tables.GroupCode(g.Id);
                var w = code >> 6;
                var present = gi < _group.Length && w < _group[gi].Length && _group[gi][w] > 0;
                if (!present)
                {
                    drift++;
                    if (logOffender != null && logged++ < 3)
                        logOffender($"group-bit MISSING: series={t.SeriesId} group={g.Id} live={t.ScheduleSlot.Date.Date}/{t.ScheduleSlot.PairNumber} flatCode={t.FlatCode} expectedCode={code}");
                }
            }
        }

        // реверс-скан: зайвих учительських бітів бути не повинно (призначений
        // стан конфлікт-вільний → максимум 1 трекер на (teacher, code))
        var ownerCodes = new HashSet<(int ti, int code)>();
        foreach (var t in slots)
        {
            if (!t.IsAssigned || t.ScheduleSlot.PairNumber is < 0 or >= SeasonFlatTables.PairsCap) continue;
            var code = tables.DateCode(t.ScheduleSlot.Date, t.ScheduleSlot.PairNumber);
            if (code < 0) continue;
            ownerCodes.Add((tables.TeacherCode(t.ScheduleSlot.GroupSubject.Teacher.Id), code));
        }
        for (var e = 0; e < _teacher.Length; e++)
        {
            var row = _teacher[e];
            if (row == null) continue;
            for (var w = 0; w < row.Length; w++)
            {
                var bits = row[w];
                while (bits != 0)
                {
                    var b = System.Numerics.BitOperations.TrailingZeroCount(bits);
                    var code = w * 64 + b;
                    if (!ownerCodes.Contains((e, code)))
                    {
                        drift++;
                        if (logOffender != null && logged++ < 3)
                            logOffender($"teacher-bit EXTRA: entity={e} code={code} (живий трекер там відсутній)");
                    }
                    bits &= bits - 1;
                }
            }
        }
        VerifyRuns++;
        if (drift > 0)
            VerifyDrifts++;
        return drift;
    }
}
