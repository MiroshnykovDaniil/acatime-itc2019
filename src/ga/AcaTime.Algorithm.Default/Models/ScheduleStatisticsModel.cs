using System.ComponentModel.DataAnnotations;

namespace AcaTime.Algorithm.Default.Models
{
    /// <summary>
    /// Модель для збереження статистики виконання алгоритму формування розкладу.
    /// </summary>
    public class ScheduleStatisticsModel
    {
        /// <summary>
        /// Максимальний час виконання алгоритму, сек.
        /// </summary>
        [Display(Name = "Максимальний час виконання (сек)")]
        public double MaxExecutionTimeSec { get; set; }

        /// <summary>
        /// Чи використовувалася стохастика у розв’язанні.
        /// </summary>
        [Display(Name = "Без застосування стохастики (так/ні)")]
        public bool Deterministic { get; set; }

        /// <summary>
        /// Кількість паралельних екземплярів/потоків виконання.
        /// </summary>
        [Display(Name = "Кількість паралельних обчислень")]
        public int ParallelCount { get; set; }

        /// <summary>
        /// Максимальна кількість ітерацій для пошуку кращого варіанту.
        /// </summary>
        [Display(Name = "Максимальна кількість ітерацій для обрання кращих варіантів")]
        public int MaxIterations { get; set; }

        /// <summary>
        /// Кількість пріоритетних предметів (Top K) для вибору слотів.
        /// </summary>
        [Display(Name = "Кількість пріоритетних предметів (Top K) SlotsTopK")]
        public int SlotsTopK { get; set; }

        /// <summary>
        /// Температура статистичного шуму для вибору предметів (слотів).
        /// </summary>
        [Display(Name = "Температура статистичного шуму для вибору предметів SlotsTemperature")]
        public double SlotsTemperature { get; set; }

        /// <summary>
        /// Кількість пріоритетних значень (Top K) для доменів.
        /// </summary>
        [Display(Name = "Кількість пріоритетних значень (Top K) DomainsTopK")]
        public int DomainsTopK { get; set; }

        /// <summary>
        /// Температура статистичного шуму для вибору значень домену.
        /// </summary>
        [Display(Name = "Температура статистичного шуму для вибору розкладу предмету DomainsTemperature")]
        public double DomainsTemperature { get; set; }

        /// <summary>
        /// Останній крок, якщо виконання було перервано (наприклад, таймером).
        /// </summary>
        [Display(Name = "Останній крок (у разі скасування за таймером без знаходження розкладу)")]
        public int? LastStep { get; set; }

        /// <summary>
        /// Значення цільової функції (оцінка алгоритму) у разі знаходження розкладу.
        /// </summary>
        [Display(Name = "Оцінка алгоритму (у разі знаходження розкладу)")]
        public double? ResultScore { get; set; }

        /// <summary>
        /// Час роботи алгоритму, мс.
        /// </summary>
        [Display(Name = "Час роботи алгоритму (мс)")]
        public long ExecutionTimeMs { get; set; }
    }

}