namespace AcaTime.ScheduleCommon.Models.Calc
{
    /// <summary>
    /// DTO для представлення аудиторії.
    /// </summary>
    public class ClassroomDTO
    {
        /// <summary>Ідентифікатор аудиторії.</summary>
        public long Id { get; set; }

        /// <summary>Назва аудиторії.</summary>
        public string Name { get; set; }

        /// <summary>Кількість студентів в аудиторії.</summary>
        public int StudentCount { get; set; }

         /// <summary>Список обраних типів аудиторій.</summary>
        public List<SelectedClassroomTypeDTO> ClassroomTypes { get; set; } = new List<SelectedClassroomTypeDTO>();
    }
}
