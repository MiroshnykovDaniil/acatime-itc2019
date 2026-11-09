using AcaTime.ScriptModels;

namespace AcaTime.ScheduleCommon.Models.Calc
{
    /// <summary>
    /// DTO для представлення предметів, закріплених за групами.
    /// </summary>
    public class GroupSubjectDTO : IGroupSubject
    {
        /// <summary>Ідентифікатор предмета по групах.</summary>
        public long Id { get; set; }

        /// <summary>DTO для викладача, призначеного на предмет.</summary>
        public TeacherDTO Teacher { get; set; }

        /// <summary>DTO для предмету.</summary>
        public SubjectDTO Subject { get; set; }

        public FacultySeasonDTO Faculty { get; set; }

        /// <summary>Список DTO для груп та підгруп.</summary>
        public List<StudentLessonGroupDTO> Groups { get; set; } = new List<StudentLessonGroupDTO>();

        /// <summary>Список розкладених уроків.</summary>
        public List<ScheduleSlotDTO> ScheduleSlots { get; set; } = new List<ScheduleSlotDTO>();

        /// <summary>
        /// Кількість студентів в групі
        /// </summary>
        public int StudentCount { get; set; }

        ITeacher IGroupSubject.Teacher => Teacher;

        ISubject IGroupSubject.Subject => Subject;

        IFacultySeason IGroupSubject.Faculty => Faculty;

        IReadOnlyList<IStudentLessonGroup> IGroupSubject.Groups => Groups;

        IReadOnlyList<IScheduleSlot> IGroupSubject.ScheduleSlots => ScheduleSlots;
    }

}
