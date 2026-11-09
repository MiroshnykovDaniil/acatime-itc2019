using global::AcaTime.ScheduleCommon.Models.Calc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;


namespace AcaTime.Algorithm.Default.Services.Calc
{

    /// <summary>
    /// Сервіс для покращення розподілу аудиторій у розкладі.
    /// </summary>
    public class ClassroomReassignmentService
    {
        // Статичне поле для “нульової” кімнати
        public static ClassroomDTO NullClassroom { get; } = new ClassroomDTO { Id = 0, Name = "NullClassroom" };

        private readonly ILogger logger;
        private readonly List<ClassroomDTO> classrooms;
        private readonly List<ScheduleSlotDTO> slots;

        //   private Dictionary<DateTime, Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>>> assignedClassrooms;

        // Тимчасові структури
        private readonly Dictionary<DateTime, Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>>> tempClassroomAssignments
            = new Dictionary<DateTime, Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>>>();

        private readonly Dictionary<ScheduleSlotDTO, ClassroomDTO> slotToClassroom
            = new Dictionary<ScheduleSlotDTO, ClassroomDTO>();

        // Набір “заморожених” слотів
        private readonly HashSet<ScheduleSlotDTO> frozenSlots = new HashSet<ScheduleSlotDTO>();

        /// <summary>
        /// Конструктор приймає вхідні параметри, необхідні для роботи сервісу.
        /// </summary>
        public ClassroomReassignmentService(
            ILogger logger,
            List<ClassroomDTO> classrooms,
            List<ScheduleSlotDTO> slots
        //  Dictionary<DateTime, Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>>> assignedClassrooms
        )
        {
            this.logger = logger;
            this.classrooms = classrooms;
            this.slots = slots.Where(s => !s.GroupSubject.Subject.NoClassroom).ToList();
        }

        /// <summary>
        /// Основний метод для покращення розподілу аудиторій (точка входу).
        /// </summary>
        /// <exception cref="Exception">Викидається у разі некоректних даних або конфліктів.</exception>
        public void ButifyResult()
        {

            // 1. Ініціалізація словників і початкове виставлення нульових аудиторій
            PrepareInitialAssignments();

            // 2. Призначення аудиторій для серій
            AttemptAssignRoomsForSeries();

            // 3. Призначення аудиторій для “залишкових” слотів (через алгоритм Хопкрофта-Карпа)
            AttemptAssignRoomsForLeftoverSlots();

            // 4. Фінальне застосування змін
            ApplyAssignments();
        }

        /// <summary>
        /// Ініціалізує “нульові” аудиторії для усіх слотів, яким потрібна кімната.
        /// </summary>
        private void PrepareInitialAssignments()
        {
            foreach (var slotTracker in slots)
            {
                // Присвоюємо кожному слоту “нульову” аудиторію
                slotToClassroom[slotTracker] = NullClassroom;
            }
        }

        /// <summary>
        /// Шукає та призначає аудиторії для серій за пріоритетом.
        /// </summary>
        private void AttemptAssignRoomsForSeries()
        {

            // 1. Отримуємо всі “серії”
            var seriesGroups = GetSeriesGroups();

            // 2. Перебираємо серії за пріоритетом
            foreach (var series in seriesGroups)
            {
                // Якщо всі слоти серії вже “заморожені” – пропускаємо
                if (AreAllSlotsFrozen(series))
                    continue;

                // 3. Шукаємо цільову аудиторію для серії
                var targetClassroom = FindTargetClassroomForSeries(series);

                // Якщо нічого не знайшли – пропускаємо
                if (targetClassroom == null)
                    continue;

                // 4. Призначаємо знайдену аудиторію для всієї серії
                AssignClassroomForSeries(series, targetClassroom);
            }
        }

        /// <summary>
        /// Повертає перелік серій (згрупованих слотів), відсортованих за пріоритетом.
        /// </summary>
        private List<SeriesGroup> GetSeriesGroups()
        {
            return slots
                .GroupBy(s => new { s.LessonSeriesId, GroupSubjectId = s.GroupSubject.Id })
                .Select(g => new SeriesGroup
                {
                    SeriesId = g.Key.LessonSeriesId,
                    GroupSubjectId = g.Key.GroupSubjectId,
                    Slots = g.OrderBy(s => s.Date)
                             .ThenBy(s => s.PairNumber)
                             .ToList(),
                    Priority = g.First().GroupSubject.StudentCount * g.Count()
                })
                .OrderByDescending(x => x.Priority)
                .ToList();
        }

        /// <summary>
        /// Перевіряє, чи всі слоти серії вже “заморожені”.
        /// </summary>
        private bool AreAllSlotsFrozen(SeriesGroup series)
        {
            return series.Slots.All(s => frozenSlots.Contains(s));
        }

        /// <summary>
        /// Шукає підходящу аудиторію для переданої серії.
        /// </summary>
        private ClassroomDTO? FindTargetClassroomForSeries(SeriesGroup series)
        {
            var firstSlot = series.Slots.First();

            // Кількість студентів:
            int requiredStudentCount = firstSlot.GroupSubject.StudentCount;

            // Типи аудиторій
            var requiredClassroomTypes = firstSlot.GroupSubject.Subject.ClassroomTypes
                .Select(ct => ct.ClassroomTypeId)
                .ToHashSet();

            var priorityClassroomTypeId = firstSlot.GroupSubject.Subject.ClassroomTypes
                .FirstOrDefault()?.ClassroomTypeId;

            if (priorityClassroomTypeId == null)
            {
                throw new Exception($"Не вказано тип аудиторії для предмета {firstSlot.GroupSubject.Subject.Name}.");
            }

            // Фільтр (чи підходить аудиторія)
            bool FilterPredicate(ClassroomDTO x) =>
                x.StudentCount >= requiredStudentCount &&
                x.ClassroomTypes.Any(ct => requiredClassroomTypes.Contains(ct.ClassroomTypeId)) &&
                !IsClassroomInTempAssignments(firstSlot.Date, firstSlot.PairNumber, x);

            // Сортування (яка аудиторія пріоритетніша)
            int SortPredicate(ClassroomDTO x)
            {
                // Якщо це пріоритетний тип
                if (x.ClassroomTypes.First().ClassroomTypeId == priorityClassroomTypeId)
                    return 1000000 - (x.ClassroomTypes.Count * 100);

                // Якщо хоч якась з типів входить в набір
                if (requiredClassroomTypes.Contains(x.ClassroomTypes.First().ClassroomTypeId))
                    return 1000;

                return 100;
            }

            var availableClassrooms = classrooms
                .Where(FilterPredicate)
                .OrderByDescending(SortPredicate)
                .ThenBy(x => x.StudentCount) // Щоб мінімізувати “зайві” вільні місця
                .ToList();

            // Спробувати кожну аудиторію з відфільтрованих
            foreach (var classroom in availableClassrooms)
            {
                if (CanUseClassroomForAllSlots(series.Slots, classroom))
                {
                    return classroom;
                }
            }

            return null;
        }

        /// <summary>
        /// Перевіряє, чи можна призначити аудиторію на всі слоти серії (з урахуванням “замороженості”).
        /// </summary>
        private bool CanUseClassroomForAllSlots(List<ScheduleSlotDTO> slots, ClassroomDTO classroom)
        {
            foreach (var slotTracker in slots)
            {
                var slot = slotTracker;
                var date = slot.Date;
                var pairNumber = slot.PairNumber;

                // Якщо слот уже має іншу (!) кімнату відмінну від NullClassroom
                if (slotToClassroom.TryGetValue(slot, out var existingClassroom) &&
                    existingClassroom != NullClassroom)
                {
                    return false;
                }

                // Перевірка, чи аудиторія не “заморожена” для цього часу
                bool isOccupiedByFrozenSlot =
                    tempClassroomAssignments.ContainsKey(date) &&
                    tempClassroomAssignments[date].ContainsKey(pairNumber) &&
                    tempClassroomAssignments[date][pairNumber].ContainsKey(classroom) &&
                    frozenSlots.Contains(tempClassroomAssignments[date][pairNumber][classroom]);

                if (isOccupiedByFrozenSlot)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Додає записи в тимчасовий словник і “заморожує” слоти.
        /// </summary>
        private void AssignClassroomForSeries(SeriesGroup series, ClassroomDTO targetClassroom)
        {
            foreach (var slotTracker in series.Slots)
            {
                var slot = slotTracker;
                var date = slot.Date;
                var pairNumber = slot.PairNumber;

                if (!tempClassroomAssignments.ContainsKey(date))
                {
                    tempClassroomAssignments[date] = new Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>>();
                }

                if (!tempClassroomAssignments[date].ContainsKey(pairNumber))
                {
                    tempClassroomAssignments[date][pairNumber] = new Dictionary<ClassroomDTO, ScheduleSlotDTO>();
                }

                // Записуємо призначення
                tempClassroomAssignments[date][pairNumber][targetClassroom] = slot;
                slotToClassroom[slot] = targetClassroom;

                // Додаємо слот до заморожених
                frozenSlots.Add(slot);
            }
        }

        /// <summary>
        /// Спроба призначити аудиторії для слотів із NullClassroom за допомогою алгоритму Хопкрофта-Карпа.
        /// </summary>
        private void AttemptAssignRoomsForLeftoverSlots()
        {
            // Знаходимо слоти, у яких досі NullClassroom
            var notAssignedSlots = slotToClassroom
                .Where(x => x.Value == NullClassroom)
                .Select(x => x.Key)
                .ToList();

            // Групуємо за датою і парою
            var groupedSlots = notAssignedSlots
                .GroupBy(x => new { x.Date, x.PairNumber });

            foreach (var group in groupedSlots)
            {
                var date = group.Key.Date;
                var pairNumber = group.Key.PairNumber;

                if (!tempClassroomAssignments.ContainsKey(date))
                    tempClassroomAssignments[date] = new Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>>();

                if (!tempClassroomAssignments[date].ContainsKey(pairNumber))
                    tempClassroomAssignments[date][pairNumber] = new Dictionary<ClassroomDTO, ScheduleSlotDTO>();

                // Слоти для дати/пари
                var freeSlots = group.ToList();

                // Вільні кімнати
                var freeClassrooms = classrooms
                    .Where(x => !tempClassroomAssignments[date][pairNumber].ContainsKey(x))
                    .ToList();

                // Ваш алгоритм пошуку максимального парування:
                var bipartiteMatching = HopcroftKarpAlgorithm.FindOptimalClassroomAssignment(freeSlots, freeClassrooms);


                while (bipartiteMatching.Count != freeSlots.Count)
                {
                    var badSlot = freeSlots
                        .Where(x => !bipartiteMatching.ContainsKey(x)).OrderByDescending(x => x.GroupSubject.StudentCount).First();

                    var badClassrooms = freeClassrooms
                        .Where(x => !bipartiteMatching.ContainsValue(x)).OrderByDescending(x => x.StudentCount).ToList();

                    var pairToChange = freePairToChange(badSlot, badClassrooms);
                    tempClassroomAssignments[date][pairNumber].Remove(pairToChange.room);
                    slotToClassroom[pairToChange.slot] = NullClassroom;
                    frozenSlots.Remove(pairToChange.slot);
                    freeSlots.Add(pairToChange.slot);
                    freeClassrooms.Add(pairToChange.room);

                    bipartiteMatching = HopcroftKarpAlgorithm.FindOptimalClassroomAssignment(freeSlots, freeClassrooms);
                }

                if (bipartiteMatching == null || bipartiteMatching.Count != freeSlots.Count)
                {
                    logger.LogWarning($"Не вдалося покращити розподіл аудиторій для дати {date} пари {pairNumber}.");
                    // Інший лог або return; залежно від логіки
                    return;
                }

                // Якщо вдалося знайти відповідності – оновлюємо
                foreach (var pair in bipartiteMatching)
                {
                    slotToClassroom[pair.Key] = pair.Value;
                    tempClassroomAssignments[date][pairNumber][pair.Value] = pair.Key;
                }
            }
        }

        private (ScheduleSlotDTO slot,ClassroomDTO room) freePairToChange(ScheduleSlotDTO badSlot, List<ClassroomDTO> badClassrooms)
        {
            var variants = tempClassroomAssignments[badSlot.Date][badSlot.PairNumber];
            var studentCount = badSlot.GroupSubject.StudentCount;
            var roomTypeIds = badSlot.GroupSubject.Subject.ClassroomTypes
                .Select(x => x.ClassroomTypeId)
                .ToHashSet();

            var goodRoom = variants.Where(x =>
              // аудиторія підходить поганому слоту
              x.Key.StudentCount>= studentCount && x.Key.ClassroomTypes.Any(n => roomTypeIds.Contains(n.ClassroomTypeId)

              // слот кімнати можна поміняти на одну з поганих аудиторій
                     && badClassrooms.Any(c => c.StudentCount >= x.Value.GroupSubject.StudentCount
                              && c.ClassroomTypes.Any(n => x.Value.GroupSubject.Subject.ClassroomTypes.Any(b => b.ClassroomTypeId == n.ClassroomTypeId))))
                              )
                .OrderBy(x => x.Value.GroupSubject.StudentCount) // хай страдають менше студентів
                .Select(x => x.Key)
                .FirstOrDefault();

            // Якщо знайшли кімнату, яка підходить для поганого слоту
            if (goodRoom!=null) return (variants[goodRoom] , goodRoom);

            goodRoom = variants.OrderByDescending(x => x.Key.StudentCount - x.Value.GroupSubject.StudentCount).Select(x => x.Key).FirstOrDefault();
            return (variants[goodRoom], goodRoom);

        }

        /// <summary>
        /// Фінальне застосування тимчасових призначень до слотів.
        /// </summary>
        private void ApplyAssignments()
        {
            // Якщо в тимчасових призначеннях є “нульові” аудиторії – не застосовуємо
            if (slotToClassroom.Values.Any(x => x == NullClassroom))
                return;

            foreach (var entry in slotToClassroom)
            {
                var slot = entry.Key;
                var classroom = entry.Value;
                slot.Classroom = classroom;
            }
        }

        /// <summary>
        /// Перевіряє, чи класна кімната вже присутня у тимчасових призначеннях на вказану дату/пару.
        /// </summary>
        private bool IsClassroomInTempAssignments(DateTime date, int pairNumber, ClassroomDTO classroom)
        {
            return tempClassroomAssignments.ContainsKey(date)
                && tempClassroomAssignments[date].ContainsKey(pairNumber)
                && tempClassroomAssignments[date][pairNumber].ContainsKey(classroom);
        }
    }

    /// <summary>
    /// Допоміжний клас для зберігання даних про “серію”.
    /// </summary>
    public class SeriesGroup
    {
        public int? SeriesId { get; set; }
        public long GroupSubjectId { get; set; }
        public List<ScheduleSlotDTO> Slots { get; set; } = new List<ScheduleSlotDTO>();
        public int Priority { get; set; }
    }


}
