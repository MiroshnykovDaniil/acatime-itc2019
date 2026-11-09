namespace AcaTime.ScheduleCommon.Models.Api
{
    /// <summary>
    /// Спрощена версія DTO користувацьких функцій без циклічних залежностей
    /// </summary>
    public class SimplifiedUserFunctionsDTO
    {
        /// <summary>
        /// Функції оцінки оптимальності розкладу - тільки назви й описи
        /// </summary>
        public List<SimplifiedConstraintDTO> ScheduleEstimations { get; set; } = new List<SimplifiedConstraintDTO>();

        /// <summary>
        /// Функції оцінки оптимальності значення слоту - тільки назви й описи
        /// </summary>
        public List<SimplifiedConstraintDTO> ScheduleSlotEstimations { get; set; } = new List<SimplifiedConstraintDTO>();

        /// <summary>
        /// Абсолютні обмеження на значення слоту - тільки назви й описи
        /// </summary>
        public List<SimplifiedConstraintDTO> UnitaryConstraints { get; set; } = new List<SimplifiedConstraintDTO>();

        /// <summary>
        /// Пріоритет визначення розкладу для слоту - тільки назви й описи
        /// </summary>
        public List<SimplifiedConstraintDTO> SlotPriorities { get; set; } = new List<SimplifiedConstraintDTO>();

        /// <summary>
        /// Обмеження на значення слоту відносно інших розподілених слотів - тільки назви й описи
        /// </summary>
        public List<SimplifiedConstraintDTO> SlotValidators { get; set; } = new List<SimplifiedConstraintDTO>();
    }
} 