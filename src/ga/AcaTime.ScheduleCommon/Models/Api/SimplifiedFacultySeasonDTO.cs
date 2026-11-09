using AcaTime.ScheduleCommon.Models.Calc;

namespace AcaTime.ScheduleCommon.Models.Api
{
    /// <summary>
    /// Спрощена версія DTO факультету та сезону без циклічних залежностей
    /// </summary>
    public class SimplifiedFacultySeasonDTO
    {
        /// <summary>Ідентифікатор факультету та сезону.</summary>
        public long Id { get; set; }

        /// <summary>Назва факультету та сезону.</summary>
        public string Name { get; set; }

        /// <summary>Дата початку занять</summary>
        public DateTime BeginSeason { get; set; }

        /// <summary>Дата завершення занять</summary>
        public DateTime EndSeason { get; set; }

        /// <summary>Максимальна кількість пар в день</summary>
        public int MaxLessonsPerDay { get; set; }

        /// <summary>Список предметів, закріплених за групами.</summary>
        public List<SimplifiedGroupSubjectDTO> GroupSubjects { get; set; } = new List<SimplifiedGroupSubjectDTO>();

        /// <summary>Список аудиторій.</summary>
        public List<SimplifiedClassroomDTO> Classrooms { get; set; } = new List<SimplifiedClassroomDTO>();
    }
} 