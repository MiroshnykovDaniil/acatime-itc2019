using AcaTime.ScriptModels;

namespace AcaTime.ScheduleCommon.Models.Constraints
{
    /// <summary>
    /// Оцінка розкладу
    /// </summary>
    public class ScheduleEstimation : BaseConstraint 
    {   
        public Func<IFacultySeason, int> Func { get; set; }

        public int Estimate (IFacultySeason faculty) => Func (faculty);
    }
}
