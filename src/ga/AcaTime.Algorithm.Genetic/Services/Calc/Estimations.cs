using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScriptModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AcaTime.Algorithm.Genetic.Services.Calc
{
    public static class Estimations
    {

        /// <summary>
        /// Функція оцінки вибору значення для слоту
        /// </summary>
        /// <param name="slot"></param>
        /// <param name="assignedSLots"></param>
        /// <returns></returns>
        public static int DefaultSlotValueEstimation(IScheduleSlot slot, IAssignedSlots assignedSLots)
        {
            int score = 0;
            // Оцінка компактності для викладача.
            var candidateTeacherId = slot.GroupSubject.Teacher.Id;

            var teacherAdjacentSlots = assignedSLots.GetSlotsByTeacher(candidateTeacherId)
               .Where(s => s.Date.Date == slot.Date.Date)
                .Select(s => s.PairNumber)
                .ToList();

            foreach (var pair in teacherAdjacentSlots)
            {
                if (Math.Abs(pair - slot.PairNumber) == 1)
                {
                    score += 1;
                }
            }
            // Оцінка компактності для студентських груп.
            foreach (var candidateGroup in slot.GroupSubject.Groups)
            {

                //   Dictionary<long,List<long>> variants

                var groupAdjacentSlots = assignedSLots.GetSlotsByGroup(candidateGroup.Id)
                    .Where(s => s.Date.Date == slot.Date.Date)
                    .ToList();

                foreach (var pair in groupAdjacentSlots)
                {
                    if (Math.Abs(pair.PairNumber - slot.PairNumber) == 1)
                    {
                        score += 3;
                    }

                    if (pair.PairNumber == slot.PairNumber) // Склєйка підгруп
                    {
                        score += 5;

                        if (pair.LessonSeriesLength == slot.LessonSeriesLength) // Довжина серії уроків співпадає
                            score += 12;

                        if (pair.GroupSubject.Subject.Id == slot.GroupSubject.Subject.Id) // Один і той сами предмет для різних підгруп
                            score += 10;
                    }
                }

                var variants = groupAdjacentSlots.Union([slot]).SelectMany(x => x.GroupSubject.Groups.Where(g => g.Id == candidateGroup.Id)).Select(x => new { variant = x.SubgroupVariantId ?? 0, subgroup = x.SubgroupId ?? 0 });
                var maxInVariants = variants.GroupBy(v => v.variant)
                                             .Select(g => new
                                             {
                                                 Variant = g.Key,
                                                 MaxCount = g.GroupBy(x => x.subgroup)
                                                             .Max(subGroup => subGroup.Count())
                                             })
                                             .ToList();

                var maxStudentLessons = maxInVariants.Sum(x => x.MaxCount);

                if (maxStudentLessons > 3)
                    score -= 1000;

            }
            return score;
        }


        /// <summary>
        /// Функція оцінки вибору наступного слоту
        /// </summary>
        /// <param name="slot"></param>
        /// <returns></returns>
        public static int DefaultSlotEstimation(ISlotEstimation slot)
        {
            int res = slot.AvailableDomains * -10 + slot.LessonSeriesLength * 2 + slot.GroupCount;

            if (slot.EndsOnIncompleteWeek)
                res += 100;

            return res;
        }
    }
}
