using System.ComponentModel.DataAnnotations;

namespace AcaTime.ScheduleCommon.Models.Api
{
    /// <summary>
    /// Модель запиту для збереження розкладу
    /// </summary>
    public class SaveScheduleSlotsRequest
    {
        /// <summary>
        /// API-ключ для доступу до даних
        /// </summary>
        [Required]
        public string ApiKey { get; set; }

        /// <summary>
        /// Список слотів розкладу для збереження
        /// </summary>
        [Required]
        public List<SimplifiedScheduleSlotDTO> Slots { get; set; }

        /// <summary>
        /// Оцінка розкладу
        /// </summary>
        [Required]
        public int Score { get; set; }

        /// <summary>
        /// Назва алгоритму, який згенерував розклад
        /// </summary>
        [Required]
        public string AlgorithmName { get; set; }
    }
} 