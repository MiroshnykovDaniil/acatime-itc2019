namespace AcaTime.ScheduleCommon.Models.Api
{
    /// <summary>
    /// DTO для представлення аудиторії.
    /// </summary>
    public class SimplifiedClassroomDTO
    {
        /// <summary>Ідентифікатор аудиторії.</summary>
        public long Id { get; set; }

        /// <summary>Назва аудиторії.</summary>
        public string Name { get; set; }

        /// <summary>Кількість студентів в аудиторії.</summary>
        public int StudentCount { get; set; }

        /// <summary>Список обраних типів аудиторій.</summary>
        public List<SimplifiedSelectedClassroomTypeDTO> ClassroomTypes { get; set; } = new List<SimplifiedSelectedClassroomTypeDTO>();
    }
} 