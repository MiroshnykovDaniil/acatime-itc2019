namespace AcaTime.ScheduleCommon.Models.Api
{
    /// <summary>
    /// Спрощена версія DTO обмеження без циклічних залежностей
    /// </summary>
    public class SimplifiedConstraintDTO
    {
        /// <summary>
        /// Ідентифікатор обмеження
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Назва обмеження
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Опис обмеження
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Скрипт вибору об'єктів для обмеження
        /// </summary>
        public string? SelectorScript { get; set; }

        /// <summary>
        /// Скрипт перевірки об'єктів для обмеження
        /// </summary>
        public string? MainScript { get; set; }
    }
} 