using AcaTime.ScriptModels;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace AcaTime.ScheduleCommon.Services
{
    /// <summary>
    /// Сервіс для виконання та валідації скриптів.
    /// </summary>
    public class ScriptExecutionService
    {

        /// <summary>
        /// Отримує список асемблі, які використовуються в ScriptExecutionService
        /// </summary>
        public static string[] GetAssemblies()
        {
            return new[]
            {
                // Системні збірки
                typeof(object).Assembly.Location,
                typeof(DateTime).Assembly.Location,
                typeof(Enumerable).Assembly.Location,
                
                // Збірки проекту
                typeof(IFacultySeason).Assembly.Location,

            };
        }

        private readonly ScriptOptions _scriptOptions;

        public ScriptExecutionService()
        {
            _scriptOptions = ScriptOptions.Default
                .AddReferences(
                    typeof(object).Assembly,
                    typeof(DateTime).Assembly,
                    typeof(Enumerable).Assembly,
                    typeof(IFacultySeason).Assembly)

                .AddImports(
                    "System",
                    "System.Linq",
                    "System.Collections.Generic",
                    "AcaTime.ScriptModels");
        }


      

        /// <summary>
        /// Перевіряє синтаксичну, семантичну коректність скрипта та відповідність сигнатури типу.
        /// </summary>
        /// <param name="scriptBody">Тіло функції, яке написав користувач.</param>
        /// <param name="ruleType">Тип правила для генерації шаблону.</param>
        /// <returns>Список помилок або порожній список, якщо помилок немає.</returns>
        public async Task<string> ValidateScript(string scriptBody, long ruleType)
        {
            try
            {
                // Перевірка сигнатури функції залежно від типу правила
                switch (ruleType)
                {
                    case -1:
                        var resSelector = await CompileSlotsSelectorAsync(scriptBody);
                        break;
                    case 1:
                        var res1 = await CompileScheduleEstimationAsync(scriptBody);
                        break;
                    case 2:
                        var res2 = await CompileSlotValueEstimationAsync(scriptBody);
                        break;
                    case 3:
                        var res3 = await CompileSlotValidationAsync(scriptBody);
                        break;
                    case 4:
                        var res4 = await CompileSlotEstimationAsync(scriptBody);
                        break;
                    case 11:
                        var res11 = await CompileUnarySlotValidationAsync(scriptBody);
                        break;

                    default:
                        throw new NotSupportedException($"Тип правила {ruleType} не підтримується.");
                }

                return null; // Помилок немає
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }




        /// <summary>
        /// Компілює скрипт для оцінки розкладу.
        /// </summary>
        /// <param name="scriptBody">Тіло функції користувача.</param>
        /// <returns>Скомпільований делегат або помилка.</returns>
        public async Task<Func<IFacultySeason, int>> CompileScheduleEstimationAsync(string scriptBody)
        {
            var script = scriptBody;


            // Компілюємо скрипт і отримуємо делегат
            var compiledDelegate = await CSharpScript.EvaluateAsync<Func<IFacultySeason, int>>(
                script,
                _scriptOptions
            );

            return compiledDelegate;

        }


        /// <summary>
        /// Компілює скрипт для оцінки слоту в розкладі.
        /// </summary>
        /// <param name="scriptBody"></param>
        /// <returns></returns>
        public async Task<Func<IScheduleSlot, IAssignedSlots, int>> CompileSlotValueEstimationAsync(string scriptBody)
        {
            var script = scriptBody;


            // Компілюємо скрипт і отримуємо делегат
            var compiledDelegate = await CSharpScript.EvaluateAsync<Func<IScheduleSlot, IAssignedSlots, int>>(
                script,
                _scriptOptions
            );

            return compiledDelegate;

        }

        /// <summary>
        /// Компілює скрипт для валідації слоту в розкладі.
        /// </summary>
        /// <param name="scriptBody"></param>
        /// <returns></returns>
        public async Task<Func<IScheduleSlot, IAssignedSlots, bool>> CompileSlotValidationAsync(string scriptBody)
        {
            var script = scriptBody;


            // Компілюємо скрипт і отримуємо делегат
            var compiledDelegate = await CSharpScript.EvaluateAsync<Func<IScheduleSlot, IAssignedSlots, bool>>(
                script,
                _scriptOptions
            );

            return compiledDelegate;

        }

        /// <summary>
        /// Компілює скрипт для вибору слотів в розкладі для подальшої валідації.
        /// </summary>
        /// <param name="scriptBody"></param>
        /// <returns></returns>
        public async Task<Func<IFacultySeason, IEnumerable<IScheduleSlot>>> CompileSlotsSelectorAsync(string scriptBody)
        {
            var script = scriptBody;


            // Компілюємо скрипт і отримуємо делегат
            var compiledDelegate = await CSharpScript.EvaluateAsync<Func<IFacultySeason, IEnumerable<IScheduleSlot>>>(
                script,
                _scriptOptions
            );

            return compiledDelegate;

        }

        /// <summary>
        /// Компілює скрипт для валідації слоту в розкладі.
        /// </summary>
        /// <param name="scriptBody"></param>
        /// <returns></returns>
        public async Task<Func<IScheduleSlot, bool>> CompileUnarySlotValidationAsync(string scriptBody)
        {
            var script = scriptBody;


            // Компілюємо скрипт і отримуємо делегат
            var compiledDelegate = await CSharpScript.EvaluateAsync<Func<IScheduleSlot, bool>>(
                script,
                _scriptOptions
            );

            return compiledDelegate;

        }

        /// <summary>
        /// Компілює скрипт для оцінки пріоритету слоту. Потрібно для визначення порядку вибору слотів.
        /// </summary>
        /// <param name="scriptBody"></param>
        /// <returns></returns>
        public async Task<Func<ISlotEstimation, int>> CompileSlotEstimationAsync(string scriptBody)
        {
            var script = scriptBody;

            // Компілюємо скрипт і отримуємо делегат
            var compiledDelegate = await CSharpScript.EvaluateAsync<Func<ISlotEstimation, int>>(
                script,
                _scriptOptions
            );

            return compiledDelegate;

        }

    }
}