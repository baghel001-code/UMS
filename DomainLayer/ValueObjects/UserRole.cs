using DomainLayer.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainLayer.ValueObjects
{
    public sealed class UserRole : ValueObject
    {
        public string Name { get; }
        public bool IsPrimary { get; }

        public UserRole(string name, bool isPrimary)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new DomainException("Role name cannot be empty.");

            Name = name;
            IsPrimary = isPrimary;
        }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Name;
            yield return IsPrimary;
        }
    }

}
