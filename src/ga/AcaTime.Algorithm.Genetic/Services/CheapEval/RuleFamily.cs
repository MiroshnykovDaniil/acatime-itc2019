using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScriptModels;

namespace AcaTime.Algorithm.Genetic.Services.CheapEval;

/// <summary>
/// Сімейство "одиниць володіння" (ownership units) для чорно-скірнової декомпозиції
/// користувацьких правил оцінки. Правило вважається адитивним по клітинках
/// (unit, date), якщо сума його значень на підмножинах-в'ю дорівнює повній оцінці.
/// Жодної семантики правил тут немає — лише структура даних сезону.
/// </summary>
public class RuleFamily
{
    public string Name;
    public Func<IStudentLessonGroup, long>? GroupKeyOf;
    public IReadOnlyDictionary<long, List<long>> Members;
    public IReadOnlyDictionary<long, long[]> GsUnits;

    public RuleFamily(string name, Func<IStudentLessonGroup, long>? groupKeyOf, Dictionary<long, List<long>> members)
    {
        Name = name;
        GroupKeyOf = groupKeyOf;
        Members = members;

        var gsToUnits = new Dictionary<long, List<long>>();
        foreach (var (unit, gsIds) in members)
            foreach (var gs in gsIds)
            {
                if (!gsToUnits.TryGetValue(gs, out var list))
                    gsToUnits[gs] = list = new List<long>();
                list.Add(unit);
            }

        GsUnits = gsToUnits.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    /// <summary>
    /// Стандартні сімейства від найдрібніших підмножин до найкрупніших.
    /// Пошук іде знизу вверх: чим менша підмножина, тим дешевший запуск скрипта.
    /// </summary>
    public static List<RuleFamily> BuildDefault(FacultySeasonDTO root)
    {
        var gs = root.GroupSubjects;
        var list = new List<RuleFamily>
        {
            new("gs", null, gs.ToDictionary(g => g.Id, g => new List<long> { g.Id })),
            new("subgroupVariant", g => g.SubgroupVariantId ?? -1,
                GroupBy(gs, g => g.Groups.Where(x => x.SubgroupVariantId.HasValue).Select(x => (x.SubgroupVariantId!.Value, g.Id)))),
            new("subgroup", g => g.SubgroupId ?? -1,
                GroupBy(gs, g => g.Groups.Where(x => x.SubgroupId.HasValue).Select(x => (x.SubgroupId!.Value, g.Id)))),
            new("studentGroup", g => g.Id,
                GroupBy(gs, g => g.Groups.Select(x => (x.Id, g.Id)))),
            new("teacher", null,
                gs.GroupBy(g => g.Teacher.Id).ToDictionary(k => k.Key, k => k.Select(g => g.Id).Distinct().ToList())),
            new("courseYear", g => g.CourseYearId,
                GroupBy(gs, g => g.Groups.Select(x => (x.CourseYearId, g.Id)))),
        };
        return list.Where(f => f.Members.Count > 0).ToList();
    }

    private static Dictionary<long, List<long>> GroupBy(
        IEnumerable<GroupSubjectDTO> gs,
        Func<GroupSubjectDTO, IEnumerable<(long key, long gsId)>> selector)
        => gs.SelectMany(selector)
            .GroupBy(x => x.key)
            .ToDictionary(k => k.Key, k => k.Select(x => x.gsId).Distinct().ToList());
}
