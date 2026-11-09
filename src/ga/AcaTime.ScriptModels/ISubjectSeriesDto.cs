namespace AcaTime.ScriptModels
{
    public interface ISubjectSeriesDto
    {
        int NumberOfLessons { get; }
        int SeriesNumber { get; }
        SubjectSeriesSplitType SplitType { get; }

        /// <summary>
/// Початок серії може бути в будь-якому тижні, інакше початковий тиждень перший для однотипних серій та перший + другий для двотижневих
        /// </summary>
        bool StartInAnyWeek { get; }
    }
}