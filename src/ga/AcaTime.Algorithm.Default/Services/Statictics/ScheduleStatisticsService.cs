using AcaTime.Algorithm.Default.Models;
using Microsoft.Extensions.Logging;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AcaTime.Algorithm.Default.Services.Statictics
{

    /// <summary>
    /// Сервіс для збереження статистики роботи алгоритму у таблицю schedule_statistics.
    /// </summary>
    public class ScheduleStatisticsService : IScheduleStatisticsService
    {
        private readonly string _connectionString;
        private readonly ILogger<ScheduleStatisticsService> logger;

        /// <summary>
        /// Ініціалізує сервіс з рядком підключення.
        /// </summary>
        public ScheduleStatisticsService(string connectionString, ILogger<ScheduleStatisticsService> logger)
        {
            _connectionString = connectionString;
            this.logger = logger;
        }

        /// <summary>
        /// Зберігає список статистик у таблицю schedule_statistics.
        /// </summary>
        /// <param name="statistics">Список статистик для збереження.</param>
        public async Task SaveStatisticsAsync(List<ScheduleStatisticsModel> statistics)
        {
            if (statistics == null || statistics.Count == 0)
                return;

            const string sql = @"
            INSERT INTO schedule_statistics
            (max_execution_time_sec, deterministic, parallel_count, max_iterations,
             slots_top_k, slots_temperature, domains_top_k, domains_temperature,
             last_step, result_score, execution_time_ms)
            VALUES
            (@max_execution_time_sec, @deterministic, @parallel_count, @max_iterations,
             @slots_top_k, @slots_temperature, @domains_top_k, @domains_temperature,
             @last_step, @result_score, @execution_time_ms);";


  
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

           

            try
            {
                foreach (var stat in statistics)
                {
                    await using var cmd = new NpgsqlCommand(sql, conn);

                    cmd.Parameters.AddWithValue("max_execution_time_sec", stat.MaxExecutionTimeSec);
                    cmd.Parameters.AddWithValue("deterministic", stat.Deterministic);
                    cmd.Parameters.AddWithValue("parallel_count", stat.ParallelCount);
                    cmd.Parameters.AddWithValue("max_iterations", stat.MaxIterations);

                    cmd.Parameters.AddWithValue("slots_top_k", stat.SlotsTopK);
                    cmd.Parameters.AddWithValue("slots_temperature", stat.SlotsTemperature);
                    cmd.Parameters.AddWithValue("domains_top_k", stat.DomainsTopK);
                    cmd.Parameters.AddWithValue("domains_temperature", stat.DomainsTemperature);

                    if (stat.LastStep.HasValue)
                        cmd.Parameters.AddWithValue("last_step", stat.LastStep.Value);
                    else
                        cmd.Parameters.AddWithValue("last_step", DBNull.Value);

                    if (stat.ResultScore.HasValue)
                        cmd.Parameters.AddWithValue("result_score", stat.ResultScore.Value);
                    else
                        cmd.Parameters.AddWithValue("result_score", DBNull.Value);

                    cmd.Parameters.AddWithValue("execution_time_ms", stat.ExecutionTimeMs);

                    await cmd.ExecuteNonQueryAsync();
                }

              
            }
            catch(Exception e)
            {
               logger.LogError(e, "Помилка при збереженні статистики розкладу: {Message}", e.Message);
               

            }
        }
    }
}
