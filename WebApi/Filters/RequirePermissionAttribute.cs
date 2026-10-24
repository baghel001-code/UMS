using ApplicationLayer.Common;
using ApplicationLayer.Interfaces;
using DomainLayer.Authorization;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using DomainLayer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Net;
using System.Security.Claims;

namespace WebApi.Filters
{
    /// <summary>
    /// UMS protects its own admin API with its own decision engine: the caller must be granted
    /// UMS.{Module}.{Resource}.{Action} (roles + ABAC policies) in their tenant.
    /// Skipped when Security:RequireAuthentication is false (local development).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class RequirePermissionAttribute : TypeFilterAttribute
    {
        public RequirePermissionAttribute(string module, string resource, AccessAction action) : base(typeof(RequirePermissionFilter))
        {
            Arguments = new object[] { module, resource, action };
        }
    }

    public sealed class RequirePermissionFilter : IAsyncActionFilter
    {
        private readonly string _module;
        private readonly string _resource;
        private readonly AccessAction _action;
        private readonly IConfiguration _config;
        private readonly IAccessContextProvider _context;
        private readonly ILogRepository _logs;
        private readonly TimeProvider _time;

        public RequirePermissionFilter(string module, string resource, AccessAction action, IConfiguration config, IAccessContextProvider context,
            ILogRepository logs, TimeProvider time)
        {
            _module = module;
            _resource = resource;
            _action = action;
            _config = config;
            _context = context;
            _logs = logs;
            _time = time;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
        {
            if (!_config.GetValue("Security:RequireAuthentication", true))
            {
                await next();
                return;
            }

            var principal = ctx.HttpContext.User;
            if (principal.Identity?.IsAuthenticated != true || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                || !Guid.TryParse(principal.FindFirstValue(Infrastructure.Services.CurrentTenantService.ClaimType), out var tenantId))
            {
                ctx.Result = new UnauthorizedResult();
                return;
            }

            // admins work inside their own tenant; only an active SUPER_ADMIN may switch to another tenant
            var header = ctx.HttpContext.Request.Headers[Infrastructure.Services.CurrentTenantService.HeaderName].FirstOrDefault();
            if (Guid.TryParse(header, out var requested) && requested != tenantId && !await IsSuperAdminAsync(tenantId, userId))
            {
                ctx.Result = Forbidden("Tenant: you can only administer your own tenant.");
                return;
            }

            var ip = ctx.HttpContext.Connection.RemoteIpAddress;
            var env = new AccessEnvironment(LocalClock.Now(_time), ip?.ToString() ?? "", IsPrivate(ip) ? "Office LAN" : "Internet", "Managed", "Web");
            var request = new AccessRequest(userId, "UMS", _module, _resource, _action, new Dictionary<string, string>(), env);
            var decision = AccessDecisionService.Decide(request, await _context.LoadAsync(tenantId));

            var mutating = !HttpMethods.IsGet(ctx.HttpContext.Request.Method);
            if (!decision.Allowed || mutating)
            {
                var name = principal.Identity?.Name ?? userId.ToString();
                await _logs.AddUserActionAsync(new UserActionLog(Guid.NewGuid(), tenantId, env.DateTime, userId, name, "UMS",
                    $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}", $"UMS.{_module}.{_resource}.{_action}".ToUpperInvariant(),
                    decision.Allowed ? AccessDecisionResult.Allow : AccessDecisionResult.Deny, decision.Reason, env.Ip));
            }

            if (!decision.Allowed)
            {
                ctx.Result = Forbidden(decision.Reason);
                return;
            }
            await next();
        }

        private async Task<bool> IsSuperAdminAsync(Guid tenantId, Guid userId)
        {
            var data = await _context.LoadAsync(tenantId);
            var user = data.Users.FirstOrDefault(u => u.Id == userId);
            if (user is null) return false;
            return AccessDecisionService.ActiveAssignments(user, data.Roles, LocalClock.Today(_time)).Any(a => a.Role.Code == "SUPER_ADMIN");
        }

        private static ObjectResult Forbidden(string reason) =>
            new(new ProblemDetails { Title = "Access denied", Detail = reason, Status = StatusCodes.Status403Forbidden }) { StatusCode = StatusCodes.Status403Forbidden };

        private static bool IsPrivate(IPAddress? ip)
        {
            if (ip is null) return false;
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            var b = ip.GetAddressBytes();
            return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168));
        }
    }
}
