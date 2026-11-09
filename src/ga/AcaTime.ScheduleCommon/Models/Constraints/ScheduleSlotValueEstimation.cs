using AcaTime.ScriptModels;

namespace AcaTime.ScheduleCommon.Models.Constraints
{
    /// <summary>
    /// Оцінка комірки розкладу
    /// </summary>
    public class ScheduleSlotValueEstimation : BaseConstraint
    {
        public Func<IScheduleSlot, IAssignedSlots, int> Func { get; set; }

        public int Estimate(IScheduleSlot slot, IAssignedSlots assigned) => Func(slot, assigned);
    }
}
