using AMS.Core.Security;
using AMS.Core.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMS.Infrastructure.Ums
{
    // Contract of the UMS Web API endpoints AMS uses (WebApi/Controllers/V1: AuthController, MeController).
    public sealed record UmsLoginResult(string Token, Guid UserId, Guid TenantId, string FullName, string Email, string EmpCode);
    public sealed record UmsDecisionStep(string Layer, string Status, string Detail);
    public sealed record UmsDecision(bool Allowed, string Reason, List<UmsDecisionStep>? Steps);
    public sealed record UmsEnvironment(DateTime? DateTime, string? Ip, string? Network, string? Device, string? Channel);
    public sealed record UmsCheck(string App, string Module, string Resource, string Action, IReadOnlyDictionary<string, string>? ResourceAttributes, string? Route = null);
    public sealed record UmsDirectoryEntry(Guid Id, string EmpCode, string FullName, string Email, string Department, string Designation, string Location,
        string Region, string EmploymentType, string Status);

    /// <summary>UMS rejected or could not answer the call (401 = the user's UMS session ended).</summary>
    public class UmsException : Exception
    {
        public HttpStatusCode? Status { get; }
        public UmsException(HttpStatusCode? status, string message, Exception? inner = null) : base(message, inner) => Status = status;
        public bool IsUnauthorized => Status == HttpStatusCode.Unauthorized;
    }

    /// <summary>Typed HTTP client for UMS. Every call except login carries the signed-in user's own UMS token.</summary>
    public class UmsClient
    {
        public const string AppCode = "AMS";
        public const int MaxBatch = 2000;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        private readonly HttpClient _http;

        public UmsClient(HttpClient http) => _http = http;

        public Uri? BaseAddress => _http.BaseAddress;

        public async Task<UmsLoginResult> LoginAsync(string email, string password, CancellationToken ct = default) =>
            await SendAsync<UmsLoginResult>(new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/login") { Content = JsonContent.Create(new { email, password }, options: Json) }, ct);

        public async Task<UmsDecision> AuthorizeAsync(string token, UmsCheck check, UmsEnvironment env, CancellationToken ct = default) =>
            await SendAsync<UmsDecision>(Authed(HttpMethod.Post, "api/v1/me/authorize", token, new
            {
                check.App, check.Module, check.Resource, check.Action, check.ResourceAttributes, Env = env, Log = true, check.Route
            }), ct);

        public async Task<List<UmsDecision>> AuthorizeBatchAsync(string token, IReadOnlyList<UmsCheck> checks, UmsEnvironment env, CancellationToken ct = default)
        {
            var result = new List<UmsDecision>(checks.Count);
            for (var i = 0; i < checks.Count; i += MaxBatch)
            {
                var chunk = checks.Skip(i).Take(MaxBatch).ToList();
                result.AddRange(await SendAsync<List<UmsDecision>>(Authed(HttpMethod.Post, "api/v1/me/authorize/batch", token, new { Checks = chunk, Env = env, Log = false }), ct));
            }
            return result;
        }

        public async Task<List<UmsDirectoryEntry>> DirectoryAsync(string token, string? search, int take, CancellationToken ct = default) =>
            await SendAsync<List<UmsDirectoryEntry>>(Authed(HttpMethod.Get, $"api/v1/me/directory?search={Uri.EscapeDataString(search ?? "")}&take={take}", token, null), ct);

        private static HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body)
        {
            var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
            catch (HttpRequestException ex)
            {
                throw new UmsException(null, $"UMS is not reachable at {_http.BaseAddress} ({ex.Message}).", ex);
            }
            using (res)
            {
                if (res.IsSuccessStatusCode) return (await res.Content.ReadFromJsonAsync<T>(Json, ct))!;
                string message = $"UMS returned {(int)res.StatusCode} {res.ReasonPhrase}.";
                try
                {
                    var problem = await res.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
                    if (problem.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String) message = d.GetString()!;
                    else if (problem.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String) message = t.GetString()!;
                }
                catch (JsonException) { }
                if (res.StatusCode == HttpStatusCode.Unauthorized && req.RequestUri?.ToString().Contains("auth/login") != true)
                    message = "Your UMS session has ended. Please sign in again.";
                throw new UmsException(res.StatusCode, message);
            }
        }
    }

    /// <summary>The UMS sign-in of the current browser session (one per Blazor circuit).</summary>
    public class UmsSession
    {
        public string? Token { get; private set; }
        public Actor? Actor { get; private set; }
        public string ClientIp { get; set; } = "";
        public bool IsSignedIn => Token != null && Actor != null;

        public void SignIn(string token, Actor actor)
        {
            Token = token;
            Actor = actor;
        }

        public void SignOut()
        {
            Token = null;
            Actor = null;
        }
    }

    public class UmsOptions
    {
        /// <summary>UMS Web API base URL, e.g. http://localhost:5125.</summary>
        public string BaseUrl { get; set; } = "";
        /// <summary>Device trust reported to UMS policies (env.device): Managed | Unmanaged. AMS is a web app, so this is a deployment setting.</summary>
        public string Device { get; set; } = "Managed";
        public string Channel { get; set; } = "Web";
    }

    /// <summary>
    /// The AMS policy enforcement point: forwards every question to UMS with the user's token. Nothing is decided locally —
    /// roles, role scopes, office hours and ABAC rules are all evaluated by UMS, so admins change AMS access only in the UMS portal.
    /// </summary>
    public class UmsAccessGuard : IAccessGuard
    {
        private readonly UmsClient _ums;
        private readonly UmsSession _session;
        private readonly UmsOptions _options;

        public UmsAccessGuard(UmsClient ums, UmsSession session, UmsOptions options)
        {
            _ums = ums;
            _session = session;
            _options = options;
        }

        public Actor Actor => _session.Actor ?? throw new UmsException(HttpStatusCode.Unauthorized, "Please sign in.");
        private string Token => _session.Token ?? throw new UmsException(HttpStatusCode.Unauthorized, "Please sign in.");

        /// <summary>Request context for env.* policy conditions (time is taken by UMS in the tenant's time zone).</summary>
        public UmsEnvironment Environment() => new(null, _session.ClientIp, NetworkOf(_session.ClientIp), _options.Device, _options.Channel);

        public static string NetworkOf(string ip)
        {
            if (!IPAddress.TryParse(ip, out var a)) return "Internet";
            if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
            if (IPAddress.IsLoopback(a)) return "Office LAN";
            var b = a.GetAddressBytes();
            if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) ? "Office LAN" : "Internet";
            return a.IsIPv6SiteLocal || a.IsIPv6LinkLocal || (b[0] & 0xFE) == 0xFC ? "Office LAN" : "Internet";
        }

        private static UmsCheck ToUms(AccessCheck c) =>
            new(UmsClient.AppCode, c.Module, c.Resource, c.Action, c.Attributes, $"/ams/{c.Module}/{c.Resource}/{c.Action}".ToLowerInvariant());

        public async Task EnsureAsync(AccessCheck check, CancellationToken ct = default)
        {
            var decision = await _ums.AuthorizeAsync(Token, ToUms(check), Environment(), ct);
            if (!decision.Allowed) throw new AccessDeniedException(check.Key, decision.Reason);
        }

        public async Task<IReadOnlyList<bool>> CanAsync(IReadOnlyList<AccessCheck> checks, CancellationToken ct = default)
        {
            if (checks.Count == 0) return Array.Empty<bool>();
            var decisions = await _ums.AuthorizeBatchAsync(Token, checks.Select(ToUms).ToList(), Environment(), ct);
            return decisions.Select(d => d.Allowed).ToList();
        }
    }

    /// <summary>Colleagues from the UMS directory (same tenant as the signed-in user).</summary>
    public class UmsPeopleDirectory : IPeopleDirectory
    {
        private readonly UmsClient _ums;
        private readonly UmsSession _session;

        public UmsPeopleDirectory(UmsClient ums, UmsSession session)
        {
            _ums = ums;
            _session = session;
        }

        private string Token => _session.Token ?? throw new UmsException(HttpStatusCode.Unauthorized, "Please sign in.");

        private static Person Map(UmsDirectoryEntry e) =>
            new(e.EmpCode, e.FullName, e.Email, e.Department, e.Designation, e.Location, e.Region, e.EmploymentType, e.Status);

        public async Task<IReadOnlyList<Person>> SearchAsync(string? search, int take = 30, CancellationToken ct = default) =>
            (await _ums.DirectoryAsync(Token, search, take, ct)).Select(Map).ToList();

        public async Task<Person?> FindAsync(string empCode, CancellationToken ct = default)
        {
            var code = empCode.Trim();
            var hits = await _ums.DirectoryAsync(Token, code, 20, ct);
            var e = hits.FirstOrDefault(x => string.Equals(x.EmpCode, code, StringComparison.OrdinalIgnoreCase));
            return e == null ? null : Map(e);
        }
    }
}
