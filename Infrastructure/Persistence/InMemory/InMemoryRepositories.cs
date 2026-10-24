using ApplicationLayer.Interfaces;
using DomainLayer.Entities;

namespace Infrastructure.Persistence.InMemory
{
    /// <summary>
    /// Process-wide store holding the demo data (Persistence:Provider = "InMemory"). Data is lost on restart.
    /// Intended for local development, demos and tests; production uses the SQL Server repositories.
    /// </summary>
    public sealed class InMemoryUmsStore
    {
        internal readonly object Gate = new();
        internal readonly List<Tenant> Tenants;
        internal readonly Dictionary<Guid, SecuritySettings> Settings;
        internal readonly List<Application> Applications;
        internal readonly List<Role> Roles;
        internal readonly List<User> Users;
        internal readonly List<Policy> Policies;
        internal readonly List<Schedule> Schedules;
        internal readonly List<Holiday> Holidays;
        internal readonly List<AuditLog> AuditLogs;
        internal readonly List<UserActionLog> ActionLogs;
        internal readonly List<ExceptionLog> ExceptionLogs;

        public InMemoryUmsStore(DemoSeed seed)
        {
            Tenants = seed.Tenants;
            Settings = seed.Settings;
            Applications = seed.Applications;
            Roles = seed.Roles;
            Users = seed.Users;
            Policies = seed.Policies;
            Schedules = seed.Schedules;
            Holidays = seed.Holidays;
            AuditLogs = seed.AuditLogs;
            ActionLogs = seed.ActionLogs;
            ExceptionLogs = seed.ExceptionLogs;
        }

        internal T Read<T>(Func<T> read)
        {
            lock (Gate) return read();
        }

        internal Task Write(Action write)
        {
            lock (Gate) write();
            return Task.CompletedTask;
        }

        internal static void Upsert<T>(List<T> list, T item) where T : BaseEntity
        {
            var i = list.FindIndex(x => x.Id == item.Id);
            if (i >= 0) list[i] = item;
            else list.Add(item);
        }
    }

    public sealed class InMemoryTenantRepository : ITenantRepository
    {
        private readonly InMemoryUmsStore _s;
        public InMemoryTenantRepository(InMemoryUmsStore store) => _s = store;

        public Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken ct = default) => Task.FromResult(_s.Read<IReadOnlyList<Tenant>>(() => _s.Tenants.ToList()));
        public Task<Tenant?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_s.Read(() => _s.Tenants.FirstOrDefault(t => t.Id == id)));
        public Task SaveAsync(Tenant tenant, CancellationToken ct = default) => _s.Write(() => InMemoryUmsStore.Upsert(_s.Tenants, tenant));
        public Task<SecuritySettings?> GetSettingsAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_s.Read(() => _s.Settings.TryGetValue(tenantId, out var v) ? v : null));
        public Task SaveSettingsAsync(Guid tenantId, SecuritySettings settings, CancellationToken ct = default) => _s.Write(() => _s.Settings[tenantId] = settings);
    }

    public sealed class InMemoryApplicationRepository : IApplicationRepository
    {
        private readonly InMemoryUmsStore _s;
        public InMemoryApplicationRepository(InMemoryUmsStore store) => _s = store;

        public Task<IReadOnlyList<Application>> ListAsync(Guid? tenantId = null, CancellationToken ct = default) =>
            Task.FromResult(_s.Read<IReadOnlyList<Application>>(() => _s.Applications.Where(a => tenantId is null || a.IsEnabledFor(tenantId.Value)).ToList()));
        public Task<Application?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_s.Read(() => _s.Applications.FirstOrDefault(a => a.Id == id)));
        public Task SaveAsync(Application application, CancellationToken ct = default) => _s.Write(() => InMemoryUmsStore.Upsert(_s.Applications, application));
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => _s.Write(() => _s.Applications.RemoveAll(a => a.Id == id));
    }

    public sealed class InMemoryRoleRepository : IRoleRepository
    {
        private readonly InMemoryUmsStore _s;
        public InMemoryRoleRepository(InMemoryUmsStore store) => _s = store;

        public Task<IReadOnlyList<Role>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_s.Read<IReadOnlyList<Role>>(() => _s.Roles.Where(r => r.TenantId == tenantId).ToList()));
        public Task<Role?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_s.Read(() => _s.Roles.FirstOrDefault(r => r.Id == id)));
        public Task SaveAsync(Role role, CancellationToken ct = default) => _s.Write(() => InMemoryUmsStore.Upsert(_s.Roles, role));
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => _s.Write(() => _s.Roles.RemoveAll(r => r.Id == id));
    }

    public sealed class InMemoryUserAccountRepository : IUserAccountRepository
    {
        private readonly InMemoryUmsStore _s;
        public InMemoryUserAccountRepository(InMemoryUmsStore store) => _s = store;

        public Task<IReadOnlyList<User>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_s.Read<IReadOnlyList<User>>(() => _s.Users.Where(u => u.TenantId == tenantId).ToList()));
        public Task<User?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_s.Read(() => _s.Users.FirstOrDefault(u => u.Id == id)));
        public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
            Task.FromResult(_s.Read(() => _s.Users.FirstOrDefault(u => string.Equals(u.Email.Value, email, StringComparison.OrdinalIgnoreCase))));
        public Task<User?> GetByEmpCodeAsync(string empCode, CancellationToken ct = default) =>
            Task.FromResult(_s.Read(() => _s.Users.FirstOrDefault(u => string.Equals(u.EmpCode, empCode, StringComparison.OrdinalIgnoreCase))));
        public Task<int> CountWithRoleAsync(Guid roleId, CancellationToken ct = default) =>
            Task.FromResult(_s.Read(() => _s.Users.Count(u => u.RoleAssignments.Any(a => a.RoleId == roleId))));
        public Task SaveAsync(User user, CancellationToken ct = default) => _s.Write(() => InMemoryUmsStore.Upsert(_s.Users, user));
    }

    public sealed class InMemoryPolicyRepository : IPolicyRepository
    {
        private readonly InMemoryUmsStore _s;
        public InMemoryPolicyRepository(InMemoryUmsStore store) => _s = store;

        public Task<IReadOnlyList<Policy>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_s.Read<IReadOnlyList<Policy>>(() => _s.Policies.Where(p => p.TenantId == tenantId).ToList()));
        public Task<Policy?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_s.Read(() => _s.Policies.FirstOrDefault(p => p.Id == id)));
        public Task SaveAsync(Policy policy, CancellationToken ct = default) => _s.Write(() => InMemoryUmsStore.Upsert(_s.Policies, policy));
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => _s.Write(() => _s.Policies.RemoveAll(p => p.Id == id));
    }

    public sealed class InMemoryScheduleRepository : IScheduleRepository
    {
        private readonly InMemoryUmsStore _s;
        public InMemoryScheduleRepository(InMemoryUmsStore store) => _s = store;

        public Task<IReadOnlyList<Schedule>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_s.Read<IReadOnlyList<Schedule>>(() => _s.Schedules.Where(x => x.TenantId == tenantId).ToList()));
        public Task<Schedule?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_s.Read(() => _s.Schedules.FirstOrDefault(x => x.Id == id)));
        public Task SaveAsync(Schedule schedule, CancellationToken ct = default) => _s.Write(() => InMemoryUmsStore.Upsert(_s.Schedules, schedule));
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => _s.Write(() => _s.Schedules.RemoveAll(x => x.Id == id));

        public Task<IReadOnlyList<Holiday>> ListHolidaysAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_s.Read<IReadOnlyList<Holiday>>(() => _s.Holidays.Where(h => h.TenantId == tenantId).ToList()));
        public Task<Holiday?> GetHolidayAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_s.Read(() => _s.Holidays.FirstOrDefault(h => h.Id == id)));
        public Task SaveHolidayAsync(Holiday holiday, CancellationToken ct = default) => _s.Write(() => InMemoryUmsStore.Upsert(_s.Holidays, holiday));
        public Task DeleteHolidayAsync(Guid id, CancellationToken ct = default) => _s.Write(() => _s.Holidays.RemoveAll(h => h.Id == id));
    }

    public sealed class InMemoryLogRepository : ILogRepository
    {
        private readonly InMemoryUmsStore _s;
        public InMemoryLogRepository(InMemoryUmsStore store) => _s = store;

        public Task AddAuditAsync(AuditLog log, CancellationToken ct = default) => _s.Write(() => _s.AuditLogs.Add(log));
        public Task<IReadOnlyList<AuditLog>> ListAuditAsync(Guid tenantId, int take, CancellationToken ct = default) =>
            Task.FromResult(_s.Read<IReadOnlyList<AuditLog>>(() => _s.AuditLogs.Where(a => a.TenantId == tenantId).OrderByDescending(a => a.Timestamp).Take(take).ToList()));

        public Task AddUserActionAsync(UserActionLog log, CancellationToken ct = default) => _s.Write(() => _s.ActionLogs.Add(log));
        public Task<IReadOnlyList<UserActionLog>> ListUserActionsAsync(Guid tenantId, int take, CancellationToken ct = default) =>
            Task.FromResult(_s.Read<IReadOnlyList<UserActionLog>>(() => _s.ActionLogs.Where(a => a.TenantId == tenantId).OrderByDescending(a => a.Timestamp).Take(take).ToList()));

        public Task AddExceptionAsync(ExceptionLog log, CancellationToken ct = default) => _s.Write(() => _s.ExceptionLogs.Add(log));
        public Task<IReadOnlyList<ExceptionLog>> ListExceptionsAsync(Guid tenantId, int take, CancellationToken ct = default) =>
            Task.FromResult(_s.Read<IReadOnlyList<ExceptionLog>>(() => _s.ExceptionLogs.Where(e => e.TenantId == tenantId).OrderByDescending(e => e.Timestamp).Take(take).ToList()));
        public Task<ExceptionLog?> GetExceptionAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_s.Read(() => _s.ExceptionLogs.FirstOrDefault(e => e.Id == id)));
        public Task SaveExceptionAsync(ExceptionLog log, CancellationToken ct = default) => _s.Write(() => InMemoryUmsStore.Upsert(_s.ExceptionLogs, log));
    }
}
