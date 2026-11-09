using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AcaTime.Algorithm.Default.Models;
using AcaTime.Algorithm.Default.Services;
using AcaTime.ScheduleCommon.Abstract;
using AcaTime.ScheduleCommon.Models.Calc;
using AcaTime.ScriptModels;
using AcaTime.Algorithm.Default.Services.Statictics;
using AcaTime.ScheduleCommon.Models.Api;
using AcaTime.ScheduleCommon.Services;
using AcaTime.ScheduleCommon.Utils;

namespace AcaTime.ItcRunner;

/// <summary>
/// Local benchmark runner for converted ITC 2019 instances.
/// Usage: dotnet run --project itc2019-benchmark/runner -- <api-scheduledata.json> <outDir> [algorithm] [key=value ...]
///   algorithm: "default" (constructive DFS) | "genetic"
///   params:    ResultsCount MaxIterations TimeoutInSeconds SlotsTopK DomainsTopK
///              SlotsTemperature DomainsTemperature (AlgorithmParams keys)
/// Writes:    slot-solution JSON with every algorithm result, plus a run-summary JSON.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: <api-scheduledata.json> <outDir> [algorithm] [Params] [key=value ...]");
            return 2;
        }

        if (args.Length >= 2 && args[0] == "selftest")
        {
            return SelfTest(args[1]).GetAwaiter().GetResult();
        }

        var dataFile = args[0];
        var outDir = args[1];
        var algorithmName = args.Length > 2 ? args[2] : "default";
        var runParams = new Dictionary<string, string>();
        for (int i = 3; i < args.Length; i++)
        {
            var kv = args[i].Split('=', 2);
            if (kv.Length == 2) runParams[kv[0]] = kv[1];
        }

        Directory.CreateDirectory(outDir);
        var swTotal = Stopwatch.StartNew();

        var json = File.ReadAllText(dataFile);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dto = JsonSerializer.Deserialize<SimplifiedScheduleDataDto>(json, opts)!;
        var data = ReverseScheduleMapper.CreateScheduleAlgorithmData(dto);
        var root = data.FacultySeason;
        var uf = data.UserFunctions;

        // replicate ScheduleBuilderService.GenerateScheduleSlots (LessonNumber = i+1)
        foreach (var gs in root.GroupSubjects)
        {
            var n = gs.Subject.NumberOfLessons;
            for (int i = 0; i < n; i++)
            {
                gs.ScheduleSlots.Add(new ScheduleCommon.Models.Calc.ScheduleSlotDTO
                {
                    Id = 0,
                    LessonNumber = i + 1,
                    PairNumber = 0,
                    Date = DateTime.MinValue,
                    GroupSubject = gs
                });
            }
        }

        Console.WriteLine($"groups={root.GroupSubjects.Count} slots={root.GroupSubjects.Sum(g => g.ScheduleSlots.Count)} classrooms={root.Classrooms.Count} maxPairs={root.MaxLessonsPerDay}");
        Console.WriteLine($"days={(root.EndSeason.Date - root.BeginSeason.Date).Days + 1}");

        // compile scripts exactly like ScheduleGeneratorHost.StartAsync
        var svc = new ScriptExecutionService();
        var compileTask = Task.Run(async () =>
        {
            foreach (var e in uf.ScheduleEstimations)
                e.Func = await svc.CompileScheduleEstimationAsync(e.MainScript);
            foreach (var e in uf.SlotValueEstimations)
                e.Func = await svc.CompileSlotValueEstimationAsync(e.MainScript);
            foreach (var u in uf.UnitaryConstraints)
            {
                u.SelectorFunc = await svc.CompileSlotsSelectorAsync(u.SelectorScript);
                u.Func = await svc.CompileUnarySlotValidationAsync(u.MainScript);
            }
            foreach (var c in uf.SlotEstimations)
                c.Func = await svc.CompileSlotEstimationAsync(c.MainScript);
            foreach (var v in uf.SlotValidators)
                v.Func = await svc.CompileSlotValidationAsync(v.MainScript);
            return 0;
        });
        var compileSw = Stopwatch.StartNew();
        compileTask.GetAwaiter().GetResult();
        Console.WriteLine($"scripts compiled in {compileSw.Elapsed.TotalSeconds:0.0}s (scheduleEst={uf.ScheduleEstimations.Count} slotValueEst={uf.SlotValueEstimations.Count} unitary={uf.UnitaryConstraints.Count} validators={uf.SlotValidators.Count})");

        IScheduleAlgorithm algorithm = algorithmName switch
        {
            "default" => new DefaultScheduleAlgorithm(new FakeScheduleStatisticsService()),
            "genetic" => new AcaTime.Algorithm.Genetic.Services.GeneticScheduleAlgorithm(),
            _ => throw new ArgumentException($"unknown algorithm '{algorithmName}' (default|genetic)")
        };

        var loggerFactory = new NoopLoggerFactory();
        var logger = loggerFactory.CreateLogger("itc");

        Console.WriteLine($"running {algorithmName} ...");
        var ignoreClassrooms = runParams.TryGetValue("IgnoreClassrooms", out var igv) && igv == "true";
        var swRun = Stopwatch.StartNew();
        var cts = new CancellationTokenSource();
        List<AlgorithmResultDTO> results;
        try
        {
            results = algorithm.Run(root, uf, runParams, ignoreClassrooms: ignoreClassrooms, logger, cts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // cancellation paths in the algorithm can throw (e.g. NRE in timeout logging);
            // record as failure without altering algorithm code
            Console.WriteLine($"RUN FAILED:\n{ex}");
            results = new List<AlgorithmResultDTO>
            {
                new AlgorithmResultDTO { Name = algorithmName, TotalEstimation = int.MinValue, Error = ex.Message }
            };
        }
        swRun.Stop();

        // export each result's slot assignment
        var outResults = new List<object>();
        int idx = 0;
        foreach (var res in results)
        {
            var slots = new List<object>();
            foreach (var s in res.ScheduleSlots)
            {
                slots.Add(new
                {
                    classId = s.GroupSubject.Id,
                    lessonNumber = s.LessonNumber,
                    date = s.Date.ToString("yyyy-MM-dd"),
                    pair = s.PairNumber,
                    basePath = (int)(s.Date.Date - root.BeginSeason.Date).TotalDays,
                    room = s.Classroom?.Id
                });
            }
            outResults.Add(new
            {
                name = res.Name,
                totalEstimation = res.TotalEstimation,
                error = res.Error,
                slots,
            });
            idx++;
        }

        var report = new
        {
            algorithm = algorithmName,
            dataFile = Path.GetFileName(dataFile),
            parameters = runParams,
            beginSeason = root.BeginSeason.ToString("yyyy-MM-dd"),
            totalSeconds = swTotal.Elapsed.TotalSeconds,
            runSeconds = swRun.Elapsed.TotalSeconds,
            results = outResults
        };
        var outFile = Path.Combine(outDir, $"run-{algorithmName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        var jsonOut = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = false });
        File.WriteAllText(outFile, jsonOut);
        Console.WriteLine($"wrote {outFile}");
        var best = results.Count > 0 ? results.MaxBy(x => x.TotalEstimation) : null;
        Console.WriteLine($"results={results.Count} bestEst={(best != null ? best.TotalEstimation : 0)} runTime={swRun.Elapsed.TotalSeconds:0.0}s");

        // raw stats line for aggregation
        var bestEst = results.Count > 0 ? results.Max(x => x.TotalEstimation) : int.MinValue;
        Console.WriteLine($"STATS algo={algorithmName} best={bestEst} results={results.Count} secs={swRun.Elapsed.TotalSeconds:0}");
        return 0;
    }

    private static async Task<int> SelfTest(string dataFile)
    {
        var json = File.ReadAllText(dataFile);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dto = JsonSerializer.Deserialize<SimplifiedScheduleDataDto>(json, opts)!;
        var data = ReverseScheduleMapper.CreateScheduleAlgorithmData(dto);
        var root = data.FacultySeason;
        var uf = data.UserFunctions;

        var svc = new ScriptExecutionService();
        var validator = uf.SlotValidators.Single();
        validator.Func = await svc.CompileSlotValidationAsync(validator.MainScript);

        // reproduce the mixed assignment: class 370, both meetings Mon of week 1,
        // pairs 1 and 3 (its candidates are Mon x weeks 0-1, one pair per candidate)
        var gs = root.GroupSubjects.First(g => g.Id == 370);
        var teacherId = gs.Teacher.Id;
        var baseDate = root.BeginSeason.Date;
        var mon1 = baseDate.AddDays(7); // week 1 Monday

        var slotA = new ScheduleCommon.Models.Calc.ScheduleSlotDTO
        { Id = 1, LessonNumber = 1, Date = mon1, PairNumber = 1, GroupSubject = gs };
        var slotB = new ScheduleCommon.Models.Calc.ScheduleSlotDTO
        { Id = 2, LessonNumber = 2, Date = mon1, PairNumber = 3, GroupSubject = gs };

        IAssignedSlots assigned = new FakeAssignedSlots(new[] { slotA });
        var okB = validator.Func(slotB, assigned);
        Console.WriteLine($"validator(slotB with slotA assigned) = {okB}  (expect False)");

        var okA = validator.Func(slotA, assigned);
        Console.WriteLine($"validator(slotA vs itself-assigned) = {okA}");
        return 0;
    }

    private sealed class FakeAssignedSlots : IAssignedSlots
    {
        private readonly IReadOnlyList<IScheduleSlot> _slots;
        public FakeAssignedSlots(IEnumerable<IScheduleSlot> slots) { _slots = slots.ToList(); }
        public IReadOnlyList<IScheduleSlot> Slots => _slots;
        public IReadOnlyList<IScheduleSlot> GetSlotsByTeacher(long teacherId) =>
            _slots.Where(s => s.GroupSubject.Teacher.Id == teacherId).ToList();
        public IReadOnlyList<IScheduleSlot> GetSlotsByGroup(long groupId) =>
            _slots.Where(s => s.GroupSubject.Groups.Any(g => g.Id == groupId)).ToList();
        public IReadOnlyList<IScheduleSlot> GetSlotsByTeacherAndDate(long teacherId, DateTime date) =>
            GetSlotsByTeacher(teacherId).Where(s => s.Date.Date == date.Date).ToList();
        public IReadOnlyList<IScheduleSlot> GetSlotsByGroupAndDate(long groupId, DateTime date) =>
            GetSlotsByGroup(groupId).Where(s => s.Date.Date == date.Date).ToList();
    }

    private sealed class FakeScheduleStatisticsService2 { }
}

internal sealed class FakeScheduleStatisticsService : IScheduleStatisticsService
{
    public Task SaveStatisticsAsync(List<ScheduleStatisticsModel> statistics) => Task.CompletedTask;
}

internal sealed class NoopLoggerFactory : Microsoft.Extensions.Logging.ILoggerFactory
{
    private readonly Microsoft.Extensions.Logging.ILoggerFactory inner =
        Microsoft.Extensions.Logging.LoggerFactory.Create(b =>
        {
            b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "[HH:mm:ss] "; });
            b.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
        });

    public void AddProvider(Microsoft.Extensions.Logging.ILoggerProvider provider) => inner.AddProvider(provider);
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => inner.CreateLogger(categoryName);
    public void Dispose() => inner.Dispose();
}
