using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DomainLayer.ValueObjects
{
    public sealed class PhoneNumber : ValueObject
    {
        public string CountryCode { get; }
        public string Number { get; }


        private PhoneNumber(string countryCode, string number)
        {
            CountryCode = countryCode;
            Number = number;
        }


        public static PhoneNumber Create(string countryCode, string number)
        {
            if (string.IsNullOrWhiteSpace(countryCode))
                throw new ArgumentException("Country code is required.");


            if (string.IsNullOrWhiteSpace(number))
                throw new ArgumentException("Phone number is required.");


            if (!Regex.IsMatch(number, "^[0-9]{6,15}$"))
                throw new ArgumentException("Phone number must contain only digits (6-15 chars).");


            return new PhoneNumber(countryCode.Trim(), number.Trim());
        }


        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return CountryCode;
            yield return Number;
        }


        public override string ToString() => $"{CountryCode}-{Number}";
    }
}
