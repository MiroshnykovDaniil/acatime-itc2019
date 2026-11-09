using System.Collections.Generic;


namespace AcaTime.ScriptModels
{
    /// <summary>
    /// Інтерфейс для представлення предмету, закріпленого за групами, з властивостями тільки для читання.
    /// </summary>
    public interface IGroupSubject
    {
        /// <summary>Ідентифікатор предмету, закріпленого за групами.</summary>
        long Id { get; }

        /// <summary>Викладач, який веде предмет.</summary>
        ITeacher Teacher { get; }

        /// <summary>Предмет.</summary>
        ISubject Subject { get; }

        /// <summary>Факультет та сезон.</summary>
        IFacultySeason Faculty { get; }

        /// <summary>Список груп студентів.</summary>
        IReadOnlyList<IStudentLessonGroup> Groups { get; }

        /// <summary>Список слотів розкладу.</summary>
        IReadOnlyList<IScheduleSlot> ScheduleSlots { get; }
    }
}