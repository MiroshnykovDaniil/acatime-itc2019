using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScheduleCommon.Models.Constraints;
using Microsoft.Extensions.Logging;

namespace AcaTime.ScheduleCommon.Abstract
{
    /// <summary>
    /// Алгоритм розподілу розкладу
    /// </summary>
    public interface IScheduleAlgorithm
    {

        /// <summary>
        /// Запускає алгоритм розподілу розкладу
        /// </summary>        
        Task<List<AlgorithmResultDTO>> Run(FacultySeasonDTO root, UserFunctions userFunctions, Dictionary<string, string> parameters, bool ignoreClassrooms, ILogger logger, CancellationToken cancellationToken = default);

        /// <summary>
        /// Отримує статистику роботи алгоритму, довільний формат. Просто корисна інформація для користувача.
        /// </summary>
        /// <returns></returns>
        string GetStatistics();

        /// <summary>
        /// Назва алгоритму. Бажано англомовна і коротка - використовується в конфігах та пропонується на ввод користувачем.
        /// </summary>
        /// <returns></returns>
        string GetName();   

        /// <summary>
        /// Отримує список параметрів, які використовує алгоритм. Значення параметрів або прописуються в конфігурації, або вводяться користувачем. Надаються на вхід функції Run як Dictionary<string, string>.
        /// </summary>
        /// <returns>Список параметрів з описом, типом та значенням за замовчуванням</returns>
        List<AlgorithmParameterDTO> GetParameters();
    }
}