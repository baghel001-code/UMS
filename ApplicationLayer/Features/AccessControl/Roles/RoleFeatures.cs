using ApplicationLayer.Common;
using ApplicationLayer.Exceptions;
using ApplicationLayer.Features.AccessControl.DTOs;
using ApplicationLayer.Interfaces;
using ApplicationLayer.Mapping;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using FluentValidation;
using MediatR;

namespace ApplicationLayer.Features.AccessControl.Roles
{
    public record GetRolesQuery(Guid TenantId) : IRequest<List<RoleDto>>;

    public class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, List<RoleDto>>
    {
        private readonly IRoleRepository _roles;
        public GetRolesQueryHandler(IRoleRepository roles) => _roles = roles;

        public async Task<List<RoleDto>> Handle(GetRolesQuery request, CancellationToken ct) =>
            (await _roles.ListAsync(request.TenantId, ct)).Select(r => r.ToDto()).ToList();
    }

    public record SaveRoleCommand(Guid TenantId, RoleDto Role) : IRequest<Result<RoleDto>>;

    public class SaveRoleCommandValidator : AbstractValidator<SaveRoleCommand>
    {
        public SaveRoleCommandValidator()
        {
            RuleFor(x => x.Role.Code).NotEmpty().MaximumLength(50);
            RuleFor(x => x.Role.Name).NotEmpty().MaximumLength(200);
        }
    }

    public class SaveRoleCommandHandler : IRequestHandler<SaveRoleCommand, Result<RoleDto>>
    {
        private readonly IRoleRepository _roles;
        private readonly IAuditService _audit;

        public SaveRoleCommandHandler(IRoleRepository roles, IAuditService audit)
        {
            _roles = roles;
            _audit = audit;
        }

        public async Task<Result<RoleDto>> Handle(SaveRoleCommand request, CancellationToken ct)
        {
            var dto = request.Role;
            var tenantRoles = await _roles.ListAsync(request.TenantId, ct);
            if (tenantRoles.Any(r => r.Id != dto.Id && string.Equals(r.Code, dto.Code.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new AppException($"Role code {dto.Code} already exists in this tenant.");

            var existing = await _roles.GetAsync(dto.Id, ct);
            if (existing != null && existing.TenantId != request.TenantId)
                throw new NotFoundException("Role", dto.Id);

            var before = existing?.ToDto();
            Role role;
            if (existing is null)
                // new roles are never system roles: those are seeded
                role = new Role(dto.Id, request.TenantId, dto.Code, dto.Name, dto.Description, dto.AppId, dto.Permissions, false, dto.IsActive);
            else
            {
                role = existing;
                role.Update(dto.Code, dto.Name, dto.Description, dto.AppId, dto.Permissions, dto.IsActive);
            }
            await _roles.SaveAsync(role, ct);

            var after = role.ToDto();
            var added = after.Permissions.Except(before?.Permissions ?? new List<string>()).Count();
            var removed = (before?.Permissions ?? new List<string>()).Except(after.Permissions).Count();
            var summary = $"{(existing is null ? "Created" : "Updated")} role {role.Code}"
                + (added > 0 ? $" +{added} permission(s)" : "") + (removed > 0 ? $" −{removed} permission(s)" : "");
            await _audit.LogAsync("Role", role.Id.ToString(), existing is null ? AuditAction.Create : AuditAction.Update, summary, before, after, ct);
            return Result<RoleDto>.Success(after);
        }
    }

    public record DeleteRoleCommand(Guid TenantId, Guid Id) : IRequest<Result<Guid>>;

    public class DeleteRoleCommandHandler : IRequestHandler<DeleteRoleCommand, Result<Guid>>
    {
        private readonly IRoleRepository _roles;
        private readonly IUserAccountRepository _users;
        private readonly IAuditService _audit;

        public DeleteRoleCommandHandler(IRoleRepository roles, IUserAccountRepository users, IAuditService audit)
        {
            _roles = roles;
            _users = users;
            _audit = audit;
        }

        public async Task<Result<Guid>> Handle(DeleteRoleCommand request, CancellationToken ct)
        {
            var role = await _roles.GetAsync(request.Id, ct);
            if (role is null || role.TenantId != request.TenantId) throw new NotFoundException("Role", request.Id);
            if (role.IsSystem) throw new AppException("System roles cannot be deleted.");
            var members = await _users.CountWithRoleAsync(role.Id, ct);
            if (members > 0) throw new AppException($"Role {role.Code} is assigned to {members} user(s). Revoke it first.");

            await _roles.DeleteAsync(role.Id, ct);
            await _audit.LogAsync("Role", role.Id.ToString(), AuditAction.Delete, $"Deleted role {role.Code}", role.ToDto(), null, ct);
            return Result<Guid>.Success(role.Id);
        }
    }
}
