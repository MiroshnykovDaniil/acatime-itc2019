using System;
using System.Collections.Generic;


namespace AcaTime.ScriptModels
{
    /// <summary>
    /// Інтерфейс для представлення факультету та сезону з властивостями тільки для читання.
    /// </summary>
    public interface IFacultySeason
    {
        /// <summary>Ідентифікатор факультету та сезону.</summary>
        long Id { get; }

        /// <summary>Назва факультету та сезону.</summary>
        string Name { get; }

        /// <summary>Список предметів, закріплених за групами.</summary>
        IReadOnlyList<IGroupSubject> GroupSubjects { get; }

        /// <summary>Дата початку занять</summary>
        DateTime BeginSeason { get; }

        /// <summary>Дата завершення занять</summary>
        DateTime EndSeason { get; }

        /// <summary>Максимальна кількість пар в день</summary>
        int MaxLessonsPerDay { get; }
    }
}