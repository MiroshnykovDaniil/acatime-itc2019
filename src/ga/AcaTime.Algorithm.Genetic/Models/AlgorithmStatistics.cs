namespace AcaTime.Algorithm.Genetic.Models
{
    /// <summary>
    /// Статистика роботи алгоритму
    /// </summary>
    public class AlgorithmStatistics
    {
        /// <summary>
        /// Кількість успішних ітерацій
        /// </summary>
        public int Success { get; set; }


        /// <summary>
        /// Кількість неуспішних ітерацій
        /// </summary>
        public int Failed { get; set; }


    
        /// <summary>
        /// Найкращий результат
        /// </summary>
        public int BestResult { get; set; }

    }

}