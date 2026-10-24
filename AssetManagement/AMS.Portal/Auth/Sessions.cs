using AMS.Application.Interfaces;
using AMS.Infrastructure.Ums;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;

namespace AMS.Portal.Auth
{
    /// <summary>"Auth" section of the portal settings. Idle timeout and single-device login come from UMS (per tenant), not from here.</summary>
    public sealed class AuthSettings
    {
        public const string SectionName = "Auth";
        /// <summary>How often a signed-in browser re-checks its UMS session (catches "signed in on another device", revoked sessions).</summary>
        public int RevalidationSeconds { get; set; } = 60;
        /// <summary>Sliding lifetime of the portal cookie; kept alive by the keep-alive while the user is active.</summary>
        public int CookieLifetimeMinutes { get; set; } = 60;
        /// <summary>Hard limit after sign-in, even for an active user.</summary>
        public int AbsoluteLifetimeHours { get; set; } = 12;
    }

    public static class AuthConstants
    {
        public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
        public const string LoginPath = "/account/login";
        public const string LogoutPagePath = "/account/logout";
        public const string AccessDeniedPath = "/account/access-denied";
        // the only places the cookie is written or cleared (real HTTP requests; an interactive page talks over a WebSocket)
        public const string CompleteLoginEndpoint = "/account/complete-login";
        public const string LogoutEndpoint = "/account/sign-out";
        public const string AntiforgeryTokenEndpoint = "/account/antiforgery-token";
        public const string KeepAliveEndpoint = "/account/keepalive";
        public const string EndReasonItem = "ams.session.end";
    }

    public static class AuthClaimTypes
    {
        public const string SessionId = "ums_sid";         // the UMS sign-in session; tokens are kept server-side under it
        public const string TenantId = "tenant_id";
        public const string EmpCode = "emp_code";
        public const string IdleMinutes = "idle_min";      // the tenant's idle timeout from UMS security settings
        public const string AuthTime = "auth_time";
    }

    /// <summary>The signed-in user as stored in the portal cookie.</summary>
    public sealed record PortalUser(Guid SessionId, Guid UserId, Guid TenantId, string EmpCode, string Name, string Email, int IdleMinutes, DateTimeOffset SignedInAt)
    {
        public static PortalUser? From(ClaimsPrincipal? p)
        {
            if (p?.Identity?.IsAuthenticated != true) return null;
            if (!Guid.TryParse(p.FindFirstValue(AuthClaimTypes.SessionId), out var sid) || !Guid.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                || !Guid.TryParse(p.FindFirstValue(AuthClaimTypes.TenantId), out var tid)) return null;
            int.TryParse(p.FindFirstValue(AuthClaimTypes.IdleMinutes), out var idle);
            long.TryParse(p.FindFirstValue(AuthClaimTypes.AuthTime), NumberStyles.None, CultureInfo.InvariantCulture, out var at);
            return new PortalUser(sid, uid, tid, p.FindFirstValue(AuthClaimTypes.EmpCode) ?? "", p.FindFirstValue(ClaimTypes.Name) ?? "",
                p.FindFirstValue(ClaimTypes.Email) ?? "", idle > 0 ? idle : 30, DateTimeOffset.FromUnixTimeSeconds(at));
        }

        public ClaimsPrincipal ToPrincipal() => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()), new Claim(ClaimTypes.Name, Name), new Claim(ClaimTypes.Email, Email),
            new Claim(AuthClaimTypes.SessionId, SessionId.ToString()), new Claim(AuthClaimTypes.TenantId, TenantId.ToString()),
            new Claim(AuthClaimTypes.EmpCode, EmpCode), new Claim(AuthClaimTypes.IdleMinutes, IdleMinutes.ToString(CultureInfo.InvariantCulture)),
            new Claim(AuthClaimTypes.AuthTime, SignedInAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        }, AuthConstants.Scheme, ClaimTypes.Name, ClaimTypes.Role));
    }

    /// <summary>Messages for the sign-in page (?reason=…), matching UMS session end reasons.</summary>
    public static class SessionEndReasons
    {
        public static string? Message(string? reason) => reason?.ToLowerInvariant() switch
        {
            "replaced" => "You signed in on another device, so you were signed out here.",
            "idle" => "You were signed out after a period of inactivity.",
            "revokedbyadmin" => "An administrator ended your session.",
            "tokenreuse" => "Your session was ended for security reasons. Please sign in again.",
            "accountdisabled" => "Your account is no longer active.",
            "expired" => "Your session has expired. Please sign in again.",
            "signedout" => "You have signed out.",
            _ => null
        };
    }

    /// <summary>
    /// UMS tokens of every signed-in browser session, kept on the server (never in the browser). The access token is renewed with the
    /// rotating refresh token shortly before it expires - one refresh at a time per session, since presenting an old refresh token makes
    /// UMS end the session. In memory: after a portal restart users sign in again (behind a load balancer use sticky sessions).
    /// </summary>
    public sealed class UmsTokenStore
    {
        private sealed class Entry
        {
            public string AccessToken = "";
            public DateTime AccessExpiresUtc;
            public string RefreshToken = "";
            public readonly SemaphoreSlim Gate = new(1, 1);
        }

        private static readonly TimeSpan RenewBefore = TimeSpan.FromSeconds(60);
        private readonly ConcurrentDictionary<Guid, Entry> _tokens = new();
        private readonly UmsClient _ums;
        private readonly EndedSessions _ended;
        private readonly TimeProvider _time;
        private readonly ILogger<UmsTokenStore> _logger;

        public UmsTokenStore(UmsClient ums, EndedSessions ended, TimeProvider time, ILogger<UmsTokenStore> logger)
        {
            _ums = ums;
            _ended = ended;
            _time = time;
            _logger = logger;
        }

        public void Save(UmsLoginResult r) => _tokens[r.SessionId] = new Entry { AccessToken = r.AccessToken, AccessExpiresUtc = r.AccessTokenExpiresAt, RefreshToken = r.RefreshToken };

        public bool Has(Guid sessionId) => _tokens.ContainsKey(sessionId);

        public void Remove(Guid sessionId) => _tokens.TryRemove(sessionId, out _);

        /// <summary>A usable access token, refreshed when needed; null when the session is gone (the reason is recorded in <see cref="EndedSessions"/>).</summary>
        public async Task<string?> GetAccessTokenAsync(Guid sessionId, CancellationToken ct = default)
        {
            if (!_tokens.TryGetValue(sessionId, out var e)) return null;
            if (e.AccessExpiresUtc - _time.GetUtcNow().UtcDateTime > RenewBefore) return e.AccessToken;
            await e.Gate.WaitAsync(ct);
            try
            {
                if (e.AccessExpiresUtc - _time.GetUtcNow().UtcDateTime > RenewBefore) return e.AccessToken;   // refreshed meanwhile
                try
                {
                    var r = await _ums.RefreshAsync(e.RefreshToken, ct);
                    e.AccessToken = r.AccessToken;
                    e.AccessExpiresUtc = r.AccessTokenExpiresAt;
                    e.RefreshToken = r.RefreshToken;
                    return e.AccessToken;
                }
                catch (UmsCallException ex) when (ex.IsUnauthorized)
                {
                    var reason = ex.Code?.StartsWith("session.") == true ? ex.Code["session.".Length..] : "expired";
                    _logger.LogInformation("UMS ended session {Session}: {Reason}", sessionId, reason);
                    _ended.Add(sessionId, reason);
                    Remove(sessionId);
                    return null;
                }
            }
            finally
            {
                e.Gate.Release();
            }
        }
    }

    /// <summary>
    /// Sessions found ended (signed in elsewhere, idle, revoked…). The cookie of such a session is refused on its very next request, not
    /// only at the next revalidation. In memory, per server.
    /// </summary>
    public sealed class EndedSessions
    {
        private static readonly TimeSpan Keep = TimeSpan.FromHours(13);
        private readonly ConcurrentDictionary<Guid, (string Reason, DateTimeOffset Until)> _ended = new();
        private readonly TimeProvider _time;
        public EndedSessions(TimeProvider time) => _time = time;

        public void Add(Guid sessionId, string reason)
        {
            var now = _time.GetUtcNow();
            foreach (var (k, v) in _ended) if (v.Until <= now) _ended.TryRemove(k, out _);
            _ended[sessionId] = (reason, now + Keep);
        }

        public bool TryGet(Guid sessionId, out string reason)
        {
            reason = "";
            if (!_ended.TryGetValue(sessionId, out var e) || e.Until <= _time.GetUtcNow()) return false;
            reason = e.Reason;
            return true;
        }
    }

    /// <summary>One-time notice for the first page after sign-in ("you were signed out on your other device"), keyed by session.</summary>
    public sealed class LoginNotices
    {
        private readonly ConcurrentDictionary<Guid, DateTimeOffset> _notices = new();
        private readonly TimeProvider _time;
        public LoginNotices(TimeProvider time) => _time = time;
        public void Add(Guid sessionId) => _notices[sessionId] = _time.GetUtcNow().AddMinutes(2);
        public bool Take(Guid sessionId) => _notices.TryRemove(sessionId, out var until) && until > _time.GetUtcNow();
    }

    public sealed record LoginTicket(PortalUser User, bool EndedOtherSessions, DateTimeOffset ExpiresUtc);

    /// <summary>
    /// Hands a verified sign-in from the Blazor circuit (which cannot set cookies) to the /account/complete-login HTTP endpoint (which can).
    /// Tickets are random, single-use and expire after 60 seconds.
    /// </summary>
    public sealed class LoginTicketStore
    {
        private readonly ConcurrentDictionary<string, LoginTicket> _tickets = new();
        private readonly TimeProvider _time;
        public LoginTicketStore(TimeProvider time) => _time = time;

        public string Issue(PortalUser user, bool endedOthers)
        {
            var now = _time.GetUtcNow();
            foreach (var (k, t) in _tickets) if (t.ExpiresUtc <= now) _tickets.TryRemove(k, out _);
            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _tickets[ticket] = new LoginTicket(user, endedOthers, now.AddSeconds(60));
            return ticket;
        }

        public LoginTicket? Redeem(string? ticket) =>
            !string.IsNullOrEmpty(ticket) && ticket.Length == 64 && _tickets.TryRemove(ticket, out var t) && t.ExpiresUtc > _time.GetUtcNow() ? t : null;
    }

    /// <summary>
    /// Is this portal session still good? Shared by the cookie handler (HTTP requests) and the circuit revalidator (open pages).
    /// Returns null when it is, otherwise the reason (replaced, idle, expired…).
    /// </summary>
    public sealed class SessionChecker
    {
        private readonly UmsTokenStore _tokens;
        private readonly EndedSessions _ended;
        private readonly UmsClient _ums;
        private readonly AuthSettings _settings;
        private readonly TimeProvider _time;

        public SessionChecker(UmsTokenStore tokens, EndedSessions ended, UmsClient ums, Microsoft.Extensions.Options.IOptions<AuthSettings> settings, TimeProvider time)
        {
            _tokens = tokens;
            _ended = ended;
            _ums = ums;
            _settings = settings.Value;
            _time = time;
        }

        public string? QuickCheck(PortalUser? user)
        {
            if (user == null) return "expired";
            if (_time.GetUtcNow() - user.SignedInAt > TimeSpan.FromHours(_settings.AbsoluteLifetimeHours)) return "expired";
            if (_ended.TryGet(user.SessionId, out var reason)) return reason;
            return _tokens.Has(user.SessionId) ? null : "expired";
        }

        /// <param name="touch">Record user activity at UMS (keep-alive).</param>
        public async Task<string?> CheckWithUmsAsync(PortalUser? user, bool touch, CancellationToken ct)
        {
            if (QuickCheck(user) is { } quick) return quick;
            var token = await _tokens.GetAccessTokenAsync(user!.SessionId, ct);
            if (token == null) return _ended.TryGet(user.SessionId, out var r) ? r : "expired";
            try
            {
                var status = await _ums.SessionAsync(token, touch, ct);
                if (status.Active) return null;
                _ended.Add(user.SessionId, status.Reason.ToLowerInvariant());
                return status.Reason.ToLowerInvariant();
            }
            catch (UmsCallException ex) when (ex.IsUnauthorized)
            {
                var reason = (ex.SessionEnded ?? "expired").ToLowerInvariant();
                _ended.Add(user.SessionId, reason);
                return reason;
            }
            // UMS unreachable: keep the user signed in until the next check (callers count consecutive failures)
        }
    }

    /// <summary>Per circuit: when the user last interacted with the page (idle timeout).</summary>
    public sealed class UserActivityState
    {
        private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
        public TimeSpan IdleFor => DateTimeOffset.UtcNow - new DateTimeOffset(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Touch() => Interlocked.Exchange(ref _ticks, DateTimeOffset.UtcNow.UtcTicks);
    }

    /// <summary>Per circuit: why the session ended (set by the revalidator, read by the layout that posts the sign-out).</summary>
    public sealed class SessionEndState
    {
        public string? Reason { get; set; }
    }

    /// <summary>The browser's address and user agent, captured on the first HTTP request of the circuit.</summary>
    public sealed class ClientInfo
    {
        public string? Ip { get; set; }
        public string? UserAgent { get; set; }
    }

    /// <summary>Gives API calls made from a page the signed-in user's (fresh) UMS access token.</summary>
    public sealed class CircuitTokenProvider : IAccessTokenProvider
    {
        private readonly AuthenticationStateProvider _auth;
        private readonly UmsTokenStore _tokens;
        private readonly EndedSessions _ended;
        private readonly ClientInfo _client;
        private readonly SessionEndState _endState;

        public CircuitTokenProvider(AuthenticationStateProvider auth, UmsTokenStore tokens, EndedSessions ended, ClientInfo client, SessionEndState endState)
        {
            _auth = auth;
            _tokens = tokens;
            _ended = ended;
            _client = client;
            _endState = endState;
        }

        public string? ClientIp => _client.Ip;

        public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
        {
            var user = PortalUser.From((await _auth.GetAuthenticationStateAsync()).User);
            return user == null ? null : await _tokens.GetAccessTokenAsync(user.SessionId, ct);
        }

        public void SessionEnded(string reason)
        {
            _endState.Reason ??= reason.ToLowerInvariant();
            if (_auth is AppRevalidatingAuthStateProvider p && p.CurrentUser is { } u) _ended.Add(u.SessionId, reason.ToLowerInvariant());
        }
    }
}
