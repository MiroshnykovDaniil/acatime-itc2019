using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScriptModels;

namespace AcaTime.Algorithm.Genetic.Models.Genetic;


// тут будуть зберігатись саме ті зміни що присутні в дані особі
// таким чином ми зможемо зберігати в памʼяті один розклад,
// а всі зміни що є в особинах популяції будуть зберігатись
// окремо в даному класі
public class IndividualGenes
{
    public IndividualGenes()
    {
        Slots = new Dictionary<IScheduleSlot, SlotTracker>();
        teacherSlots = new Dictionary<long, List<SlotTracker>>();
        groupsSlots = new Dictionary<long, List<SlotTracker>>();
        FirstTrackers = new List<SlotTracker>();
        assignedSlotsByTeacherDate = new Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>>();
        assignedSlotsByGroupDate = new Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>>();
        assignedClassrooms = new Dictionary<DateTime, Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>>>();
    }

    public Dictionary<IScheduleSlot, SlotTracker> Slots { get; internal set; }

    internal Dictionary<long, List<SlotTracker>> teacherSlots { get; set; }
    internal Dictionary<long, List<SlotTracker>> groupsSlots { get; set; }
    internal List<SlotTracker> FirstTrackers { get; set; }
    
    internal Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>> assignedSlotsByTeacherDate = new Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>>();
    internal Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>> assignedSlotsByGroupDate = new Dictionary<long, Dictionary<DateTime, HashSet<SlotTracker>>>();

    private Dictionary<DateTime, Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>>> assignedClassrooms = new Dictionary<DateTime, Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>>>();


    public void SaveSlot(IScheduleSlot slot, SlotTracker tracker)
    {
        Slots[slot] = tracker;
    }

    public void SaveTeacherSlot(List<SlotTracker> slotTrackers)
    {
        var id = slotTrackers[0].ScheduleSlot.GroupSubject.Teacher.Id;
        teacherSlots.Add(id, slotTrackers);
    }

    public void SaveGroupSlot(long groupId ,List<SlotTracker> slotTrackers)
    {
        groupsSlots.Add(groupId, slotTrackers);
    }

    public void SaveFirstTracker(SlotTracker slotTrackers)
    {
        FirstTrackers.Add(slotTrackers);
    }

    public void SaveAssignedSlotByTeacherDate(long teacherId, Dictionary<DateTime, HashSet<SlotTracker>> assignedDates)
    {
        assignedSlotsByTeacherDate.Add(teacherId, assignedDates);
    }

    public void RemoveAssignedSlotByTeacherDate(long teacherId)
    {
        assignedSlotsByTeacherDate.Remove(teacherId);
    }

    public void ReplaceAssignedSlotByTeacherDate(long teacherId,
        Dictionary<DateTime, HashSet<SlotTracker>> assignedDates)
    {
        assignedSlotsByTeacherDate[teacherId] = assignedDates;
    }

    public void SaveAssignedSlotsByGroupDate(long groupId, Dictionary<DateTime, HashSet<SlotTracker>> assignedDates)
    {
        assignedSlotsByGroupDate.Add(groupId, assignedDates);
    }

    public void RemoveAssignedSlotsByGroupDate(long groupId)
    {
        assignedSlotsByGroupDate.Remove(groupId);
    }

    public void ReplaceAssignedSlotsByGroupDate(long groupId, Dictionary<DateTime, HashSet<SlotTracker>> assignedDates)
    {
        assignedSlotsByGroupDate[groupId] = assignedDates;
    }

    public void SaveClassroom(DateTime dateTime, Dictionary<int, Dictionary<ClassroomDTO, ScheduleSlotDTO>> classroom)
    {
        assignedClassrooms.Add(dateTime, classroom);
    }

}