namespace AcaTime.Algorithm.Genetic.Utils
{
    /// <summary>
    /// Статичний клас-хелпер для сортування списків за вірогідністю,
    /// використовуючи softmax з температурою.
    /// </summary>
    public static class WeightedSortingHelper
    {
        private const double TemperatureThreshold = 0.001;

        /// <summary>
        /// Перемішує весь список елементів згідно їх оцінок,
        /// використовуючи softmax з температурою для обчислення ваг.
        /// Якщо температура менша за порогове значення, виконується детерміноване сортування.
        /// </summary>
        /// <typeparam name="T">Тип елементів списку</typeparam>
        /// <param name="items">Список елементів</param>
        /// <param name="scoreSelector">Функція для отримання оцінки елемента</param>
        /// <param name="temperature">Параметр температури для softmax</param>
        /// <returns>Перемішаний список елементів</returns>
        public static List<T> WeightedShuffle<T>(
            this IEnumerable<T> items,
            Func<T, double> scoreSelector,
            double temperature)
        {
            // Якщо температура дуже мала – виконуємо звичайне сортування за спаданням оцінки
            if (temperature < TemperatureThreshold)
            {
                return items.OrderByDescending(scoreSelector).ToList();
            }

            // Обчислюємо оцінки для кожного елемента
            var evaluatedItems = items
                .Select(item => new { Item = item, Score = scoreSelector(item) })
                .ToList();

            // Стабілізуємо softmax: знаходимо максимальне значення оцінки
            double maxScore = evaluatedItems.Any() ? evaluatedItems.Max(x => x.Score) : 0;

            // Попередньо обчислюємо вагу для кожного елемента
            var weightedItems = evaluatedItems
                .Select(x => (x.Item, Weight: Math.Exp((x.Score - maxScore) / temperature)))
                .ToList();

            var random = Random.Shared;
            var result = new List<T>();

            // Виконуємо weighted shuffle
            while (weightedItems.Count > 0)
            {
                double weightSum = weightedItems.Sum(x => x.Weight);
                double randValue = random.NextDouble() * weightSum;
                double cumulative = 0;

                for (int i = 0; i < weightedItems.Count; i++)
                {
                    cumulative += weightedItems[i].Weight;
                    if (randValue <= cumulative)
                    {
                        result.Add(weightedItems[i].Item);
                        weightedItems.RemoveAt(i);
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Пересортовує перші k елементів списку за вірогідністю.
        /// Якщо кількість елементів менша або дорівнює k, сортуємо весь список.
        /// Якщо k або температура менші за порогове значення, виконуємо звичайне сортування за спаданням оцінки.
        /// Приклад: [елементи1...k (пересортовані)] + [залишок без змін].
        /// </summary>
        /// <typeparam name="T">Тип елементів списку</typeparam>
        /// <param name="items">Список елементів</param>
        /// <param name="scoreSelector">Функція для отримання оцінки елемента</param>
        /// <param name="k">Кількість перших елементів для пересортування</param>
        /// <param name="temperature">Параметр температури для softmax</param>
        /// <returns>Новий список, де перші k елементів пересортовані за вірогідністю</returns>
        public static List<T> ResortFirstK<T>(
            this IEnumerable<T> items,
            Func<T, double> scoreSelector,
            int k,
            double temperature)
        {
            // Якщо k <= 0 або температура дуже мала – виконуємо детерміноване сортування всього списку
            if (k <= 1 || temperature < TemperatureThreshold)
            {
                return items.OrderByDescending(scoreSelector).ToList();
            }

            var itemList = items.ToList();

            // Якщо загальна кількість елементів менша або дорівнює k – сортуємо весь список weightedShuffle
            if (itemList.Count <= k)
            {
                return itemList.WeightedShuffle(scoreSelector, temperature);
            }
            else
            {
                var scoreDic = itemList.ToDictionary(x => x, x => scoreSelector(x));

                 
                var sortedPairs = scoreDic.OrderByDescending(x => x.Value).ToList();

                // Розбиваємо список на дві частини: перші k та залишок
                var firstK = sortedPairs.Take(k).Select(x => x.Key).ToList();
                var remainder = sortedPairs.Skip(k).Select(x => x.Key).ToList();

                // Перемішуємо перші k елементів
                var sortedFirstK = firstK.WeightedShuffle((x) => scoreDic[x], temperature);

                // Об'єднуємо пересортовані перші k елементів з залишком (без змін)
                return sortedFirstK.Concat(remainder).ToList();
            }
        }
    }
}