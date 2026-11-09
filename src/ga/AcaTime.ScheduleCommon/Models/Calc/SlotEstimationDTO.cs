using AcaTime.ScriptModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AcaTime.ScheduleCommon.Models.Calc
{
    /// <summary>
    /// DTO для оцінки пріоритету залучення слота в розподіл розкладу.
    /// </summary>
    public class SlotEstimationDTO : ISlotEstimation
    {
        /// <summary>Кількість доступних доменів для слота (наприклад, кількість доступних аудиторій).</summary>
        public int AvailableDomains { get; set; }

        /// <summary>Довжина серії уроків, яку задає слот.</summary>
        public int LessonSeriesLength { get; set; }

        /// <summary>Кількість груп, які беруть участь у слоті.</summary>
        public int GroupCount { get; set; }

        /// <summary>Позначка, що останній урок серії припадає на неповний тиждень.</summary>
        public bool EndsOnIncompleteWeek { get; set; }

        /// <summary>
        /// Предмет до якого належить слот
        /// </summary>
        public GroupSubjectDTO GroupSubject { get; set; }

        IGroupSubject ISlotEstimation.GroupSubject => GroupSubject;
    }
}
