using AcaTime.Algorithm.Genetic.Models;
using AcaTime.Algorithm.Genetic.Models.Genetic;
using AcaTime.Algorithm.Genetic.Services;
using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScriptModels;

namespace AcaTime.Algorithm.Genetic.Utils
{
    /// <summary>
    /// Допоміжний клас для ScheduleBuilderService, який зберігає інформацію про призначені слоти.
    /// </summary>
    public static class CloneHelper
    {

        public static GeneticScheduleAlgorithmUnit CloneFromDefault(this DefaultScheduleAlgorithmUnit source)
        {
            var resRoot = source.Root.Clone();

            Dictionary<GroupSubjectDTO, GroupSubjectDTO> groupMap = source.Root.GroupSubjects.ToDictionary(x => x, x => x.Clone(resRoot));
            Dictionary<SlotTracker, SlotTracker> trackerMap = source.Slots.Values.ToDictionary(x => x, x => x.Clone(groupMap[x.ScheduleSlot.GroupSubject]));

            var res = new GeneticScheduleAlgorithmUnit();

            res.Setup(resRoot, source.logger, source.UserFunctions, source.Parameters);

            res.teacherSlots = source.teacherSlots.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Select(x => trackerMap[x]).ToList());
            res.groupsSlots = source.groupsSlots.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Select(x => trackerMap[x]).ToList());
            res.FirstTrackers = source.FirstTrackers.Select(x => trackerMap[x]).ToList();
            res.Slots = source.Slots.ToDictionary(kvp => trackerMap[kvp.Value].ScheduleSlot as IScheduleSlot, kvp => trackerMap[kvp.Value]);
            
            // These indexes are mutable during assignment and must not be shared
            // between parallel branches. ApplyGenome rebuilds them from scratch.
            res.assignedSlotsByTeacherDate = new Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>>();
            res.assignedSlotsByGroupDate = new Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>>();
            
            return res;
            
        }
        
        public static DefaultScheduleAlgorithmUnit Clone(this DefaultScheduleAlgorithmUnit source)
        {

            var resRoot = source.Root.Clone();

            // Dictionary<ScheduleSlotDTO, ScheduleSlotDTO> slotMap = new Dictionary<ScheduleSlotDTO, ScheduleSlotDTO>();

            Dictionary<GroupSubjectDTO, GroupSubjectDTO> groupMap = source.Root.GroupSubjects.ToDictionary(x => x, x => x.Clone(resRoot));
            Dictionary<SlotTracker, SlotTracker> trackerMap = source.Slots.Values.ToDictionary(x => x, x => x.Clone(groupMap[x.ScheduleSlot.GroupSubject]));


            var res = new DefaultScheduleAlgorithmUnit();

            res.Setup(resRoot, source.logger, source.UserFunctions, source.Parameters);

            res.teacherSlots = source.teacherSlots.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Select(x => trackerMap[x]).ToList());
            res.groupsSlots = source.groupsSlots.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Select(x => trackerMap[x]).ToList());
            res.FirstTrackers = source.FirstTrackers.Select(x => trackerMap[x]).ToList();
            res.Slots = source.Slots.ToDictionary(kvp => trackerMap[kvp.Value].ScheduleSlot as IScheduleSlot, kvp => trackerMap[kvp.Value]);
            
            return res;

        }

        public static GeneticScheduleAlgorithmUnit CloneWithPrivateCache(this GeneticScheduleAlgorithmUnit source)
        {
            var resRoot = source.Root.Clone();

            // Dictionary<ScheduleSlotDTO, ScheduleSlotDTO> slotMap = new Dictionary<ScheduleSlotDTO, ScheduleSlotDTO>();
            
            Dictionary<GroupSubjectDTO, GroupSubjectDTO> groupMap = source.Root.GroupSubjects.ToDictionary(x => x, x => x.Clone(resRoot));
            Dictionary<SlotTracker, SlotTracker> trackerMap = source.Slots.Values.ToDictionary(x => x, x => x.Clone(groupMap[x.ScheduleSlot.GroupSubject]));
            
            var res = new GeneticScheduleAlgorithmUnit();

            res.Setup(resRoot, source.logger, source.UserFunctions, source.Parameters);

            res.assignedSlotsByTeacherDate= source.assignedSlotsByTeacherDate;
            // res.assignedSlotsByTeacherDate = source.assignedSlotsByTeacherDate.ToDictionary(
            //     kvp => kvp.Key, 
            //     kvp => kvp.Value.ToDictionary(
            //         kvp2 => kvp2.Key, 
            //         kvp2 => kvp2.Value.Select(x => trackerMap[x]).ToHashSet()
            //     )
            // );
            res.assignedSlotsByGroupDate = source.assignedSlotsByGroupDate;
            // res.assignedSlotsByGroupDate = source.assignedSlotsByGroupDate.ToDictionary(
            //     kvp => kvp.Key, 
            //     kvp => kvp.Value.ToDictionary(
            //         kvp2 => kvp2.Key, 
            //         kvp2 => kvp2.Value.Select(x => trackerMap[x]).ToHashSet()
            //     )
            // );
            
            res.teacherSlots = source.teacherSlots.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Select(x => trackerMap[x]).ToList());
            res.groupsSlots = source.groupsSlots.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Select(x => trackerMap[x]).ToList());
            res.FirstTrackers = source.FirstTrackers.Select(x => trackerMap[x]).ToList();
            res.Slots = source.Slots.ToDictionary(kvp => trackerMap[kvp.Value].ScheduleSlot as IScheduleSlot, kvp => trackerMap[kvp.Value]);
                
            // todo make cloning work from trackerMap
            // res.assignedSlotsByTeacherDate = source.assignedSlotsByTeacherDate.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToDictionary(kvp2 => kvp2.Key, kvp2 => kvp2.Value.Select(x => trackerMap[x]).ToHashSet()));
            // res.assignedSlotsByGroupDate = source.assignedSlotsByGroupDate.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToDictionary(kvp2 => kvp2.Key, kvp2 => kvp2.Value.Select(x => trackerMap[x]).ToHashSet()));
            

            // res.assignedSlotsByTeacherDate = source.assignedSlotsByTeacherDate.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            // res.assignedSlotsByGroupDate = source.assignedSlotsByGroupDate.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
            return res;

        }


        /// <summary>
        // /// Клонування об'єкта ScheduleBuilderService.
        // /// </summary>
        // /// <param name="source"></param>
        // /// <returns></returns>
        // public static GeneticScheduleAlgorithmUnit Clone(this GeneticScheduleAlgorithmUnit source)
        // {
        //
        //     var resRoot = source.Root.Clone();
        //
        //     // Dictionary<ScheduleSlotDTO, ScheduleSlotDTO> slotMap = new Dictionary<ScheduleSlotDTO, ScheduleSlotDTO>();
        //
        //     Dictionary<GroupSubjectDTO, GroupSubjectDTO> groupMap = source.Root.GroupSubjects.ToDictionary(x => x, x => x.Clone(resRoot));
        //     Dictionary<SlotTracker, SlotTracker> trackerMap = source.Slots.Values.ToDictionary(x => x, x => x.Clone(groupMap[x.ScheduleSlot.GroupSubject]));
        //
        //
        //     var res = new GeneticScheduleAlgorithmUnit();
        //
        //     res.Setup(resRoot, source.logger, source.UserFunctions, source.Parameters);
        //
        //     res.teacherSlots = source.teacherSlots.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Select(x => trackerMap[x]).ToList());
        //     res.groupsSlots = source.groupsSlots.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Select(x => trackerMap[x]).ToList());
        //     res.FirstTrackers = source.FirstTrackers.Select(x => trackerMap[x]).ToList();
        //     res.Slots = source.Slots.ToDictionary(kvp => trackerMap[kvp.Value].ScheduleSlot as IScheduleSlot, kvp => trackerMap[kvp.Value]);
        //  
        //     return res;
        //
        // }

        public static FacultySeasonDTO Clone(this FacultySeasonDTO src)
        {
            var clone = new FacultySeasonDTO
            {
                Id = src.Id,
                Name = src.Name,
                BeginSeason = src.BeginSeason,
                EndSeason = src.EndSeason,
                MaxLessonsPerDay = src.MaxLessonsPerDay,
                GroupSubjects = new List<GroupSubjectDTO>(),
                Classrooms = src.Classrooms
            };
            return clone;
        }

        private static GroupSubjectDTO Clone(this GroupSubjectDTO x, FacultySeasonDTO root)
        {
            var res = new GroupSubjectDTO
            {
                Id = x.Id,
                Teacher = x.Teacher,
                Subject = x.Subject,
                Faculty = x.Faculty,
                Groups = x.Groups,
                StudentCount = x.StudentCount,
                ScheduleSlots = new List<ScheduleSlotDTO>()
            };

            root.GroupSubjects.Add(res);
            return res;
        }

        public static SlotTracker Clone(this SlotTracker src, GroupSubjectDTO groupSubject)
        {
            var clone = new SlotTracker
            {
                 ScheduleSlot = src.ScheduleSlot.Clone(groupSubject),
                AssignStep = src.AssignStep,
                // COW: колекції доменів поділяються з джерелом — мутатори
                // (ForwardCheck/RestoreRejectedDomains) копіюють за потреби.
                // Це знімає ~90% часу клонування (45с+ на раунд).
                AvailableDomains = src.AvailableDomains,
                DomainsOwned = false,
                IsAssigned = src.IsAssigned,
                IsFirstTrackerInSeries = src.IsFirstTrackerInSeries,
                IsLowDaysDanger = src.IsLowDaysDanger,
                RejectedDomains = src.RejectedDomains,
                RejectedOwned = false,
                SeriesId = src.SeriesId,
                SeriesLength = src.SeriesLength,
                WeekShift = src.WeekShift,
                // КРИТИЧНО для дешевої оцінки: клон може створюватись із
                // непогашеними dirty-змінами (після UndoSeriesPlacement
                // турніру). Без копіювання прапорця/prev клон успадковує
                // клітинки попереднього стану БЕЗ маркерів — тихе розходження
                // і втрата декомпозиції правила до кінця раунду.
                CheapDirty = src.CheapDirty,
                CheapPrevDate = src.CheapPrevDate,
                CheapPrevPair = src.CheapPrevPair,
                FlatCode = src.FlatCode,
                FlatSetSeq = src.FlatSetSeq
            };

            return clone;
        }

        public static SlotTracker CloneReplace(this SlotTracker src, GroupSubjectDTO groupSubject)
        {
            var clone = new SlotTracker
            {
                ScheduleSlot = src.ScheduleSlot.CloneReplace(groupSubject),
                AssignStep = src.AssignStep,
                AvailableDomains = new SortedSet<DomainValue>(src.AvailableDomains),
                IsAssigned = src.IsAssigned,
                IsFirstTrackerInSeries = src.IsFirstTrackerInSeries,
                IsLowDaysDanger = src.IsLowDaysDanger,
                RejectedDomains = src.RejectedDomains.ToDictionary(kvp => kvp.Key, kvp => new List<DomainValue>(kvp.Value)),
                SeriesId = src.SeriesId,
                SeriesLength = src.SeriesLength,
                WeekShift = src.WeekShift,
                FlatCode = src.FlatCode
            };

            return clone;
        }

        public static ScheduleSlotDTO Clone(this ScheduleSlotDTO src, GroupSubjectDTO groupSubject)
        {
            var clone = new ScheduleSlotDTO
            {
                Id = src.Id,
                LessonNumber = src.LessonNumber,
                Date = src.Date,
                PairNumber = src.PairNumber,
                LessonSeriesLength = src.LessonSeriesLength,
                LessonSeriesId = src.LessonSeriesId,
                GroupSubject = groupSubject
            };

            groupSubject.ScheduleSlots.Add(clone);

            return clone;
        }
        
        public static ScheduleSlotDTO CloneReplace(this ScheduleSlotDTO src, GroupSubjectDTO groupSubject)
        {
            var clone = new ScheduleSlotDTO
            {
                Id = src.Id,
                LessonNumber = src.LessonNumber,
                Date = src.Date,
                PairNumber = src.PairNumber,
                LessonSeriesLength = src.LessonSeriesLength,
                LessonSeriesId = src.LessonSeriesId,
                GroupSubject = groupSubject
            };

            var index = groupSubject.ScheduleSlots.IndexOf(src);
            groupSubject.ScheduleSlots[index] = clone;
            return clone;
        }

        public static void ReplaceGroupSubjectSlot(this ScheduleSlotDTO src, ScheduleSlotDTO replaceWith,  GroupSubjectDTO groupSubject)
        {
            var index = groupSubject.ScheduleSlots.IndexOf(src);
            groupSubject.ScheduleSlots[index] = replaceWith;
        }

        public static Individual CloneFromUnit(this GeneticScheduleAlgorithmUnit source)
        {
            var resRoot = source.Root.Clone();

            Dictionary<GroupSubjectDTO, GroupSubjectDTO> groupMap = source.Root.GroupSubjects.ToDictionary(x => x, x => x.Clone(resRoot));
            Dictionary<SlotTracker, SlotTracker> trackerMap = source.Slots.Values.ToDictionary(x => x, x => x.Clone(groupMap[x.ScheduleSlot.GroupSubject]));

            var res = new Individual();

            res.Setup(resRoot, source.logger, source.UserFunctions, source.Parameters);

            res.teacherSlots = source.teacherSlots.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Select(x => trackerMap[x]).ToList());
            res.groupsSlots = source.groupsSlots.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Select(x => trackerMap[x]).ToList());
            res.FirstTrackers = source.FirstTrackers.Select(x => trackerMap[x]).ToList();
            res.Slots = source.Slots.ToDictionary(kvp => trackerMap[kvp.Value].ScheduleSlot as IScheduleSlot, kvp => trackerMap[kvp.Value]);
            
            res.assignedSlotsByTeacherDate = CloneAssignedIndex(
                source.assignedSlotsByTeacherDate, trackerMap);
            res.assignedSlotsByGroupDate = CloneAssignedIndex(
                source.assignedSlotsByGroupDate, trackerMap);
            
            return res;
        }

        public static Individual clone(this Individual source)
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                return CloneInner(source);
            }
            finally
            {
                Models.Genetic.Individual.TicksClone += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            }
        }

        private static Individual CloneInner(this Individual source)
        {
            var tRoot = System.Diagnostics.Stopwatch.GetTimestamp();
            var resRoot = source.Root.Clone();
            var tGroups = System.Diagnostics.Stopwatch.GetTimestamp();
            Models.Genetic.Individual.TicksCloneRoot += tGroups - tRoot;

            Dictionary<GroupSubjectDTO, GroupSubjectDTO> groupMap = source.Root.GroupSubjects.ToDictionary(x => x, x => x.Clone(resRoot));
            var tTrackers = System.Diagnostics.Stopwatch.GetTimestamp();
            Models.Genetic.Individual.TicksCloneGroups += tTrackers - tGroups;

            Dictionary<SlotTracker, SlotTracker> trackerMap = source.Slots.Values.ToDictionary(x => x, x => x.Clone(groupMap[x.ScheduleSlot.GroupSubject]));
            var tRest = System.Diagnostics.Stopwatch.GetTimestamp();
            Models.Genetic.Individual.TicksCloneTrackers += tRest - tTrackers;

            var tMaps0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var res = new Individual();

            res.Setup(resRoot, source.logger, source.UserFunctions, source.Parameters);

            res.teacherSlots = new Dictionary<long, List<SlotTracker>>(source.teacherSlots.Count);
            foreach (var kvp in source.teacherSlots)
            {
                var list = new List<SlotTracker>(kvp.Value.Count);
                foreach (var t in kvp.Value) list.Add(trackerMap[t]);
                res.teacherSlots[kvp.Key] = list;
            }
            res.groupsSlots = new Dictionary<long, List<SlotTracker>>(source.groupsSlots.Count);
            foreach (var kvp in source.groupsSlots)
            {
                var list = new List<SlotTracker>(kvp.Value.Count);
                foreach (var t in kvp.Value) list.Add(trackerMap[t]);
                res.groupsSlots[kvp.Key] = list;
            }
            res.FirstTrackers = source.FirstTrackers.Select(x => trackerMap[x]).ToList();
            res.Slots = new Dictionary<IScheduleSlot, SlotTracker>(source.Slots.Count);
            foreach (var kvp in source.Slots)
                res.Slots[trackerMap[kvp.Value].ScheduleSlot] = trackerMap[kvp.Value];
            var tDeltas0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Models.Genetic.Individual.TicksCloneMaps += tDeltas0 - tMaps0;

            res.DeltaEvents.AddRange(source.DeltaEvents);
            var tIdx0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Models.Genetic.Individual.TicksCloneDeltas += tIdx0 - tDeltas0;

            // FLAT-2a (FALSIFIED): лінивий ребілд індексів коштує стільки ж,
            // скільки глибоке копіювання (18.3с vs 19.2с thread-time) — ребілд
            // обмежений алокациями вкладених Dictionary/HashSet (~1.8мс на
            // ребілд × ~11к клонів). Повертаємо eager-copy; справжнє знищення
            // цього бакета — flat owner-масиви з int-Id трекерів (memcpy, 2b).
            res.assignedSlotsByTeacherDate = CloneAssignedIndex(
                source.assignedSlotsByTeacherDate, trackerMap);
            res.assignedSlotsByGroupDate = CloneAssignedIndex(
                source.assignedSlotsByGroupDate, trackerMap);
            Models.Genetic.Individual.TicksCloneIndexes += System.Diagnostics.Stopwatch.GetTimestamp() - tIdx0;

            res._cheapEngine = source._cheapEngine?.CloneFor(res, trackerMap);

            // FLAT-дзеркало: стан клонa == стан джерела → чистий memcpy рядків
            // бітсетів (масиви ~13KB, мікросекунди). FlatCode трекерів уже
            // скопійований у клонах. Верифікація паріті в Estimate ловить
            // будь-яке розходження.
            if (source.flatBits != null && source.flatBits.Enabled)
            {
                res.flatTables = source.flatTables;
                res.flatBits = source.flatBits.CloneArrays();
            }

            return res;

        }

        // NOTE (COW неможливий): сетки індексу містять ТРЕКЕРИ конкретного
        // individual (пер-індивідуальна ідентичність), тому ділити структуру
        // між клонами не можна — на відміну від AvailableDomains (імутабельні
        // value-структури). Єдиний шлях — дешевий ребілд: ручні цикли з
        // пресайзингом замість LINQ-ланцюжків (12.1s на 60s-ран → вимір).
        private static Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>> CloneAssignedIndex(
            Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>> source,
            Dictionary<SlotTracker, SlotTracker> trackerMap)
        {
            var res = new Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>>(source.Count);
            foreach (var entityPair in source)
            {
                var datesCopy = new Dictionary<DateTime, HashSet<SlotTracker>>(entityPair.Value.Count);
                foreach (var datePair in entityPair.Value)
                {
                    var setCopy = new HashSet<SlotTracker>(datePair.Value.Count);
                    foreach (var tracker in datePair.Value)
                        if (trackerMap.TryGetValue(tracker, out var mapped))
                            setCopy.Add(mapped);
                    datesCopy[datePair.Key] = setCopy;
                }
                res[entityPair.Key] = datesCopy;
            }
            return res;
        }


    }
}
