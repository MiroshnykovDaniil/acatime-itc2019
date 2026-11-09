using AcaTime.ScriptModels;

namespace AcaTime.ScheduleCommon.Models.Constraints
{
    /// <summary>
    /// Оцінка вибору слота для розкладу
    /// </summary>
    public class SlotEstimation : BaseConstraint
    {
        public Func<ISlotEstimation, int> Func { get; set; }

        public int Estimate(ISlotEstimation slot) => Func(slot);
    }
}
