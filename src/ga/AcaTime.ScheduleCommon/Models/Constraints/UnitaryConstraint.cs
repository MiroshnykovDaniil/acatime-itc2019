using AcaTime.ScriptModels;

namespace AcaTime.ScheduleCommon.Models.Constraints
{
    /// <summary>
    /// Обмеження для уроку
    /// </summary>
    public class UnitaryConstraint : BaseConstraint
    {
        public Func<IFacultySeason, IEnumerable<IScheduleSlot>> SelectorFunc { get; set; }

        public Func<IScheduleSlot, bool> Func { get; set; }
    

        public IEnumerable<IScheduleSlot> Select(IFacultySeason faculty) => SelectorFunc (faculty);

        public bool Check(IScheduleSlot slot) => Func(slot);
    }
}
