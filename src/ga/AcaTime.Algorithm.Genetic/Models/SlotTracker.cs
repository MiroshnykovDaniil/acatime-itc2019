using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.Algorithm.Genetic.Services.CheapEval;

namespace AcaTime.Algorithm.Genetic.Models
{
    public class SlotTracker
    {
        /// <summary>
        /// Рушій дешевої оцінки цього індивідуума (null поки не ініціалізовано).
        /// </summary>
        internal CheapEvaluationEngine? CheapEngine;

        /// <summary>Ознака незберігеної зміни позиції для дешевої оцінки.</summary>
        internal bool CheapDirty;
        internal DateTime CheapPrevDate;
        internal int CheapPrevPair;

        /// <summary>
        /// Код (day,pair) позиції слота в бітсетах FlatBitsets (-1 = бітів немає).
        /// Мirror TRUE-стану: жива позиція призначеного слота; фіксується при
        /// assign, знімається при unassign, перепoint-иться при move.
        /// </summary>
        internal int FlatCode = -1;
        internal long FlatSetSeq;

        /// <summary>
        /// Слот, для якого ведеться трекінг.
        /// </summary>
        public ScheduleSlotDTO ScheduleSlot { get; set; }

        // Copy-on-write для AvailableDomains/RejectedDomains: клон індивідуума
        // поділяє колекції з джерелом (але не мутує їх без копіювання).
        // true = колекція ексклюзивна (новостворені трекери, після forWrite).
        internal bool DomainsOwned = true;
        internal bool RejectedOwned = true;

        /// <summary>
        /// Доступні доменні значення (наприклад, дата та номер пари).
        /// </summary>
        public SortedSet<DomainValue> AvailableDomains { get; set; } = new SortedSet<DomainValue>();

        private SortedSet<DomainValue> AvailableDomainsForWrite()
        {
            if (!DomainsOwned)
            {
                AvailableDomains = new SortedSet<DomainValue>(AvailableDomains);
                DomainsOwned = true;
            }
            return AvailableDomains;
        }

        /// <summary>
        /// Відкинуті доменні значення, розбиті за кроками пошуку.
        /// Ключ – номер кроку, значення – список вилучених доменів.
        /// </summary>
        public Dictionary<int, List<DomainValue>> RejectedDomains { get; set; } = new Dictionary<int, List<DomainValue>>();

        private Dictionary<int, List<DomainValue>> RejectedDomainsForWrite()
        {
            if (!RejectedOwned)
            {
                RejectedDomains = RejectedDomains.ToDictionary(kvp => kvp.Key, kvp => new List<DomainValue>(kvp.Value));
                RejectedOwned = true;
            }
            return RejectedDomains;
        }

        /// <summary>
        /// Реєструє вилучені домени для кроку пошуку (COW: копіює словник за потреби).
        /// </summary>
        internal void AddRejectedDomains(int step, List<DomainValue> removed)
        {
            var rejected = RejectedDomainsForWrite();
            if (!rejected.ContainsKey(step))
                rejected[step] = new List<DomainValue>();
            rejected[step].AddRange(removed);
        }

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
            SetDomainRaw(val.Date, val.PairNumber);
        }

        public void SetDomain(DomainValue val)
        {
            SetDomainRaw(val.Date, val.PairNumber);
        }

        /// <summary>
        /// Єдина точка мутації позиції слота: фіксує попередню синхронізовану
        /// позицію для дешевої оцінки і оновлює поля, які бачать скрипти.
        /// </summary>
        internal void SetDomainRaw(DateTime date, int pair)
        {
            var prevDate = ScheduleSlot.Date;
            var prevPair = ScheduleSlot.PairNumber;
            if (CheapEngine != null && !CheapDirty &&
                (ScheduleSlot.Date != date || ScheduleSlot.PairNumber != pair))
            {
                CheapDirty = true;
                CheapPrevDate = prevDate;
                CheapPrevPair = prevPair;
                CheapEngine.NoteDirty(this);
            }
            ScheduleSlot.Date = date;
            ScheduleSlot.PairNumber = pair;
            // FLAT-дзеркало: живий переїзд призначеного слота (unassigned —
            // no-op усередині NoteMoved). Незалежно від CheapDirty-стану.
            CheapEngine?.FlatMoved(this, prevDate, prevPair);
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
                var restored = RejectedDomains[step];
                var available = AvailableDomainsForWrite();
                foreach (var domain in restored)
                {
                    if (!available.Contains(domain))
                    {
                        available.Add(domain);
                    }
                }
                RejectedDomainsForWrite().Remove(step);
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
