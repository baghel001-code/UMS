using DomainLayer.Exceptions;
using DomainLayer.Interfaces;

namespace DomainLayer.Entities
{
    /// <summary>An isolated business unit or partner network. Every other access-control record carries a TenantId.</summary>
    public sealed class Tenant : BaseEntity, IAggregateRoot
    {
        public string Code { get; private set; } = string.Empty;
        public string Name { get; private set; } = string.Empty;
        public bool IsActive { get; private set; }
        public DateOnly CreatedOn { get; private set; }

        private Tenant() { }

        public Tenant(Guid id, string code, string name, bool isActive, DateOnly createdOn) : base(id)
        {
            CreatedOn = createdOn;
            Update(code, name, isActive);
        }

        public void Update(string code, string name, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Tenant code is required.");
            if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Tenant name is required.");
            Code = code.Trim().ToUpperInvariant();
            Name = name.Trim();
            IsActive = isActive;
        }
    }
}
