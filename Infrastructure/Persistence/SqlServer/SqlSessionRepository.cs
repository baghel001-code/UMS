using ApplicationLayer.Interfaces;
using Dapper;
using DomainLayer.Entities;
using Infrastructure.Database;

namespace Infrastructure.Persistence.SqlServer
{
    /// <summary>Sign-in sessions and lockout counters (database/002_sessions_and_lockout.sql).</summary>
    public sealed class SqlSessionRepository : IUserSessionRepository
    {
        private readonly IDbConnectionFactory _db;
        public SqlSessionRepository(IDbConnectionFactory db) => _db = db;

        private sealed class Row
        {
            public Guid Id { get; set; }
            public Guid TenantId { get; set; }
            public Guid UserId { get; set; }
            public string UserName { get; set; } = "";
            public string EmpCode { get; set; } = "";
            public string AppCode { get; set; } = "";
            public string IpAddress { get; set; } = "";
            public string UserAgent { get; set; } = "";
            public DateTime CreatedAt { get; set; }
            public DateTime ExpiresAt { get; set; }
            public DateTime LastSeenAt { get; set; }
            public string RefreshTokenHash { get; set; } = "";
            public DateTime? EndedAt { get; set; }
            public string EndReason { get; set; } = "None";

            public UserSession ToEntity() => new(Id, TenantId, UserId, UserName, EmpCode, AppCode, IpAddress, UserAgent, CreatedAt, ExpiresAt, LastSeenAt,
                RefreshTokenHash, EndedAt, Sql.ParseEnum<SessionEndReason>(EndReason));
        }

        private sealed class AttemptRow
        {
            public string Login { get; set; } = "";
            public int FailedCount { get; set; }
            public DateTime? LockedUntil { get; set; }
        }

        private const string Columns = "Id, TenantId, UserId, UserName, EmpCode, AppCode, IpAddress, UserAgent, CreatedAt, ExpiresAt, LastSeenAt, RefreshTokenHash, EndedAt, EndReason";

        public async Task<UserSession?> GetAsync(Guid id, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            return (await conn.QueryFirstOrDefaultAsync<Row>($"SELECT {Columns} FROM dbo.UserSessions WHERE Id = @id", new { id }))?.ToEntity();
        }

        public async Task SaveAsync(UserSession s, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(@"
UPDATE dbo.UserSessions SET LastSeenAt = @LastSeenAt, RefreshTokenHash = @RefreshTokenHash, EndedAt = @EndedAt, EndReason = @EndReason WHERE Id = @Id;
IF @@ROWCOUNT = 0
    INSERT INTO dbo.UserSessions (" + Columns + @")
    VALUES (@Id, @TenantId, @UserId, @UserName, @EmpCode, @AppCode, @IpAddress, @UserAgent, @CreatedAt, @ExpiresAt, @LastSeenAt, @RefreshTokenHash, @EndedAt, @EndReason);",
                new
                {
                    s.Id, s.TenantId, s.UserId, s.UserName, s.EmpCode, s.AppCode, s.IpAddress, s.UserAgent, s.CreatedAt, s.ExpiresAt, s.LastSeenAt,
                    s.RefreshTokenHash, s.EndedAt, EndReason = s.EndReason.ToString()
                });
        }

        public async Task<IReadOnlyList<UserSession>> ListOpenAsync(Guid userId, string? appCode = null, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            return (await conn.QueryAsync<Row>($@"SELECT {Columns} FROM dbo.UserSessions
WHERE UserId = @userId AND EndReason = 'None' AND (@appCode IS NULL OR AppCode = @appCode)", new { userId, appCode = appCode?.ToUpperInvariant() }))
                .Select(r => r.ToEntity()).ToList();
        }

        public async Task<IReadOnlyList<UserSession>> ListAsync(Guid tenantId, bool openOnly, int take, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            return (await conn.QueryAsync<Row>($@"SELECT TOP (@take) {Columns} FROM dbo.UserSessions
WHERE TenantId = @tenantId AND (@openOnly = 0 OR EndReason = 'None') ORDER BY CreatedAt DESC", new { tenantId, openOnly, take }))
                .Select(r => r.ToEntity()).ToList();
        }

        public async Task<LoginAttempt?> GetLoginAttemptAsync(string login, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            var r = await conn.QueryFirstOrDefaultAsync<AttemptRow>(
                "SELECT Login, FailedCount, LockedUntil FROM dbo.LoginAttempts WHERE Login = @login", new { login = login.Trim().ToLowerInvariant() });
            return r == null ? null : new LoginAttempt(r.Login, r.FailedCount, r.LockedUntil);
        }

        public async Task SaveLoginAttemptAsync(LoginAttempt a, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(@"
UPDATE dbo.LoginAttempts SET FailedCount = @FailedCount, LockedUntil = @LockedUntil WHERE Login = @Login;
IF @@ROWCOUNT = 0 INSERT INTO dbo.LoginAttempts (Login, FailedCount, LockedUntil) VALUES (@Login, @FailedCount, @LockedUntil);",
                new { a.Login, a.FailedCount, a.LockedUntil });
        }
    }
}
