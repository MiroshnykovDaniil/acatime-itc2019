using AcaTime.ScheduleCommon.Models.Calc;

namespace AcaTime.Algorithm.Default.Models
{
    public class SlotTracker
    {
        /// <summary>
        /// Слот, для якого ведеться трекінг.
        /// </summary>
        public ScheduleSlotDTO ScheduleSlot { get; set; }

        /// <summary>
        /// Доступні доменні значення (наприклад, дата та номер пари).
        /// </summary>
        public SortedSet<DomainValue> AvailableDomains { get; set; } = new SortedSet<DomainValue>();

        /// <summary>
        /// Відкинуті доменні значення, розбиті за кроками пошуку.
        /// Ключ – номер кроку, значення – список вилучених доменів.
        /// </summary>
        public Dictionary<int, List<DomainValue>> RejectedDomains { get; set; } = new Dictionary<int, List<DomainValue>>();

        /// <summary>
        /// Прапорець, що позначає, чи був для цього слоту зроблений вибір.
        /// </summary>
        public bool IsAssigned { get; set; }

        /// <summary>
        /// Крок, на якому було здійснено призначення.
        /// </summary>
        public int AssignStep { get; set; }

        /// <summary>
        /// Призначає домен для слоту.
        /// </summary>
        /// <param name="val">Вибране доменне значення.</param>
        /// <param name="step">Поточний крок призначення.</param>
        public void SetDomain(DomainValue val, int step)
        {
            AssignStep = step;
            ScheduleSlot.Date = val.Date;
            ScheduleSlot.PairNumber = val.PairNumber;
        }

        /// <summary>
        /// Відновлює відкинуті доменні значення для заданого кроку.
        /// Додає їх назад до списку доступних, якщо вони там відсутні,
        /// і видаляє запис для цього кроку.
        /// </summary>
        /// <param name="step">Номер кроку, для якого потрібно відновлення.</param>
        public void RestoreRejectedDomains(int step)
        {
            if (RejectedDomains.ContainsKey(step))
            {
                foreach (var domain in RejectedDomains[step])
                {
                    if (!AvailableDomains.Contains(domain))
                    {
                        AvailableDomains.Add(domain);
                    }
                }
                RejectedDomains.Remove(step);
            }
        }



        // слоти з однієї серії відносяться до одного предмету та проводяться в один і той самий день тижня з однаковим номером пари
        public int? SeriesId { get { return ScheduleSlot.LessonSeriesId; } set { ScheduleSlot.LessonSeriesId = value; } }
        public int SeriesLength {get {return ScheduleSlot.LessonSeriesLength;} set {ScheduleSlot.LessonSeriesLength = value;}}
        public int WeekShift { get; set; }
        public bool IsFirstTrackerInSeries { get; set; } = false;
        public bool IsLowDaysDanger { get; set; }


       

    }


}
