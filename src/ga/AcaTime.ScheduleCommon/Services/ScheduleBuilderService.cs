using AcaTime.ScheduleCommon.Abstract;
using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScheduleCommon.Models.Constraints;
using AcaTime.ScheduleCommon.Utils;
using Microsoft.Extensions.Logging;

namespace AcaTime.ScheduleCommon.Services
{
    public class ScheduleBuilderService
    {
        internal readonly IScheduleBuilderDataService dataService;
        private readonly ILogger<ScheduleBuilderService> logger;

        public ScheduleBuilderService(IScheduleBuilderDataService dataService, ILogger<ScheduleBuilderService> logger)
        {
            this.dataService = dataService;
            this.logger = logger;
        }

        /// <summary>
        /// Завантажує дані для розкладу
        /// </summary>
        /// <param name="facultySeasonId"></param>
        /// <returns></returns>
        //private async Task<(FacultySeasonDTO, UserFunctions)> Load(long facultySeasonId)
        //{
        //    var root = await dataService.GetFacultySeasonScheduleAsync(facultySeasonId);
        //    GenerateScheduleSlots(root);
        //    var userFunctions = await dataService.GetUserFunctions(facultySeasonId);
        //    return (root, userFunctions);
        //}

        /// <summary>
        /// Генерує слоти для розкладу
        /// </summary>
        /// <param name="facultySeason"></param>
        private void GenerateScheduleSlots(FacultySeasonDTO facultySeason)
        {
            foreach (var groupSubject in facultySeason.GroupSubjects)
            {
                if (groupSubject.Subject == null) continue;

                for (int i = 0; i < groupSubject.Subject.NumberOfLessons; i++)
                {
                    groupSubject.ScheduleSlots.Add(new ScheduleSlotDTO
                    {
                        Id = 0, // Тимчасовий ID
                        LessonNumber = i + 1,
                        PairNumber = 0, // Буде визначено пізніше
                        Date = DateTime.MinValue, // Буде визначено алгоритмом
                        GroupSubject = groupSubject
                    });
                }
            }
        }


        public void ChecAndPatchkData(FacultySeasonDTO root, bool ignoreClassrooms = false)
        {

            foreach (var groupSubject in root.GroupSubjects)
            {

                if (groupSubject.Groups.Count == 0)
                    throw new Exception($"Група не визначена для предмета {groupSubject.Subject?.Name ?? groupSubject.Id.ToString()}");

                if (groupSubject.Subject == null)
                    throw new Exception($"Предмет не визначений для групи {groupSubject.Groups.First().Name}");

                // Перевірка наявності викладача у всіх групових предметах
                if (groupSubject.Teacher == null)
                {
                    throw new Exception($"Викладач не визначений для предмета {groupSubject.Subject.Name} для групи {groupSubject.Groups.First().Name}");
                }

                if (!ignoreClassrooms && !groupSubject.Subject.NoClassroom && groupSubject.Subject.ClassroomTypes.Count == 0)
                {
                    throw new Exception($"Тип аудиторії не визначений для предмета {groupSubject.Subject.Name} для групи {groupSubject.Groups.First().Name}");
                }

                if (ignoreClassrooms)
                {
                    groupSubject.Subject.NoClassroom = true;
                    groupSubject.Subject.ClassroomTypes.Clear();
                }
            }
        }

        /// <summary>
        /// Отримує дані та запускає алгоритм
        /// </summary>
        /// <param name="facultySeasonId"></param>
        /// <param name="parameters"></param>
        /// <param name="scheduleAlgorithm"></param>
        /// <param name="ignoreClassrooms"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<List<long>> DoAll(
            FacultySeasonDTO root,
            UserFunctions userFunctions,
            IScheduleAlgorithm scheduleAlgorithm,
            Dictionary<string, string> parameters,
            bool ignoreClassrooms = false,
            CancellationToken cancellationToken = default)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            long facultySeasonId = root.Id;

            var dd = new DebugData("schedule", true);
            List<AlgorithmResultDTO> resultDTOs = new List<AlgorithmResultDTO>();
            var res = new List<long>();

            try
            {
                GenerateScheduleSlots(root);
                ChecAndPatchkData(root, ignoreClassrooms);
                dd.Step("load");
                resultDTOs = await scheduleAlgorithm.Run(root, userFunctions, parameters, ignoreClassrooms, logger, cancellationToken);
                resultDTOs.ForEach(e => e.ScheduleSlots = e.ScheduleSlots.OrderBy(s => s.Date).ToList());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Помилка при генерації розкладу");
                res.Add(await dataService.SaveError(facultySeasonId, scheduleAlgorithm.GetName(), ex.Message));

            }

            dd.Step("calculate");

            if (resultDTOs != null && resultDTOs.Count > 0)
            {
                foreach (var resultDTO in resultDTOs)
                {
                    var id = await dataService.SaveScheduleSlots(facultySeasonId, resultDTO.ScheduleSlots, resultDTO.TotalEstimation, resultDTO.Name);
                    res.Add(id);
                }
            }
            else
            {
                if (!res.Any())
                    res.Add(await dataService.SaveError(facultySeasonId, scheduleAlgorithm.GetName(), "Результатів не знайдено"));
            }


            dd.Step("save");

            if (resultDTOs != null && resultDTOs.Count > 0)
                logger.LogInformation($"Збережено {res.Count} варіантів розкладу");
                
            logger.LogDebug(dd.ToString());

            return res;

        }
    }
}