using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AcaTime.ScheduleCommon.Models;
using AcaTime.ScheduleCommon.Models.Api;
using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScheduleCommon.Models.Constraints;
using AcaTime.ScheduleCommon.Services;
using AcaTime.ScriptModels;

namespace AcaTime.ScheduleCommon.Utils
{
    /// <summary>
    /// Утилітарний клас для зворотного мапування об'єктів розкладу та компіляції скриптів
    /// </summary>
    public static class ReverseScheduleMapper
    {
        #region Зворотне мапування (для алгоритму)

        /// <summary>
        /// Конвертує спрощену модель семестру факультету в повну модель для алгоритму
        /// </summary>
        /// <param name="simplifiedFacultySeason">Спрощений DTO об'єкт для семестру факультету</param>
        /// <returns>Повна модель даних семестру для алгоритму</returns>
        public static FacultySeasonDTO FromSimplifiedDto(SimplifiedFacultySeasonDTO simplifiedFacultySeason)
        {
            if (simplifiedFacultySeason == null)
                return null;

            var facultySeason = new FacultySeasonDTO
            {
                Id = simplifiedFacultySeason.Id,
                Name = simplifiedFacultySeason.Name,
                BeginSeason = simplifiedFacultySeason.BeginSeason,
                EndSeason = simplifiedFacultySeason.EndSeason,
                MaxLessonsPerDay = simplifiedFacultySeason.MaxLessonsPerDay,
                GroupSubjects = new List<GroupSubjectDTO>()
            };

            if (simplifiedFacultySeason.GroupSubjects != null)
            {
                foreach (var simplifiedGs in simplifiedFacultySeason.GroupSubjects)
                {
                    var groupSubject = FromSimplifiedDto(simplifiedGs);
                    groupSubject.Faculty = facultySeason;
                    facultySeason.GroupSubjects.Add(groupSubject);
                }
            }

            if (simplifiedFacultySeason.Classrooms != null)
            {
                foreach (var simplifiedClassroom in simplifiedFacultySeason.Classrooms)
                {
                    var classroom = FromSimplifiedDto(simplifiedClassroom);
                    facultySeason.Classrooms.Add(classroom);
                }
            }

            return facultySeason;
        }

        /// <summary>
        /// Конвертує спрощену модель групового предмету в повну модель для алгоритму
        /// </summary>
        /// <param name="simplifiedGs">Спрощений DTO об'єкт для групового предмету</param>
        /// <returns>Повна модель даних групового предмету для алгоритму</returns>
        public static GroupSubjectDTO FromSimplifiedDto(SimplifiedGroupSubjectDTO simplifiedGs)
        {
            if (simplifiedGs == null)
                return null;

            return new GroupSubjectDTO
            {
                Id = simplifiedGs.Id,
                Teacher = FromSimplifiedDto(simplifiedGs.Teacher),
                Subject = FromSimplifiedDto(simplifiedGs.Subject),
                StudentCount = simplifiedGs.StudentCount,
                Groups = simplifiedGs.Groups?.Select(g => FromSimplifiedDto(g)).ToList() ?? new List<StudentLessonGroupDTO>(),
                ScheduleSlots = new List<ScheduleSlotDTO>()
            };
        }


        /// <summary>
        /// Конвертує спрощену модель аудиторії в повну модель для алгоритму
        /// </summary>
        /// <param name="simplifiedClassroom">Спрощений DTO об'єкт для аудиторії</param>
        /// <returns>Повна модель даних аудиторії для алгоритму</returns>
        public static ClassroomDTO FromSimplifiedDto(SimplifiedClassroomDTO simplifiedClassroom)
        {
            if (simplifiedClassroom == null)
                return null;

            return new ClassroomDTO
            {
                Id = simplifiedClassroom.Id,
                Name = simplifiedClassroom.Name,
                StudentCount = simplifiedClassroom.StudentCount,
                ClassroomTypes = simplifiedClassroom.ClassroomTypes.Select(c => new SelectedClassroomTypeDTO
                {
                    ClassroomTypeId = c.ClassroomTypeId,
                    ClassroomTypeName = c.ClassroomTypeName,
                    Priority = c.Priority
                }).ToList()
            };
        }

        /// <summary>
        /// Конвертує спрощену модель викладача в повну модель для алгоритму
        /// </summary>
        /// <param name="simplifiedTeacher">Спрощений DTO об'єкт для викладача</param>
        /// <returns>Повна модель даних викладача для алгоритму</returns>
        public static TeacherDTO FromSimplifiedDto(SimplifiedTeacherDTO simplifiedTeacher)
        {
            if (simplifiedTeacher == null)
                return null;

            return new TeacherDTO
            {
                Id = simplifiedTeacher.Id,
                Name = simplifiedTeacher.Name,
                Position = simplifiedTeacher.Position
            };
        }

        /// <summary>
        /// Конвертує спрощену модель предмета в повну модель для алгоритму
        /// </summary>
        /// <param name="simplifiedSubject">Спрощений DTO об'єкт для предмета</param>
        /// <returns>Повна модель даних предмета для алгоритму</returns>
        public static SubjectDTO FromSimplifiedDto(SimplifiedSubjectDTO simplifiedSubject)
        {
            if (simplifiedSubject == null)
                return null;

            return new SubjectDTO
            {
                Id = simplifiedSubject.Id,
                Name = simplifiedSubject.Name,
                NumberOfLessons = simplifiedSubject.NumberOfLessons,
                DisciplineId = simplifiedSubject.DisciplineId,
                DisciplineName = simplifiedSubject.DisciplineName,
                SubjectTypeId = simplifiedSubject.SubjectTypeId,
                SubjectTypeName = simplifiedSubject.SubjectTypeName,
                SubjectTypeShortName = simplifiedSubject.SubjectTypeShortName,
                NoClassroom = simplifiedSubject.NoClassroom,
                ClassroomTypes = simplifiedSubject.ClassroomTypes.Select(s => new SelectedClassroomTypeDTO
                {
                    ClassroomTypeId = s.ClassroomTypeId,
                    ClassroomTypeName = s.ClassroomTypeName,
                    Priority = s.Priority
                }).ToList(),
                DefinedSeries = simplifiedSubject.DefinedSeries?.Select(s => new SubjectSeriesDto
                {
                    NumberOfLessons = s.NumberOfLessons,
                    SeriesNumber = s.SeriesNumber,
                    SplitType = (SubjectSeriesSplitType)s.SplitType,
                    StartInAnyWeek = s.StartInAnyWeek
                }).ToList() ?? new List<SubjectSeriesDto>()
            };
        }

        /// <summary>
        /// Конвертує спрощену модель групи студентів в повну модель для алгоритму
        /// </summary>
        /// <param name="simplifiedGroup">Спрощений DTO об'єкт для групи студентів</param>
        /// <returns>Повна модель даних групи студентів для алгоритму</returns>
        public static StudentLessonGroupDTO FromSimplifiedDto(SimplifiedStudentLessonGroupDTO simplifiedGroup)
        {
            if (simplifiedGroup == null)
                return null;

            return new StudentLessonGroupDTO
            {
                Id = simplifiedGroup.Id,
                Name = simplifiedGroup.Name,
                SubgroupName = simplifiedGroup.SubgroupName,
                SubgroupId = simplifiedGroup.SubgroupId,
                SubgroupVariantId = simplifiedGroup.SubgroupVariantId,
                SubgroupVariantName = simplifiedGroup.SubgroupVariantName,
                SubgroupCount = simplifiedGroup.SubgroupCount,
                CourseYearId = simplifiedGroup.CourseYearId,
                CourseYearName = simplifiedGroup.CourseYearName,
                EducationalProgramId = simplifiedGroup.EducationalProgramId,
                EducationalProgramName = simplifiedGroup.EducationalProgramName
            };
        }

        /// <summary>
        /// Створює об'єкт обмеження ScheduleEstimation з спрощеного DTO без компіляції скриптів
        /// </summary>
        /// <param name="simplifiedConstraint">Спрощений DTO для обмеження</param>
        /// <returns>Об'єкт обмеження для оцінки розкладу</returns>
        public static ScheduleEstimation CreateScheduleEstimation(SimplifiedConstraintDTO simplifiedConstraint)
        {
            return new ScheduleEstimation
            {
                Id = simplifiedConstraint.Id,
                Name = simplifiedConstraint.Name,
                Description = simplifiedConstraint.Description,
                SelectorScript = simplifiedConstraint.SelectorScript,
                MainScript = simplifiedConstraint.MainScript,
                RuleType = 1
            };
        }

        /// <summary>
        /// Створює об'єкт обмеження ScheduleSlotEstimation з спрощеного DTO без компіляції скриптів
        /// </summary>
        /// <param name="simplifiedConstraint">Спрощений DTO для обмеження</param>
        /// <returns>Об'єкт обмеження для оцінки слоту розкладу</returns>
        public static ScheduleSlotValueEstimation CreateSlotValueEstimation(SimplifiedConstraintDTO simplifiedConstraint)
        {
            return new ScheduleSlotValueEstimation
            {
                Id = simplifiedConstraint.Id,
                Name = simplifiedConstraint.Name,
                Description = simplifiedConstraint.Description,
                SelectorScript = simplifiedConstraint.SelectorScript,
                MainScript = simplifiedConstraint.MainScript,
                RuleType = 2
            };
        }

        /// <summary>
        /// Створює об'єкт обмеження UnitaryConstraint з спрощеного DTO без компіляції скриптів
        /// </summary>
        /// <param name="simplifiedConstraint">Спрощений DTO для обмеження</param>
        /// <returns>Об'єкт унітарного обмеження</returns>
        public static UnitaryConstraint CreateUnitaryConstraint(SimplifiedConstraintDTO simplifiedConstraint)
        {
            return new UnitaryConstraint
            {
                Id = simplifiedConstraint.Id,
                Name = simplifiedConstraint.Name,
                Description = simplifiedConstraint.Description,
                SelectorScript = simplifiedConstraint.SelectorScript,
                MainScript = simplifiedConstraint.MainScript,
                RuleType = 11
            };
        }

        /// <summary>
        /// Створює об'єкт обмеження SlotEstimation з спрощеного DTO без компіляції скриптів
        /// </summary>
        /// <param name="simplifiedConstraint">Спрощений DTO для обмеження</param>
        /// <returns>Об'єкт обмеження для оцінки пріоритету слоту</returns>
        public static SlotEstimation CreateSlotEstimation(SimplifiedConstraintDTO simplifiedConstraint)
        {
            return new SlotEstimation
            {
                Id = simplifiedConstraint.Id,
                Name = simplifiedConstraint.Name,
                Description = simplifiedConstraint.Description,
                SelectorScript = simplifiedConstraint.SelectorScript,
                MainScript = simplifiedConstraint.MainScript,
                RuleType = 4
            };
        }

        /// <summary>
        /// Створює об'єкт обмеження ScheduleSlotValidation з спрощеного DTO без компіляції скриптів
        /// </summary>
        /// <param name="simplifiedConstraint">Спрощений DTO для обмеження</param>
        /// <returns>Об'єкт обмеження для валідації слоту</returns>
        public static ScheduleSlotValidation CreateScheduleSlotValidation(SimplifiedConstraintDTO simplifiedConstraint)
        {
            return new ScheduleSlotValidation
            {
                Id = simplifiedConstraint.Id,
                Name = simplifiedConstraint.Name,
                Description = simplifiedConstraint.Description,
                SelectorScript = simplifiedConstraint.SelectorScript,
                MainScript = simplifiedConstraint.MainScript,
                RuleType = 3
            };
        }

        /// <summary>
        /// Конвертує спрощений DTO для функцій користувача в повну модель для алгоритму без компіляції скриптів
        /// </summary>
        /// <param name="simplifiedUserFunctions">Спрощений DTO з функціями користувача</param>
        /// <returns>Повна модель з функціями користувача для алгоритму</returns>
        public static UserFunctions FromSimplifiedDto(SimplifiedUserFunctionsDTO simplifiedUserFunctions)
        {
            if (simplifiedUserFunctions == null)
                return null;

            return new UserFunctions
            {
                ScheduleEstimations = simplifiedUserFunctions.ScheduleEstimations?.Select(CreateScheduleEstimation).ToList() ?? new List<ScheduleEstimation>(),
                SlotValueEstimations = simplifiedUserFunctions.ScheduleSlotEstimations?.Select(CreateSlotValueEstimation).ToList() ?? new List<ScheduleSlotValueEstimation>(),
                UnitaryConstraints = simplifiedUserFunctions.UnitaryConstraints?.Select(CreateUnitaryConstraint).ToList() ?? new List<UnitaryConstraint>(),
                SlotEstimations = simplifiedUserFunctions.SlotPriorities?.Select(CreateSlotEstimation).ToList() ?? new List<SlotEstimation>(),
                SlotValidators = simplifiedUserFunctions.SlotValidators?.Select(CreateScheduleSlotValidation).ToList() ?? new List<ScheduleSlotValidation>()
            };
        }



        /// <summary>
        /// Створює дані для запуску алгоритму розкладу на основі DTO, отриманого з API без компіляції скриптів
        /// </summary>
        /// <param name="scheduleData">DTO з даними розкладу</param>
        /// <returns>Кортеж з моделлю семестру і функціями користувача для алгоритму</returns>
        public static ScheduleDataDto CreateScheduleAlgorithmData(SimplifiedScheduleDataDto scheduleData)
        {
            if (scheduleData == null)
                throw new ArgumentNullException(nameof(scheduleData));

            var facultySeason = FromSimplifiedDto(scheduleData.FacultySeason);
            var userFunctions = FromSimplifiedDto(scheduleData.UserFunctions);

            return new ScheduleDataDto
            {
                FacultySeason = facultySeason,
                UserFunctions = userFunctions
            };
        }

        /// <summary>
        /// Конвертує модель слоту розкладу в спрощену модель
        /// </summary>
        /// <param name="slot"></param>
        /// <returns></returns>
        public static SimplifiedScheduleSlotDTO ToSimplifiedScheduleSlotDto(ScheduleSlotDTO slot)
        {
            if (slot == null)
                return null;

            return new SimplifiedScheduleSlotDTO
            {
                Id = slot.Id,
                LessonNumber = slot.LessonNumber,
                Date = slot.Date,
                PairNumber = slot.PairNumber,
                LessonSeriesLength = slot.LessonSeriesLength,
                LessonSeriesId = slot.LessonSeriesId,
                GroupSubjectId = slot.GroupSubject.Id,
                ClassroomId = slot.Classroom?.Id
            };
        }

        /// <summary>
        /// Конвертує список слотів розкладу в спрощену модель
        /// </summary>
        /// <param name="slots"></param>
        /// <returns></returns>
        public static List<SimplifiedScheduleSlotDTO> ToSimplifiedScheduleSlotDtos(IEnumerable<ScheduleSlotDTO> slots)
        {
            if (slots == null)
                return new List<SimplifiedScheduleSlotDTO>();

            return slots.Select(slot => ToSimplifiedScheduleSlotDto(slot)).ToList();
        }

        #endregion

      
    }
}