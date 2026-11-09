namespace AcaTime.ScheduleCommon.Models.Calc
{
    /// <summary>
    /// DTO для обраного типу аудиторії 
    /// </summary>
    public class SelectedClassroomTypeDTO
    {
        /// <summary>Пріоритет типу аудиторії.</summary>
        public int Priority { get; set; }

        /// <summary>Ідентифікатор типу аудиторії.</summary>
        public long ClassroomTypeId { get; set; }      

        /// <summary>Назва типу аудиторії.</summary>
        public string ClassroomTypeName { get; set; }
        
    }
}
