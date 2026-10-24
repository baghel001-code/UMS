using ApplicationLayer.Features.AccessControl.DTOs;
using ApplicationLayer.Features.AccessControl.Sessions;
using ApplicationLayer.Interfaces;
using DomainLayer.Entities;
using Infrastructure.Identity;
using Infrastructure.Options;
using Infrastructure.Persistence;
using Infrastructure.Persistence.InMemory;
using Infrastructure.Services;
using System.IdentityModel.Tokens.Jwt;

namespace UMS.Tests
{
    // Sign-in sessions: single-device login, rotating refresh tokens, idle/absolute expiry, lockout.
    public class SessionTests
    {
        private sealed class Clock : TimeProvider
        {
            public DateTimeOffset Now = new(2026, 10, 23, 5, 30, 0, TimeSpan.Zero);   // 11:00 IST
            public override DateTimeOffset GetUtcNow() => Now;
        }

        private sealed class NoCacheValidator : ISessionValidator
        {
            private readonly IUserSessionRepository _repo;
            private readonly TimeProvider _time;
            public NoCacheValidator(IUserSessionRepository repo, TimeProvider time) { _repo = repo; _time = time; }
            public async Task<SessionEndReason> CheckAsync(Guid id, CancellationToken ct = default) =>
                (await _repo.GetAsync(id, ct))?.StatusAt(_time.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(30)) ?? SessionEndReason.Expired;
            public void Forget(Guid id) { }
        }

        private sealed class Scopes : Microsoft.Extensions.DependencyInjection.IServiceScopeFactory, Microsoft.Extensions.DependencyInjection.IServiceScope, IServiceProvider
        {
            private readonly object[] _services;
            public Scopes(params object[] services) => _services = services;
            public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope() => this;
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type type) => _services.FirstOrDefault(type.IsInstanceOfType);
            public void Dispose() { }
        }

        private sealed class Env
        {
            public readonly Clock Clock = new();
            public readonly InMemoryUmsStore Store;
            public readonly InMemorySessionRepository Sessions;
            public readonly NoCacheValidator Validator;
            private readonly IdentityService _tokens;

            public Env()
            {
                var seed = DemoSeed.Create(new DateTime(2026, 10, 23, 11, 0, 0));
                DemoDataSeeder.ApplyDemoPasswords(seed, new PasswordHasherService(), "Admin@12345", "Demo@12345");
                Store = new InMemoryUmsStore(seed);
                Sessions = new InMemorySessionRepository(Store);
                Validator = new NoCacheValidator(Sessions, Clock);
                _tokens = new IdentityService(new JwtTokenService(new JwtOptions { Issuer = "t", Audience = "t", Secret = new string('k', 40) }, Clock));
            }

            public Task<ApplicationLayer.Common.Result<LoginResultDto>> Login(string email, string password = "Demo@12345", string app = "AMS") =>
                new LoginCommandHandler(new InMemoryUserAccountRepository(Store), new InMemoryTenantRepository(Store), new InMemoryApplicationRepository(Store),
                        Sessions, Validator, new PasswordHasherService(), _tokens, new InMemoryLogRepository(Store), Clock)
                    .Handle(new LoginCommand(email, password, app, new ClientInfoDto("10.1.2.3", "test")), default);

            public Task<ApplicationLayer.Common.Result<LoginResultDto>> Refresh(string token) =>
                new RefreshSessionCommandHandler(Sessions, new InMemoryUserAccountRepository(Store), new InMemoryTenantRepository(Store), Validator, _tokens, Clock)
                    .Handle(new RefreshSessionCommand(token), default);

            public Task<SessionStatusDto> Status(Guid id, bool touch = false) =>
                new GetSessionStatusQueryHandler(Sessions, new InMemoryTenantRepository(Store), Validator, Clock).Handle(new GetSessionStatusQuery(id, touch), default);
        }

        private const string Sneha = "sneha.iyer@vmm.example";

        [Fact]
        public async Task Validator_records_the_end_of_a_session_that_went_idle()
        {
            var env = new Env();
            var r = (await env.Login(Sneha)).Value;
            var validator = new SessionValidator(new Scopes(env.Sessions, new InMemoryTenantRepository(env.Store)), env.Clock);

            Assert.Equal(SessionEndReason.None, await validator.CheckAsync(r.SessionId));
            env.Clock.Now = env.Clock.Now.AddMinutes(31);   // idle timeout is 30 minutes
            Assert.Equal(SessionEndReason.Idle, await validator.CheckAsync(r.SessionId));
            var session = (await env.Sessions.GetAsync(r.SessionId))!;
            Assert.True(session.IsEnded);
            Assert.Equal(SessionEndReason.Idle, session.EndReason);
        }

        [Fact]
        public async Task Sign_in_issues_a_session_bound_access_token_and_a_refresh_token()
        {
            var env = new Env();
            var r = (await env.Login(Sneha)).Value;
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(r.AccessToken);
            Assert.Equal(r.SessionId.ToString(), jwt.Claims.First(c => c.Type == "sid").Value);
            Assert.Equal("AMS", jwt.Claims.First(c => c.Type == "app").Value);
            Assert.Equal(env.Clock.Now.UtcDateTime.AddMinutes(15), r.AccessTokenExpiresAt);   // JwtExpiryMinutes
            Assert.Equal(30, r.IdleTimeoutMinutes);
            Assert.True(RefreshTokens.TryGetSessionId(r.RefreshToken, out var sid) && sid == r.SessionId);
            Assert.DoesNotContain(r.RefreshToken, (await env.Sessions.GetAsync(r.SessionId))!.RefreshTokenHash);
        }

        [Fact]
        public async Task Signing_in_on_another_device_ends_the_first_session_in_the_same_app_only()
        {
            var env = new Env();
            var laptop = (await env.Login(Sneha)).Value;
            var umsPortal = (await env.Login(Sneha, app: "UMS")).Value;
            Assert.False(laptop.EndedOtherSessions);

            env.Clock.Now = env.Clock.Now.AddMinutes(5);
            var phone = (await env.Login(Sneha)).Value;
            Assert.True(phone.EndedOtherSessions);
            Assert.Equal(SessionEndReason.Replaced, await env.Validator.CheckAsync(laptop.SessionId));
            Assert.Equal(SessionEndReason.None, await env.Validator.CheckAsync(phone.SessionId));
            Assert.Equal(SessionEndReason.None, await env.Validator.CheckAsync(umsPortal.SessionId));

            var refresh = await env.Refresh(laptop.RefreshToken);
            Assert.False(refresh.IsSuccess);
            Assert.Equal("session.replaced", refresh.Error.Code);
        }

        [Fact]
        public async Task Refresh_tokens_rotate_and_reusing_an_old_one_ends_the_session()
        {
            var env = new Env();
            var first = (await env.Login(Sneha)).Value;
            var second = (await env.Refresh(first.RefreshToken)).Value;
            Assert.NotEqual(first.RefreshToken, second.RefreshToken);
            Assert.Equal(first.SessionId, second.SessionId);

            var replay = await env.Refresh(first.RefreshToken);
            Assert.Equal("session.tokenreuse", replay.Error.Code);
            Assert.False((await env.Refresh(second.RefreshToken)).IsSuccess);   // the whole session is gone
        }

        [Fact]
        public async Task Sessions_end_after_the_idle_timeout_unless_the_user_is_active()
        {
            var env = new Env();
            var s = (await env.Login(Sneha)).Value;
            env.Clock.Now = env.Clock.Now.AddMinutes(25);
            Assert.True((await env.Status(s.SessionId, touch: true)).Active);   // keep-alive while working
            env.Clock.Now = env.Clock.Now.AddMinutes(25);
            Assert.True((await env.Status(s.SessionId)).Active);
            env.Clock.Now = env.Clock.Now.AddMinutes(10);                      // 35 minutes without activity
            var status = await env.Status(s.SessionId);
            Assert.False(status.Active);
            Assert.Equal(SessionEndReason.Idle, status.Reason);
            Assert.Equal("session.idle", (await env.Refresh(s.RefreshToken)).Error.Code);
        }

        [Fact]
        public async Task Sessions_end_at_the_absolute_lifetime_even_when_active()
        {
            var env = new Env();
            var s = (await env.Login(Sneha)).Value;
            var token = s.RefreshToken;
            for (var i = 0; i < 7 * 24 * 3; i++)   // refresh every 20 minutes for 7 days
            {
                env.Clock.Now = env.Clock.Now.AddMinutes(20);
                var r = await env.Refresh(token);
                if (!r.IsSuccess)
                {
                    Assert.Equal("session.expired", r.Error.Code);
                    Assert.True(env.Clock.Now.UtcDateTime >= s.SessionExpiresAt);
                    return;
                }
                token = r.Value.RefreshToken;
            }
            Assert.Fail("The session never expired.");
        }

        [Fact]
        public async Task Too_many_wrong_passwords_lock_the_login_for_a_while()
        {
            var env = new Env();
            for (var i = 0; i < 4; i++) Assert.Equal("auth.invalid", (await env.Login(Sneha, "wrong")).Error.Code);
            Assert.Equal("auth.locked", (await env.Login(Sneha, "wrong")).Error.Code);
            Assert.Equal("auth.locked", (await env.Login(Sneha)).Error.Code);      // even the right password
            env.Clock.Now = env.Clock.Now.AddMinutes(31);
            Assert.True((await env.Login(Sneha)).IsSuccess);
        }

        [Fact]
        public async Task Signing_out_ends_the_session_and_unknown_apps_are_refused()
        {
            var env = new Env();
            var s = (await env.Login(Sneha)).Value;
            await new LogoutCommandHandler(env.Sessions, env.Validator, env.Clock).Handle(new LogoutCommand(s.SessionId), default);
            Assert.Equal(SessionEndReason.SignedOut, await env.Validator.CheckAsync(s.SessionId));
            Assert.Equal("auth.app", (await env.Login(Sneha, app: "NOPE")).Error.Code);
        }

        [Fact]
        public async Task Single_device_login_can_be_switched_off_per_tenant()
        {
            var env = new Env();
            var tenant = DemoSeed.VmmTenantId;
            env.Store.Settings[tenant] = env.Store.Settings[tenant] with { SingleDeviceLogin = false };
            var a = (await env.Login(Sneha)).Value;
            var b = (await env.Login(Sneha)).Value;
            Assert.False(b.EndedOtherSessions);
            Assert.Equal(SessionEndReason.None, await env.Validator.CheckAsync(a.SessionId));
        }
    }
}
