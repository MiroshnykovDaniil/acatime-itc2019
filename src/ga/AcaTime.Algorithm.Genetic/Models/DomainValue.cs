namespace AcaTime.Algorithm.Genetic.Models
{
    using System;

    public class DomainValue : IComparable<DomainValue>
    {
        /// <summary>Дата проведення уроку.</summary>
        public DateTime Date { get; set; }

        /// <summary>Номер пари в розкладі.</summary>
        public int PairNumber { get; set; }

        /// <summary>
        /// Порівнює поточний DomainValue з іншим за датою (без урахування часу) та номером пари.
        /// </summary>
        /// <param name="other">Об'єкт для порівняння.</param>
        /// <returns>Менше 0, якщо поточний об'єкт менший; 0, якщо рівний; більше 0, якщо більший.</returns>
        public int CompareTo(DomainValue other)
        {
            if (other == null) return 1;
            int dateComparison = Date.Date.CompareTo(other.Date.Date);
            if (dateComparison != 0)
                return dateComparison;
            return PairNumber.CompareTo(other.PairNumber);
        }

        // Перевантаження операторів порівняння
        public static bool operator <(DomainValue left, DomainValue right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator >(DomainValue left, DomainValue right)
        {
            return left.CompareTo(right) > 0;
        }

        public static bool operator <=(DomainValue left, DomainValue right)
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >=(DomainValue left, DomainValue right)
        {
            return left.CompareTo(right) >= 0;
        }

        /// <summary>
        /// Перевизначення методу Equals. Спочатку перевіряється, чи це один і той же об'єкт (оптимізація),
        /// а потім порівнюються значення дати (без урахування часу) та номер пари.
        /// </summary>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
                return true;
            if (obj is DomainValue other)
            {
                return Date.Date.Equals(other.Date.Date) && PairNumber == other.PairNumber;
            }
            return false;
        }

        /// <summary>
        /// Перевизначення GetHashCode. Використовуємо комбінацію хеш-кодів для Date.Date та PairNumber.
        /// </summary>
        public override int GetHashCode()
        {
            unchecked // для запобігання переповненню
            {
                int hash = 17;
                hash = hash * 23 + Date.Date.GetHashCode();
                hash = hash * 23 + PairNumber.GetHashCode();
                return hash;
            }
        }
    }


}
