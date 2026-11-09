using AcaTime.Algorithm.Default.Models;

namespace AcaTime.Algorithm.Default.Services.Statictics
{
    public interface IScheduleStatisticsService
    {
        Task SaveStatisticsAsync(List<ScheduleStatisticsModel> statistics);
    }
}