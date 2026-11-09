using AcaTime.ScriptModels;

namespace AcaTime.ScheduleCommon.Models.Constraints
{
    /// <summary>
    /// Перевірка чи можна призначити слот в розклад
    /// </summary>
    public class ScheduleSlotValidation : BaseConstraint
    {
        public Func<IScheduleSlot, IAssignedSlots, bool> Func { get; set; }

        public bool Validate(IScheduleSlot slot, IAssignedSlots assigned) => Func(slot, assigned);
    }
}
