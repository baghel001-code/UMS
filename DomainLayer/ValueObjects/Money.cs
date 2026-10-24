using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainLayer.ValueObjects
{
    public sealed class Money : ValueObject
    {
        public decimal Amount { get; }
        public string Currency { get; }


        private Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }


        public static Money Create(decimal amount, string currency)
        {
            if (amount < 0)
                throw new ArgumentException("Amount cannot be negative.");


            if (string.IsNullOrWhiteSpace(currency))
                throw new ArgumentException("Currency is required.");


            return new Money(amount, currency.Trim().ToUpper());
        }


        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }


        public override string ToString() => $"{Currency} {Amount:N2}";
    }
}
