using AcaTime.ScheduleCommon.Models;
using AcaTime.ScheduleCommon.Models.Constraints;
using System.Collections.Generic;

namespace AcaTime.ScheduleCommon.Models.Api
{
    /// <summary>
    /// DTO для передачі даних розкладу
    /// </summary>
    public class SimplifiedScheduleDataDto
    {
        /// <summary>
        /// Дані факультету та сезону
        /// </summary>
        public SimplifiedFacultySeasonDTO FacultySeason { get; set; }

        /// <summary>
        /// Функції користувача (оцінки, обмеження, тощо)
        /// </summary>
        public SimplifiedUserFunctionsDTO UserFunctions { get; set; }
    }
} 