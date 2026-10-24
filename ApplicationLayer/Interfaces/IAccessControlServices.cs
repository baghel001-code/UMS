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

    public sealed record IssuedToken(string Token, DateTime ExpiresAtUtc);

    /// <summary>Issues signed access tokens. Each token names its sign-in session (claim "sid") and application (claim "app").</summary>
    public interface ITokenIssuer
    {
        IssuedToken IssueAccessToken(User user, UserSession session, TimeSpan lifetime);
    }

    /// <summary>
    /// Is a sign-in session still usable? Called for every authenticated request (JWT "sid" claim), so implementations cache briefly;
    /// <see cref="Forget"/> drops the cached state when a session is ended so the change applies at once.
    /// </summary>
    public interface ISessionValidator
    {
        Task<SessionEndReason> CheckAsync(Guid sessionId, CancellationToken ct = default);
        void Forget(Guid sessionId);
    }

    public interface IPasswordHasher
    {
        string Hash(string password);
        bool Verify(string hashed, string provided);
    }
}
