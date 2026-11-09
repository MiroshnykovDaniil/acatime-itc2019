using AcaTime.ScheduleCommon.Models.Calc;

namespace AcaTime.Algorithm.Default.Services.Calc
{
    /// <summary>
    /// Логіка пошуку оптимальних аудиторій.
    /// </summary>
    public static class HopcroftKarpAlgorithm
    {
        /// <summary>
        /// Знаходить оптимальне призначення аудиторій для набору слотів,
        /// використовуючи алгоритм Хопкрофта-Карпа.
        /// </summary>
        /// <param name="slots">Список слотів (занять), для яких шукаємо аудиторії.</param>
        /// <returns>Словник (слот -> аудиторія) або null, якщо не вдалося призначити всім слотам.</returns>
        public static Dictionary<ScheduleSlotDTO, ClassroomDTO> FindOptimalClassroomAssignment(List<ScheduleSlotDTO> slots, List<ClassroomDTO> classrooms)
        {
            // 1. Побудова двочасткового графа (ліва частина — слоти, права — аудиторії).
            //    Записуємо у slotToAvailableClassrooms, які аудиторії підходять для кожного слоту,
            //    а у classroomToAvailableSlots навпаки (які слоти може брати кожна аудиторія).

            var slotToAvailableClassrooms = new Dictionary<ScheduleSlotDTO, List<ClassroomDTO>>();
            var classroomToAvailableSlots = new Dictionary<ClassroomDTO, List<ScheduleSlotDTO>>();

            foreach (var slot in slots)
            {
                // Потрібна кількість місць
                int requiredStudentCount = slot.GroupSubject.StudentCount;

                // Необхідні типи аудиторій
                var requiredClassroomTypes = slot.GroupSubject.Subject.ClassroomTypes
                                                 .Select(ct => ct.ClassroomTypeId)
                                                 .ToHashSet();

                // Вибираємо всі аудиторії, що мають достатню місткість і містять потрібні типи
                var suitableClassrooms =classrooms
                    .Where(c => c.StudentCount >= requiredStudentCount &&
                                c.ClassroomTypes.Any(ct => requiredClassroomTypes.Contains(ct.ClassroomTypeId)))
                    .ToList();

                // Якщо жодна аудиторія не підходить хоча б для одного слоту,
                // далі можна не шукати — повертаємо null
                if (!suitableClassrooms.Any())
                    return new Dictionary<ScheduleSlotDTO, ClassroomDTO>();

                slotToAvailableClassrooms[slot] = suitableClassrooms;

                foreach (var classroom in suitableClassrooms)
                {
                    if (!classroomToAvailableSlots.ContainsKey(classroom))
                        classroomToAvailableSlots[classroom] = new List<ScheduleSlotDTO>();

                    classroomToAvailableSlots[classroom].Add(slot);
                }
            }

            // Виконуємо алгоритм Хопкрофта–Карпа для пошуку максимального паросполучення
            var matching = HopcroftKarp(slotToAvailableClassrooms, classroomToAvailableSlots);

            // Перевіряємо, чи всі слоти отримали аудиторії
            //int neededSlotsCount = slots.Count();
            //if (matching.Count < neededSlotsCount)
            //    return null;

            return matching;
        }

        /// <summary>
        /// Реалізація алгоритму Хопкрофта–Карпа для двочасткового графа:
        /// слоти (ліва частина) - аудиторії (права частина).
        /// Повертає словник (Slot -> Classroom), що утворює максимальне паросполучення.
        /// </summary>
        /// <param name="slotToClassrooms">Відображення слота до переліку аудиторій, які йому підходять</param>
        /// <param name="classroomToSlots">Відображення аудиторії до переліку слотів, які їй підходять</param>
        /// <returns>Паросполучення (Slot -> Classroom)</returns>
        public static Dictionary<ScheduleSlotDTO, ClassroomDTO> HopcroftKarp(
            Dictionary<ScheduleSlotDTO, List<ClassroomDTO>> slotToClassrooms,
            Dictionary<ClassroomDTO, List<ScheduleSlotDTO>> classroomToSlots)
        {
            // Для зручності:
            // matchedSlotToClassroom[u] = v означає, що слот u призначений аудиторії v.
            // matchedClassroomToSlot[v] = u означає, що аудиторія v призначена слоту u.

            var matchedSlotToClassroom = new Dictionary<ScheduleSlotDTO, ClassroomDTO>();
            var matchedClassroomToSlot = new Dictionary<ClassroomDTO, ScheduleSlotDTO>();

            // Зберігаємо "рівні" (layer) для кожного слоту у BFS
            var distances = new Dictionary<ScheduleSlotDTO, int>();

            // Додаткове поле, яке грає роль "дистанції до NIL" (якщо її досягнуто)
            int distanceNIL = 0;

            // Поки BFS знаходить збільшуючий шлях (distanceNIL != ∞), виконуємо DFS
            while (BfsHopcroftKarp(
                       slotToClassrooms,
                       matchedSlotToClassroom,
                       matchedClassroomToSlot,
                       distances,
                       ref distanceNIL))
            {
                // Для кожного слоту, який ще не має призначеної аудиторії, намагаємося знайти новий шлях у DFS
                foreach (var slot in slotToClassrooms.Keys)
                {
                    if (!matchedSlotToClassroom.ContainsKey(slot))
                    {
                        DfsHopcroftKarp(
                            slot,
                            slotToClassrooms,
                            matchedSlotToClassroom,
                            matchedClassroomToSlot,
                            distances,
                            distanceNIL);
                    }
                }
            }

            return matchedSlotToClassroom;
        }

        /// <summary>
        /// Побудова "рівневого" графа за допомогою BFS:
        /// обчислюємо мінімальні відстані (dist) від неприсвоєних слотів до "віртуального" NIL.
        /// Якщо вдалося "досягти" NIL, повертаємо true, інакше false.
        /// </summary>
        private static bool BfsHopcroftKarp(
            Dictionary<ScheduleSlotDTO, List<ClassroomDTO>> slotToClassrooms,
            Dictionary<ScheduleSlotDTO, ClassroomDTO> matchedSlotToClassroom,
            Dictionary<ClassroomDTO, ScheduleSlotDTO> matchedClassroomToSlot,
            Dictionary<ScheduleSlotDTO, int> distances,
            ref int distanceNIL)
        {
            // Спочатку ставимо відстань до NIL у ∞
            distanceNIL = int.MaxValue;

            var queue = new Queue<ScheduleSlotDTO>();

            // Ініціалізуємо відстані для всіх слотів
            foreach (var slot in slotToClassrooms.Keys)
            {
                // Якщо слот ще нікому не призначений
                if (!matchedSlotToClassroom.ContainsKey(slot))
                {
                    // Дистанція від "неприсвоєного" слоту до NIL = 0,
                    // і ми відправляємо його в чергу BFS
                    distances[slot] = 0;
                    queue.Enqueue(slot);
                }
                else
                {
                    // Якщо слот уже призначений, поки що робимо йому "∞"
                    distances[slot] = int.MaxValue;
                }
            }

            // BFS
            while (queue.Count > 0)
            {
                var slot = queue.Dequeue();

                // Якщо відстань цього слота ще не "∞",
                // продовжуємо обхід сусідів
                if (distances[slot] < distanceNIL)
                {
                    // Перебираємо всі аудиторії, які можуть відповідати цьому слоту
                    if (!slotToClassrooms.TryGetValue(slot, out var possibleClassrooms))
                        continue;

                    foreach (var classroom in possibleClassrooms)
                    {
                        // Якщо ця аудиторія поки вільна (не matched),
                        // то ми "досягаємо" NIL на відстані distances[slot] + 1
                        if (!matchedClassroomToSlot.ContainsKey(classroom))
                        {
                            distanceNIL = distances[slot] + 1;
                        }
                        else
                        {
                            // Інакше дивимося, слот matchedClassroomToSlot[classroom]
                            // і якщо у нього ще "∞", то оновлюємо відстань і додаємо в чергу
                            var nextSlot = matchedClassroomToSlot[classroom];
                            if (distances[nextSlot] == int.MaxValue)
                            {
                                distances[nextSlot] = distances[slot] + 1;
                                queue.Enqueue(nextSlot);
                            }
                        }
                    }
                }
            }

            // Якщо distanceNIL усе ще int.MaxValue, отже ми не знайшли жодного шляху до "вільної" аудиторії
            return distanceNIL != int.MaxValue;
        }

        /// <summary>
        /// DFS шукає збільшуючий шлях від слоту до вільної аудиторії
        /// (за умови, що після BFS знаємо коректні відстані).
        /// </summary>
        /// <param name="slot">Слот, від якого шукаємо augmenting path.</param>
        /// <param name="distanceNIL">Рівень NIL, знайдений у BFS (якщо знайдений).</param>
        /// <returns>True, якщо вдалося знайти/покращити паросполучення, інакше False.</returns>
        private static bool DfsHopcroftKarp(
            ScheduleSlotDTO slot,
            Dictionary<ScheduleSlotDTO, List<ClassroomDTO>> slotToClassrooms,
            Dictionary<ScheduleSlotDTO, ClassroomDTO> matchedSlotToClassroom,
            Dictionary<ClassroomDTO, ScheduleSlotDTO> matchedClassroomToSlot,
            Dictionary<ScheduleSlotDTO, int> distances,
            int distanceNIL)
        {
            // Перебираємо всі можливі аудиторії для даного слоту
            if (slotToClassrooms.TryGetValue(slot, out var possibleClassrooms))
            {
                foreach (var classroom in possibleClassrooms)
                {
                    // Якщо аудиторія вільна (не присвоєна нікому),
                    // то можна одразу призначати поточному слоту
                    if (!matchedClassroomToSlot.ContainsKey(classroom))
                    {
                        matchedSlotToClassroom[slot] = classroom;
                        matchedClassroomToSlot[classroom] = slot;
                        return true;
                    }
                    else
                    {
                        // Спробуємо "витіснити" слот, який зараз зайняв цю аудиторію.
                        var nextSlot = matchedClassroomToSlot[classroom];

                        // Якщо nextSlot ще має актуальний шлях (dist[nextSlot] == dist[slot] + 1),
                        // і ми можемо піти далі, тоді перевизначаємо парування.
                        if (distances[nextSlot] == distances[slot] + 1 &&
                            DfsHopcroftKarp(nextSlot, slotToClassrooms, matchedSlotToClassroom,
                                            matchedClassroomToSlot, distances, distanceNIL))
                        {
                            matchedSlotToClassroom[slot] = classroom;
                            matchedClassroomToSlot[classroom] = slot;
                            return true;
                        }
                    }
                }
            }

            // Якщо шлях не знайдено, щоб не шукати щоразу марно,
            // встановлюємо для цього слоту відстань у "∞"
            distances[slot] = int.MaxValue;
            return false;
        }
    }


}
