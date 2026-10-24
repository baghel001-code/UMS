using AMS.Domain.Models;
using AMS.Domain.Security;

namespace AMS.Application.Interfaces
{
    /// <summary>
    /// Policy enforcement point. Every use case calls <see cref="EnsureAsync"/> before it reads or changes data; the decision is
    /// always made by UMS (roles, role scopes, office hours and ABAC policies managed in the UMS admin portal).
    /// </summary>
    public interface IAccessGuard
    {
        /// <summary>Asks UMS; throws AccessDeniedException with UMS's reason when denied. Logged in the UMS user action trail.</summary>
        Task EnsureAsync(AccessCheck check, CancellationToken ct = default);

        /// <summary>Many questions in one call (list filtering, which actions to offer). Not logged.</summary>
        Task<IReadOnlyList<bool>> CanAsync(IReadOnlyList<AccessCheck> checks, CancellationToken ct = default);
    }

    /// <summary>The signed-in user of the current API request (from the UMS access token).</summary>
    public interface ICurrentActor
    {
        Actor Actor { get; }
        /// <summary>The caller's UMS access token, forwarded to UMS so decisions are made for this user and session.</summary>
        string AccessToken { get; }
        /// <summary>Browser address reported by the portal (UMS env.ip / env.network).</summary>
        string? ClientIp { get; }
    }

    /// <summary>Colleagues from the UMS directory (people to check items out to).</summary>
    public interface IPeopleDirectory
    {
        Task<IReadOnlyList<Person>> SearchAsync(string? search, int take = 30, CancellationToken ct = default);
        Task<Person?> FindAsync(string empCode, CancellationToken ct = default);
    }

    /// <summary>Business wall-clock time (IST) used for dates such as "checked out at" and for due/expiry checks.</summary>
    public interface IBusinessClock
    {
        DateTime Now { get; }
        DateTime Today => Now.Date;
    }
}
