namespace DomainLayer.Entities
{
    /// <summary>Why a sign-in session stopped working. Returned to clients so they can tell the user.</summary>
    public enum SessionEndReason
    {
        None = 0,
        /// <summary>The user signed out.</summary>
        SignedOut = 1,
        /// <summary>The same user signed in to the same application on another device (single-device login).</summary>
        Replaced = 2,
        /// <summary>The absolute session lifetime (refresh token lifetime) ran out.</summary>
        Expired = 3,
        /// <summary>No activity for longer than the tenant's idle timeout.</summary>
        Idle = 4,
        /// <summary>An administrator ended the session in UMS.</summary>
        RevokedByAdmin = 5,
        /// <summary>An old refresh token was presented again (possible theft); the whole session is ended.</summary>
        TokenReuse = 6,
        /// <summary>The account was disabled, locked or deleted.</summary>
        AccountDisabled = 7,
    }

    /// <summary>
    /// One sign-in of a user to one application on one device. Access tokens carry its id (claim "sid") and are only accepted
    /// while it is active; refresh tokens rotate on every use and are stored as SHA-256 hashes only.
    /// </summary>
    public sealed class UserSession
    {
        public Guid Id { get; }
        public Guid TenantId { get; }
        public Guid UserId { get; }
        public string UserName { get; }
        public string EmpCode { get; }
        public string AppCode { get; }
        public string IpAddress { get; }
        public string UserAgent { get; }
        public DateTime CreatedAt { get; }
        public DateTime ExpiresAt { get; }
        public DateTime LastSeenAt { get; private set; }
        public string RefreshTokenHash { get; private set; }
        public DateTime? EndedAt { get; private set; }
        public SessionEndReason EndReason { get; private set; }

        public UserSession(Guid id, Guid tenantId, Guid userId, string userName, string empCode, string appCode, string ipAddress, string userAgent,
            DateTime createdAt, DateTime expiresAt, DateTime lastSeenAt, string refreshTokenHash, DateTime? endedAt = null, SessionEndReason endReason = SessionEndReason.None)
        {
            if (string.IsNullOrWhiteSpace(appCode)) throw new ArgumentException("Application code is required.", nameof(appCode));
            if (expiresAt <= createdAt) throw new ArgumentException("A session must expire after it starts.", nameof(expiresAt));
            Id = id;
            TenantId = tenantId;
            UserId = userId;
            UserName = userName;
            EmpCode = empCode;
            AppCode = appCode.Trim().ToUpperInvariant();
            IpAddress = ipAddress ?? "";
            UserAgent = userAgent ?? "";
            CreatedAt = createdAt;
            ExpiresAt = expiresAt;
            LastSeenAt = lastSeenAt;
            RefreshTokenHash = refreshTokenHash;
            EndedAt = endedAt;
            EndReason = endReason;
        }

        public bool IsEnded => EndReason != SessionEndReason.None;

        /// <summary>None while the session may be used at <paramref name="nowUtc"/>; otherwise why not (without changing it).</summary>
        public SessionEndReason StatusAt(DateTime nowUtc, TimeSpan idleTimeout)
        {
            if (IsEnded) return EndReason;
            if (nowUtc >= ExpiresAt) return SessionEndReason.Expired;
            if (idleTimeout > TimeSpan.Zero && nowUtc - LastSeenAt > idleTimeout) return SessionEndReason.Idle;
            return SessionEndReason.None;
        }

        /// <summary>Records user activity (keeps the idle timer from running out).</summary>
        public void Touch(DateTime nowUtc)
        {
            if (!IsEnded && nowUtc > LastSeenAt) LastSeenAt = nowUtc;
        }

        /// <summary>Replaces the refresh token after it was used (rotation).</summary>
        public void Rotate(string newRefreshTokenHash, DateTime nowUtc)
        {
            if (IsEnded) throw new InvalidOperationException("An ended session cannot be refreshed.");
            RefreshTokenHash = newRefreshTokenHash;
            Touch(nowUtc);
        }

        public void End(SessionEndReason reason, DateTime nowUtc)
        {
            if (reason == SessionEndReason.None) throw new ArgumentException("An end reason is required.", nameof(reason));
            if (IsEnded) return;
            EndReason = reason;
            EndedAt = nowUtc;
        }
    }

    /// <summary>Failed sign-in counter per login name; the account is locked for a while after too many failures.</summary>
    public sealed class LoginAttempt
    {
        public string Login { get; }
        public int FailedCount { get; private set; }
        public DateTime? LockedUntil { get; private set; }

        public LoginAttempt(string login, int failedCount = 0, DateTime? lockedUntil = null)
        {
            Login = login.Trim().ToLowerInvariant();
            FailedCount = failedCount;
            LockedUntil = lockedUntil;
        }

        public bool IsLocked(DateTime nowUtc) => LockedUntil is { } until && until > nowUtc;

        /// <summary>Counts a failure; returns true when this failure locked the account.</summary>
        public bool RegisterFailure(DateTime nowUtc, int maxFailures, TimeSpan lockout)
        {
            if (LockedUntil is { } until && until <= nowUtc) { LockedUntil = null; FailedCount = 0; }
            FailedCount++;
            if (maxFailures <= 0 || FailedCount < maxFailures) return false;
            LockedUntil = nowUtc + lockout;
            FailedCount = 0;
            return true;
        }
    }
}
