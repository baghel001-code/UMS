using DomainLayer.Enumerations;

namespace DomainLayer.Entities
{
    /// <summary>Who changed what in UMS, with before/after values (AuditLogs table).</summary>
    public sealed class AuditLog : BaseEntity
    {
        public Guid TenantId { get; private set; }
        public DateTime Timestamp { get; private set; }
        public string Actor { get; private set; } = string.Empty;
        public string Entity { get; private set; } = string.Empty;
        public string EntityId { get; private set; } = string.Empty;
        public AuditAction Action { get; private set; }
        public string Summary { get; private set; } = string.Empty;
        /// <summary>JSON snapshots.</summary>
        public string? OldValues { get; private set; }
        public string? NewValues { get; private set; }
        public string Ip { get; private set; } = string.Empty;
        public string Device { get; private set; } = string.Empty;

        private AuditLog() { }

        public AuditLog(Guid id, Guid tenantId, DateTime timestamp, string actor, string entity, string entityId, AuditAction action,
            string summary, string? oldValues, string? newValues, string ip, string device) : base(id)
        {
            TenantId = tenantId;
            Timestamp = timestamp;
            Actor = actor;
            Entity = entity;
            EntityId = entityId;
            Action = action;
            Summary = summary;
            OldValues = oldValues;
            NewValues = newValues;
            Ip = ip;
            Device = device;
        }
    }

    /// <summary>An authorization decision taken for an end user (UserActions table).</summary>
    public sealed class UserActionLog : BaseEntity
    {
        public Guid TenantId { get; private set; }
        public DateTime Timestamp { get; private set; }
        public Guid UserId { get; private set; }
        public string UserName { get; private set; } = string.Empty;
        public string App { get; private set; } = string.Empty;
        public string Route { get; private set; } = string.Empty;
        /// <summary>Permission key that was checked.</summary>
        public string Action { get; private set; } = string.Empty;
        public AccessDecisionResult Decision { get; private set; }
        public string Reason { get; private set; } = string.Empty;
        public string Ip { get; private set; } = string.Empty;

        private UserActionLog() { }

        public UserActionLog(Guid id, Guid tenantId, DateTime timestamp, Guid userId, string userName, string app, string route,
            string action, AccessDecisionResult decision, string reason, string ip) : base(id)
        {
            TenantId = tenantId;
            Timestamp = timestamp;
            UserId = userId;
            UserName = userName;
            App = app;
            Route = route;
            Action = action;
            Decision = decision;
            Reason = reason;
            Ip = ip;
        }
    }

    /// <summary>An unhandled exception captured by the exception-logging middleware (ExceptionLogs table).</summary>
    public sealed class ExceptionLog : BaseEntity
    {
        public Guid TenantId { get; private set; }
        public DateTime Timestamp { get; private set; }
        public string App { get; private set; } = string.Empty;
        public ExceptionSeverity Severity { get; private set; }
        public string Message { get; private set; } = string.Empty;
        public string StackTrace { get; private set; } = string.Empty;
        public string? UserName { get; private set; }
        public string RequestPath { get; private set; } = string.Empty;
        public string Ip { get; private set; } = string.Empty;
        public bool Resolved { get; private set; }

        private ExceptionLog() { }

        public ExceptionLog(Guid id, Guid tenantId, DateTime timestamp, string app, ExceptionSeverity severity, string message,
            string stackTrace, string? userName, string requestPath, string ip, bool resolved) : base(id)
        {
            TenantId = tenantId;
            Timestamp = timestamp;
            App = app;
            Severity = severity;
            Message = message;
            StackTrace = stackTrace;
            UserName = userName;
            RequestPath = requestPath;
            Ip = ip;
            Resolved = resolved;
        }

        public void Resolve() => Resolved = true;
    }

    /// <summary>Authentication and session policy of a tenant (TenantSettings table).</summary>
    public sealed record SecuritySettings(
        int PasswordMinLength,
        int PasswordExpiryDays,
        int MaxFailedLogins,
        int LockoutMinutes,
        int SessionTimeoutMinutes,
        bool MfaRequiredForAdmins,
        bool MfaRequiredOutsideOffice,
        int JwtExpiryMinutes,
        int RefreshTokenDays,
        string AllowedIpRanges,
        bool SingleDeviceLogin = true)
    {
        // SessionTimeoutMinutes = idle timeout of a sign-in session; JwtExpiryMinutes = access token lifetime;
        // RefreshTokenDays = absolute session lifetime; SingleDeviceLogin = a new sign-in to an application ends the user's other sessions there.
        public static readonly SecuritySettings Default = new(12, 90, 5, 30, 30, true, true, 15, 7, "10.0.0.0/8, 172.16.0.0/12", true);
    }
}
