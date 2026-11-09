using System;


namespace AcaTime.ScriptModels
{
    /// <summary>
    /// Інтерфейс для представлення слоту розкладу з властивостями тільки для читання.
    /// </summary>
    public interface IScheduleSlot
    {
        /// <summary>Ідентифікатор слоту розкладу.</summary>
        long Id { get; }

        /// <summary>Номер уроку.</summary>
        int LessonNumber { get; }

        /// <summary>Дата проведення уроку.</summary>
        DateTime Date { get; }

        /// <summary>Номер пари.</summary>
        int PairNumber { get; }

        /// <summary>Предмет, закріплений за групами.</summary>
        IGroupSubject GroupSubject { get; }

        /// <summary>
        /// Кількість уроків для предмета в один і той же день тижня і той же номер пари разом с поточним заняттям
        /// </summary>
        int LessonSeriesLength { get; }

        /// <summary>
        /// Optional classroom hint (ITC seed schedules); null when no room is
        /// assigned. Core AcaTime scheduling leaves this null.
        /// </summary>
        long? ClassroomId { get; }
    }
}