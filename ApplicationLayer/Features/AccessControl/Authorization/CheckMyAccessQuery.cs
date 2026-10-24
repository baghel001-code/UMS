using ApplicationLayer.Common;
using ApplicationLayer.Exceptions;
using ApplicationLayer.Features.AccessControl.DTOs;
using ApplicationLayer.Interfaces;
using DomainLayer.Authorization;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using DomainLayer.Services;
using DomainLayer.ValueObjects;
using FluentValidation;
using VMM.Mediator;

namespace ApplicationLayer.Features.AccessControl.Authorization
{
    /// <summary>One permission question about the signed-in user, e.g. AMS.ASSETS.ASSET.Checkout on asset A-1001 (resource attributes).</summary>
    public record MyAccessCheckDto(string App, string Module, string Resource, AccessAction Action, Dictionary<string, string>? ResourceAttributes, string? Route = null);

    /// <summary>
    /// Decisions for the caller identified by their UMS token. Protected applications (e.g. the Asset Management System) call this
    /// with the user's own token for every action; they never decide access themselves. The user and tenant come from the token,
    /// so a caller can only ask about itself. Batch form is used to filter lists record by record and to show/hide UI actions.
    /// </summary>
    public record CheckMyAccessBatchQuery(Guid TenantId, Guid UserId, List<MyAccessCheckDto> Checks, AccessEnvironmentDto? Env, bool Log, string CallerIp)
        : IRequest<List<AccessDecision>>;

    public class CheckMyAccessBatchQueryValidator : AbstractValidator<CheckMyAccessBatchQuery>
    {
        public CheckMyAccessBatchQueryValidator()
        {
            RuleFor(x => x.Checks).NotEmpty().Must(c => c.Count <= 2000).WithMessage("At most 2000 checks per call.");
            RuleForEach(x => x.Checks).ChildRules(c =>
            {
                c.RuleFor(x => x.App).NotEmpty();
                c.RuleFor(x => x.Module).NotEmpty();
                c.RuleFor(x => x.Resource).NotEmpty();
            });
        }
    }

    public class CheckMyAccessBatchQueryHandler : IRequestHandler<CheckMyAccessBatchQuery, List<AccessDecision>>
    {
        private readonly IAccessContextProvider _context;
        private readonly ILogRepository _logs;
        private readonly TimeProvider _time;

        public CheckMyAccessBatchQueryHandler(IAccessContextProvider context, ILogRepository logs, TimeProvider time)
        {
            _context = context;
            _logs = logs;
            _time = time;
        }

        public async Task<List<AccessDecision>> Handle(CheckMyAccessBatchQuery q, CancellationToken ct)
        {
            // one context load for the whole batch
            var data = await _context.LoadAsync(q.TenantId, ct);
            var user = data.Users.FirstOrDefault(u => u.Id == q.UserId) ?? throw new NotFoundException("User", q.UserId);
            var env = new AccessEnvironment(
                q.Env?.DateTime ?? LocalClock.Now(_time),
                q.Env?.Ip ?? q.CallerIp,
                q.Env?.Network ?? "Office LAN",
                q.Env?.Device ?? "Managed",
                q.Env?.Channel ?? "Web");

            var decisions = new List<AccessDecision>(q.Checks.Count);
            foreach (var c in q.Checks)
            {
                var request = new AccessRequest(user.Id, c.App.Trim().ToUpperInvariant(), c.Module.Trim().ToUpperInvariant(), c.Resource.Trim().ToUpperInvariant(),
                    c.Action, c.ResourceAttributes ?? new Dictionary<string, string>(), env);
                var decision = AccessDecisionService.Decide(request, data);
                decisions.Add(decision);

                if (q.Log)
                    await _logs.AddUserActionAsync(new UserActionLog(Guid.NewGuid(), user.TenantId, LocalClock.Now(_time), user.Id, user.Name, request.App,
                        c.Route ?? $"/{request.Module}/{request.Resource}".ToLowerInvariant(),
                        PermissionKey.Of(request.App, request.Module, request.Resource, request.Action.ToString()),
                        decision.Allowed ? AccessDecisionResult.Allow : AccessDecisionResult.Deny, decision.Reason, env.Ip), ct);
            }
            return decisions;
        }
    }

    /// <summary>Public profile of a colleague, for pickers in protected applications (e.g. "check out to…").</summary>
    public record DirectoryEntryDto(Guid Id, string EmpCode, string FullName, string Email, string Department, string Designation, string Location,
        string Region, EmploymentType EmploymentType, UserStatus Status);

    public record GetDirectoryQuery(Guid TenantId, string? Search, int Take = 50) : IRequest<List<DirectoryEntryDto>>;

    public class GetDirectoryQueryHandler : IRequestHandler<GetDirectoryQuery, List<DirectoryEntryDto>>
    {
        private readonly IUserAccountRepository _users;
        public GetDirectoryQueryHandler(IUserAccountRepository users) => _users = users;

        public async Task<List<DirectoryEntryDto>> Handle(GetDirectoryQuery q, CancellationToken ct)
        {
            IEnumerable<User> users = await _users.ListAsync(q.TenantId, ct);
            if (!string.IsNullOrWhiteSpace(q.Search))
                users = users.Where(u => $"{u.Name} {u.EmpCode} {u.Email.Value} {u.Department} {u.Location}".Contains(q.Search, StringComparison.OrdinalIgnoreCase));
            return users.OrderBy(u => u.Name).Take(Math.Clamp(q.Take, 1, 500))
                .Select(u => new DirectoryEntryDto(u.Id, u.EmpCode, u.Name, u.Email.Value, u.Department, u.Designation, u.Location, u.Region, u.EmploymentType, u.Status))
                .ToList();
        }
    }
}
