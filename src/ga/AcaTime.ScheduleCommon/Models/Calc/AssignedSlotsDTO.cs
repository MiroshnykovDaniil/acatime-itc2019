using AcaTime.ScriptModels;

namespace AcaTime.ScheduleCommon.Models.Calc
{
    /// <summary>
    /// Призначені слоти з підтримкою відкладеного прорахунку, кешування та потокобезпечного доступу.
    /// </summary>
    public class AssignedSlotsDTO : IAssignedSlots
    {
        private readonly Func<IEnumerable<IScheduleSlot>> _slotFactory;
        private readonly Func<long, IEnumerable<IScheduleSlot>> _teacherSlotFactory;
        private readonly Func<long, IEnumerable<IScheduleSlot>> _groupSlotFactory;

        private IReadOnlyList<IScheduleSlot> _slots;
        private readonly object _slotsLock = new();

        private readonly Func<long, DateTime, IEnumerable<IScheduleSlot>> _teacherAndDateSlotFactory;
        private readonly Func<long, DateTime, IEnumerable<IScheduleSlot>> _groupAndDateSlotFactory;

        /// <summary>
        /// Конструктор з підтримкою фабрик для відкладеного обчислення.
        /// </summary>
        public AssignedSlotsDTO(
            Func<IEnumerable<IScheduleSlot>> slotFactory,
            Func<long, IEnumerable<IScheduleSlot>> slotsByTeacherFactory,
            Func<long, IEnumerable<IScheduleSlot>> slotsByGroupFactory,
            Func<long, DateTime, IEnumerable<IScheduleSlot>> slotsByTeacherAndDateFactory,
            Func<long, DateTime, IEnumerable<IScheduleSlot>> slotsByGroupAndDateFactory)
        {
            _slotFactory = slotFactory;
            _teacherSlotFactory = slotsByTeacherFactory;
            _groupSlotFactory = slotsByGroupFactory;
            _teacherAndDateSlotFactory = slotsByTeacherAndDateFactory;
            _groupAndDateSlotFactory = slotsByGroupAndDateFactory;
        }
        
        /// <summary>
        /// Всі призначені слоти (відкладене обчислення, кешоване, потокобезпечне).
        /// </summary>
        public IReadOnlyList<IScheduleSlot> Slots
        {
            get
            {
                if (_slots != null)
                    return _slots;

                lock (_slotsLock)
                {
                    if (_slots == null)
                    {
                        _slots = _slotFactory().ToList();
                    }
                }

                return _slots;
            }
        }

        /// <summary>
        /// Призначені слоти для конкретного викладача (потокобезпечне кешування).
        /// </summary>
        public IReadOnlyList<IScheduleSlot> GetSlotsByTeacher(long teacherId)
        {
            return _teacherSlotFactory(teacherId).ToList();
        }

        /// <summary>
        /// Призначені слоти для конкретної групи (потокобезпечне кешування).
        /// </summary>
        public IReadOnlyList<IScheduleSlot> GetSlotsByGroup(long groupId)
        {
            return _groupSlotFactory(groupId).ToList();
        }

        /// <summary>
        /// Призначені слоти для конкретного викладача та дати (потокобезпечне кешування).
        /// </summary>
        public IReadOnlyList<IScheduleSlot> GetSlotsByTeacherAndDate(long teacherId, DateTime date)
        {
            return _teacherAndDateSlotFactory(teacherId, date).ToList();    
        }

        /// <summary>
        /// Призначені слоти для конкретної групи та дати (потокобезпечне кешування).
        /// </summary>
        public IReadOnlyList<IScheduleSlot> GetSlotsByGroupAndDate(long groupId, DateTime date)
        {
            return _groupAndDateSlotFactory(groupId, date).ToList();
        }
    }


}
