using System.Globalization;

namespace AcaTime.Algorithm.Default.Models
{
    /// <summary>
    /// Параметри алгоритму
    /// </summary>
    public class AlgorithmParams
    {
        /// <summary>
        /// Кількість результатів, які потрібно знайти
        /// </summary>
        public int ResultsCount { get; set; }

        /// <summary>
        /// Максимальна кількість ітерацій
        /// </summary>
        public int MaxIterations { get; set; }

        /// <summary>
        /// Максимальний час роботи алгоритму в секундах
        /// </summary>
        public int TimeoutInSeconds { get; set; }

        /// <summary>
        /// Кількість кращих слотів для вибору
        /// </summary>
        public int SlotsTopK { get; set; }

        /// <summary>
        /// Кількість кращих доменів для вибору
        /// </summary>
        public int DomainsTopK { get; set; }


        /// <summary>
        /// Температура для вибору слотів
        /// </summary>
        public double SlotsTemperature { get; set; }


        /// <summary>
        /// Температура для вибору доменів
        /// </summary>
        public double DomainsTemperature { get; set; }

        /// <summary>
        /// Шлях до зовнішнього (зовнішньо знайденого) допустимого розкладу.
        /// Якщо задано, юніт стартує з нього замість конструктивного DFS
        /// (домени слотів НЕ звужуються: оператори далі можуть перепризначати слоти).
        /// </summary>
        public string SeedSchedule { get; set; }


        /// <summary>
        /// Увімкнути скриптову перевірку доменів у ForwardCheck (1 = так, 0 = ні).
        /// Скрипти-валідатори (UserFunctions.SlotValidators) використовуються не лише
        /// при пробному призначенні, а й для раннього звуження доменів сусідніх слотів.
        /// </summary>
        public int ScriptForwardCheck { get; set; }

        /// <summary>
        /// Максимальна кількість сусідніх слотів, для яких застосовується скриптова
        /// перевірка доменів (захист від надмірної вартості на великих кліках).
        /// </summary>
        public int ScriptForwardCheckMaxPeers { get; set; }


        public AlgorithmParams(Dictionary<string,string> parameters)
        {
            // встановлюємо значення параметрів з словника
            ResultsCount = parameters.ContainsKey("ResultsCount") ? int.Parse(parameters["ResultsCount"]) : 1;
            MaxIterations = parameters.ContainsKey("MaxIterations") ? int.Parse(parameters["MaxIterations"]) : 1000;
            TimeoutInSeconds = parameters.ContainsKey("TimeoutInSeconds") ? int.Parse(parameters["TimeoutInSeconds"]) : 60;
            SlotsTopK = parameters.ContainsKey("SlotsTopK") ? int.Parse(parameters["SlotsTopK"]) : 1;
            DomainsTopK = parameters.ContainsKey("DomainsTopK") ? int.Parse(parameters["DomainsTopK"]) : 1;
            SlotsTemperature = parameters.ContainsKey("SlotsTemperature") ? double.Parse(parameters["SlotsTemperature"], CultureInfo.InvariantCulture) : 1;
            DomainsTemperature = parameters.ContainsKey("DomainsTemperature") ? double.Parse(parameters["DomainsTemperature"], CultureInfo.InvariantCulture) : 1;
            SeedSchedule = parameters.ContainsKey("SeedSchedule") ? parameters["SeedSchedule"] : null;
            ScriptForwardCheck = parameters.ContainsKey("ScriptForwardCheck") ? int.Parse(parameters["ScriptForwardCheck"]) : 0;
            ScriptForwardCheckMaxPeers = parameters.ContainsKey("ScriptForwardCheckMaxPeers") ? int.Parse(parameters["ScriptForwardCheckMaxPeers"]) : 200;

        }

    }

}