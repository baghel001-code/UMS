using ApplicationLayer.Common;
using ApplicationLayer.Interfaces;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.Services
{
    public class TenantOptions
    {
        /// <summary>Used when a request carries neither an X-Tenant-Id header nor a tenant claim.</summary>
        public Guid DefaultTenantId { get; set; }
    }

    public class CurrentTenantService : ICurrentTenantService
    {
        public const string HeaderName = "X-Tenant-Id";
        public const string ClaimType = "tenant_id";

        private readonly IHttpContextAccessor _http;
        private readonly TenantOptions _options;

        public CurrentTenantService(IHttpContextAccessor http, TenantOptions options)
        {
            _http = http;
            _options = options;
        }

        public Guid TenantId
        {
            get
            {
                var ctx = _http.HttpContext;
                if (ctx != null && Guid.TryParse(ctx.Request.Headers[HeaderName].FirstOrDefault(), out var fromHeader)) return fromHeader;
                if (ctx != null && Guid.TryParse(ctx.User.FindFirst(ClaimType)?.Value, out var fromClaim)) return fromClaim;
                return _options.DefaultTenantId;
            }
        }
    }

    public class AuditService : IAuditService
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        private readonly ILogRepository _logs;
        private readonly ICurrentUserService _currentUser;
        private readonly ICurrentTenantService _tenant;
        private readonly IHttpContextAccessor _http;
        private readonly TimeProvider _time;

        public AuditService(ILogRepository logs, ICurrentUserService currentUser, ICurrentTenantService tenant, IHttpContextAccessor http, TimeProvider time)
        {
            _logs = logs;
            _currentUser = currentUser;
            _tenant = tenant;
            _http = http;
            _time = time;
        }

        public Task LogAsync(string entity, string entityId, AuditAction action, string summary, object? oldValues = null, object? newValues = null, CancellationToken ct = default)
        {
            var ctx = _http.HttpContext;
            var log = new AuditLog(Guid.NewGuid(), _tenant.TenantId, LocalClock.Now(_time), _currentUser.UserName ?? "ums.admin", entity, entityId, action, summary,
                oldValues is null ? null : JsonSerializer.Serialize(oldValues, Json),
                newValues is null ? null : JsonSerializer.Serialize(newValues, Json),
                ctx?.Connection.RemoteIpAddress?.ToString() ?? "",
                ctx?.Request.Headers["User-Agent"].ToString() is { Length: > 0 } ua ? (ua.Length > 300 ? ua[..300] : ua) : "");
            return _logs.AddAuditAsync(log, ct);
        }
    }

    /// <summary>Adapts the existing static PBKDF2 <see cref="Identity.PasswordHasher"/> to the application port.</summary>
    public class PasswordHasherService : IPasswordHasher
    {
        public string Hash(string password) => Identity.PasswordHasher.Hash(password);
        public bool Verify(string hashed, string provided) => Identity.PasswordHasher.Verify(hashed, provided);
    }
}
