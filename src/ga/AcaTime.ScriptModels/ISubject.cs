namespace AcaTime.ScriptModels
{
    /// <summary>
    /// Інтерфейс для представлення предмету з властивостями тільки для читання.
    /// </summary>
    public interface ISubject
    {
        /// <summary>Ідентифікатор предмету.</summary>
        long Id { get; }

        /// <summary>Назва предмету.</summary>
        string Name { get; }

        /// <summary>Кількість уроків.</summary>
        int NumberOfLessons { get; }

        /// <summary>Ідентифікатор дисципліни.</summary>
        long DisciplineId { get; }

        /// <summary>Назва дисципліни.</summary>
        string DisciplineName { get; }

        /// <summary>Ідентифікатор типу предмету.</summary>
        long SubjectTypeId { get; }

        /// <summary>Назва типу предмету.</summary>
        string SubjectTypeName { get; }

        /// <summary>Скорочена назва типу предмету.</summary>
        string SubjectTypeShortName { get; }

        /// <summary>Список серій уроків, які вже визначені для цього предмету.</summary>
        IReadOnlyList<ISubjectSeriesDto> DefinedSeries { get; }

    }
}