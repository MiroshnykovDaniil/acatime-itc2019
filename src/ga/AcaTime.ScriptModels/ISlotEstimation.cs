namespace AcaTime.ScriptModels
{
    /// <summary>
    /// Інтерфейс для оцінки слотів
    /// </summary>
    public interface ISlotEstimation
    {
        /// <summary>
        /// Кількість можливих варіантів для розкладу
        /// </summary>
        int AvailableDomains { get; }

        /// <summary>
        /// Чи закінчується на неповному тижні
        /// </summary>
        bool EndsOnIncompleteWeek { get; }

        /// <summary>
        /// Кількість груп
        /// </summary>
        int GroupCount { get; }

        /// <summary>
        /// Довжина серії занять
        /// </summary>
        int LessonSeriesLength { get; }


        /// <summary>Предмет, закріплений за групами.</summary>
        IGroupSubject GroupSubject { get; }
    }
}