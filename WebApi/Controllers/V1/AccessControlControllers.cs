using ApplicationLayer.Common;
using ApplicationLayer.Features.AccessControl.Applications;
using ApplicationLayer.Features.AccessControl.Authorization;
using ApplicationLayer.Features.AccessControl.DTOs;
using ApplicationLayer.Features.AccessControl.Logs;
using ApplicationLayer.Features.AccessControl.Policies;
using ApplicationLayer.Features.AccessControl.Roles;
using ApplicationLayer.Features.AccessControl.Schedules;
using ApplicationLayer.Features.AccessControl.Sessions;
using ApplicationLayer.Features.AccessControl.Tenants;
using ApplicationLayer.Features.AccessControl.UserAccounts;
using ApplicationLayer.Interfaces;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using VMM.Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Filters;
using A = DomainLayer.Enumerations.AccessAction;

namespace WebApi.Controllers.V1
{
    /// <summary>Shared plumbing: the mediator, the tenant of the request (X-Tenant-Id) and Result → HTTP mapping.</summary>
    [ApiController]
    [Produces("application/json")]
    public abstract class AccessControlControllerBase : ControllerBase
    {
        protected readonly IMediator Mediator;
        protected readonly ICurrentTenantService Tenant;

        protected AccessControlControllerBase(IMediator mediator, ICurrentTenantService tenant)
        {
            Mediator = mediator;
            Tenant = tenant;
        }

        protected ActionResult<T> FromResult<T>(Result<T> result) =>
            result.IsSuccess ? Ok(result.Value) : Problem(title: result.Error.Message, statusCode: StatusCodes.Status400BadRequest);

        protected static void EnsureSameId(Guid routeId, Guid bodyId)
        {
            if (routeId != bodyId) throw new ArgumentException("The id in the URL does not match the id in the body.");
        }
    }

    [Route("api/v1/auth")]
    public class AuthController : AccessControlControllerBase
    {
        public AuthController(IMediator mediator, ICurrentTenantService tenant) : base(mediator, tenant) { }

        /// <param name="AppCode">Application being signed in to (default UMS). Single-device login and idle timeout apply per application.</param>
        /// <param name="ClientIp">Browser address when a server-side portal signs the user in on its behalf.</param>
        public record LoginRequest(string Email, string Password, string? AppCode = null, string? ClientIp = null, string? UserAgent = null);
        public record RefreshRequest(string RefreshToken);
        public record LogoutRequest(SessionEndReason Reason = SessionEndReason.SignedOut);

        private ClientInfoDto Client(string? ip, string? userAgent) =>
            new(string.IsNullOrWhiteSpace(ip) ? HttpContext.Connection.RemoteIpAddress?.ToString() : ip,
                string.IsNullOrWhiteSpace(userAgent) ? Request.Headers.UserAgent.ToString() : userAgent);

        /// <summary>401 with a machine-readable "code" (auth.invalid, auth.locked, session.replaced, …) clients can act on.</summary>
        private ObjectResult Unauthorized(Error error) => new(new ProblemDetails
        {
            Title = error.Message,
            Status = StatusCodes.Status401Unauthorized,
            Extensions = { ["code"] = error.Code }
        }) { StatusCode = StatusCodes.Status401Unauthorized };

        /// <summary>Sign in: returns a short-lived access token (JWT with "sid" and "app") and a rotating refresh token.</summary>
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<LoginResultDto>> Login(LoginRequest body, CancellationToken ct)
        {
            var result = await Mediator.Send(new LoginCommand(body.Email, body.Password, body.AppCode, Client(body.ClientIp, body.UserAgent)), ct);
            return result.IsSuccess ? Ok(result.Value) : Unauthorized(result.Error);
        }

        /// <summary>New token pair for a refresh token. The old refresh token stops working; presenting it again ends the session.</summary>
        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<ActionResult<LoginResultDto>> Refresh(RefreshRequest body, CancellationToken ct)
        {
            var result = await Mediator.Send(new RefreshSessionCommand(body.RefreshToken), ct);
            return result.IsSuccess ? Ok(result.Value) : Unauthorized(result.Error);
        }

        /// <summary>Is my session still active? touch=true records user activity (portal keep-alive) and resets the idle timer.</summary>
        [HttpGet("session")]
        [Authorize]
        public async Task<ActionResult<SessionStatusDto>> Session([FromQuery] bool touch, CancellationToken ct) =>
            await Mediator.Send(new GetSessionStatusQuery(CurrentSession(), touch), ct);

        /// <summary>Ends my session (reason SignedOut or Idle).</summary>
        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout(LogoutRequest? body, CancellationToken ct)
        {
            await Mediator.Send(new LogoutCommand(CurrentSession(), body?.Reason ?? SessionEndReason.SignedOut), ct);
            return NoContent();
        }

        private Guid CurrentSession() =>
            Guid.TryParse(User.FindFirst(Infrastructure.Identity.IdentityService.SessionClaim)?.Value, out var sid)
                ? sid
                : throw new UnauthorizedAccessException("The token has no session.");
    }

    /// <summary>Sign-in sessions of the tenant: who is signed in where, and ending a session (e.g. a lost device).</summary>
    [Route("api/v1/sessions")]
    public class SessionsController : AccessControlControllerBase
    {
        public SessionsController(IMediator mediator, ICurrentTenantService tenant) : base(mediator, tenant) { }

        [HttpGet, RequirePermission("SETUP", "SESSION", A.View)]
        public async Task<ActionResult<List<UserSessionDto>>> List([FromQuery] bool activeOnly = true, [FromQuery] int take = 500, CancellationToken ct = default) =>
            await Mediator.Send(new ListSessionsQuery(Tenant.TenantId, activeOnly, take), ct);

        [HttpDelete("{id:guid}"), RequirePermission("SETUP", "SESSION", A.Delete)]
        public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
        {
            await Mediator.Send(new RevokeSessionCommand(Tenant.TenantId, id), ct);
            return NoContent();
        }
    }

    [Route("api/v1/tenants")]
    public class TenantsController : AccessControlControllerBase
    {
        public TenantsController(IMediator mediator, ICurrentTenantService tenant) : base(mediator, tenant) { }

        [HttpGet, RequirePermission("SETUP", "TENANT", A.View)]
        public async Task<ActionResult<List<TenantDto>>> List(CancellationToken ct) => await Mediator.Send(new GetTenantsQuery(), ct);

        [HttpPut("{id:guid}"), RequirePermission("SETUP", "TENANT", A.Edit)]
        public async Task<ActionResult<TenantDto>> Save(Guid id, TenantDto body, CancellationToken ct)
        {
            EnsureSameId(id, body.Id);
            return FromResult(await Mediator.Send(new SaveTenantCommand(body), ct));
        }

        [HttpGet("{id:guid}/settings"), RequirePermission("SETUP", "SETTINGS", A.View)]
        public async Task<ActionResult<SecuritySettings>> GetSettings(Guid id, CancellationToken ct) => await Mediator.Send(new GetSecuritySettingsQuery(id), ct);

        [HttpPut("{id:guid}/settings"), RequirePermission("SETUP", "SETTINGS", A.Edit)]
        public async Task<ActionResult<SecuritySettings>> SaveSettings(Guid id, SecuritySettings body, CancellationToken ct) =>
            FromResult(await Mediator.Send(new SaveSecuritySettingsCommand(id, body), ct));
    }

    [Route("api/v1/applications")]
    public class ApplicationsController : AccessControlControllerBase
    {
        public ApplicationsController(IMediator mediator, ICurrentTenantService tenant) : base(mediator, tenant) { }

        /// <summary>Applications enabled for the current tenant; all=true returns every registered application.</summary>
        [HttpGet, RequirePermission("SETUP", "APPLICATION", A.View)]
        public async Task<ActionResult<List<ApplicationDto>>> List([FromQuery] bool all, CancellationToken ct) =>
            await Mediator.Send(new GetApplicationsQuery(all ? null : Tenant.TenantId), ct);

        [HttpPut("{id:guid}"), RequirePermission("SETUP", "APPLICATION", A.Edit)]
        public async Task<ActionResult<ApplicationDto>> Save(Guid id, ApplicationDto body, CancellationToken ct)
        {
            EnsureSameId(id, body.Id);
            return FromResult(await Mediator.Send(new SaveApplicationCommand(body), ct));
        }

        [HttpDelete("{id:guid}"), RequirePermission("SETUP", "APPLICATION", A.Delete)]
        public async Task<ActionResult<Guid>> Delete(Guid id, CancellationToken ct) => FromResult(await Mediator.Send(new DeleteApplicationCommand(id), ct));
    }

    [Route("api/v1/roles")]
    public class RolesController : AccessControlControllerBase
    {
        public RolesController(IMediator mediator, ICurrentTenantService tenant) : base(mediator, tenant) { }

        [HttpGet, RequirePermission("ROLES", "ROLE", A.View)]
        public async Task<ActionResult<List<RoleDto>>> List(CancellationToken ct) => await Mediator.Send(new GetRolesQuery(Tenant.TenantId), ct);

        [HttpPut("{id:guid}"), RequirePermission("ROLES", "ROLE", A.Edit)]
        public async Task<ActionResult<RoleDto>> Save(Guid id, RoleDto body, CancellationToken ct)
        {
            EnsureSameId(id, body.Id);
            return FromResult(await Mediator.Send(new SaveRoleCommand(Tenant.TenantId, body), ct));
        }

        [HttpDelete("{id:guid}"), RequirePermission("ROLES", "ROLE", A.Delete)]
        public async Task<ActionResult<Guid>> Delete(Guid id, CancellationToken ct) => FromResult(await Mediator.Send(new DeleteRoleCommand(Tenant.TenantId, id), ct));
    }

    [Route("api/v1/users")]
    public class UserAccountsController : AccessControlControllerBase
    {
        public UserAccountsController(IMediator mediator, ICurrentTenantService tenant) : base(mediator, tenant) { }

        [HttpGet, RequirePermission("USERS", "USER", A.View)]
        public async Task<ActionResult<List<UserAccountDto>>> List([FromQuery] string? search, [FromQuery] string? department, [FromQuery] Guid? roleId,
            [FromQuery] string? region, [FromQuery] UserStatus? status, CancellationToken ct) =>
            await Mediator.Send(new GetUserAccountsQuery(Tenant.TenantId, search, department, roleId, region, status), ct);

        [HttpGet("{id:guid}"), RequirePermission("USERS", "USER", A.View)]
        public async Task<ActionResult<UserAccountDto>> Get(Guid id, CancellationToken ct) => await Mediator.Send(new GetUserAccountQuery(Tenant.TenantId, id), ct);

        [HttpPut("{id:guid}"), RequirePermission("USERS", "USER", A.Edit)]
        public async Task<ActionResult<UserAccountDto>> Save(Guid id, UserAccountDto body, CancellationToken ct)
        {
            EnsureSameId(id, body.Id);
            return FromResult(await Mediator.Send(new SaveUserAccountCommand(Tenant.TenantId, body), ct));
        }

        public record BulkRequest(List<Guid> UserIds, UserStatus? Status, RoleAssignmentDto? AssignRole);

        [HttpPost("bulk"), RequirePermission("USERS", "USER", A.Edit)]
        public async Task<ActionResult<int>> Bulk(BulkRequest body, CancellationToken ct) =>
            FromResult(await Mediator.Send(new BulkUpdateUsersCommand(Tenant.TenantId, body.UserIds, body.Status, body.AssignRole), ct));

        [HttpPost("import"), RequirePermission("USERS", "USER", A.Create)]
        public async Task<ActionResult<ImportResultDto>> Import(List<UserAccountDto> body, CancellationToken ct) =>
            FromResult(await Mediator.Send(new ImportUsersCommand(Tenant.TenantId, body), ct));

        [HttpGet("{id:guid}/effective-permissions"), RequirePermission("USERS", "USER", A.View)]
        public async Task<ActionResult<List<EffectivePermissionDto>>> EffectivePermissions(Guid id, CancellationToken ct) =>
            await Mediator.Send(new GetEffectivePermissionsQuery(Tenant.TenantId, id), ct);

        public record PasswordRequest(string Password);

        [HttpPost("{id:guid}/password"), RequirePermission("USERS", "USER", A.Edit)]
        public async Task<ActionResult<Guid>> SetPassword(Guid id, PasswordRequest body, CancellationToken ct) =>
            FromResult(await Mediator.Send(new SetPasswordCommand(Tenant.TenantId, id, body.Password), ct));
    }

    [Route("api/v1/policies")]
    public class PoliciesController : AccessControlControllerBase
    {
        public PoliciesController(IMediator mediator, ICurrentTenantService tenant) : base(mediator, tenant) { }

        [HttpGet, RequirePermission("POLICIES", "POLICY", A.View)]
        public async Task<ActionResult<List<PolicyDto>>> List(CancellationToken ct) => await Mediator.Send(new GetPoliciesQuery(Tenant.TenantId), ct);

        [HttpPut("{id:guid}"), RequirePermission("POLICIES", "POLICY", A.Edit)]
        public async Task<ActionResult<PolicyDto>> Save(Guid id, PolicyDto body, CancellationToken ct)
        {
            EnsureSameId(id, body.Id);
            return FromResult(await Mediator.Send(new SavePolicyCommand(Tenant.TenantId, body), ct));
        }

        [HttpDelete("{id:guid}"), RequirePermission("POLICIES", "POLICY", A.Delete)]
        public async Task<ActionResult<Guid>> Delete(Guid id, CancellationToken ct) => FromResult(await Mediator.Send(new DeletePolicyCommand(Tenant.TenantId, id), ct));
    }

    [Route("api/v1")]
    public class SchedulesController : AccessControlControllerBase
    {
        public SchedulesController(IMediator mediator, ICurrentTenantService tenant) : base(mediator, tenant) { }

        [HttpGet("schedules"), RequirePermission("POLICIES", "SCHEDULE", A.View)]
        public async Task<ActionResult<List<ScheduleDto>>> List(CancellationToken ct) => await Mediator.Send(new GetSchedulesQuery(Tenant.TenantId), ct);

        [HttpPut("schedules/{id:guid}"), RequirePermission("POLICIES", "SCHEDULE", A.Edit)]
        public async Task<ActionResult<ScheduleDto>> Save(Guid id, ScheduleDto body, CancellationToken ct)
        {
            EnsureSameId(id, body.Id);
            return FromResult(await Mediator.Send(new SaveScheduleCommand(Tenant.TenantId, body), ct));
        }

        [HttpDelete("schedules/{id:guid}"), RequirePermission("POLICIES", "SCHEDULE", A.Delete)]
        public async Task<ActionResult<Guid>> Delete(Guid id, CancellationToken ct) => FromResult(await Mediator.Send(new DeleteScheduleCommand(Tenant.TenantId, id), ct));

        [HttpGet("holidays"), RequirePermission("POLICIES", "SCHEDULE", A.View)]
        public async Task<ActionResult<List<HolidayDto>>> Holidays(CancellationToken ct) => await Mediator.Send(new GetHolidaysQuery(Tenant.TenantId), ct);

        [HttpPost("holidays"), RequirePermission("POLICIES", "SCHEDULE", A.Create)]
        public async Task<ActionResult<HolidayDto>> AddHoliday(HolidayDto body, CancellationToken ct) =>
            FromResult(await Mediator.Send(new AddHolidayCommand(Tenant.TenantId, body), ct));

        [HttpDelete("holidays/{id:guid}"), RequirePermission("POLICIES", "SCHEDULE", A.Delete)]
        public async Task<ActionResult<Guid>> DeleteHoliday(Guid id, CancellationToken ct) =>
            FromResult(await Mediator.Send(new DeleteHolidayCommand(Tenant.TenantId, id), ct));
    }

    /// <summary>Policy Decision Point used by every protected application.</summary>
    [Route("api/v1/authorize")]
    public class AuthorizeController : AccessControlControllerBase
    {
        public AuthorizeController(IMediator mediator, ICurrentTenantService tenant) : base(mediator, tenant) { }

        /// <summary>Can this user perform this action on this resource now? Returns the decision with a layer-by-layer explanation.</summary>
        [HttpPost, RequirePermission("POLICIES", "DECISION", A.View)]
        public async Task<ActionResult<DomainLayer.Authorization.AccessDecision>> Check(AccessRequestDto body, CancellationToken ct) =>
            await Mediator.Send(new CheckAccessQuery(Tenant.TenantId, body, HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""), ct);

        /// <summary>The same request evaluated for every hour of the week that contains env.dateTime (used by the simulator heat-map).</summary>
        [HttpPost("week"), RequirePermission("POLICIES", "DECISION", A.View)]
        public async Task<ActionResult<List<WeekCellDto>>> Week(AccessRequestDto body, CancellationToken ct) =>
            await Mediator.Send(new CheckAccessWeekQuery(Tenant.TenantId, body), ct);
    }

    [Route("api/v1")]
    public class LogsController : AccessControlControllerBase
    {
        public LogsController(IMediator mediator, ICurrentTenantService tenant) : base(mediator, tenant) { }

        [HttpGet("audit-logs"), RequirePermission("LOGS", "AUDIT_LOG", A.View)]
        public async Task<ActionResult<List<AuditLogDto>>> Audit([FromQuery] int take = 1000, CancellationToken ct = default) =>
            await Mediator.Send(new GetAuditLogsQuery(Tenant.TenantId, take), ct);

        [HttpGet("user-actions"), RequirePermission("LOGS", "ACTION_LOG", A.View)]
        public async Task<ActionResult<List<UserActionLogDto>>> UserActions([FromQuery] int take = 1000, CancellationToken ct = default) =>
            await Mediator.Send(new GetUserActionsQuery(Tenant.TenantId, take), ct);

        [HttpGet("exceptions"), RequirePermission("LOGS", "EXCEPTION_LOG", A.View)]
        public async Task<ActionResult<List<ExceptionLogDto>>> Exceptions([FromQuery] int take = 1000, CancellationToken ct = default) =>
            await Mediator.Send(new GetExceptionLogsQuery(Tenant.TenantId, take), ct);

        [HttpPut("exceptions/{id:guid}/resolve"), RequirePermission("LOGS", "EXCEPTION_LOG", A.Edit)]
        public async Task<ActionResult<ExceptionLogDto>> Resolve(Guid id, CancellationToken ct) =>
            FromResult(await Mediator.Send(new ResolveExceptionCommand(Tenant.TenantId, id), ct));
    }
}

namespace WebApi.Controllers.V1
{
    using ApplicationLayer.Features.AccessControl.Authorization;
    using DomainLayer.Authorization;
    using System.Security.Claims;

    /// <summary>
    /// Self-service endpoints for applications protected by UMS (e.g. the Asset Management System). They forward the signed-in
    /// user's UMS token; the user and tenant are taken from the token, never from the request.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/me")]
    [Produces("application/json")]
    public class MeController : ControllerBase
    {
        private readonly IMediator _mediator;
        public MeController(IMediator mediator) => _mediator = mediator;

        public record MyAccessBatchRequest(List<MyAccessCheckDto> Checks, AccessEnvironmentDto? Env, bool Log = false);
        public record MyAccessRequest(string App, string Module, string Resource, AccessAction Action, Dictionary<string, string>? ResourceAttributes,
            AccessEnvironmentDto? Env, bool Log = true, string? Route = null);

        private (Guid TenantId, Guid UserId) Caller()
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
                !Guid.TryParse(User.FindFirstValue(Infrastructure.Services.CurrentTenantService.ClaimType), out var tenantId))
                throw new UnauthorizedAccessException("The token does not identify a UMS user.");
            return (tenantId, userId);
        }

        private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

        /// <summary>May I perform this action on this resource now? Logged to the user action trail by default.</summary>
        [HttpPost("authorize")]
        public async Task<ActionResult<AccessDecision>> Authorize(MyAccessRequest body, CancellationToken ct)
        {
            var (tenantId, userId) = Caller();
            var check = new MyAccessCheckDto(body.App, body.Module, body.Resource, body.Action, body.ResourceAttributes, body.Route);
            var result = await _mediator.Send(new CheckMyAccessBatchQuery(tenantId, userId, new List<MyAccessCheckDto> { check }, body.Env, body.Log, Ip), ct);
            return result[0];
        }

        /// <summary>Many questions at once (list filtering, showing/hiding actions). Not logged unless log=true.</summary>
        [HttpPost("authorize/batch")]
        public async Task<ActionResult<List<AccessDecision>>> AuthorizeBatch(MyAccessBatchRequest body, CancellationToken ct)
        {
            var (tenantId, userId) = Caller();
            return await _mediator.Send(new CheckMyAccessBatchQuery(tenantId, userId, body.Checks, body.Env, body.Log, Ip), ct);
        }

        /// <summary>Colleagues in my tenant (minimal profile) for "assign to…" pickers.</summary>
        [HttpGet("directory")]
        public async Task<ActionResult<List<DirectoryEntryDto>>> Directory([FromQuery] string? search, [FromQuery] int take = 50, CancellationToken ct = default)
        {
            var (tenantId, _) = Caller();
            return await _mediator.Send(new GetDirectoryQuery(tenantId, search, take), ct);
        }
    }
}
