using ApplicationLayer.Common;
using ApplicationLayer.Exceptions;
using ApplicationLayer.Features.AccessControl.DTOs;
using ApplicationLayer.Interfaces;
using ApplicationLayer.Mapping;
using DomainLayer.Enumerations;
using MediatR;

namespace ApplicationLayer.Features.AccessControl.Logs
{
    public record GetAuditLogsQuery(Guid TenantId, int Take = 1000) : IRequest<List<AuditLogDto>>;

    public class GetAuditLogsQueryHandler : IRequestHandler<GetAuditLogsQuery, List<AuditLogDto>>
    {
        private readonly ILogRepository _logs;
        public GetAuditLogsQueryHandler(ILogRepository logs) => _logs = logs;

        public async Task<List<AuditLogDto>> Handle(GetAuditLogsQuery q, CancellationToken ct) =>
            (await _logs.ListAuditAsync(q.TenantId, Math.Clamp(q.Take, 1, 10_000), ct)).Select(a => a.ToDto()).ToList();
    }

    public record GetUserActionsQuery(Guid TenantId, int Take = 1000) : IRequest<List<UserActionLogDto>>;

    public class GetUserActionsQueryHandler : IRequestHandler<GetUserActionsQuery, List<UserActionLogDto>>
    {
        private readonly ILogRepository _logs;
        public GetUserActionsQueryHandler(ILogRepository logs) => _logs = logs;

        public async Task<List<UserActionLogDto>> Handle(GetUserActionsQuery q, CancellationToken ct) =>
            (await _logs.ListUserActionsAsync(q.TenantId, Math.Clamp(q.Take, 1, 10_000), ct)).Select(a => a.ToDto()).ToList();
    }

    public record GetExceptionLogsQuery(Guid TenantId, int Take = 1000) : IRequest<List<ExceptionLogDto>>;

    public class GetExceptionLogsQueryHandler : IRequestHandler<GetExceptionLogsQuery, List<ExceptionLogDto>>
    {
        private readonly ILogRepository _logs;
        public GetExceptionLogsQueryHandler(ILogRepository logs) => _logs = logs;

        public async Task<List<ExceptionLogDto>> Handle(GetExceptionLogsQuery q, CancellationToken ct) =>
            (await _logs.ListExceptionsAsync(q.TenantId, Math.Clamp(q.Take, 1, 10_000), ct)).Select(e => e.ToDto()).ToList();
    }

    public record ResolveExceptionCommand(Guid TenantId, Guid Id) : IRequest<Result<ExceptionLogDto>>;

    public class ResolveExceptionCommandHandler : IRequestHandler<ResolveExceptionCommand, Result<ExceptionLogDto>>
    {
        private readonly ILogRepository _logs;
        private readonly IAuditService _audit;

        public ResolveExceptionCommandHandler(ILogRepository logs, IAuditService audit)
        {
            _logs = logs;
            _audit = audit;
        }

        public async Task<Result<ExceptionLogDto>> Handle(ResolveExceptionCommand request, CancellationToken ct)
        {
            var log = await _logs.GetExceptionAsync(request.Id, ct);
            if (log is null || log.TenantId != request.TenantId) throw new NotFoundException("Exception", request.Id);
            log.Resolve();
            await _logs.SaveExceptionAsync(log, ct);
            await _audit.LogAsync("ExceptionLog", log.Id.ToString(), AuditAction.Update,
                $"Marked exception resolved: {(log.Message.Length > 60 ? log.Message[..60] : log.Message)}", null, null, ct);
            return Result<ExceptionLogDto>.Success(log.ToDto());
        }
    }
}
