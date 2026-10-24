using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainLayer.ValueObjects
{
    public sealed class Address : ValueObject
    {
        public string Street { get; }
        public string City { get; }
        public string State { get; }
        public string PostalCode { get; }


        private Address(string street, string city, string state, string postalCode)
        {
            Street = street;
            City = city;
            State = state;
            PostalCode = postalCode;
        }


        public static Address Create(string street, string city, string state, string postalCode)
        {
            if (string.IsNullOrWhiteSpace(street)) throw new ArgumentException("Street is required");
            if (string.IsNullOrWhiteSpace(city)) throw new ArgumentException("City is required");
            if (string.IsNullOrWhiteSpace(state)) throw new ArgumentException("State is required");
            if (string.IsNullOrWhiteSpace(postalCode)) throw new ArgumentException("Postal code is required");


            return new Address(street.Trim(), city.Trim(), state.Trim(), postalCode.Trim());
        }


        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Street;
            yield return City;
            yield return State;
            yield return PostalCode;
        }


        public override string ToString() => $"{Street}, {City}, {State}, {PostalCode}";
    }
}
