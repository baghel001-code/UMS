using DomainLayer.Enumerations;
using System.Text.Json;

namespace ApplicationLayer.Features.AccessControl.DTOs
{
    // Shapes exchanged with the admin panel (admin-panel/src/types.ts). JSON is camelCase.

    public record TenantDto(Guid Id, string Code, string Name, bool IsActive, DateOnly CreatedOn);

    public record AppResourceDto(Guid Id, string Code, string Name, List<AccessAction> Actions);
    public record AppModuleDto(Guid Id, string Code, string Name, List<AppResourceDto> Resources);
    public record ApplicationDto(Guid Id, string Code, string Name, AppType Type, string Vendor, string OwnerDept, string Description,
        bool IsActive, List<Guid> TenantIds, List<AppModuleDto> Modules);

    public record RoleDto(Guid Id, Guid TenantId, string Code, string Name, string Description, Guid? AppId, List<string> Permissions,
        bool IsSystem, bool IsActive);

    public record RoleAssignmentDto(Guid RoleId, Dictionary<string, string> Scope, DateOnly? ValidFrom, DateOnly? ValidTo,
        string? AssignedBy, DateOnly? AssignedOn);

    public record UserAccountDto(Guid Id, Guid TenantId, string EmpCode, string FullName, string Email, string? Phone, string Department,
        string Designation, string Location, string Region, EmploymentType EmploymentType, UserStatus Status, bool MfaEnabled,
        DateTime? LastLogin, DateOnly? AccountExpiry, Dictionary<string, string> Attributes, List<RoleAssignmentDto> Roles);

    public record EffectivePermissionDto(string Key, List<string> ViaRoles);

    public record PolicyTargetDto(string App, string Module, string Resource, List<string> Actions, List<string> Roles);
    public record PolicyConditionDto(Guid Id, string Attribute, ConditionOperator Operator, string Value, bool ValueIsAttribute);
    public record PolicyDto(Guid Id, Guid TenantId, string Name, string Description, PolicyEffect Effect, PolicyTargetDto Target,
        ConditionMatch Match, List<PolicyConditionDto> Conditions, int Priority, bool IsActive, string? CreatedBy, DateTime? CreatedAt,
        string? RuleExpression);

    public record TimeWindowDto(List<Weekday> Days, string Start, string End);
    public record ScheduleDto(Guid Id, Guid TenantId, string Code, string Name, string Timezone, bool ExcludeHolidays, List<TimeWindowDto> Windows);
    public record HolidayDto(Guid Id, Guid TenantId, DateOnly Date, string Name, List<string> Regions);

    public record AuditLogDto(Guid Id, Guid TenantId, DateTime Timestamp, string Actor, string Entity, string EntityId, AuditAction Action,
        string Summary, JsonElement? OldValues, JsonElement? NewValues, string Ip, string Device);
    public record UserActionLogDto(Guid Id, Guid TenantId, DateTime Timestamp, Guid UserId, string UserName, string App, string Route,
        string Action, AccessDecisionResult Decision, string Reason, string Ip);
    public record ExceptionLogDto(Guid Id, Guid TenantId, DateTime Timestamp, string App, ExceptionSeverity Severity, string Message,
        string StackTrace, string? UserName, string RequestPath, string Ip, bool Resolved);

    /// <summary>Environment of an access check. Missing values default to "now" in the tenant time zone, Office LAN, Managed, Web.</summary>
    public record AccessEnvironmentDto(DateTime? DateTime, string? Ip, string? Network, string? Device, string? Channel);

    /// <summary>Body of POST /api/v1/authorize. Identify the user by UserId or EmpCode.</summary>
    public record AccessRequestDto(Guid? UserId, string? EmpCode, string App, string Module, string Resource, AccessAction Action,
        Dictionary<string, string>? ResourceAttributes, AccessEnvironmentDto? Env, bool Log = true, string? Route = null);

    public record LoginResultDto(string Token, Guid UserId, Guid TenantId, string FullName, string Email);
}
