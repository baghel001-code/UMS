using ApplicationLayer.Common;
using ApplicationLayer.Exceptions;
using ApplicationLayer.Features.AccessControl.DTOs;
using ApplicationLayer.Interfaces;
using DomainLayer.Authorization;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using DomainLayer.Services;
using DomainLayer.ValueObjects;
using VMM.Mediator;

namespace ApplicationLayer.Features.AccessControl.Authorization
{
    /// <summary>
    /// The Policy Decision Point endpoint used by every protected application and by the admin panel's simulator:
    /// "can this user do this action on this resource, now, from here?" Optionally records the decision in the user action trail.
    /// </summary>
    public record CheckAccessQuery(Guid TenantId, AccessRequestDto Request, string CallerIp) : IRequest<AccessDecision>;

    public class CheckAccessQueryHandler : IRequestHandler<CheckAccessQuery, AccessDecision>
    {
        private readonly IAccessContextProvider _context;
        private readonly IUserAccountRepository _users;
        private readonly ILogRepository _logs;
        private readonly TimeProvider _time;

        public CheckAccessQueryHandler(IAccessContextProvider context, IUserAccountRepository users, ILogRepository logs, TimeProvider time)
        {
            _context = context;
            _users = users;
            _logs = logs;
            _time = time;
        }

        public async Task<AccessDecision> Handle(CheckAccessQuery query, CancellationToken ct)
        {
            var dto = query.Request;
            User? user = dto.UserId is { } id ? await _users.GetAsync(id, ct)
                : !string.IsNullOrWhiteSpace(dto.EmpCode) ? await _users.GetByEmpCodeAsync(dto.EmpCode, ct) : null;
            if (user is null || user.TenantId != query.TenantId)
                throw new NotFoundException("User", (object?)dto.UserId ?? dto.EmpCode ?? "(none)");

            var env = new AccessEnvironment(
                dto.Env?.DateTime ?? LocalClock.Now(_time),
                dto.Env?.Ip ?? query.CallerIp,
                dto.Env?.Network ?? "Office LAN",
                dto.Env?.Device ?? "Managed",
                dto.Env?.Channel ?? "Web");
            var request = new AccessRequest(user.Id, dto.App.Trim().ToUpperInvariant(), dto.Module.Trim().ToUpperInvariant(), dto.Resource.Trim().ToUpperInvariant(),
                dto.Action, dto.ResourceAttributes ?? new Dictionary<string, string>(), env);

            var decision = AccessDecisionService.Decide(request, await _context.LoadAsync(query.TenantId, ct));

            if (dto.Log)
            {
                var key = PermissionKey.Of(request.App, request.Module, request.Resource, request.Action.ToString());
                await _logs.AddUserActionAsync(new UserActionLog(Guid.NewGuid(), user.TenantId, LocalClock.Now(_time), user.Id, user.Name, request.App,
                    dto.Route ?? $"/{request.Module}/{request.Resource}".ToLowerInvariant(), key,
                    decision.Allowed ? AccessDecisionResult.Allow : AccessDecisionResult.Deny, decision.Reason, env.Ip), ct);
            }
            return decision;
        }
    }
}

namespace ApplicationLayer.Features.AccessControl.Authorization
{
    public record WeekCellDto(Weekday Day, int Hour, DateTime At, bool Allowed, string Reason);

    /// <summary>
    /// Evaluates the same request at hh:30 for every hour of the week containing Env.DateTime (Mon–Sun),
    /// so the admin can see when access is allowed. Nothing is logged.
    /// </summary>
    public record CheckAccessWeekQuery(Guid TenantId, AccessRequestDto Request) : IRequest<List<WeekCellDto>>;

    public class CheckAccessWeekQueryHandler : IRequestHandler<CheckAccessWeekQuery, List<WeekCellDto>>
    {
        private readonly IAccessContextProvider _context;
        private readonly IUserAccountRepository _users;
        private readonly TimeProvider _time;

        public CheckAccessWeekQueryHandler(IAccessContextProvider context, IUserAccountRepository users, TimeProvider time)
        {
            _context = context;
            _users = users;
            _time = time;
        }

        public async Task<List<WeekCellDto>> Handle(CheckAccessWeekQuery query, CancellationToken ct)
        {
            var dto = query.Request;
            User? user = dto.UserId is { } id ? await _users.GetAsync(id, ct)
                : !string.IsNullOrWhiteSpace(dto.EmpCode) ? await _users.GetByEmpCodeAsync(dto.EmpCode, ct) : null;
            if (user is null || user.TenantId != query.TenantId)
                throw new NotFoundException("User", (object?)dto.UserId ?? dto.EmpCode ?? "(none)");

            var data = await _context.LoadAsync(query.TenantId, ct);
            var anchor = (dto.Env?.DateTime ?? LocalClock.Now(_time)).Date;
            var monday = anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7));
            var cells = new List<WeekCellDto>(168);
            for (var d = 0; d < 7; d++)
                for (var h = 0; h < 24; h++)
                {
                    var at = monday.AddDays(d).AddHours(h).AddMinutes(30);
                    var env = new AccessEnvironment(at, dto.Env?.Ip ?? "", dto.Env?.Network ?? "Office LAN", dto.Env?.Device ?? "Managed", dto.Env?.Channel ?? "Web");
                    var decision = AccessDecisionService.Decide(new AccessRequest(user.Id, dto.App.ToUpperInvariant(), dto.Module.ToUpperInvariant(),
                        dto.Resource.ToUpperInvariant(), dto.Action, dto.ResourceAttributes ?? new Dictionary<string, string>(), env), data);
                    cells.Add(new WeekCellDto(ScheduleEvaluator.WeekdayOf(at), h, at, decision.Allowed, decision.Reason));
                }
            return cells;
        }
    }
}
