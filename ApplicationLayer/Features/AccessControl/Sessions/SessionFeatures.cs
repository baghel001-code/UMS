using ApplicationLayer.Common;
using ApplicationLayer.Exceptions;
using ApplicationLayer.Features.AccessControl.DTOs;
using ApplicationLayer.Interfaces;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using FluentValidation;
using MediatR;
using System.Security.Cryptography;
using System.Text;

namespace ApplicationLayer.Features.AccessControl.Sessions
{
    /// <summary>
    /// Refresh tokens are "{sessionId:N}.{random}". Only the SHA-256 hash of the whole token is stored, so a database leak
    /// does not reveal usable tokens; the session id prefix lets UMS find the session and detect re-use of an old token.
    /// </summary>
    public static class RefreshTokens
    {
        public static string New(Guid sessionId) =>
            $"{sessionId:N}.{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";

        public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        public static bool TryGetSessionId(string? token, out Guid sessionId)
        {
            sessionId = Guid.Empty;
            var dot = token?.IndexOf('.') ?? -1;
            return dot == 32 && Guid.TryParseExact(token![..dot], "N", out sessionId);
        }

        public static bool Matches(string token, string storedHash) =>
            CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(token)), Encoding.ASCII.GetBytes(storedHash));
    }

    public static class SessionMessages
    {
        public static string For(SessionEndReason reason) => reason switch
        {
            SessionEndReason.Replaced => "You signed in on another device, so this session was ended.",
            SessionEndReason.Idle => "You were signed out after a period of inactivity.",
            SessionEndReason.Expired => "Your session has expired. Please sign in again.",
            SessionEndReason.SignedOut => "You have signed out.",
            SessionEndReason.RevokedByAdmin => "An administrator ended your session.",
            SessionEndReason.TokenReuse => "Your session was ended for security reasons. Please sign in again.",
            SessionEndReason.AccountDisabled => "Your account is no longer active.",
            _ => "Please sign in again."
        };
    }

    /// <summary>Shared by sign-in and refresh: issues the token pair for a session.</summary>
    internal static class SessionTokens
    {
        public static LoginResultDto Issue(ITokenIssuer tokens, User user, UserSession session, string refreshToken, SecuritySettings settings, bool endedOthers)
        {
            var access = tokens.IssueAccessToken(user, session, TimeSpan.FromMinutes(Math.Max(1, settings.JwtExpiryMinutes)));
            return new LoginResultDto(access.Token, access.ExpiresAtUtc, refreshToken, session.ExpiresAt, session.Id, user.Id, user.TenantId, user.Name,
                user.Email.Value, user.EmpCode, session.AppCode, settings.SessionTimeoutMinutes, endedOthers);
        }

        public static TimeSpan Idle(SecuritySettings s) => TimeSpan.FromMinutes(Math.Max(1, s.SessionTimeoutMinutes));
    }

    // ------------------------------------------------------------------ sign in

    /// <param name="AppCode">Application the user signs in to (e.g. AMS, UMS). Single-device login applies per application.</param>
    public record LoginCommand(string Email, string Password, string? AppCode = null, ClientInfoDto? Client = null) : IRequest<Result<LoginResultDto>>;

    public class LoginCommandValidator : AbstractValidator<LoginCommand>
    {
        public LoginCommandValidator()
        {
            RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
            RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
            RuleFor(x => x.AppCode).MaximumLength(30);
        }
    }

    public class LoginCommandHandler : IRequestHandler<LoginCommand, Result<LoginResultDto>>
    {
        public const string DefaultApp = "UMS";

        private readonly IUserAccountRepository _users;
        private readonly ITenantRepository _tenants;
        private readonly IApplicationRepository _apps;
        private readonly IUserSessionRepository _sessions;
        private readonly ISessionValidator _validator;
        private readonly IPasswordHasher _hasher;
        private readonly ITokenIssuer _tokens;
        private readonly ILogRepository _logs;
        private readonly TimeProvider _time;

        public LoginCommandHandler(IUserAccountRepository users, ITenantRepository tenants, IApplicationRepository apps, IUserSessionRepository sessions,
            ISessionValidator validator, IPasswordHasher hasher, ITokenIssuer tokens, ILogRepository logs, TimeProvider time)
        {
            _users = users;
            _tenants = tenants;
            _apps = apps;
            _sessions = sessions;
            _validator = validator;
            _hasher = hasher;
            _tokens = tokens;
            _logs = logs;
            _time = time;
        }

        public async Task<Result<LoginResultDto>> Handle(LoginCommand request, CancellationToken ct)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var login = request.Email.Trim();
            var invalid = Result<LoginResultDto>.Failure(new Error("auth.invalid", "Invalid email or password."));

            var attempt = await _sessions.GetLoginAttemptAsync(login, ct) ?? new LoginAttempt(login);
            if (attempt.IsLocked(now))
                return Result<LoginResultDto>.Failure(new Error("auth.locked",
                    $"Too many failed sign-ins. Try again after {LocalClock.ToLocal(attempt.LockedUntil!.Value):hh:mm tt}."));

            var user = await _users.GetByEmailAsync(login, ct);
            var settings = user == null ? SecuritySettings.Default : await _tenants.GetSettingsAsync(user.TenantId, ct) ?? SecuritySettings.Default;
            if (user?.PasswordHash is null || !_hasher.Verify(user.PasswordHash, request.Password))
            {
                var locked = attempt.RegisterFailure(now, settings.MaxFailedLogins, TimeSpan.FromMinutes(Math.Max(1, settings.LockoutMinutes)));
                await _sessions.SaveLoginAttemptAsync(attempt, ct);
                return locked
                    ? Result<LoginResultDto>.Failure(new Error("auth.locked", $"Too many failed sign-ins. The account is locked for {settings.LockoutMinutes} minutes."))
                    : invalid;
            }

            var tenant = await _tenants.GetAsync(user.TenantId, ct);
            if (tenant is not { IsActive: true } || !user.IsUsableOn(LocalClock.Today(_time)))
                return Result<LoginResultDto>.Failure(new Error("auth.disabled", "This account is disabled."));

            var appCode = string.IsNullOrWhiteSpace(request.AppCode) ? DefaultApp : request.AppCode.Trim().ToUpperInvariant();
            var app = (await _apps.ListAsync(user.TenantId, ct)).FirstOrDefault(a => a.Code == appCode);
            if (app == null)
                return Result<LoginResultDto>.Failure(new Error("auth.app", $"Application {appCode} is not registered or not enabled for your organisation."));

            if (attempt.FailedCount > 0 || attempt.LockedUntil != null)
                await _sessions.SaveLoginAttemptAsync(new LoginAttempt(login), ct);

            // single-device login: the new sign-in replaces the user's other sessions in this application
            var endedOthers = false;
            if (settings.SingleDeviceLogin)
            {
                foreach (var other in await _sessions.ListOpenAsync(user.Id, appCode, ct))
                {
                    if (other.StatusAt(now, SessionTokens.Idle(settings)) == SessionEndReason.None) endedOthers = true;
                    other.End(SessionEndReason.Replaced, now);
                    await _sessions.SaveAsync(other, ct);
                    _validator.Forget(other.Id);
                }
            }

            var id = Guid.NewGuid();
            var refresh = RefreshTokens.New(id);
            var session = new UserSession(id, user.TenantId, user.Id, user.Name, user.EmpCode, appCode, request.Client?.Ip ?? "", Trim(request.Client?.UserAgent),
                now, now.AddDays(Math.Max(1, settings.RefreshTokenDays)), now, RefreshTokens.Hash(refresh));
            await _sessions.SaveAsync(session, ct);

            user.RecordLogin(LocalClock.Now(_time));
            await _users.SaveAsync(user, ct);
            await _logs.AddUserActionAsync(new UserActionLog(Guid.NewGuid(), user.TenantId, LocalClock.Now(_time), user.Id, user.Name, appCode, "/auth/login",
                $"{appCode}.SESSION.SIGN_IN", AccessDecisionResult.Allow, endedOthers ? "Signed in; the session on another device was ended" : "Signed in",
                session.IpAddress), ct);

            return Result<LoginResultDto>.Success(SessionTokens.Issue(_tokens, user, session, refresh, settings, endedOthers));
        }

        private static string Trim(string? s) => s == null ? "" : s.Length <= 300 ? s : s[..300];
    }

    // ------------------------------------------------------------------ refresh

    public record RefreshSessionCommand(string RefreshToken) : IRequest<Result<LoginResultDto>>;

    public class RefreshSessionCommandHandler : IRequestHandler<RefreshSessionCommand, Result<LoginResultDto>>
    {
        private readonly IUserSessionRepository _sessions;
        private readonly IUserAccountRepository _users;
        private readonly ITenantRepository _tenants;
        private readonly ISessionValidator _validator;
        private readonly ITokenIssuer _tokens;
        private readonly TimeProvider _time;

        public RefreshSessionCommandHandler(IUserSessionRepository sessions, IUserAccountRepository users, ITenantRepository tenants, ISessionValidator validator,
            ITokenIssuer tokens, TimeProvider time)
        {
            _sessions = sessions;
            _users = users;
            _tenants = tenants;
            _validator = validator;
            _tokens = tokens;
            _time = time;
        }

        public static Error Ended(SessionEndReason reason) => new($"session.{reason.ToString().ToLowerInvariant()}", SessionMessages.For(reason));

        public async Task<Result<LoginResultDto>> Handle(RefreshSessionCommand request, CancellationToken ct)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            if (!RefreshTokens.TryGetSessionId(request.RefreshToken, out var id) || await _sessions.GetAsync(id, ct) is not { } session)
                return Result<LoginResultDto>.Failure(Ended(SessionEndReason.Expired));

            var settings = await _tenants.GetSettingsAsync(session.TenantId, ct) ?? SecuritySettings.Default;
            var status = session.StatusAt(now, SessionTokens.Idle(settings));
            if (status != SessionEndReason.None) return await EndAsync(session, status, now, ct);

            // an old (already rotated) refresh token: someone else may hold a copy, so the whole session ends
            if (!RefreshTokens.Matches(request.RefreshToken, session.RefreshTokenHash))
                return await EndAsync(session, SessionEndReason.TokenReuse, now, ct);

            var user = await _users.GetAsync(session.UserId, ct);
            var tenant = user == null ? null : await _tenants.GetAsync(user.TenantId, ct);
            if (user == null || tenant is not { IsActive: true } || !user.IsUsableOn(LocalClock.Today(_time)))
                return await EndAsync(session, SessionEndReason.AccountDisabled, now, ct);

            var refresh = RefreshTokens.New(session.Id);
            session.Rotate(RefreshTokens.Hash(refresh), now);
            await _sessions.SaveAsync(session, ct);
            _validator.Forget(session.Id);
            return Result<LoginResultDto>.Success(SessionTokens.Issue(_tokens, user, session, refresh, settings, false));
        }

        private async Task<Result<LoginResultDto>> EndAsync(UserSession session, SessionEndReason reason, DateTime now, CancellationToken ct)
        {
            if (!session.IsEnded)
            {
                session.End(reason, now);
                await _sessions.SaveAsync(session, ct);
                _validator.Forget(session.Id);
            }
            return Result<LoginResultDto>.Failure(Ended(session.EndReason));
        }
    }

    // ------------------------------------------------------------------ status / keep-alive / sign out

    /// <param name="Touch">True when the user was active (portal keep-alive): resets the idle timer.</param>
    public record GetSessionStatusQuery(Guid SessionId, bool Touch) : IRequest<SessionStatusDto>;

    public class GetSessionStatusQueryHandler : IRequestHandler<GetSessionStatusQuery, SessionStatusDto>
    {
        private readonly IUserSessionRepository _sessions;
        private readonly ITenantRepository _tenants;
        private readonly ISessionValidator _validator;
        private readonly TimeProvider _time;

        public GetSessionStatusQueryHandler(IUserSessionRepository sessions, ITenantRepository tenants, ISessionValidator validator, TimeProvider time)
        {
            _sessions = sessions;
            _tenants = tenants;
            _validator = validator;
            _time = time;
        }

        public async Task<SessionStatusDto> Handle(GetSessionStatusQuery q, CancellationToken ct)
        {
            var session = await _sessions.GetAsync(q.SessionId, ct) ?? throw new NotFoundException("Session", q.SessionId);
            var settings = await _tenants.GetSettingsAsync(session.TenantId, ct) ?? SecuritySettings.Default;
            var now = _time.GetUtcNow().UtcDateTime;
            var status = session.StatusAt(now, SessionTokens.Idle(settings));
            if (status != SessionEndReason.None && !session.IsEnded)
            {
                session.End(status, now);
                await _sessions.SaveAsync(session, ct);
                _validator.Forget(session.Id);
            }
            else if (status == SessionEndReason.None && q.Touch)
            {
                session.Touch(now);
                await _sessions.SaveAsync(session, ct);
                _validator.Forget(session.Id);
            }
            return new SessionStatusDto(session.Id, status == SessionEndReason.None, status, session.ExpiresAt, session.LastSeenAt, settings.SessionTimeoutMinutes);
        }
    }

    public record LogoutCommand(Guid SessionId, SessionEndReason Reason = SessionEndReason.SignedOut) : IRequest<Unit>;

    public class LogoutCommandHandler : IRequestHandler<LogoutCommand, Unit>
    {
        private readonly IUserSessionRepository _sessions;
        private readonly ISessionValidator _validator;
        private readonly TimeProvider _time;

        public LogoutCommandHandler(IUserSessionRepository sessions, ISessionValidator validator, TimeProvider time)
        {
            _sessions = sessions;
            _validator = validator;
            _time = time;
        }

        public async Task<Unit> Handle(LogoutCommand request, CancellationToken ct)
        {
            // the client may only end its own session for these reasons
            var reason = request.Reason is SessionEndReason.Idle ? SessionEndReason.Idle : SessionEndReason.SignedOut;
            if (await _sessions.GetAsync(request.SessionId, ct) is { IsEnded: false } session)
            {
                session.End(reason, _time.GetUtcNow().UtcDateTime);
                await _sessions.SaveAsync(session, ct);
            }
            _validator.Forget(request.SessionId);
            return Unit.Value;
        }
    }

    // ------------------------------------------------------------------ administration

    public record ListSessionsQuery(Guid TenantId, bool ActiveOnly, int Take = 500) : IRequest<List<UserSessionDto>>;

    public class ListSessionsQueryHandler : IRequestHandler<ListSessionsQuery, List<UserSessionDto>>
    {
        private readonly IUserSessionRepository _sessions;
        private readonly ITenantRepository _tenants;
        private readonly TimeProvider _time;

        public ListSessionsQueryHandler(IUserSessionRepository sessions, ITenantRepository tenants, TimeProvider time)
        {
            _sessions = sessions;
            _tenants = tenants;
            _time = time;
        }

        public async Task<List<UserSessionDto>> Handle(ListSessionsQuery q, CancellationToken ct)
        {
            var settings = await _tenants.GetSettingsAsync(q.TenantId, ct) ?? SecuritySettings.Default;
            var now = _time.GetUtcNow().UtcDateTime;
            var idle = SessionTokens.Idle(settings);
            return (await _sessions.ListAsync(q.TenantId, q.ActiveOnly, Math.Clamp(q.Take, 1, 5000), ct))
                .Select(s => (Session: s, Status: s.StatusAt(now, idle)))
                .Where(x => !q.ActiveOnly || x.Status == SessionEndReason.None)
                .Select(x => new UserSessionDto(x.Session.Id, x.Session.UserId, x.Session.UserName, x.Session.EmpCode, x.Session.AppCode, x.Session.IpAddress,
                    x.Session.UserAgent, x.Session.CreatedAt, x.Session.LastSeenAt, x.Session.ExpiresAt, x.Session.EndedAt, x.Status, x.Status == SessionEndReason.None))
                .ToList();
        }
    }

    public record RevokeSessionCommand(Guid TenantId, Guid SessionId) : IRequest<Unit>;

    public class RevokeSessionCommandHandler : IRequestHandler<RevokeSessionCommand, Unit>
    {
        private readonly IUserSessionRepository _sessions;
        private readonly ISessionValidator _validator;
        private readonly IAuditService _audit;
        private readonly TimeProvider _time;

        public RevokeSessionCommandHandler(IUserSessionRepository sessions, ISessionValidator validator, IAuditService audit, TimeProvider time)
        {
            _sessions = sessions;
            _validator = validator;
            _audit = audit;
            _time = time;
        }

        public async Task<Unit> Handle(RevokeSessionCommand request, CancellationToken ct)
        {
            var session = await _sessions.GetAsync(request.SessionId, ct);
            if (session == null || session.TenantId != request.TenantId) throw new NotFoundException("Session", request.SessionId);
            if (!session.IsEnded)
            {
                session.End(SessionEndReason.RevokedByAdmin, _time.GetUtcNow().UtcDateTime);
                await _sessions.SaveAsync(session, ct);
                await _audit.LogAsync("Session", session.Id.ToString(), AuditAction.Revoke,
                    $"Ended {session.UserName}'s {session.AppCode} session ({session.IpAddress})", null, null, ct);
            }
            _validator.Forget(session.Id);
            return Unit.Value;
        }
    }
}
