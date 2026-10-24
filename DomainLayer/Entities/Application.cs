using DomainLayer.Enumerations;
using DomainLayer.Exceptions;
using DomainLayer.Interfaces;
using System.Text.RegularExpressions;

namespace DomainLayer.Entities
{
    /// <summary>
    /// An in-house or third-party application protected by UMS, with its Module → Resource → Action catalogue.
    /// Every checked action becomes a grantable permission APP.MODULE.RESOURCE.ACTION.
    /// </summary>
    public sealed class Application : BaseEntity, IAggregateRoot
    {
        private static readonly Regex CodePattern = new("^[A-Z][A-Z0-9_-]*$", RegexOptions.Compiled);

        public string Code { get; private set; } = string.Empty;
        public string Name { get; private set; } = string.Empty;
        public AppType Type { get; private set; }
        public string Vendor { get; private set; } = string.Empty;
        public string OwnerDept { get; private set; } = string.Empty;
        public string Description { get; private set; } = string.Empty;
        public bool IsActive { get; private set; }

        private readonly List<Guid> _tenantIds = new();
        public IReadOnlyList<Guid> TenantIds => _tenantIds.AsReadOnly();

        private readonly List<AppModule> _modules = new();
        public IReadOnlyList<AppModule> Modules => _modules.AsReadOnly();

        private Application() { }

        public Application(Guid id, string code, string name, AppType type, string vendor, string ownerDept, string description,
            bool isActive, IEnumerable<Guid> tenantIds, IEnumerable<AppModule> modules) : base(id)
        {
            Update(code, name, type, vendor, ownerDept, description, isActive, tenantIds, modules);
        }

        public void Update(string code, string name, AppType type, string vendor, string ownerDept, string description,
            bool isActive, IEnumerable<Guid> tenantIds, IEnumerable<AppModule> modules)
        {
            code = (code ?? string.Empty).Trim().ToUpperInvariant();
            if (!CodePattern.IsMatch(code)) throw new DomainException("Application code must be UPPER_CASE letters, digits, _ or -.");
            if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Application name is required.");

            var moduleList = modules.ToList();
            var duplicate = moduleList.GroupBy(m => m.Code).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null) throw new DomainException($"Module code {duplicate.Key} is used twice in {code}.");
            foreach (var m in moduleList)
            {
                if (!CodePattern.IsMatch(m.Code)) throw new DomainException($"Module code '{m.Code}' must be UPPER_CASE.");
                var dupRes = m.Resources.GroupBy(r => r.Code).FirstOrDefault(g => g.Count() > 1);
                if (dupRes != null) throw new DomainException($"Resource code {dupRes.Key} is used twice in module {m.Code}.");
                foreach (var r in m.Resources)
                    if (!CodePattern.IsMatch(r.Code)) throw new DomainException($"Resource code '{r.Code}' must be UPPER_CASE.");
            }

            Code = code;
            Name = name.Trim();
            Type = type;
            Vendor = vendor?.Trim() ?? string.Empty;
            OwnerDept = ownerDept?.Trim() ?? string.Empty;
            Description = description?.Trim() ?? string.Empty;
            IsActive = isActive;
            _tenantIds.Clear();
            _tenantIds.AddRange(tenantIds.Distinct());
            _modules.Clear();
            _modules.AddRange(moduleList);
        }

        public bool IsEnabledFor(Guid tenantId) => _tenantIds.Contains(tenantId);
    }

    public sealed record AppModule(Guid Id, string Code, string Name, IReadOnlyList<AppResource> Resources);

    public sealed record AppResource(Guid Id, string Code, string Name, IReadOnlyList<AccessAction> Actions);
}
