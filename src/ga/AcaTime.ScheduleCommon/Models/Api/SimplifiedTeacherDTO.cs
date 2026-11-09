namespace AcaTime.ScheduleCommon.Models.Api
{
    /// <summary>
    /// Спрощена версія DTO викладача
    /// </summary>
    public class SimplifiedTeacherDTO
    {
        /// <summary>Ідентифікатор викладача.</summary>
        public long Id { get; set; }

        /// <summary>Ім'я викладача.</summary>
        public string Name { get; set; }

        /// <summary>Посада викладача.</summary>
        public string Position { get; set; }
    }
} 