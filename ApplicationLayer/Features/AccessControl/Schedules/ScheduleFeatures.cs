using ApplicationLayer.Common;
using ApplicationLayer.Exceptions;
using ApplicationLayer.Features.AccessControl.DTOs;
using ApplicationLayer.Interfaces;
using ApplicationLayer.Mapping;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using FluentValidation;
using VMM.Mediator;

namespace ApplicationLayer.Features.AccessControl.Schedules
{
    public record GetSchedulesQuery(Guid TenantId) : IRequest<List<ScheduleDto>>;

    public class GetSchedulesQueryHandler : IRequestHandler<GetSchedulesQuery, List<ScheduleDto>>
    {
        private readonly IScheduleRepository _schedules;
        public GetSchedulesQueryHandler(IScheduleRepository schedules) => _schedules = schedules;

        public async Task<List<ScheduleDto>> Handle(GetSchedulesQuery request, CancellationToken ct) =>
            (await _schedules.ListAsync(request.TenantId, ct)).Select(s => s.ToDto()).ToList();
    }

    public record SaveScheduleCommand(Guid TenantId, ScheduleDto Schedule) : IRequest<Result<ScheduleDto>>;

    public class SaveScheduleCommandValidator : AbstractValidator<SaveScheduleCommand>
    {
        public SaveScheduleCommandValidator()
        {
            RuleFor(x => x.Schedule.Code).NotEmpty().MaximumLength(50);
            RuleFor(x => x.Schedule.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Schedule.Windows).NotEmpty().WithMessage("A schedule needs at least one time window.");
        }
    }

    public class SaveScheduleCommandHandler : IRequestHandler<SaveScheduleCommand, Result<ScheduleDto>>
    {
        private readonly IScheduleRepository _schedules;
        private readonly IAuditService _audit;

        public SaveScheduleCommandHandler(IScheduleRepository schedules, IAuditService audit)
        {
            _schedules = schedules;
            _audit = audit;
        }

        public async Task<Result<ScheduleDto>> Handle(SaveScheduleCommand request, CancellationToken ct)
        {
            var dto = request.Schedule;
            var all = await _schedules.ListAsync(request.TenantId, ct);
            if (all.Any(s => s.Id != dto.Id && string.Equals(s.Code, dto.Code.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new AppException($"Schedule code {dto.Code} already exists.");

            var existing = all.FirstOrDefault(s => s.Id == dto.Id);
            var before = existing?.ToDto();
            var windows = dto.Windows.Select(w => new TimeWindow(w.Days.Distinct().OrderBy(d => d).ToList(), w.Start, w.End));
            Schedule schedule;
            if (existing is null)
                schedule = new Schedule(dto.Id, request.TenantId, dto.Code, dto.Name, dto.Timezone, dto.ExcludeHolidays, windows);
            else
            {
                schedule = existing;
                schedule.Update(dto.Code, dto.Name, dto.Timezone, dto.ExcludeHolidays, windows);
            }
            await _schedules.SaveAsync(schedule, ct);

            var after = schedule.ToDto();
            await _audit.LogAsync("Schedule", schedule.Id.ToString(), existing is null ? AuditAction.Create : AuditAction.Update,
                $"{(existing is null ? "Created" : "Updated")} schedule {schedule.Name}", before, after, ct);
            return Result<ScheduleDto>.Success(after);
        }
    }

    public record DeleteScheduleCommand(Guid TenantId, Guid Id) : IRequest<Result<Guid>>;

    public class DeleteScheduleCommandHandler : IRequestHandler<DeleteScheduleCommand, Result<Guid>>
    {
        private readonly IScheduleRepository _schedules;
        private readonly IPolicyRepository _policies;
        private readonly IAuditService _audit;

        public DeleteScheduleCommandHandler(IScheduleRepository schedules, IPolicyRepository policies, IAuditService audit)
        {
            _schedules = schedules;
            _policies = policies;
            _audit = audit;
        }

        public async Task<Result<Guid>> Handle(DeleteScheduleCommand request, CancellationToken ct)
        {
            var schedule = await _schedules.GetAsync(request.Id, ct);
            if (schedule is null || schedule.TenantId != request.TenantId) throw new NotFoundException("Schedule", request.Id);
            var users = (await _policies.ListAsync(request.TenantId, ct))
                .Where(p => p.Conditions.Any(c => c.Value == schedule.Id.ToString() || string.Equals(c.Value, schedule.Code, StringComparison.OrdinalIgnoreCase)))
                .Select(p => p.Name).ToList();
            if (users.Count > 0) throw new AppException($"Schedule is used by: {string.Join("; ", users)}");

            await _schedules.DeleteAsync(schedule.Id, ct);
            await _audit.LogAsync("Schedule", schedule.Id.ToString(), AuditAction.Delete, $"Deleted schedule {schedule.Name}", schedule.ToDto(), null, ct);
            return Result<Guid>.Success(schedule.Id);
        }
    }

    public record GetHolidaysQuery(Guid TenantId) : IRequest<List<HolidayDto>>;

    public class GetHolidaysQueryHandler : IRequestHandler<GetHolidaysQuery, List<HolidayDto>>
    {
        private readonly IScheduleRepository _schedules;
        public GetHolidaysQueryHandler(IScheduleRepository schedules) => _schedules = schedules;

        public async Task<List<HolidayDto>> Handle(GetHolidaysQuery request, CancellationToken ct) =>
            (await _schedules.ListHolidaysAsync(request.TenantId, ct)).OrderBy(h => h.Date).Select(h => h.ToDto()).ToList();
    }

    public record AddHolidayCommand(Guid TenantId, HolidayDto Holiday) : IRequest<Result<HolidayDto>>;

    public class AddHolidayCommandHandler : IRequestHandler<AddHolidayCommand, Result<HolidayDto>>
    {
        private readonly IScheduleRepository _schedules;
        private readonly IAuditService _audit;

        public AddHolidayCommandHandler(IScheduleRepository schedules, IAuditService audit)
        {
            _schedules = schedules;
            _audit = audit;
        }

        public async Task<Result<HolidayDto>> Handle(AddHolidayCommand request, CancellationToken ct)
        {
            var dto = request.Holiday;
            var holiday = new Holiday(dto.Id, request.TenantId, dto.Date, dto.Name, dto.Regions);
            await _schedules.SaveHolidayAsync(holiday, ct);
            await _audit.LogAsync("Holiday", holiday.Id.ToString(), AuditAction.Create, $"Added holiday {holiday.Name} on {holiday.Date:yyyy-MM-dd}", null, holiday.ToDto(), ct);
            return Result<HolidayDto>.Success(holiday.ToDto());
        }
    }

    public record DeleteHolidayCommand(Guid TenantId, Guid Id) : IRequest<Result<Guid>>;

    public class DeleteHolidayCommandHandler : IRequestHandler<DeleteHolidayCommand, Result<Guid>>
    {
        private readonly IScheduleRepository _schedules;
        private readonly IAuditService _audit;

        public DeleteHolidayCommandHandler(IScheduleRepository schedules, IAuditService audit)
        {
            _schedules = schedules;
            _audit = audit;
        }

        public async Task<Result<Guid>> Handle(DeleteHolidayCommand request, CancellationToken ct)
        {
            var holiday = await _schedules.GetHolidayAsync(request.Id, ct);
            if (holiday is null || holiday.TenantId != request.TenantId) throw new NotFoundException("Holiday", request.Id);
            await _schedules.DeleteHolidayAsync(holiday.Id, ct);
            await _audit.LogAsync("Holiday", holiday.Id.ToString(), AuditAction.Delete, $"Removed holiday {holiday.Name}", holiday.ToDto(), null, ct);
            return Result<Guid>.Success(holiday.Id);
        }
    }
}
