// using AcaTime.ScheduleCommon.Models.Calc;
//
// namespace AcaTime.Algorithm.Genetic.Models.Genetic;
//
// public class ScheduleChromosome
// {
//     
//     // краще спробувати використати словник на слот і його трекер
//     public Dictionary<ScheduleSlotDTO, SlotTracker> SlotTrackers { get; set; } = new Dictionary<ScheduleSlotDTO, SlotTracker>();
//     
//     public List<ScheduleSlotDTO> Slots { get; set; } = new List<ScheduleSlotDTO>();
//     
//     public int Fitness { get; set; }
//
//     public ScheduleChromosome DeepClone()
//     {
//         return new ScheduleChromosome
//         {
//             Slots = Slots.Select(s => new ScheduleSlotDTO
//             {
//                 Date = s.Date,
//                 PairNumber = s.PairNumber,
//                 GroupSubject = s.GroupSubject, // You may need to clone deeply if mutable
//                 Classroom = s.Classroom,
//                 LessonNumber = s.LessonNumber,
//                 LessonSeriesLength = s.LessonSeriesLength,
//                 LessonSeriesId = s.LessonSeriesId,
//             }).ToList()
//         };
//     }
// }