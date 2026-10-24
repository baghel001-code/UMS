using DomainLayer.Exceptions;
using DomainLayer.Interfaces;
using DomainLayer.ValueObjects;
using System.Text.RegularExpressions;

namespace DomainLayer.Entities
{
    /// <summary>A named bundle of permission keys (APP.MODULE.RESOURCE.ACTION, wildcards allowed), scoped to a tenant.</summary>
    public sealed class Role : BaseEntity, IAggregateRoot
    {
        private static readonly Regex CodePattern = new("^[A-Z][A-Z0-9_]*$", RegexOptions.Compiled);

        public Guid TenantId { get; private set; }
        public string Code { get; private set; } = string.Empty;
        public string Name { get; private set; } = string.Empty;
        public string Description { get; private set; } = string.Empty;
        /// <summary>Application the role belongs to, or null for a cross-application role.</summary>
        public Guid? AppId { get; private set; }
        public bool IsSystem { get; private set; }
        public bool IsActive { get; private set; }

        private readonly List<string> _permissions = new();
        public IReadOnlyList<string> Permissions => _permissions.AsReadOnly();

        private Role() { }

        public Role(Guid id, Guid tenantId, string code, string name, string description, Guid? appId,
            IEnumerable<string> permissions, bool isSystem, bool isActive) : base(id)
        {
            TenantId = tenantId;
            IsSystem = isSystem;
            Update(code, name, description, appId, permissions, isActive);
        }

        public void Update(string code, string name, string description, Guid? appId, IEnumerable<string> permissions, bool isActive)
        {
            code = (code ?? string.Empty).Trim().ToUpperInvariant();
            if (!CodePattern.IsMatch(code)) throw new DomainException("Role code must be UPPER_CASE letters, digits or _.");
            // system roles are referenced by name in policies and code; their code is fixed
            if (IsSystem && !string.IsNullOrEmpty(Code) && Code != code) throw new DomainException("The code of a system role cannot be changed.");
            if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Role name is required.");

            Code = code;
            Name = name.Trim();
            Description = description?.Trim() ?? string.Empty;
            AppId = appId;
            IsActive = isActive;
            _permissions.Clear();
            _permissions.AddRange(permissions.Select(p => PermissionKey.Create(p).Value).Distinct());
        }

        public bool Grants(string requestedKey) => _permissions.Any(p => PermissionKey.Matches(p, requestedKey));
    }
}
