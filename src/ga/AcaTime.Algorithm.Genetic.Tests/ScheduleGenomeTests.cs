using AcaTime.Algorithm.Genetic.Models.Genetic;
using AcaTime.ScheduleCommon.Models.Calc;
using Xunit;

namespace AcaTime.Algorithm.Genetic.Tests;

public class ScheduleGenomeTests
{
    [Fact]
    public void Clone_IsolatedFromOriginal()
    {
        var original = ScheduleGenome.FromResult(new AlgorithmResultDTO
        {
            TotalEstimation = 42,
            ScheduleSlots = [CreateSlot(1, 10, 1, 1)]
        });

        var clone = original.Clone();
        clone.Set(CreateSlot(1, 11, 2, 2));

        Assert.Equal(1, original.Count);
        Assert.Equal(2, clone.Count);
        Assert.True(original.TryGet(1, 10, 1, out var originalGene));
        Assert.Equal(1, originalGene.PairNumber);
    }

    [Fact]
    public void TransferGroupSubject_CanBeRolledBack()
    {
        var recipient = ScheduleGenome.FromResult(new AlgorithmResultDTO
        {
            ScheduleSlots = [CreateSlot(1, 10, 1, 1)]
        });
        var donor = ScheduleGenome.FromResult(new AlgorithmResultDTO
        {
            ScheduleSlots = [CreateSlot(1, 10, 1, 5)]
        });

        var change = recipient.TransferGroupSubjectFrom(donor, 1);
        Assert.True(recipient.TryGet(1, 10, 1, out var changedGene));
        Assert.Equal(5, changedGene.PairNumber);

        change.Rollback();

        Assert.True(recipient.TryGet(1, 10, 1, out var restoredGene));
        Assert.Equal(1, restoredGene.PairNumber);
    }

    [Fact]
    public void ApplyTo_RestoresPlacementWithoutRetainingDtoReferences()
    {
        var classroom = new ClassroomDTO { Id = 7, Name = "A" };
        var sourceSlot = CreateSlot(1, 10, 1, 3);
        sourceSlot.Classroom = classroom;
        var genome = ScheduleGenome.FromResult(new AlgorithmResultDTO
        {
            ScheduleSlots = [sourceSlot]
        });

        var targetClassroom = new ClassroomDTO { Id = 7, Name = "A" };
        var targetSlot = CreateSlot(1, 10, 1, 1);
        var target = new FacultySeasonDTO
        {
            Classrooms = [targetClassroom],
            GroupSubjects = [new GroupSubjectDTO { Id = 1, ScheduleSlots = [targetSlot] }]
        };

        Assert.Equal(1, genome.ApplyTo(target));
        Assert.Equal(3, targetSlot.PairNumber);
        Assert.Same(targetClassroom, targetSlot.Classroom);
        Assert.NotSame(classroom, targetSlot.Classroom);
    }

    private static ScheduleSlotDTO CreateSlot(long groupSubjectId, long slotId, int lessonNumber, int pairNumber)
    {
        return new ScheduleSlotDTO
        {
            Id = slotId,
            LessonNumber = lessonNumber,
            Date = new DateTime(2026, 1, 12),
            PairNumber = pairNumber,
            GroupSubject = new GroupSubjectDTO { Id = groupSubjectId }
        };
    }
}
