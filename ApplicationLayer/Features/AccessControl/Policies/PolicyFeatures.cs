using ApplicationLayer.Common;
using ApplicationLayer.Exceptions;
using ApplicationLayer.Features.AccessControl.DTOs;
using ApplicationLayer.Interfaces;
using ApplicationLayer.Mapping;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using FluentValidation;
using MediatR;
using System.Text.Json;

namespace ApplicationLayer.Features.AccessControl.Policies
{
    public record GetPoliciesQuery(Guid TenantId) : IRequest<List<PolicyDto>>;

    public class GetPoliciesQueryHandler : IRequestHandler<GetPoliciesQuery, List<PolicyDto>>
    {
        private readonly IPolicyRepository _policies;
        private readonly IScheduleRepository _schedules;

        public GetPoliciesQueryHandler(IPolicyRepository policies, IScheduleRepository schedules)
        {
            _policies = policies;
            _schedules = schedules;
        }

        public async Task<List<PolicyDto>> Handle(GetPoliciesQuery request, CancellationToken ct)
        {
            var schedules = await _schedules.ListAsync(request.TenantId, ct);
            return (await _policies.ListAsync(request.TenantId, ct)).OrderBy(p => p.Priority).Select(p => p.ToDto(schedules)).ToList();
        }
    }

    public record SavePolicyCommand(Guid TenantId, PolicyDto Policy) : IRequest<Result<PolicyDto>>;

    public class SavePolicyCommandValidator : AbstractValidator<SavePolicyCommand>
    {
        public SavePolicyCommandValidator()
        {
            RuleFor(x => x.Policy.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Policy.Priority).InclusiveBetween(0, 10_000);
            RuleForEach(x => x.Policy.Conditions).ChildRules(c => c.RuleFor(x => x.Attribute).NotEmpty());
        }
    }

    public class SavePolicyCommandHandler : IRequestHandler<SavePolicyCommand, Result<PolicyDto>>
    {
        private readonly IPolicyRepository _policies;
        private readonly IScheduleRepository _schedules;
        private readonly IAuditService _audit;
        private readonly ICurrentUserService _currentUser;
        private readonly TimeProvider _time;

        public SavePolicyCommandHandler(IPolicyRepository policies, IScheduleRepository schedules, IAuditService audit, ICurrentUserService currentUser, TimeProvider time)
        {
            _policies = policies;
            _schedules = schedules;
            _audit = audit;
            _currentUser = currentUser;
            _time = time;
        }

        public async Task<Result<PolicyDto>> Handle(SavePolicyCommand request, CancellationToken ct)
        {
            var dto = request.Policy;
            var schedules = await _schedules.ListAsync(request.TenantId, ct);
            // schedule conditions must point at a schedule of this tenant
            foreach (var c in dto.Conditions.Where(c => c.Operator is ConditionOperator.InSchedule or ConditionOperator.NotInSchedule))
                if (!schedules.Any(s => s.Id.ToString() == c.Value || string.Equals(s.Code, c.Value, StringComparison.OrdinalIgnoreCase)))
                    throw new AppException($"Schedule '{c.Value}' does not exist in this tenant.");

            var existing = await _policies.GetAsync(dto.Id, ct);
            if (existing != null && existing.TenantId != request.TenantId) throw new NotFoundException("Policy", dto.Id);
            var before = existing?.ToDto(schedules);

            Policy policy;
            if (existing is null)
                policy = new Policy(dto.Id, request.TenantId, dto.Name, dto.Description, dto.Effect, dto.Target.ToTarget(), dto.Match,
                    dto.Conditions.ToConditions(), dto.Priority, dto.IsActive, _currentUser.UserName ?? "ums.admin", LocalClock.Now(_time));
            else
            {
                policy = existing;
                policy.Update(dto.Name, dto.Description, dto.Effect, dto.Target.ToTarget(), dto.Match, dto.Conditions.ToConditions(), dto.Priority, dto.IsActive);
            }
            await _policies.SaveAsync(policy, ct);

            var after = policy.ToDto(schedules);
            string summary;
            if (existing is null) summary = $"Created policy '{policy.Name}' ({policy.Effect})";
            // only the Active switch changed (records holding lists compare by reference, so compare as JSON)
            else if (before!.IsActive != after.IsActive &&
                     JsonSerializer.Serialize(before with { IsActive = after.IsActive, RuleExpression = null }) == JsonSerializer.Serialize(after with { RuleExpression = null }))
                summary = $"{(after.IsActive ? "Enabled" : "Disabled")} policy '{policy.Name}'";
            else summary = $"Updated policy '{policy.Name}' ({policy.Effect})";
            await _audit.LogAsync("Policy", policy.Id.ToString(), existing is null ? AuditAction.Create : AuditAction.Update, summary, before, after, ct);
            return Result<PolicyDto>.Success(after);
        }
    }

    public record DeletePolicyCommand(Guid TenantId, Guid Id) : IRequest<Result<Guid>>;

    public class DeletePolicyCommandHandler : IRequestHandler<DeletePolicyCommand, Result<Guid>>
    {
        private readonly IPolicyRepository _policies;
        private readonly IScheduleRepository _schedules;
        private readonly IAuditService _audit;

        public DeletePolicyCommandHandler(IPolicyRepository policies, IScheduleRepository schedules, IAuditService audit)
        {
            _policies = policies;
            _schedules = schedules;
            _audit = audit;
        }

        public async Task<Result<Guid>> Handle(DeletePolicyCommand request, CancellationToken ct)
        {
            var policy = await _policies.GetAsync(request.Id, ct);
            if (policy is null || policy.TenantId != request.TenantId) throw new NotFoundException("Policy", request.Id);
            await _policies.DeleteAsync(policy.Id, ct);
            await _audit.LogAsync("Policy", policy.Id.ToString(), AuditAction.Delete, $"Deleted policy '{policy.Name}'",
                policy.ToDto(await _schedules.ListAsync(request.TenantId, ct)), null, ct);
            return Result<Guid>.Success(policy.Id);
        }
    }
}
