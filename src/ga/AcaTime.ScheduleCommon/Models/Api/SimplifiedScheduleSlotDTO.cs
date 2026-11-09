namespace AcaTime.ScheduleCommon.Models.Api
{
    /// <summary>
    /// Спрощена версія DTO слоту розкладу
    /// </summary>
    public class SimplifiedScheduleSlotDTO
    {
        /// <summary>Ідентифікатор розкладеного уроку.</summary>
        public long Id { get; set; }

        /// <summary>Номер уроку.</summary>
        public int LessonNumber { get; set; }

        /// <summary>Дата проведення уроку.</summary>
        public DateTime Date { get; set; }

        /// <summary>Номер пари в розкладі.</summary>
        public int PairNumber { get; set; }

        /// <summary>
        /// Ідентифікатор предмета, до якого належить слот
        /// </summary>
        public long GroupSubjectId { get; set; }

        /// <summary>
        /// Кількість уроків для предмета в один і той же день тижня і той же номер пари
        /// </summary>
        public int LessonSeriesLength { get; set; } = 1;

        /// <summary>
        /// Ідентифікатор серії уроків. Використовується для об'єднання уроків в одну серію.
        /// </summary>
        public int? LessonSeriesId { get; set; }


        /// <summary>   
        /// Ідентифікатор аудиторії для заняття
        /// </summary>
        public long? ClassroomId { get; set; }
    }
} 