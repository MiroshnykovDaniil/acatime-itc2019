using AcaTime.ScheduleCommon.Models.Constraints;

namespace AcaTime.ScheduleCommon.Models.Calc
{
    /// <summary>
    /// DTO для передачі даних розкладу
    /// </summary>
    public class ScheduleDataDto
    {
        /// <summary>
        /// Дані факультету та сезону
        /// </summary>
        public FacultySeasonDTO FacultySeason { get; set; }

        /// <summary>
        /// Функції користувача (оцінки, обмеження, тощо)
        /// </summary>
        public UserFunctions UserFunctions { get; set; }
    }

}
