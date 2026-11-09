using AcaTime.ScheduleCommon.Models.Calc;

namespace AcaTime.Algorithm.Genetic.Models.Genetic;

/// <summary>
/// Compact representation of a schedule candidate.
/// It intentionally contains no DTOs, trackers, caches or object references.
/// </summary>
public sealed class ScheduleGenome
{
    private readonly Dictionary<SlotGeneKey, SlotGene> genes;

    public int? Fitness { get; set; }

    public int Count => genes.Count;

    public IReadOnlyDictionary<SlotGeneKey, SlotGene> Genes => genes;

    public ScheduleGenome()
    {
        genes = new Dictionary<SlotGeneKey, SlotGene>();
    }

    private ScheduleGenome(Dictionary<SlotGeneKey, SlotGene> genes, int? fitness)
    {
        this.genes = genes;
        Fitness = fitness;
    }

    public static ScheduleGenome FromResult(AlgorithmResultDTO result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return FromSlots(result.ScheduleSlots, result.TotalEstimation);
    }

    public static ScheduleGenome FromSlots(IEnumerable<ScheduleSlotDTO> slots, int? fitness = null)
    {
        ArgumentNullException.ThrowIfNull(slots);

        var genome = new ScheduleGenome { Fitness = fitness };
        foreach (var slot in slots)
            genome.Set(slot);

        return genome;
    }

    public ScheduleGenome Clone()
    {
        return new ScheduleGenome(new Dictionary<SlotGeneKey, SlotGene>(genes), Fitness);
    }

    public bool TryGet(long groupSubjectId, long slotId, int lessonNumber, out SlotGene gene)
    {
        var match = genes.FirstOrDefault(x =>
            x.Key.GroupSubjectId == groupSubjectId &&
            x.Key.SlotId == slotId &&
            x.Key.LessonNumber == lessonNumber);
        gene = match.Value;
        return !match.Equals(default(KeyValuePair<SlotGeneKey, SlotGene>));
    }

    public void Set(ScheduleSlotDTO slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(slot.GroupSubject);

        var key = SlotGeneKey.From(slot);
        genes[key] = SlotGene.From(slot);
    }

    public void Set(SlotGeneKey key, SlotGene gene)
    {
        genes[key] = gene;
    }

    public bool Remove(long groupSubjectId, long slotId, int lessonNumber)
    {
        var key = genes.Keys.FirstOrDefault(x =>
            x.GroupSubjectId == groupSubjectId &&
            x.SlotId == slotId &&
            x.LessonNumber == lessonNumber);
        return !key.Equals(default(SlotGeneKey)) && genes.Remove(key);
    }

    /// <summary>
    /// Copies one group-subject block from a donor. Existing values are returned
    /// so the caller can commit or roll back the operation.
    /// </summary>
    public GenomeChangeSet TransferGroupSubjectFrom(ScheduleGenome donor, long groupSubjectId)
    {
        ArgumentNullException.ThrowIfNull(donor);

        var changes = new GenomeChangeSet(this);
        foreach (var pair in donor.genes)
        {
            if (pair.Key.GroupSubjectId != groupSubjectId)
                continue;

            changes.Remember(pair.Key);
            genes[pair.Key] = pair.Value;
        }

        return changes;
    }

    internal void Restore(SlotGeneKey key, SlotGene? previous)
    {
        if (previous.HasValue)
            genes[key] = previous.Value;
        else
            genes.Remove(key);
    }

    /// <summary>
    /// Applies the compact genome to an existing schedule object graph.
    /// The graph is owned by the caller and should normally be a clone.
    /// </summary>
    public int ApplyTo(FacultySeasonDTO root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var applied = 0;
        foreach (var slot in root.GroupSubjects.SelectMany(x => x.ScheduleSlots))
        {
            var key = SlotGeneKey.From(slot);
            if (!genes.TryGetValue(key, out var gene))
                continue;

            slot.Date = gene.Date;
            slot.PairNumber = gene.PairNumber;
            slot.Classroom = gene.ClassroomId.HasValue
                ? root.Classrooms.FirstOrDefault(x => x.Id == gene.ClassroomId.Value)
                : null;
            applied++;
        }

        return applied;
    }

}

public readonly record struct SlotGeneKey(long GroupSubjectId, long SlotId, int LessonNumber, int? SeriesId = null)
{
    public static SlotGeneKey From(ScheduleSlotDTO slot)
    {
        return new SlotGeneKey(slot.GroupSubject.Id, slot.Id, slot.LessonNumber, slot.LessonSeriesId);
    }
}

public readonly record struct SlotGene(DateTime Date, int PairNumber, long? ClassroomId)
{
    public static SlotGene From(ScheduleSlotDTO slot)
    {
        return new SlotGene(slot.Date, slot.PairNumber, slot.Classroom?.Id);
    }
}

public sealed class GenomeChangeSet
{
    private readonly ScheduleGenome genome;
    private readonly Dictionary<SlotGeneKey, SlotGene?> previous = new();
    private bool completed;

    internal GenomeChangeSet(ScheduleGenome genome)
    {
        this.genome = genome;
    }

    internal void Remember(SlotGeneKey key)
    {
        if (!previous.ContainsKey(key))
            previous[key] = genome.Genes.TryGetValue(key, out var value) ? value : null;
    }

    public void Commit()
    {
        completed = true;
    }

    public void Rollback()
    {
        if (completed)
            return;

        foreach (var change in previous)
            genome.Restore(change.Key, change.Value);

        completed = true;
    }
}
