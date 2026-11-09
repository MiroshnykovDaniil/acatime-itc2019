using System.ComponentModel.DataAnnotations;

namespace AcaTime.ScheduleCommon.Models.Api
{
    /// <summary>
    /// Модель запиту з API-ключем
    /// </summary>
    public class ApiKeyRequest
    {
        /// <summary>
        /// API-ключ для доступу до даних
        /// </summary>
        [Required]
        public string ApiKey { get; set; }
    }
} 