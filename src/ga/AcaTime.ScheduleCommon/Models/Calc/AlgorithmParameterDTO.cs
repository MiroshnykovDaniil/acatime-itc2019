using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AcaTime.ScheduleCommon.Models.Calc
{
    /// <summary>
    /// DTO для опису параметра алгоритму розкладу
    /// </summary>
    public class AlgorithmParameterDTO
    {
        /// <summary>
        /// Назва параметра
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Опис параметра
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Тип даних параметра
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AlgorithmParameterType DataType { get; set; }

        /// <summary>
        /// Значення за замовчуванням
        /// </summary>
        public string DefaultValue { get; set; }

        /// <summary>
        /// Чи є параметр обов'язковим
        /// </summary>
        public bool IsRequired { get; set; } = false;

        /// <summary>
        /// Можливі значення для параметрів типу "Список"
        /// </summary>
        public List<string> PossibleValues { get; set; } = new List<string>();
    }

    /// <summary>
    /// Типи даних для параметрів алгоритму
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AlgorithmParameterType
    {
        /// <summary>
        /// Рядок
        /// </summary>
        String,

        /// <summary>
        /// Ціле число
        /// </summary>
        Integer,

        /// <summary>
        /// Дробове число
        /// </summary>
        Decimal,

        /// <summary>
        /// Логічне значення
        /// </summary>
        Boolean,

        /// <summary>
        /// Список значень
        /// </summary>
        List
    }
} 