using ApplicationLayer.Interfaces;
using DomainLayer.Authorization;

namespace ApplicationLayer.Services
{
    /// <summary>
    /// Loads one tenant's access-control data for the decision engine.
    /// For SQL Server in production, wrap this in a short-lived cache invalidated by the audit service (see docs/ARCHITECTURE.md).
    /// </summary>
    public class AccessContextProvider : IAccessContextProvider
    {
        private readonly ITenantRepository _tenants;
        private readonly IApplicationRepository _apps;
        private readonly IRoleRepository _roles;
        private readonly IUserAccountRepository _users;
        private readonly IPolicyRepository _policies;
        private readonly IScheduleRepository _schedules;

        public AccessContextProvider(ITenantRepository tenants, IApplicationRepository apps, IRoleRepository roles, IUserAccountRepository users,
            IPolicyRepository policies, IScheduleRepository schedules)
        {
            _tenants = tenants;
            _apps = apps;
            _roles = roles;
            _users = users;
            _policies = policies;
            _schedules = schedules;
        }

        public async Task<AccessContext> LoadAsync(Guid tenantId, CancellationToken ct = default) => new(
            await _tenants.ListAsync(ct),
            await _apps.ListAsync(null, ct),
            await _roles.ListAsync(tenantId, ct),
            await _users.ListAsync(tenantId, ct),
            await _policies.ListAsync(tenantId, ct),
            await _schedules.ListAsync(tenantId, ct),
            await _schedules.ListHolidaysAsync(tenantId, ct));
    }
}
