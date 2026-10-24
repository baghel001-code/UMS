using AMS.Application.Interfaces;
using AMS.Domain.Exceptions;
using AMS.Domain.Models;
using AMS.Domain.Security;
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMS.Infrastructure.Ums
{
    /// <summary>"Ums" settings shared by the AMS API and portal.</summary>
    public sealed class UmsOptions
    {
        public const string SectionName = "Ums";
        /// <summary>UMS Web API base URL, e.g. http://localhost:5125.</summary>
        public string BaseUrl { get; set; } = "";
        /// <summary>Application code registered in UMS.</summary>
        public string AppCode { get; set; } = Perm.App;
        /// <summary>Device trust reported to UMS policies (env.device): Managed | Unmanaged.</summary>
        public string Device { get; set; } = "Managed";
        public string Channel { get; set; } = "Web";
        /// <summary>JWT settings of UMS (the API validates UMS access tokens itself).</summary>
        public string JwtIssuer { get; set; } = "";
        public string JwtAudience { get; set; } = "";
        public string JwtSecret { get; set; } = "";
    }

    // Contract of the UMS endpoints used by AMS (UMS WebApi/Controllers/V1: AuthController, MeController).
    public sealed record UmsLoginResult(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime SessionExpiresAt, Guid SessionId,
        Guid UserId, Guid TenantId, string FullName, string Email, string EmpCode, string AppCode, int IdleTimeoutMinutes, bool EndedOtherSessions);
    public sealed record UmsSessionStatus(Guid SessionId, bool Active, string Reason, DateTime ExpiresAt, DateTime LastSeenAt, int IdleTimeoutMinutes);
    public sealed record UmsDecision(bool Allowed, string Reason);
    public sealed record UmsEnvironment(DateTime? DateTime, string? Ip, string? Network, string? Device, string? Channel);
    public sealed record UmsCheck(string App, string Module, string Resource, string Action, IReadOnlyDictionary<string, string>? ResourceAttributes, string? Route = null);
    public sealed record UmsDirectoryEntry(Guid Id, string EmpCode, string FullName, string Email, string Department, string Designation, string Location,
        string Region, string EmploymentType, string Status);

    /// <summary>UMS answered with an error. <see cref="Code"/> is UMS's machine-readable code (auth.locked, session.replaced…).</summary>
    public sealed class UmsCallException : Exception
    {
        public HttpStatusCode? Status { get; }
        public string? Code { get; }
        /// <summary>From the X-Session-End header: why the user's session ended (Replaced, Idle, RevokedByAdmin…).</summary>
        public string? SessionEnded { get; }

        public UmsCallException(HttpStatusCode? status, string message, string? code = null, string? sessionEnded = null, Exception? inner = null) : base(message, inner)
        {
            Status = status;
            Code = code;
            SessionEnded = sessionEnded;
        }

        public bool IsUnauthorized => Status == HttpStatusCode.Unauthorized;
    }

    /// <summary>Typed HTTP client for UMS. Calls about a user carry that user's own UMS token, so UMS also checks their session.</summary>
    public sealed class UmsClient
    {
        public const string SessionEndHeader = "X-Session-End";
        public const int MaxBatch = 2000;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        private readonly HttpClient _http;

        public UmsClient(HttpClient http) => _http = http;

        public Uri? BaseAddress => _http.BaseAddress;

        // ------------------------------------------------------------------ sign-in session (portal)

        public Task<UmsLoginResult> LoginAsync(string email, string password, string appCode, string? clientIp, string? userAgent, CancellationToken ct = default) =>
            SendAsync<UmsLoginResult>(Request(HttpMethod.Post, "api/v1/auth/login", null, new { email, password, appCode, clientIp, userAgent }), ct);

        public Task<UmsLoginResult> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
            SendAsync<UmsLoginResult>(Request(HttpMethod.Post, "api/v1/auth/refresh", null, new { refreshToken }), ct);

        public Task<UmsSessionStatus> SessionAsync(string accessToken, bool touch, CancellationToken ct = default) =>
            SendAsync<UmsSessionStatus>(Request(HttpMethod.Get, $"api/v1/auth/session?touch={(touch ? "true" : "false")}", accessToken, null), ct);

        public Task LogoutAsync(string accessToken, string reason, CancellationToken ct = default) =>
            SendAsync<object?>(Request(HttpMethod.Post, "api/v1/auth/logout", accessToken, new { reason }), ct);

        // ------------------------------------------------------------------ decisions & directory (API)

        public Task<UmsDecision> AuthorizeAsync(string token, UmsCheck check, UmsEnvironment env, CancellationToken ct = default) =>
            SendAsync<UmsDecision>(Request(HttpMethod.Post, "api/v1/me/authorize", token, new
            {
                check.App, check.Module, check.Resource, check.Action, check.ResourceAttributes, Env = env, Log = true, check.Route
            }), ct);

        public async Task<List<UmsDecision>> AuthorizeBatchAsync(string token, IReadOnlyList<UmsCheck> checks, UmsEnvironment env, CancellationToken ct = default)
        {
            var result = new List<UmsDecision>(checks.Count);
            for (var i = 0; i < checks.Count; i += MaxBatch)
            {
                var chunk = checks.Skip(i).Take(MaxBatch).ToList();
                result.AddRange(await SendAsync<List<UmsDecision>>(Request(HttpMethod.Post, "api/v1/me/authorize/batch", token, new { Checks = chunk, Env = env, Log = false }), ct));
            }
            return result;
        }

        public Task<List<UmsDirectoryEntry>> DirectoryAsync(string token, string? search, int take, CancellationToken ct = default) =>
            SendAsync<List<UmsDirectoryEntry>>(Request(HttpMethod.Get, $"api/v1/me/directory?search={Uri.EscapeDataString(search ?? "")}&take={take}", token, null), ct);

        // ------------------------------------------------------------------ plumbing

        private static HttpRequestMessage Request(HttpMethod method, string url, string? token, object? body)
        {
            var req = new HttpRequestMessage(method, url);
            if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) req.Content = JsonContent.Create(body, options: Json);
            return req;
        }

        private async Task<T> SendAsync<T>(HttpRequestMessage req, CancellationToken ct)
        {
            HttpResponseMessage res;
            try
            {
                res = await _http.SendAsync(req, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                throw new UmsCallException(null, $"UMS is not reachable at {_http.BaseAddress} ({ex.Message}).", inner: ex);
            }
            using (res)
            {
                if (res.IsSuccessStatusCode)
                {
                    if (res.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object)) return default!;
                    return (await res.Content.ReadFromJsonAsync<T>(Json, ct))!;
                }
                string message = $"UMS returned {(int)res.StatusCode} {res.ReasonPhrase}.";
                string? code = null;
                try
                {
                    var problem = await res.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
                    if (problem.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String) message = t.GetString()!;
                    if (problem.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String) message = d.GetString()!;
                    if (problem.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) code = c.GetString();
                }
                catch (Exception) { /* not a problem response */ }
                var ended = res.Headers.TryGetValues(SessionEndHeader, out var v) ? v.FirstOrDefault() : null;
                throw new UmsCallException(res.StatusCode, message, code, ended);
            }
        }
    }

    /// <summary>
    /// The AMS policy enforcement point: forwards every question to UMS with the caller's own token. Nothing is decided locally - roles,
    /// role scopes, office hours and ABAC rules are evaluated by UMS, so administrators change AMS access only in the UMS portal.
    /// </summary>
    public sealed class UmsAccessGuard : IAccessGuard
    {
        private readonly UmsClient _ums;
        private readonly ICurrentActor _current;
        private readonly UmsOptions _options;

        public UmsAccessGuard(UmsClient ums, ICurrentActor current, UmsOptions options)
        {
            _ums = ums;
            _current = current;
            _options = options;
        }

        /// <summary>Request context for env.* policy conditions (UMS takes the time itself, in the tenant's time zone).</summary>
        public UmsEnvironment Environment() => new(null, _current.ClientIp, NetworkOf(_current.ClientIp), _options.Device, _options.Channel);

        public static string NetworkOf(string? ip)
        {
            if (!IPAddress.TryParse(ip, out var a)) return "Internet";
            if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
            if (IPAddress.IsLoopback(a)) return "Office LAN";
            var b = a.GetAddressBytes();
            if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) ? "Office LAN" : "Internet";
            return a.IsIPv6SiteLocal || a.IsIPv6LinkLocal || (b[0] & 0xFE) == 0xFC ? "Office LAN" : "Internet";
        }

        private UmsCheck ToUms(AccessCheck c) =>
            new(_options.AppCode, c.Module, c.Resource, c.Action, c.Attributes, $"/ams/{c.Module}/{c.Resource}/{c.Action}".ToLowerInvariant());

        public async Task EnsureAsync(AccessCheck check, CancellationToken ct = default)
        {
            var decision = await Call(() => _ums.AuthorizeAsync(_current.AccessToken, ToUms(check), Environment(), ct));
            if (!decision.Allowed) throw new AccessDeniedException(check.Key, decision.Reason);
        }

        public async Task<IReadOnlyList<bool>> CanAsync(IReadOnlyList<AccessCheck> checks, CancellationToken ct = default)
        {
            if (checks.Count == 0) return Array.Empty<bool>();
            var decisions = await Call(() => _ums.AuthorizeBatchAsync(_current.AccessToken, checks.Select(ToUms).ToList(), Environment(), ct));
            return decisions.Select(d => d.Allowed).ToList();
        }

        /// <summary>Fail closed: an unreachable UMS or an ended session never results in access.</summary>
        internal static async Task<T> Call<T>(Func<Task<T>> call)
        {
            try
            {
                return await call();
            }
            catch (UmsCallException ex) when (ex.IsUnauthorized)
            {
                throw new SessionEndedException(ex.SessionEnded ?? "Expired", ex.Message);
            }
            catch (UmsCallException ex)
            {
                throw new AuthorizationUnavailableException($"Access could not be checked with UMS: {ex.Message}", ex);
            }
        }
    }

    /// <summary>Colleagues from the UMS directory (same tenant as the caller), cached briefly per search.</summary>
    public sealed class UmsPeopleDirectory : IPeopleDirectory
    {
        private readonly UmsClient _ums;
        private readonly ICurrentActor _current;
        private readonly IMemoryCache _cache;

        public UmsPeopleDirectory(UmsClient ums, ICurrentActor current, IMemoryCache cache)
        {
            _ums = ums;
            _current = current;
            _cache = cache;
        }

        private static Person Map(UmsDirectoryEntry e) =>
            new(e.EmpCode, e.FullName, e.Email, e.Department, e.Designation, e.Location, e.Region, e.EmploymentType, e.Status);

        public async Task<IReadOnlyList<Person>> SearchAsync(string? search, int take = 30, CancellationToken ct = default)
        {
            var key = $"dir:{_current.Actor.TenantId}:{search?.Trim().ToLowerInvariant()}:{take}";
            if (_cache.TryGetValue(key, out IReadOnlyList<Person>? hit) && hit != null) return hit;
            var people = (await UmsAccessGuard.Call(() => _ums.DirectoryAsync(_current.AccessToken, search, take, ct))).Select(Map).ToList();
            _cache.Set(key, (IReadOnlyList<Person>)people, TimeSpan.FromMinutes(2));
            return people;
        }

        public async Task<Person?> FindAsync(string empCode, CancellationToken ct = default)
        {
            var code = empCode.Trim();
            return (await SearchAsync(code, 20, ct)).FirstOrDefault(x => string.Equals(x.EmpCode, code, StringComparison.OrdinalIgnoreCase));
        }
    }
}
