using DomainLayer.Entities;

namespace ApplicationLayer.Interfaces
{
    // Persistence ports for the access-control aggregates. Implemented in Infrastructure by an in-memory store
    // (demo / tests) and by Dapper repositories for SQL Server. SaveAsync inserts or updates.

    public interface ITenantRepository
    {
        Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken ct = default);
        Task<Tenant?> GetAsync(Guid id, CancellationToken ct = default);
        Task SaveAsync(Tenant tenant, CancellationToken ct = default);
        Task<SecuritySettings?> GetSettingsAsync(Guid tenantId, CancellationToken ct = default);
        Task SaveSettingsAsync(Guid tenantId, SecuritySettings settings, CancellationToken ct = default);
    }

    public interface IApplicationRepository
    {
        /// <summary>All applications, or only those enabled for <paramref name="tenantId"/>.</summary>
        Task<IReadOnlyList<Application>> ListAsync(Guid? tenantId = null, CancellationToken ct = default);
        Task<Application?> GetAsync(Guid id, CancellationToken ct = default);
        Task SaveAsync(Application application, CancellationToken ct = default);
        Task DeleteAsync(Guid id, CancellationToken ct = default);
    }

    public interface IRoleRepository
    {
        Task<IReadOnlyList<Role>> ListAsync(Guid tenantId, CancellationToken ct = default);
        Task<Role?> GetAsync(Guid id, CancellationToken ct = default);
        Task SaveAsync(Role role, CancellationToken ct = default);
        Task DeleteAsync(Guid id, CancellationToken ct = default);
    }

    public interface IUserAccountRepository
    {
        Task<IReadOnlyList<User>> ListAsync(Guid tenantId, CancellationToken ct = default);
        Task<User?> GetAsync(Guid id, CancellationToken ct = default);
        Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
        Task<User?> GetByEmpCodeAsync(string empCode, CancellationToken ct = default);
        Task<int> CountWithRoleAsync(Guid roleId, CancellationToken ct = default);
        Task SaveAsync(User user, CancellationToken ct = default);
    }

    public interface IPolicyRepository
    {
        Task<IReadOnlyList<Policy>> ListAsync(Guid tenantId, CancellationToken ct = default);
        Task<Policy?> GetAsync(Guid id, CancellationToken ct = default);
        Task SaveAsync(Policy policy, CancellationToken ct = default);
        Task DeleteAsync(Guid id, CancellationToken ct = default);
    }

    public interface IScheduleRepository
    {
        Task<IReadOnlyList<Schedule>> ListAsync(Guid tenantId, CancellationToken ct = default);
        Task<Schedule?> GetAsync(Guid id, CancellationToken ct = default);
        Task SaveAsync(Schedule schedule, CancellationToken ct = default);
        Task DeleteAsync(Guid id, CancellationToken ct = default);

        Task<IReadOnlyList<Holiday>> ListHolidaysAsync(Guid tenantId, CancellationToken ct = default);
        Task<Holiday?> GetHolidayAsync(Guid id, CancellationToken ct = default);
        Task SaveHolidayAsync(Holiday holiday, CancellationToken ct = default);
        Task DeleteHolidayAsync(Guid id, CancellationToken ct = default);
    }

    public interface ILogRepository
    {
        Task AddAuditAsync(AuditLog log, CancellationToken ct = default);
        Task<IReadOnlyList<AuditLog>> ListAuditAsync(Guid tenantId, int take, CancellationToken ct = default);

        Task AddUserActionAsync(UserActionLog log, CancellationToken ct = default);
        Task<IReadOnlyList<UserActionLog>> ListUserActionsAsync(Guid tenantId, int take, CancellationToken ct = default);

        Task AddExceptionAsync(ExceptionLog log, CancellationToken ct = default);
        Task<IReadOnlyList<ExceptionLog>> ListExceptionsAsync(Guid tenantId, int take, CancellationToken ct = default);
        Task<ExceptionLog?> GetExceptionAsync(Guid id, CancellationToken ct = default);
        Task SaveExceptionAsync(ExceptionLog log, CancellationToken ct = default);
    }

    public interface IUserSessionRepository
    {
        Task<UserSession?> GetAsync(Guid id, CancellationToken ct = default);
        Task SaveAsync(UserSession session, CancellationToken ct = default);
        /// <summary>Sessions of a user that are not ended (expiry is not checked).</summary>
        Task<IReadOnlyList<UserSession>> ListOpenAsync(Guid userId, string? appCode = null, CancellationToken ct = default);
        /// <summary>Newest first.</summary>
        Task<IReadOnlyList<UserSession>> ListAsync(Guid tenantId, bool openOnly, int take, CancellationToken ct = default);

        Task<LoginAttempt?> GetLoginAttemptAsync(string login, CancellationToken ct = default);
        Task SaveLoginAttemptAsync(LoginAttempt attempt, CancellationToken ct = default);
    }
}
