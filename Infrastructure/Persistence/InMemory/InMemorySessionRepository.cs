using ApplicationLayer.Interfaces;
using DomainLayer.Entities;

namespace Infrastructure.Persistence.InMemory
{
    public sealed class InMemorySessionRepository : IUserSessionRepository
    {
        private readonly InMemoryUmsStore _store;
        public InMemorySessionRepository(InMemoryUmsStore store) => _store = store;

        public Task<UserSession?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_store.Read(() => _store.Sessions.GetValueOrDefault(id)));

        public Task SaveAsync(UserSession session, CancellationToken ct = default) => _store.Write(() => _store.Sessions[session.Id] = session);

        public Task<IReadOnlyList<UserSession>> ListOpenAsync(Guid userId, string? appCode = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<UserSession>>(_store.Read(() => _store.Sessions.Values
                .Where(s => s.UserId == userId && !s.IsEnded && (appCode == null || s.AppCode == appCode.ToUpperInvariant())).ToList()));

        public Task<IReadOnlyList<UserSession>> ListAsync(Guid tenantId, bool openOnly, int take, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<UserSession>>(_store.Read(() => _store.Sessions.Values
                .Where(s => s.TenantId == tenantId && (!openOnly || !s.IsEnded)).OrderByDescending(s => s.CreatedAt).Take(take).ToList()));

        public Task<LoginAttempt?> GetLoginAttemptAsync(string login, CancellationToken ct = default) =>
            Task.FromResult(_store.Read(() => _store.LoginAttempts.GetValueOrDefault(login.Trim().ToLowerInvariant())));

        public Task SaveLoginAttemptAsync(LoginAttempt attempt, CancellationToken ct = default) =>
            _store.Write(() => _store.LoginAttempts[attempt.Login] = attempt);
    }
}
