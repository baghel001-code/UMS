using DomainLayer.Authorization;
using DomainLayer.Entities;
using DomainLayer.Enumerations;

namespace ApplicationLayer.Interfaces
{
    /// <summary>Tenant of the current request (X-Tenant-Id header or the tenant claim of the signed-in user).</summary>
    public interface ICurrentTenantService
    {
        Guid TenantId { get; }
    }

    /// <summary>Writes the audit trail: who changed what, when, from where, with before/after values.</summary>
    public interface IAuditService
    {
        Task LogAsync(string entity, string entityId, AuditAction action, string summary,
            object? oldValues = null, object? newValues = null, CancellationToken ct = default);
    }

    /// <summary>Loads everything the decision engine needs for one tenant.</summary>
    public interface IAccessContextProvider
    {
        Task<AccessContext> LoadAsync(Guid tenantId, CancellationToken ct = default);
    }

    /// <summary>Issues a signed access token for an authenticated user.</summary>
    public interface ITokenIssuer
    {
        string IssueToken(User user);
    }

    public interface IPasswordHasher
    {
        string Hash(string password);
        bool Verify(string hashed, string provided);
    }
}
