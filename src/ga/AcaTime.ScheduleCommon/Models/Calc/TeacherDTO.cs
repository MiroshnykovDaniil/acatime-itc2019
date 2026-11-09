using AcaTime.ScriptModels;

namespace AcaTime.ScheduleCommon.Models.Calc
{
    /// <summary>
    /// DTO для представлення викладача.
    /// </summary>
    public class TeacherDTO : ITeacher
    {
        /// <summary>Ідентифікатор викладача.</summary>
        public long Id { get; set; }

        /// <summary>Ім'я викладача.</summary>
        public string Name { get; set; }

        /// <summary>Посада викладача.</summary>
        public string Position { get; set; }
    }

}
