using AcaTime.Algorithm.Genetic.Models.Genetic;
using AcaTime.ScheduleCommon.Models.Calc;

namespace AcaTime.Algorithm.Genetic.Utils;


// Використовуємо цей клас, щоб при формуванні початкової популяції та результатів
// зменшити кількість зберігаємої інформації,
// таким чином зменшивши кількість памʼяті необхідну на кожну особу популяції. 
public class IndividualMapper
{

    public void PrepareIndividual(Individual individual)
    {

        var root = individual.Root;

        var subjects = root.GroupSubjects;
        
        foreach (var groupSubjectDto in subjects)
        {
            // Прибираємо інформацію про викладачів
            if (!teachers.ContainsKey(groupSubjectDto.Teacher.Id))
            {
                var teacher = new TeacherDTO();
                teacher.Id = groupSubjectDto.Teacher.Id;
                teacher.Name = groupSubjectDto.Teacher.Name;
                teacher.Position = groupSubjectDto.Teacher.Position;
                teachers.Add(teacher.Id, teacher);
                groupSubjectDto.Teacher.Name = null;
            }
        }
    }
    
    Dictionary<long,TeacherDTO> teachers = new Dictionary<long,TeacherDTO>();
    
    public List<ScheduleSlotDTO> RefineIndividualSchedulleSlots(Individual individual)
    {
        var result = individual.Slots.Values.Where(v => v.IsAssigned).Select(x => x.ScheduleSlot).ToList();
        
        foreach (var scheduleSlotDto in result)
        {
            // Повертаємо інформацію про викладачів
            if (teachers.ContainsKey(scheduleSlotDto.GroupSubject.Teacher.Id))
            {
                scheduleSlotDto.GroupSubject.Teacher.Name = teachers[scheduleSlotDto.GroupSubject.Teacher.Id].Name;
                scheduleSlotDto.GroupSubject.Teacher.Position = scheduleSlotDto.GroupSubject.Teacher.Position;
            }
        }
        
        return result;
    }
    
}