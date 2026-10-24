using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainLayer.ValueObjects
{
    public sealed class Percentage : ValueObject
    {
        public decimal Value { get; }


        private Percentage(decimal value)
        {
            Value = value;
        }


        public static Percentage Create(decimal value)
        {
            if (value < 0 || value > 100)
                throw new ArgumentException("Percentage must be between 0 and 100.");


            return new Percentage(value);
        }


        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }


        public override string ToString() => $"{Value}%";
    }
}
