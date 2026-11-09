namespace AcaTime.ScheduleCommon.Models.Api
{
    /// <summary>
    /// DTO для обраного типу аудиторії 
    /// </summary>
    public class SimplifiedSelectedClassroomTypeDTO
    {
        /// <summary>Пріоритет типу аудиторії.</summary>
        public int Priority { get; set; }

        /// <summary>Ідентифікатор типу аудиторії.</summary>
        public long ClassroomTypeId { get; set; }

        /// <summary>Назва типу аудиторії.</summary>
        public string ClassroomTypeName { get; set; }

    }
} 