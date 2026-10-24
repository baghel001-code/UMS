using ApplicationLayer.Common;
using ApplicationLayer.Exceptions;
using ApplicationLayer.Features.AccessControl.DTOs;
using ApplicationLayer.Interfaces;
using ApplicationLayer.Mapping;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using FluentValidation;
using VMM.Mediator;

namespace ApplicationLayer.Features.AccessControl.Applications
{
    /// <summary>Applications enabled for <paramref name="TenantId"/>, or every registered application when null.</summary>
    public record GetApplicationsQuery(Guid? TenantId) : IRequest<List<ApplicationDto>>;

    public class GetApplicationsQueryHandler : IRequestHandler<GetApplicationsQuery, List<ApplicationDto>>
    {
        private readonly IApplicationRepository _apps;
        public GetApplicationsQueryHandler(IApplicationRepository apps) => _apps = apps;

        public async Task<List<ApplicationDto>> Handle(GetApplicationsQuery request, CancellationToken ct) =>
            (await _apps.ListAsync(request.TenantId, ct)).OrderBy(a => a.Name).Select(a => a.ToDto()).ToList();
    }

    public record SaveApplicationCommand(ApplicationDto Application) : IRequest<Result<ApplicationDto>>;

    public class SaveApplicationCommandValidator : AbstractValidator<SaveApplicationCommand>
    {
        public SaveApplicationCommandValidator()
        {
            RuleFor(x => x.Application.Code).NotEmpty().MaximumLength(30);
            RuleFor(x => x.Application.Name).NotEmpty().MaximumLength(200);
            RuleForEach(x => x.Application.Modules).ChildRules(m =>
            {
                m.RuleFor(x => x.Code).NotEmpty();
                m.RuleForEach(x => x.Resources).ChildRules(r =>
                {
                    r.RuleFor(x => x.Code).NotEmpty();
                    r.RuleFor(x => x.Actions).NotEmpty().WithMessage("Each resource needs at least one action.");
                });
            });
        }
    }

    public class SaveApplicationCommandHandler : IRequestHandler<SaveApplicationCommand, Result<ApplicationDto>>
    {
        private readonly IApplicationRepository _apps;
        private readonly IAuditService _audit;

        public SaveApplicationCommandHandler(IApplicationRepository apps, IAuditService audit)
        {
            _apps = apps;
            _audit = audit;
        }

        public async Task<Result<ApplicationDto>> Handle(SaveApplicationCommand request, CancellationToken ct)
        {
            var dto = request.Application;
            var all = await _apps.ListAsync(null, ct);
            if (all.Any(a => a.Id != dto.Id && string.Equals(a.Code, dto.Code.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new AppException($"Application code {dto.Code} already exists.");

            var existing = all.FirstOrDefault(a => a.Id == dto.Id);
            var before = existing?.ToDto();
            Application app;
            if (existing is null)
                app = new Application(dto.Id, dto.Code, dto.Name, dto.Type, dto.Vendor, dto.OwnerDept, dto.Description, dto.IsActive, dto.TenantIds, dto.Modules.ToModules());
            else
            {
                app = existing;
                app.Update(dto.Code, dto.Name, dto.Type, dto.Vendor, dto.OwnerDept, dto.Description, dto.IsActive, dto.TenantIds, dto.Modules.ToModules());
            }
            await _apps.SaveAsync(app, ct);

            var after = app.ToDto();
            await _audit.LogAsync("Application", app.Id.ToString(), existing is null ? AuditAction.Create : AuditAction.Update,
                $"{(existing is null ? "Registered" : "Updated")} application {app.Code}", before, after, ct);
            return Result<ApplicationDto>.Success(after);
        }
    }

    public record DeleteApplicationCommand(Guid Id) : IRequest<Result<Guid>>;

    public class DeleteApplicationCommandHandler : IRequestHandler<DeleteApplicationCommand, Result<Guid>>
    {
        private readonly IApplicationRepository _apps;
        private readonly IAuditService _audit;

        public DeleteApplicationCommandHandler(IApplicationRepository apps, IAuditService audit)
        {
            _apps = apps;
            _audit = audit;
        }

        public async Task<Result<Guid>> Handle(DeleteApplicationCommand request, CancellationToken ct)
        {
            var app = await _apps.GetAsync(request.Id, ct) ?? throw new NotFoundException("Application", request.Id);
            await _apps.DeleteAsync(app.Id, ct);
            // roles keep their APP.* keys; they simply stop matching anything
            await _audit.LogAsync("Application", app.Id.ToString(), AuditAction.Delete, $"Deleted application {app.Code}", app.ToDto(), null, ct);
            return Result<Guid>.Success(app.Id);
        }
    }
}
