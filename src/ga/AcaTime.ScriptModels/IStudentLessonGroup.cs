namespace AcaTime.ScriptModels
{
    /// <summary>
    /// Інтерфейс для представлення групи студентів з властивостями тільки для читання.
    /// </summary>
    public interface IStudentLessonGroup
    {
        /// <summary>Ідентифікатор групи студентів.</summary>
        long Id { get; }

        /// <summary>Назва групи студентів.</summary>
        string Name { get; }

        /// <summary>Назва підгрупи.</summary>
        string? SubgroupName { get; }

        /// <summary>Ідентифікатор підгрупи.</summary>
        long? SubgroupId { get; }

        /// <summary>Ідентифікатор варіанту підгрупи.</summary>
        long? SubgroupVariantId { get; }

        /// <summary>Назва варіанту підгрупи.</summary>
        string? SubgroupVariantName { get; }

        /// <summary>Кількість підгруп.</summary>
        int SubgroupCount { get; }

        /// <summary>Ідентифікатор курсу.</summary>
        long CourseYearId { get; }

        /// <summary>Назва курсу.</summary>
        string CourseYearName { get; }

        /// <summary>Ідентифікатор освітньої програми.</summary>
        long EducationalProgramId { get; }

        /// <summary>Назва освітньої програми.</summary>
        string EducationalProgramName { get; }
    }
}