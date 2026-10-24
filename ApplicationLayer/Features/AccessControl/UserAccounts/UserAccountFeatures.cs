using ApplicationLayer.Common;
using ApplicationLayer.Exceptions;
using ApplicationLayer.Features.AccessControl.DTOs;
using ApplicationLayer.Interfaces;
using ApplicationLayer.Mapping;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using DomainLayer.Services;
using DomainLayer.ValueObjects;
using FluentValidation;
using MediatR;

namespace ApplicationLayer.Features.AccessControl.UserAccounts
{
    // ---------------------------------------------------------------- queries

    public record GetUserAccountsQuery(Guid TenantId, string? Search = null, string? Department = null, Guid? RoleId = null,
        string? Region = null, UserStatus? Status = null) : IRequest<List<UserAccountDto>>;

    public class GetUserAccountsQueryHandler : IRequestHandler<GetUserAccountsQuery, List<UserAccountDto>>
    {
        private readonly IUserAccountRepository _users;
        public GetUserAccountsQueryHandler(IUserAccountRepository users) => _users = users;

        public async Task<List<UserAccountDto>> Handle(GetUserAccountsQuery q, CancellationToken ct)
        {
            IEnumerable<User> users = await _users.ListAsync(q.TenantId, ct);
            if (!string.IsNullOrWhiteSpace(q.Search))
                users = users.Where(u => $"{u.Name} {u.Email.Value} {u.EmpCode} {u.Location} {u.Designation}".Contains(q.Search, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(q.Department)) users = users.Where(u => u.Department == q.Department);
            if (!string.IsNullOrWhiteSpace(q.Region)) users = users.Where(u => u.Region == q.Region);
            if (q.Status is { } status) users = users.Where(u => u.Status == status);
            if (q.RoleId is { } roleId) users = users.Where(u => u.RoleAssignments.Any(a => a.RoleId == roleId));
            return users.OrderBy(u => u.EmpCode).Select(u => u.ToAccountDto()).ToList();
        }
    }

    public record GetUserAccountQuery(Guid TenantId, Guid Id) : IRequest<UserAccountDto>;

    public class GetUserAccountQueryHandler : IRequestHandler<GetUserAccountQuery, UserAccountDto>
    {
        private readonly IUserAccountRepository _users;
        public GetUserAccountQueryHandler(IUserAccountRepository users) => _users = users;

        public async Task<UserAccountDto> Handle(GetUserAccountQuery q, CancellationToken ct)
        {
            var user = await _users.GetAsync(q.Id, ct);
            if (user is null || user.TenantId != q.TenantId) throw new NotFoundException("User", q.Id);
            return user.ToAccountDto();
        }
    }

    /// <summary>What the user's active roles grant today (RBAC only; ABAC policies may still deny at runtime).</summary>
    public record GetEffectivePermissionsQuery(Guid TenantId, Guid UserId) : IRequest<List<EffectivePermissionDto>>;

    public class GetEffectivePermissionsQueryHandler : IRequestHandler<GetEffectivePermissionsQuery, List<EffectivePermissionDto>>
    {
        private readonly IUserAccountRepository _users;
        private readonly IRoleRepository _roles;
        private readonly IApplicationRepository _apps;
        private readonly TimeProvider _time;

        public GetEffectivePermissionsQueryHandler(IUserAccountRepository users, IRoleRepository roles, IApplicationRepository apps, TimeProvider time)
        {
            _users = users;
            _roles = roles;
            _apps = apps;
            _time = time;
        }

        public async Task<List<EffectivePermissionDto>> Handle(GetEffectivePermissionsQuery q, CancellationToken ct)
        {
            var user = await _users.GetAsync(q.UserId, ct);
            if (user is null || user.TenantId != q.TenantId) throw new NotFoundException("User", q.UserId);
            var perms = AccessDecisionService.EffectivePermissions(user, await _roles.ListAsync(q.TenantId, ct),
                await _apps.ListAsync(q.TenantId, ct), LocalClock.Today(_time));
            return perms.Select(p => new EffectivePermissionDto(p.Key, p.ViaRoles.ToList())).ToList();
        }
    }

    // ---------------------------------------------------------------- save

    public record SaveUserAccountCommand(Guid TenantId, UserAccountDto User) : IRequest<Result<UserAccountDto>>;

    public class SaveUserAccountCommandValidator : AbstractValidator<SaveUserAccountCommand>
    {
        public SaveUserAccountCommandValidator()
        {
            RuleFor(x => x.User.FullName).NotEmpty().MinimumLength(3).MaximumLength(200);
            RuleFor(x => x.User.Email).NotEmpty().EmailAddress();
            RuleFor(x => x.User.EmpCode).NotEmpty().MaximumLength(50);
            RuleForEach(x => x.User.Roles).Must(r => r.ValidFrom is null || r.ValidTo is null || r.ValidTo >= r.ValidFrom)
                .WithMessage("Role validity end date is before its start date.");
        }
    }

    public class SaveUserAccountCommandHandler : IRequestHandler<SaveUserAccountCommand, Result<UserAccountDto>>
    {
        private readonly IUserAccountRepository _users;
        private readonly IRoleRepository _roles;
        private readonly IAuditService _audit;
        private readonly ICurrentUserService _currentUser;
        private readonly TimeProvider _time;

        public SaveUserAccountCommandHandler(IUserAccountRepository users, IRoleRepository roles, IAuditService audit,
            ICurrentUserService currentUser, TimeProvider time)
        {
            _users = users;
            _roles = roles;
            _audit = audit;
            _currentUser = currentUser;
            _time = time;
        }

        public async Task<Result<UserAccountDto>> Handle(SaveUserAccountCommand request, CancellationToken ct)
        {
            var dto = request.User;
            var roles = await _roles.ListAsync(request.TenantId, ct);
            var (user, before) = await UserAccountWriter.ApplyAsync(_users, roles, request.TenantId, dto, _currentUser.UserName ?? "ums.admin", LocalClock.Today(_time), ct);

            var code = (Guid id) => roles.FirstOrDefault(r => r.Id == id)?.Code ?? id.ToString();
            var added = user.RoleAssignments.Where(a => before?.Roles.All(b => b.RoleId != a.RoleId) ?? true).Select(a => code(a.RoleId)).ToList();
            var revoked = (before?.Roles ?? new List<RoleAssignmentDto>()).Where(b => user.RoleAssignments.All(a => a.RoleId != b.RoleId)).Select(b => code(b.RoleId)).ToList();
            var parts = new List<string>();
            if (added.Count > 0) parts.Add($"assigned {string.Join(", ", added)}");
            if (revoked.Count > 0) parts.Add($"revoked {string.Join(", ", revoked)}");

            var after = user.ToAccountDto();
            await _audit.LogAsync("User", user.Id.ToString(), before is null ? AuditAction.Create : AuditAction.Update,
                $"{(before is null ? "Created" : "Updated")} user {user.Name}{(parts.Count > 0 ? " — " + string.Join("; ", parts) : "")}", before, after, ct);
            return Result<UserAccountDto>.Success(after);
        }
    }

    /// <summary>Shared create/update logic for single saves and bulk import.</summary>
    internal static class UserAccountWriter
    {
        public static async Task<(User User, UserAccountDto? Before)> ApplyAsync(IUserAccountRepository users, IReadOnlyList<Role> tenantRoles,
            Guid tenantId, UserAccountDto dto, string actor, DateOnly today, CancellationToken ct)
        {
            var unknown = dto.Roles.Where(a => tenantRoles.All(r => r.Id != a.RoleId)).ToList();
            if (unknown.Count > 0) throw new AppException($"Role {unknown[0].RoleId} does not exist in this tenant.");

            var sameCode = await users.GetByEmpCodeAsync(dto.EmpCode.Trim(), ct);
            if (sameCode != null && sameCode.Id != dto.Id) throw new AppException($"Employee code {dto.EmpCode} already exists.");
            var sameEmail = await users.GetByEmailAsync(dto.Email.Trim(), ct);
            if (sameEmail != null && sameEmail.Id != dto.Id) throw new AppException($"Email {dto.Email} is already used by {sameEmail.Name}.");

            var existing = await users.GetAsync(dto.Id, ct);
            if (existing != null && existing.TenantId != tenantId) throw new NotFoundException("User", dto.Id);
            var before = existing?.ToAccountDto();

            var email = new Email(dto.Email.Trim());
            var phone = AccessControlMappings.ParsePhone(dto.Phone);
            var user = existing ?? new User(dto.Id, tenantId, dto.EmpCode, dto.FullName, email, phone);
            user.UpdateProfile(dto.EmpCode, dto.FullName, email, phone, dto.Department, dto.Designation, dto.Location, dto.Region,
                dto.EmploymentType, dto.Status, dto.MfaEnabled, dto.AccountExpiry);
            user.SetAttributes(dto.Attributes);
            user.SetRoleAssignments(dto.Roles.Select(a => a.ToAssignment(actor, today)));
            await users.SaveAsync(user, ct);
            return (user, before);
        }
    }

    // ---------------------------------------------------------------- bulk

    /// <summary>Set the status of many users and/or give them all one more role.</summary>
    public record BulkUpdateUsersCommand(Guid TenantId, List<Guid> UserIds, UserStatus? Status, RoleAssignmentDto? AssignRole) : IRequest<Result<int>>;

    public class BulkUpdateUsersCommandHandler : IRequestHandler<BulkUpdateUsersCommand, Result<int>>
    {
        private readonly IUserAccountRepository _users;
        private readonly IRoleRepository _roles;
        private readonly IAuditService _audit;
        private readonly ICurrentUserService _currentUser;
        private readonly TimeProvider _time;

        public BulkUpdateUsersCommandHandler(IUserAccountRepository users, IRoleRepository roles, IAuditService audit, ICurrentUserService currentUser, TimeProvider time)
        {
            _users = users;
            _roles = roles;
            _audit = audit;
            _currentUser = currentUser;
            _time = time;
        }

        public async Task<Result<int>> Handle(BulkUpdateUsersCommand request, CancellationToken ct)
        {
            Role? role = null;
            if (request.AssignRole != null)
            {
                role = await _roles.GetAsync(request.AssignRole.RoleId, ct);
                if (role is null || role.TenantId != request.TenantId) throw new NotFoundException("Role", request.AssignRole.RoleId);
            }

            var changed = 0;
            foreach (var id in request.UserIds.Distinct())
            {
                var user = await _users.GetAsync(id, ct);
                if (user is null || user.TenantId != request.TenantId) continue;
                if (request.Status is { } status) user.SetStatus(status);
                if (role != null && user.RoleAssignments.All(a => a.RoleId != role.Id))
                    user.AssignRole(request.AssignRole!.ToAssignment(_currentUser.UserName ?? "ums.admin", LocalClock.Today(_time)));
                await _users.SaveAsync(user, ct);
                changed++;
            }

            var what = string.Join(" and ", new[] { request.Status is null ? null : $"set status {request.Status}", role is null ? null : $"assigned {role.Code}" }.Where(x => x != null));
            await _audit.LogAsync("User", string.Join(",", request.UserIds), role is null ? AuditAction.Update : AuditAction.Assign,
                $"Bulk {what} on {changed} user(s)", null, new { request.Status, Role = role?.Code }, ct);
            return Result<int>.Success(changed);
        }
    }

    public record ImportUsersCommand(Guid TenantId, List<UserAccountDto> Users) : IRequest<Result<ImportResultDto>>;

    public record ImportResultDto(int Created, int Skipped, List<string> Errors);

    public class ImportUsersCommandHandler : IRequestHandler<ImportUsersCommand, Result<ImportResultDto>>
    {
        private readonly IUserAccountRepository _users;
        private readonly IRoleRepository _roles;
        private readonly IAuditService _audit;
        private readonly ICurrentUserService _currentUser;
        private readonly TimeProvider _time;

        public ImportUsersCommandHandler(IUserAccountRepository users, IRoleRepository roles, IAuditService audit, ICurrentUserService currentUser, TimeProvider time)
        {
            _users = users;
            _roles = roles;
            _audit = audit;
            _currentUser = currentUser;
            _time = time;
        }

        public async Task<Result<ImportResultDto>> Handle(ImportUsersCommand request, CancellationToken ct)
        {
            var roles = await _roles.ListAsync(request.TenantId, ct);
            var errors = new List<string>();
            var created = 0;
            foreach (var dto in request.Users)
            {
                if (await _users.GetByEmpCodeAsync(dto.EmpCode, ct) != null)
                {
                    errors.Add($"{dto.EmpCode}: already exists");
                    continue;
                }
                try
                {
                    await UserAccountWriter.ApplyAsync(_users, roles, request.TenantId, dto with { Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id },
                        _currentUser.UserName ?? "ums.admin", LocalClock.Today(_time), ct);
                    created++;
                }
                catch (Exception ex) when (ex is AppException or DomainLayer.Exceptions.DomainException or ArgumentException)
                {
                    errors.Add($"{dto.EmpCode}: {ex.Message}");
                }
            }

            await _audit.LogAsync("User", "bulk-import", AuditAction.Create, $"Bulk imported {created} user(s); {errors.Count} skipped", null, new { created, errors }, ct);
            return Result<ImportResultDto>.Success(new ImportResultDto(created, errors.Count, errors));
        }
    }

    // ---------------------------------------------------------------- credentials

    public record SetPasswordCommand(Guid TenantId, Guid UserId, string Password) : IRequest<Result<Guid>>;

    public class SetPasswordCommandValidator : AbstractValidator<SetPasswordCommand>
    {
        public SetPasswordCommandValidator()
        {
            RuleFor(x => x.Password).NotEmpty().MinimumLength(8)
                .Matches("[A-Z]").WithMessage("Password needs an upper-case letter.")
                .Matches("[a-z]").WithMessage("Password needs a lower-case letter.")
                .Matches("[0-9]").WithMessage("Password needs a digit.");
        }
    }

    public class SetPasswordCommandHandler : IRequestHandler<SetPasswordCommand, Result<Guid>>
    {
        private readonly IUserAccountRepository _users;
        private readonly ITenantRepository _tenants;
        private readonly IPasswordHasher _hasher;
        private readonly IAuditService _audit;

        public SetPasswordCommandHandler(IUserAccountRepository users, ITenantRepository tenants, IPasswordHasher hasher, IAuditService audit)
        {
            _users = users;
            _tenants = tenants;
            _hasher = hasher;
            _audit = audit;
        }

        public async Task<Result<Guid>> Handle(SetPasswordCommand request, CancellationToken ct)
        {
            var user = await _users.GetAsync(request.UserId, ct);
            if (user is null || user.TenantId != request.TenantId) throw new NotFoundException("User", request.UserId);
            var settings = await _tenants.GetSettingsAsync(user.TenantId, ct) ?? SecuritySettings.Default;
            if (request.Password.Length < settings.PasswordMinLength)
                throw new AppException($"Password must be at least {settings.PasswordMinLength} characters.");

            user.SetPasswordHash(_hasher.Hash(request.Password));
            await _users.SaveAsync(user, ct);
            await _audit.LogAsync("User", user.Id.ToString(), AuditAction.Reset, $"Password set for {user.Name}", null, null, ct);
            return Result<Guid>.Success(user.Id);
        }
    }
}
