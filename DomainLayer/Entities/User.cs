using DomainLayer.Events;
using DomainLayer.Exceptions;
using DomainLayer.Interfaces;
using DomainLayer.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainLayer.Entities
{
    public sealed class User : BaseEntity, IAggregateRoot
    {
        public string Name { get; private set; } = string.Empty;
        public Email Email { get; private set; }

        private readonly List<UserRole> _roles = new();
        public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();


        public PhoneNumber? Phone { get; private set; }
        public Address? Address { get; private set; }
        public Money? Salary { get; private set; }
        public Percentage? BonusPercent { get; private set; }


        // One-to-many positions
        // Navigation: user can hold multiple capacities
        private readonly List<UserPosition> _positions = new();
        public IReadOnlyCollection<UserPosition> Positions => _positions.AsReadOnly();




        protected User() { } // EF/Dapper requirement

        public User(string name, Email email)
        {
            Name = name;
            Email = email;
            // Example domain event
            AddDomainEvent(new UserCreatedEvent(this));
        }

        public void Update(string name, Email email)
        {
            Name = name;
            Email = email;
        }

        public void AddRole(UserRole role)
        {
            // Domain rule: Only one primary role is allowed
            if (role.IsPrimary && _roles.Any(r => r.IsPrimary))
                throw new DomainException("User can have only one primary role.");

            _roles.Add(role);
        }

        //---new 
        public User(string name, Email email, PhoneNumber? phone, Address? address)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name is required.");
            if (string.IsNullOrWhiteSpace(email.Value))
                throw new ArgumentException("Email is required.");

            Name = name;
            Email = email;
            Phone = phone;
            Address = address;
        }


        // Add a position (capacity)
        public void AddPosition(UserPosition position)
        {
            if (position is null)
                throw new ArgumentNullException(nameof(position));

            _positions.Add(position);
        }


        // Replace all positions (used in Update)
        public void SetPositions(IEnumerable<UserPosition> positions)
        {
            _positions.Clear();
            _positions.AddRange(positions);
        }


        // Update primary user info
        public void Update(string name, Email email, PhoneNumber? phone, Address? address)
        {
            if (!string.IsNullOrWhiteSpace(name)) Name = name;
            if (!string.IsNullOrWhiteSpace(email.Value)) Email = email;
            Phone = phone;
            Address = address;
        }
    }
}
