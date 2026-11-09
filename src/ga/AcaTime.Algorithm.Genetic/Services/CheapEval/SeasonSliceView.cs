using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScriptModels;

namespace AcaTime.Algorithm.Genetic.Services.CheapEval;

/// <summary>
/// В'ю сезону з фільтром: або повний сезон, або порожній (Filter = MinValue),
/// або підмножина GroupSubjects (для клітинки (unit, date)).
/// Слоти віддаються з per-(gs,date) індексу рушія — сам об'єкт Root не копіюється.
/// Обгортки Groups фільтруються до unit, щоб спільні GroupSubjects не "протікали"
/// між клітинками різних unit'ів (правило всередині клітинки бачить лише свій unit).
/// </summary>
public class SeasonSliceView : IFacultySeason
{
    private readonly FacultySeasonDTO _source;
    private readonly List<SlicedGroupSubject> _all;
    private readonly Dictionary<long, SlicedGroupSubject> _byGs;
    private readonly Dictionary<(string family, long unit), List<SlicedGroupSubject>> _subsetCache = new();
    private static readonly List<ScheduleSlotDTO> EmptySlots = new();

    public DateTime Filter; // DateTime.MinValue => сезон без занять

    private IReadOnlyList<IGroupSubject> _current;
    internal Dictionary<long, Dictionary<DateTime, List<ScheduleSlotDTO>>> Index = new();

    /// <summary>
    /// Хеш КОНТЕНТУ кошика (gsId, date): xor міксів (SlotId, PairNumber) усіх
    /// членів. Порядок-незалежний, самінверсний — дешево підтримується в
    /// ConsumeDirtyToIndex (єдина точка мутації Index). Основа memo-кешу
    /// клітинок: однаковий композит хешів годівників => клітинка не змінилась.
    /// УВАГА: Classroom НЕ входить у хеш (у генетичній фазі статично null);
    /// порушення ловиться VerifyAll (вимкнення правила, точність не страждає).
    /// </summary>
    internal Dictionary<(long gsId, DateTime date), ulong> BucketHashes = new();

    internal ulong BucketHash(long gsId, DateTime date)
        => BucketHashes.TryGetValue((gsId, date), out var h) ? h : 0UL;

    internal static ulong SlotHash(ScheduleSlotDTO slot)
        => SlotHashWithPair(slot, slot.PairNumber);

    /// <summary>Внесок слота з ЯВНОю парою (для xor-out попереднього стану).</summary>
    internal static ulong SlotHashWithPair(ScheduleSlotDTO slot, int pair)
        => (ulong)slot.Id * 0x9E3779B97F4A7C15UL
           ^ ((ulong)(uint)pair + 0x9E3779B97F4A7C15UL) * 0xBF58476D1CE4E5B9UL;

    /// <summary>Повний перерахунок хешів з Index (викликається після BuildIndex).</summary>
    internal void RebuildBucketHashes()
    {
        BucketHashes.Clear();
        foreach (var (gsId, byDate) in Index)
            foreach (var (date, bucket) in byDate)
            {
                ulong h = 0;
                foreach (var slot in bucket)
                    h ^= SlotHash(slot);
                BucketHashes[(gsId, date)] = h;
            }
    }

    public SeasonSliceView(FacultySeasonDTO source)
    {
        _source = source;
        _all = source.GroupSubjects.Select(g => new SlicedGroupSubject(this, g)).ToList();
        _byGs = _all.ToDictionary(w => w.Id);
        _current = _all;
    }

    public long Id => _source.Id;
    public string Name => _source.Name;
    public DateTime BeginSeason => _source.BeginSeason;
    public DateTime EndSeason => _source.EndSeason;
    public int MaxLessonsPerDay => _source.MaxLessonsPerDay;

    /// <summary>Те, що бачить скрипт: повний сезон або поточна підмножина.</summary>
    public IReadOnlyList<IGroupSubject> GroupSubjects => _current;

    /// <summary>Переключити в'ю на повний сезон (без фільтра груп).</summary>
    public void UseAll()
    {
        foreach (var w in _all)
            w.UnitFilter = null;
        _current = _all;
    }

    /// <summary>
    /// Переключити в'ю на підмножину GSів unit'а; у кожної обгортки Groups фільтрується
    /// до цього unit'а, щоб правило бачило лише його групи/підгрупи/викладача.
    /// </summary>
    public void UseSubset(RuleFamily family, long unit)
    {
        if (!_subsetCache.TryGetValue((family.Name, unit), out var subset))
        {
            subset = family.Members[unit].Select(id => _byGs[id]).ToList();
            _subsetCache[(family.Name, unit)] = subset;
        }

        if (family.GroupKeyOf != null)
        {
            var filter = (unit, family.GroupKeyOf);
            foreach (var w in subset)
                w.UnitFilter = filter;
        }
        else
        {
            foreach (var w in subset)
                w.UnitFilter = null;
        }

        _current = subset;
    }

    internal List<ScheduleSlotDTO> Buckets(long gsId)
    {
        if (Filter == DateTime.MinValue)
            return EmptySlots;
        return Index.TryGetValue(gsId, out var byDate) && byDate.TryGetValue(Filter, out var bucket)
            ? bucket
            : EmptySlots;
    }
}

public class SlicedGroupSubject : IGroupSubject
{
    private readonly GroupSubjectDTO _source;
    private readonly SeasonSliceView _view;

    public SlicedGroupSubject(SeasonSliceView view, GroupSubjectDTO source)
    {
        _view = view;
        _source = source;
    }

    internal (long unit, Func<IStudentLessonGroup, long> key)? UnitFilter;

    public long Id => _source.Id;
    public ITeacher Teacher => _source.Teacher;
    public ISubject Subject => _source.Subject;
    public IFacultySeason Faculty => _view;

    public IReadOnlyList<IStudentLessonGroup> Groups
    {
        get
        {
            if (UnitFilter == null)
                return _source.Groups;
            var (unit, key) = UnitFilter.Value;
            return _source.Groups.Where(g => key(g) == unit).ToList();
        }
    }

    public IReadOnlyList<IScheduleSlot> ScheduleSlots => _view.Buckets(Id);
}
