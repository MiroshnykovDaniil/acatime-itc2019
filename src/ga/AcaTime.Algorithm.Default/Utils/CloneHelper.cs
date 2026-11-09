using AcaTime.Algorithm.Default.Models;
using AcaTime.Algorithm.Default.Services;
using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScriptModels;

namespace AcaTime.Algorithm.Default.Utils
{
    /// <summary>
    /// Допоміжний клас для ScheduleBuilderService, який зберігає інформацію про призначені слоти.
    /// </summary>
    public static class CloneHelper
    {

        /// <summary>
        /// Клонування об'єкта ScheduleBuilderService.
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
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

        private static FacultySeasonDTO Clone(this FacultySeasonDTO src)
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

        private static SlotTracker Clone(this SlotTracker src, GroupSubjectDTO groupSubject)
        {
            var clone = new SlotTracker
            {
                 ScheduleSlot = src.ScheduleSlot.Clone(groupSubject),
                AssignStep = src.AssignStep,
                AvailableDomains = new SortedSet<DomainValue>(src.AvailableDomains),
                IsAssigned = src.IsAssigned,
                IsFirstTrackerInSeries = src.IsFirstTrackerInSeries,
                IsLowDaysDanger = src.IsLowDaysDanger,
                RejectedDomains = src.RejectedDomains.ToDictionary(kvp => kvp.Key, kvp => new List<DomainValue>(kvp.Value)),
                SeriesId = src.SeriesId,
                SeriesLength = src.SeriesLength,
                WeekShift = src.WeekShift               
            };

            return clone;
        }

        private static ScheduleSlotDTO Clone(this ScheduleSlotDTO src, GroupSubjectDTO groupSubject)
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


    }
}