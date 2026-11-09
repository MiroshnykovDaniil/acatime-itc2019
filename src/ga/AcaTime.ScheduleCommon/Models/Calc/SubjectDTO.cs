using System.ComponentModel.DataAnnotations;
using AcaTime.ScriptModels;

namespace AcaTime.ScheduleCommon.Models.Calc
{
    /// <summary>
    /// DTO для представлення предмету.
    /// </summary>
    public class SubjectDTO : ISubject
    {
        /// <summary>Ідентифікатор предмету.</summary>
        public long Id { get; set; }

        /// <summary>Назва предмету.</summary>
        public string Name { get; set; }
        

        /// <summary>Кількість пар по замовчуванню для однієї групи.</summary>
        public int NumberOfLessons { get; set; }

        /// <summary>Ідентифікатор дисципліни.</summary>
        public long DisciplineId { get; set; }

        /// <summary>Назва дисципліни.</summary>
        public string DisciplineName { get; set; }

        /// <summary>Ідентифікатор типу предмету.</summary>
        public long SubjectTypeId { get; set; }

        /// <summary>Назва типу предмету.</summary>
        public string SubjectTypeName { get; set; }

        /// <summary>Скорочена назва типу предмету.</summary>
        public string SubjectTypeShortName { get; set; }

        /// <summary>
        /// Список серій уроків, які вже визначені для цього предмету.
        /// </summary>
        public List<SubjectSeriesDto> DefinedSeries { get; set; } = new List<SubjectSeriesDto>();

        
        IReadOnlyList<ISubjectSeriesDto> ISubject.DefinedSeries => DefinedSeries;

        /// <summary>
        /// Не потребує аудиторії
        /// </summary>
        public bool NoClassroom { get; set; } = false;

        /// <summary>Список обраних типів аудиторій.</summary>
        public List<SelectedClassroomTypeDTO> ClassroomTypes { get; set; } = new List<SelectedClassroomTypeDTO>();
    }

}
