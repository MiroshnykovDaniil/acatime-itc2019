using AcaTime.Algorithm.Default.Models;

namespace AcaTime.Algorithm.Default.Services.Statictics
{
    /// <summary>
    /// Сервіс для збереження статистики роботи алгоритму у таблицю schedule_statistics.
    /// </summary>
    public class FakeScheduleStatisticsService : IScheduleStatisticsService
    {
        public async Task SaveStatisticsAsync(List<ScheduleStatisticsModel> statistics)
        {
            // Імітація збереження статистики, нічого не робимо
        }
    }
}
