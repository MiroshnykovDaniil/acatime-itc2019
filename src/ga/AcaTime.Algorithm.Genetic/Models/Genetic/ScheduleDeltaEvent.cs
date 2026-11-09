namespace AcaTime.Algorithm.Genetic.Models.Genetic;

public sealed class ScheduleDeltaEvent
{
    public string Operation { get; }
    public int ScoreDelta { get; }
    public IReadOnlyDictionary<SlotGeneKey, SlotGene> Changes { get; }

    private ScheduleDeltaEvent(
        string operation,
        int scoreDelta,
        IReadOnlyDictionary<SlotGeneKey, SlotGene> changes)
    {
        Operation = operation;
        ScoreDelta = scoreDelta;
        Changes = changes;
    }

    public static ScheduleDeltaEvent? FromDifference(
        ScheduleGenome before,
        ScheduleGenome after,
        string operation)
    {
        var changedKeys = after.Genes
            .Where(pair => !before.Genes.TryGetValue(pair.Key, out var previous) || previous != pair.Value)
            .Select(pair => pair.Key)
            .ToHashSet();

        // A transfer must contain complete series, never a partial series.
        var seriesKeys = changedKeys
            .GroupBy(x => (x.GroupSubjectId, x.SeriesId))
            .Where(group => group.Key.SeriesId.HasValue)
            .Where(group => before.Genes.Keys.Count(x =>
                x.GroupSubjectId == group.Key.GroupSubjectId && x.SeriesId == group.Key.SeriesId) == group.Count())
            .SelectMany(group => before.Genes.Keys.Where(x =>
                x.GroupSubjectId == group.Key.GroupSubjectId && x.SeriesId == group.Key.SeriesId))
            .ToHashSet();

        var changes = after.Genes
            .Where(pair => seriesKeys.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return changes.Count == 0
            ? null
            : new ScheduleDeltaEvent(
                operation,
                (after.Fitness ?? 0) - (before.Fitness ?? 0),
                changes);
    }

    public void ApplyTo(ScheduleGenome genome)
    {
        foreach (var change in Changes)
            genome.Set(change.Key, change.Value);
    }

    public static ScheduleDeltaEvent? Combine(ScheduleDeltaEvent first, ScheduleDeltaEvent second)
    {
        var changes = new Dictionary<SlotGeneKey, SlotGene>(first.Changes);
        foreach (var change in second.Changes)
        {
            if (changes.TryGetValue(change.Key, out var existing) && existing != change.Value)
                return null;

            changes[change.Key] = change.Value;
        }

        return new ScheduleDeltaEvent(
            "Recombination",
            first.ScoreDelta + second.ScoreDelta,
            changes);
    }
}
