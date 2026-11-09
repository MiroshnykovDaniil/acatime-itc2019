using AcaTime.Algorithm.Default.Models;
using AcaTime.ScriptModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AcaTime.Algorithm.Default.Services.Calc
{
    public static class Validators
    {

        /// <summary>
        /// Стандартна перевірка для слотів.
        /// </summary>
        /// <param name="slot"></param>
        /// <param name="assignedSLots"></param>
        /// <returns></returns>
        public static bool StandartValidation(IScheduleSlot slot, IAssignedSlots assignedSLots)
        {
            var candidateTeacherId = slot.GroupSubject.Teacher.Id;

            var hasTeacherIntersection = assignedSLots.GetSlotsByTeacherAndDate(candidateTeacherId, slot.Date.Date)
                .Any(s => s.PairNumber == slot.PairNumber);
            if (hasTeacherIntersection)
                return false;

            HashSet<IScheduleSlot> slotsByGroup = new HashSet<IScheduleSlot>();

            foreach (var groupId in slot.GroupSubject.Groups.Select(g => g.Id).Distinct())
            {
                var slots = assignedSLots.GetSlotsByGroupAndDate(groupId, slot.Date.Date).Where(s => s.PairNumber == slot.PairNumber);
                if (slots != null)
                    slotsByGroup.UnionWith(slots);
            }

            foreach (var otherSlot in slotsByGroup)
            {
                // Перевірка для студентських груп.
                foreach (var candidateGroup in slot.GroupSubject.Groups)
                {
                    foreach (var otherGroup in otherSlot.GroupSubject.Groups)
                    {
                        if (candidateGroup.Id == otherGroup.Id)
                        {
                            // Дозволено, якщо SubgroupVariantId співпадає і SubgroupId різний.
                            if (!candidateGroup.SubgroupVariantId.HasValue ||
                                !otherGroup.SubgroupVariantId.HasValue ||
                                candidateGroup.SubgroupVariantId != otherGroup.SubgroupVariantId ||
                                candidateGroup.SubgroupId == otherGroup.SubgroupId)
                            {
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }


        /// <summary>
        /// Перевірка доменного значення для слоту в контексті вже призначених слотів.
        /// </summary>
        /// <param name="slotTracker"></param>
        /// <param name="domain"></param>
        /// <param name="assigned"></param>
        /// <returns></returns>
        public static bool ValidateAssignmentArc(SlotTracker slotTracker, DomainValue domain, List<SlotTracker> assigned)
        {
            var candidateTeacherId = slotTracker.ScheduleSlot.GroupSubject.Teacher.Id;
            foreach (var otherSlot in assigned)
            {
                // Перевірка, чи відбувається призначення в один і той самий час.
                if (otherSlot.ScheduleSlot.Date.Date == domain.Date.Date && otherSlot.ScheduleSlot.PairNumber == domain.PairNumber)
                {
                    // Перевірка викладача.
                    var otherTeacherId = otherSlot.ScheduleSlot.GroupSubject.Teacher.Id;
                    if (otherTeacherId == candidateTeacherId)
                    {
                        return false;
                    }
                    // Перевірка для студентських груп.
                    foreach (var candidateGroup in slotTracker.ScheduleSlot.GroupSubject.Groups)
                    {
                        foreach (var otherGroup in otherSlot.ScheduleSlot.GroupSubject.Groups)
                        {
                            if (candidateGroup.Id == otherGroup.Id)
                            {


                                // Дозволено, якщо SubgroupVariantId співпадає і SubgroupId різний.
                                if (!candidateGroup.SubgroupVariantId.HasValue ||
                                    !otherGroup.SubgroupVariantId.HasValue ||
                                    candidateGroup.SubgroupVariantId != otherGroup.SubgroupVariantId ||
                                    candidateGroup.SubgroupId == otherGroup.SubgroupId)
                                {
                                    return false;
                                }
                            }
                        }

                    }
                }
            }

            return true;
        }
    }
}
