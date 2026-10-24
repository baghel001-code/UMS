using AMS.Application.Interfaces;
using AMS.Domain.Security;
using AMS.Infrastructure.Api;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Security.Claims;

namespace AMS.Infrastructure.Ums
{
    /// <summary>
    /// The signed-in user of an AMS API request, read from the validated UMS access token. The browser address forwarded by the portal
    /// (X-Client-IP) is only trusted from the configured portal servers; anyone else is judged by their own address, so a caller cannot
    /// pretend to be on the office network to satisfy a UMS env.network policy.
    /// </summary>
    public sealed class HttpCurrentActor : ICurrentActor
    {
        public const string TenantClaim = "tenant_id";
        public const string EmpCodeClaim = "emp_code";

        private readonly IHttpContextAccessor _http;
        private readonly IReadOnlyCollection<IPAddress> _trustedForwarders;
        private Actor? _actor;

        public HttpCurrentActor(IHttpContextAccessor http, TrustedForwarders trusted)
        {
            _http = http;
            _trustedForwarders = trusted.Addresses;
        }

        private HttpContext Context => _http.HttpContext ?? throw new InvalidOperationException("No HTTP request.");

        public Actor Actor => _actor ??= Read(Context.User);

        public static Actor Read(ClaimsPrincipal user)
        {
            if (!Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId) || !Guid.TryParse(user.FindFirst(TenantClaim)?.Value, out var tenantId))
                throw new UnauthorizedAccessException("The token does not identify a UMS user.");
            return new Actor(userId, tenantId, user.FindFirst(EmpCodeClaim)?.Value ?? "", user.FindFirst(ClaimTypes.Name)?.Value ?? "",
                user.FindFirst(ClaimTypes.Email)?.Value ?? "");
        }

        public string AccessToken
        {
            get
            {
                var header = Context.Request.Headers.Authorization.ToString();
                return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : throw new UnauthorizedAccessException("No bearer token.");
            }
        }

        public string? ClientIp
        {
            get
            {
                var remote = Context.Connection.RemoteIpAddress;
                if (remote is { IsIPv4MappedToIPv6: true }) remote = remote.MapToIPv4();
                var forwarded = Context.Request.Headers[ApiCallService.ClientIpHeader].ToString();
                if (!string.IsNullOrWhiteSpace(forwarded) && remote != null && _trustedForwarders.Any(t => t.Equals(remote)) && IPAddress.TryParse(forwarded, out var ip))
                    return ip.ToString();
                return remote?.ToString();
            }
        }
    }

    /// <summary>Addresses of the portal servers allowed to forward the browser's IP ("Ums:TrustedForwarders", default: this machine).</summary>
    public sealed class TrustedForwarders
    {
        public IReadOnlyCollection<IPAddress> Addresses { get; }
        public TrustedForwarders(IEnumerable<string>? configured) =>
            Addresses = (configured ?? Array.Empty<string>()).Select(s => IPAddress.TryParse(s, out var a) ? a : null).OfType<IPAddress>()
                .Concat(new[] { IPAddress.Loopback, IPAddress.IPv6Loopback }).Distinct().ToList();
    }
}
