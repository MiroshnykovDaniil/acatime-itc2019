using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AcaTime.ScheduleCommon.Utils
{
    /// <summary>
    /// Помічник з формування відлагоджувальної інформації про час, витрачений на виконання кроків процедури
    /// </summary>
    public class DebugData
    {
        private readonly Stopwatch sw;
        private readonly string _operation;
        private readonly bool debug;
        private readonly Dictionary<string, long> steps;
        private string _lastStep;

        /// <summary>
        /// Останній залогований крок
        /// </summary>
        public string LastStep => _lastStep;

        public long lastTicks;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="operation">Код операції для якої формуємо статистику по крокам</param>
        public DebugData(string operation, bool debug = true)
        {
            _operation = operation;
            this.debug = debug;
            steps = new Dictionary<string, long>();
            sw = new Stopwatch();
            sw.Start();
            lastTicks = sw.ElapsedTicks;
        }

        public long GetElapsed() => steps.Sum(p => p.Value);

        /// <summary>
        /// Записати інформацію про крок. Кроку буде співставлено час, що минув з останнього виклику цієї функції 
        /// або з моменту створення цього екземпляру.
        /// Якщо вже був крок з таким кодом, час для коду сумується
        /// </summary>
        /// <param name="val">Назва кроку</param>
        public long Step(string val)
        {
            if (!debug) return 0;
            _lastStep = val;
            
            var currentTicks = sw.ElapsedTicks;
            long ms = currentTicks - lastTicks;
            lastTicks = currentTicks;
      
            long res = ms;
            if (steps.ContainsKey(val))
            {
                ms += steps[val];
            }
            steps[val] = ms;
         
            return res;
        }

        /// <summary>
        /// Виведення інформації в рядок
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return ToString(true);
        }

        /// <summary>
        /// Виведення інформації в рядок
        /// </summary>
        /// <param name="withTotal">Виводити чи ні загальну суму часу всіх кроків</param>
        /// <param name="totalCaption">Назва параметра для загальної суми</param>
        /// <returns></returns>
        public string ToString(bool withTotal, string totalCaption = "All")
        {
            if (!debug)
            {
                return $"all {sw.ElapsedMilliseconds}";  
            };

            var stepStr = string.Join(" ", steps.Select(p => $"{p.Key}:{  (int)(p.Value * 1000 / Stopwatch.Frequency )}ms"));
            if (withTotal)
                return $"{_operation} ({stepStr} {totalCaption}:{ (int)(steps.Sum(p => p.Value) * 1000 / Stopwatch.Frequency)}ms)";
            else
                return $"{_operation} ({stepStr})";
        }

        /// <summary>
        /// Затрачено на всі кроки
        /// </summary>
        public int TotalMs => (int)(steps.Sum(p => p.Value) * 1000 / Stopwatch.Frequency);
    }
}
