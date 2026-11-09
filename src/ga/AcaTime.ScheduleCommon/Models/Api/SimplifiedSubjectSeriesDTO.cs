namespace AcaTime.ScheduleCommon.Models.Api
{
    public class SimplifiedSubjectSeriesDTO
    {
          /// <summary>Номер серії.</summary>
        public int SeriesNumber { get; set; }

        /// <summary>
        /// Кількість уроків у серії.
        /// </summary>
        public int NumberOfLessons { get; set; }

        /// <summary>Тип розбиття серій уроків.</summary>
        public int SplitType { get; set; }

        /// <summary>
        /// Початок серії може бути в будь-якому тижні, інакше початковий тиждень перший для однотипних серій та перший + другий для двотижневих
        /// </summary>
        public bool StartInAnyWeek { get; set; }
    }
} 