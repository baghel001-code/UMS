using ApplicationLayer.Interfaces;
using DomainLayer.Entities;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace Infrastructure.Services
{
    /// <summary>
    /// Checks the "sid" of every authenticated request. Session and idle timeout are cached for a few seconds per session so a
    /// busy API does not query the database on each call; ending a session through UMS clears the entry at once.
    /// </summary>
    public sealed class SessionValidator : ISessionValidator
    {
        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);
        private readonly IServiceScopeFactory _scopes;
        private readonly TimeProvider _time;
        private readonly ConcurrentDictionary<Guid, (UserSession? Session, TimeSpan Idle, DateTime CachedAt)> _cache = new();

        public SessionValidator(IServiceScopeFactory scopes, TimeProvider time)
        {
            _scopes = scopes;
            _time = time;
        }

        public async Task<SessionEndReason> CheckAsync(Guid sessionId, CancellationToken ct = default)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            if (!_cache.TryGetValue(sessionId, out var entry) || now - entry.CachedAt > CacheFor)
            {
                using var scope = _scopes.CreateScope();
                var session = await scope.ServiceProvider.GetRequiredService<IUserSessionRepository>().GetAsync(sessionId, ct);
                var settings = session == null ? null : await scope.ServiceProvider.GetRequiredService<ITenantRepository>().GetSettingsAsync(session.TenantId, ct);
                entry = (session, TimeSpan.FromMinutes(Math.Max(1, (settings ?? SecuritySettings.Default).SessionTimeoutMinutes)), now);
                _cache[sessionId] = entry;
            }
            var status = entry.Session?.StatusAt(now, entry.Idle) ?? SessionEndReason.Expired;
            if (status != SessionEndReason.None && entry.Session is { IsEnded: false })
                await RecordEndAsync(sessionId, status, now, ct);
            return status;
        }

        // A session that ran out (idle / expired) is refused here before any handler runs, so record the end now:
        // the admin "Active sessions" page and the audit trail then show why it ended.
        private async Task RecordEndAsync(Guid sessionId, SessionEndReason reason, DateTime now, CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IUserSessionRepository>();
            if (await repo.GetAsync(sessionId, ct) is { IsEnded: false } session)
            {
                session.End(reason, now);
                await repo.SaveAsync(session, ct);
            }
            _cache.TryRemove(sessionId, out _);
        }

        public void Forget(Guid sessionId) => _cache.TryRemove(sessionId, out _);
    }
}
