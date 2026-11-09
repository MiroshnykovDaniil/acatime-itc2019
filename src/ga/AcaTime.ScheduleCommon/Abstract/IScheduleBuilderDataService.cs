using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScheduleCommon.Models.Constraints;

namespace AcaTime.ScheduleCommon.Abstract
{
    /// <summary>
    /// Інтерфейс для отримання та збереження даних для побудови розкладу
    /// </summary>
    public interface IScheduleBuilderDataService
    {
        /// <summary>
        /// Дані для розкладу
        /// </summary>
        /// <param name="facultySeasonId"></param>
        /// <returns></returns>
     //   Task<FacultySeasonDTO> GetFacultySeasonScheduleAsync(long facultySeasonId);

        /// <summary>
        /// Функції користувача
        /// </summary>        
     //   Task<UserFunctions> GetUserFunctions(long facultySeasonId);

        /// <summary>
        /// Збереження помилки
        /// </summary>
        /// <param name="facultySeasonId"></param>
        /// <param name="algorithmName"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        Task<long> SaveError(long facultySeasonId, string algorithmName, string error);

        /// <summary>
        /// Збереження варіанту розкладу
        /// </summary>
        /// <param name="facultySeasonId"></param>
        /// <param name="slotsDto"></param>
        /// <param name="score"></param>
        /// <param name="variantName"></param>
        /// <param name="clearPrev"></param>
        /// <returns></returns>
        Task<long> SaveScheduleSlots(long facultySeasonId, List<ScheduleSlotDTO> slotsDto, int score, string variantName);
    }
}