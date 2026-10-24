using ApplicationLayer.Common;
using ApplicationLayer.Exceptions;
using ApplicationLayer.Features.AccessControl.DTOs;
using ApplicationLayer.Interfaces;
using ApplicationLayer.Mapping;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using FluentValidation;
using MediatR;

namespace ApplicationLayer.Features.AccessControl.Tenants
{
    public record GetTenantsQuery : IRequest<List<TenantDto>>;

    public class GetTenantsQueryHandler : IRequestHandler<GetTenantsQuery, List<TenantDto>>
    {
        private readonly ITenantRepository _tenants;
        public GetTenantsQueryHandler(ITenantRepository tenants) => _tenants = tenants;

        public async Task<List<TenantDto>> Handle(GetTenantsQuery request, CancellationToken ct) =>
            (await _tenants.ListAsync(ct)).OrderBy(t => t.CreatedOn).Select(t => t.ToDto()).ToList();
    }

    public record SaveTenantCommand(TenantDto Tenant) : IRequest<Result<TenantDto>>;

    public class SaveTenantCommandValidator : AbstractValidator<SaveTenantCommand>
    {
        public SaveTenantCommandValidator()
        {
            RuleFor(x => x.Tenant.Code).NotEmpty().MaximumLength(50);
            RuleFor(x => x.Tenant.Name).NotEmpty().MaximumLength(200);
        }
    }

    public class SaveTenantCommandHandler : IRequestHandler<SaveTenantCommand, Result<TenantDto>>
    {
        private readonly ITenantRepository _tenants;
        private readonly IAuditService _audit;
        private readonly TimeProvider _time;

        public SaveTenantCommandHandler(ITenantRepository tenants, IAuditService audit, TimeProvider time)
        {
            _tenants = tenants;
            _audit = audit;
            _time = time;
        }

        public async Task<Result<TenantDto>> Handle(SaveTenantCommand request, CancellationToken ct)
        {
            var dto = request.Tenant;
            var all = await _tenants.ListAsync(ct);
            if (all.Any(t => t.Id != dto.Id && string.Equals(t.Code, dto.Code.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new AppException($"Tenant code {dto.Code} already exists.");

            var existing = all.FirstOrDefault(t => t.Id == dto.Id);
            var before = existing?.ToDto();
            Tenant tenant;
            if (existing is null)
            {
                tenant = new Tenant(dto.Id, dto.Code, dto.Name, dto.IsActive, LocalClock.Today(_time));
                // a new tenant starts with the default security settings
                await _tenants.SaveAsync(tenant, ct);
                await _tenants.SaveSettingsAsync(tenant.Id, SecuritySettings.Default, ct);
            }
            else
            {
                tenant = existing;
                tenant.Update(dto.Code, dto.Name, dto.IsActive);
                await _tenants.SaveAsync(tenant, ct);
            }

            var after = tenant.ToDto();
            await _audit.LogAsync("Tenant", tenant.Id.ToString(), existing is null ? AuditAction.Create : AuditAction.Update,
                $"{(existing is null ? "Created" : "Updated")} tenant {tenant.Name}", before, after, ct);
            return Result<TenantDto>.Success(after);
        }
    }

    public record GetSecuritySettingsQuery(Guid TenantId) : IRequest<SecuritySettings>;

    public class GetSecuritySettingsQueryHandler : IRequestHandler<GetSecuritySettingsQuery, SecuritySettings>
    {
        private readonly ITenantRepository _tenants;
        public GetSecuritySettingsQueryHandler(ITenantRepository tenants) => _tenants = tenants;

        public async Task<SecuritySettings> Handle(GetSecuritySettingsQuery request, CancellationToken ct) =>
            await _tenants.GetSettingsAsync(request.TenantId, ct) ?? SecuritySettings.Default;
    }

    public record SaveSecuritySettingsCommand(Guid TenantId, SecuritySettings Settings) : IRequest<Result<SecuritySettings>>;

    public class SaveSecuritySettingsCommandValidator : AbstractValidator<SaveSecuritySettingsCommand>
    {
        public SaveSecuritySettingsCommandValidator()
        {
            RuleFor(x => x.Settings.PasswordMinLength).InclusiveBetween(8, 128);
            RuleFor(x => x.Settings.MaxFailedLogins).InclusiveBetween(1, 50);
            RuleFor(x => x.Settings.JwtExpiryMinutes).InclusiveBetween(1, 24 * 60);
            RuleFor(x => x.Settings.SessionTimeoutMinutes).GreaterThan(0);
            RuleFor(x => x.Settings.PasswordExpiryDays).GreaterThan(0);
            RuleFor(x => x.Settings.LockoutMinutes).GreaterThan(0);
            RuleFor(x => x.Settings.RefreshTokenDays).GreaterThan(0);
        }
    }

    public class SaveSecuritySettingsCommandHandler : IRequestHandler<SaveSecuritySettingsCommand, Result<SecuritySettings>>
    {
        private readonly ITenantRepository _tenants;
        private readonly IAuditService _audit;

        public SaveSecuritySettingsCommandHandler(ITenantRepository tenants, IAuditService audit)
        {
            _tenants = tenants;
            _audit = audit;
        }

        public async Task<Result<SecuritySettings>> Handle(SaveSecuritySettingsCommand request, CancellationToken ct)
        {
            var tenant = await _tenants.GetAsync(request.TenantId, ct) ?? throw new NotFoundException("Tenant", request.TenantId);
            var old = await _tenants.GetSettingsAsync(tenant.Id, ct);
            await _tenants.SaveSettingsAsync(tenant.Id, request.Settings, ct);
            await _audit.LogAsync("SecuritySettings", tenant.Id.ToString(), AuditAction.Update, $"Updated security settings of {tenant.Name}", old, request.Settings, ct);
            return Result<SecuritySettings>.Success(request.Settings);
        }
    }
}
