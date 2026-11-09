using System.ComponentModel.DataAnnotations;
using AcaTime.ScriptModels;

namespace AcaTime.ScheduleCommon.Models.Calc
{
    /// <summary>
    /// DTO для представлення факультету та сезону.
    /// </summary>
    public class FacultySeasonDTO : IFacultySeason
    {
        /// <summary>Ідентифікатор факультету та сезону.</summary>
        public long Id { get; set; }

        /// <summary>Назва факультету та сезону.</summary>
        public string Name { get; set; }

        /// <summary>Список предметів, закріплених за групами.</summary>
        public List<GroupSubjectDTO> GroupSubjects { get; set; } = new List<GroupSubjectDTO>();

        /// <summary>Дата початку занять</summary>
        public DateTime BeginSeason { get; set; } 

        /// <summary>Дата завершення занять</summary>
        public DateTime EndSeason { get; set; }

        /// <summary>Максимаотна кількість пар в день</summary>
        public int MaxLessonsPerDay { get; set; }

        /// <summary>Список аудиторій.</summary>
        public List<ClassroomDTO> Classrooms { get; set; } = new List<ClassroomDTO>();

        IReadOnlyList<IGroupSubject> IFacultySeason.GroupSubjects => GroupSubjects;
    }
}
