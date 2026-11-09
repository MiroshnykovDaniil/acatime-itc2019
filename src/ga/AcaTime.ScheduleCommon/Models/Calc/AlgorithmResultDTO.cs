using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AcaTime.ScheduleCommon.Models.Calc
{
    /// <summary>
    /// DTO для результату роботи алгоритму.
    /// </summary>
    public class AlgorithmResultDTO
    {
        /// <summary>
        /// Список розкладених уроків.
        /// </summary>
        public List<ScheduleSlotDTO> ScheduleSlots { get; set; } = new List<ScheduleSlotDTO>();

        /// <summary>
        /// Оцінка розкладу
        /// </summary>
        public int TotalEstimation { get; set; }

        /// <summary>
        /// Ім'я алгоритму
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Помилка
        /// </summary>
        public string? Error { get; set; }



    }


   
}