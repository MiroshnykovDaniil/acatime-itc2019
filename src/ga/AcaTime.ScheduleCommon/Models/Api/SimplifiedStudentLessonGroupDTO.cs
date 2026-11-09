namespace AcaTime.ScheduleCommon.Models.Api
{
    /// <summary>
    /// Спрощена версія DTO групи студентів
    /// </summary>
    public class SimplifiedStudentLessonGroupDTO
    {
        /// <summary>Ідентифікатор групи.</summary>
        public long Id { get; set; }

        /// <summary>Назва групи.</summary>
        public string Name { get; set; }

        /// <summary>Назва підгрупи (якщо є).</summary>
        public string? SubgroupName { get; set; }

        /// <summary>Ід підгрупи (якщо є).</summary>
        public long? SubgroupId { get; set; }

        /// <summary>Ідентифікатор варіанту розподілу (якщо є).</summary>
        public long? SubgroupVariantId { get; set; }

        /// <summary>Назва варіанту розподілу (якщо є).</summary>
        public string? SubgroupVariantName { get; set; }

        /// <summary>На скільки підгруп ділить варіант розподілу.</summary>
        public int SubgroupCount { get; set; }

        /// <summary>Ідентифікатор курсу.</summary>
        public long CourseYearId { get; set; }

        /// <summary>Назва курсу.</summary>
        public string CourseYearName { get; set; }

        /// <summary>Ідентифікатор освітньої програми.</summary>
        public long EducationalProgramId { get; set; }

        /// <summary>Назва освітньої програми.</summary>
        public string EducationalProgramName { get; set; }
    }
} 