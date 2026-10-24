using DomainLayer.Enumerations;
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




        // ---------------- UMS identity & access ----------------
        public Guid TenantId { get; private set; }
        public string EmpCode { get; private set; } = string.Empty;
        public string Department { get; private set; } = string.Empty;
        public string Designation { get; private set; } = string.Empty;
        /// <summary>Store / DC / office code, available to ABAC policies as user.location.</summary>
        public string Location { get; private set; } = string.Empty;
        public string Region { get; private set; } = string.Empty;
        public EmploymentType EmploymentType { get; private set; } = EmploymentType.Permanent;
        public UserStatus Status { get; private set; } = UserStatus.Active;
        public bool MfaEnabled { get; private set; }
        public DateTime? LastLogin { get; private set; }
        /// <summary>Contract / vendor accounts stop working after this date.</summary>
        public DateOnly? AccountExpiry { get; private set; }
        public string? PasswordHash { get; private set; }

        // UserAttributes table: free-form ABAC subject attributes (user.<key>)
        private readonly Dictionary<string, string> _attributes = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, string> Attributes => _attributes;

        private readonly List<RoleAssignment> _roleAssignments = new();
        public IReadOnlyList<RoleAssignment> RoleAssignments => _roleAssignments.AsReadOnly();

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
    
        // ---------------- UMS identity & access ----------------

        public User(Guid id, Guid tenantId, string empCode, string name, Email email, PhoneNumber? phone) : base(id)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new DomainException("Name is required.");
            if (string.IsNullOrWhiteSpace(empCode))
                throw new DomainException("Employee code is required.");

            TenantId = tenantId;
            EmpCode = empCode.Trim();
            Name = name.Trim();
            Email = email;
            Phone = phone;
            AddDomainEvent(new UserCreatedEvent(this));
        }

        public void UpdateProfile(string empCode, string name, Email email, PhoneNumber? phone, string department, string designation,
            string location, string region, EmploymentType employmentType, UserStatus status, bool mfaEnabled, DateOnly? accountExpiry)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new DomainException("Name is required.");
            if (string.IsNullOrWhiteSpace(empCode))
                throw new DomainException("Employee code is required.");

            EmpCode = empCode.Trim();
            Name = name.Trim();
            Email = email;
            Phone = phone;
            Department = department?.Trim() ?? string.Empty;
            Designation = designation?.Trim() ?? string.Empty;
            Location = location?.Trim().ToUpperInvariant() ?? string.Empty;
            Region = region?.Trim() ?? string.Empty;
            EmploymentType = employmentType;
            Status = status;
            MfaEnabled = mfaEnabled;
            AccountExpiry = accountExpiry;
        }

        public void SetStatus(UserStatus status) => Status = status;

        public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

        public void RecordLogin(DateTime at) => LastLogin = at;

        public void SetAttributes(IReadOnlyDictionary<string, string> attributes)
        {
            _attributes.Clear();
            foreach (var (key, value) in attributes)
            {
                if (string.IsNullOrWhiteSpace(key))
                    throw new DomainException("Attribute key cannot be empty.");
                _attributes[key.Trim()] = value?.Trim() ?? string.Empty;
            }
        }

        public void AssignRole(RoleAssignment assignment)
        {
            if (_roleAssignments.Any(a => a.RoleId == assignment.RoleId))
                throw new DomainException("The user already has this role.");
            if (assignment.ValidFrom is not null && assignment.ValidTo is not null && assignment.ValidTo < assignment.ValidFrom)
                throw new DomainException("Role validity end date is before its start date.");

            _roleAssignments.Add(assignment);
        }

        public void RevokeRole(Guid roleId) => _roleAssignments.RemoveAll(a => a.RoleId == roleId);

        // Replace all role assignments (used by the admin "save user" flow)
        public void SetRoleAssignments(IEnumerable<RoleAssignment> assignments)
        {
            var list = assignments.ToList();
            _roleAssignments.Clear();
            foreach (var a in list)
                AssignRole(a);
        }

        public bool IsUsableOn(DateOnly day) =>
            Status == UserStatus.Active && (AccountExpiry is null || day <= AccountExpiry);
    }
}
