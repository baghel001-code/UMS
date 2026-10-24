using AMS.Infrastructure.Ums;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace AMS.Portal.Auth
{
    public enum LoginStatus { Success, Failed, ServiceUnavailable }

    public sealed record LoginResult(LoginStatus Status, string? Message = null, string? Ticket = null);

    /// <summary>
    /// Checks credentials with UMS (application AMS). Runs inside the Blazor circuit, so it never touches HttpContext; the cookie is
    /// written afterwards by <see cref="AccountEndpoints"/> from a single-use ticket. Lockout, single-device login and the idle timeout
    /// are UMS rules for the tenant.
    /// </summary>
    public sealed class AccountService
    {
        private readonly UmsClient _ums;
        private readonly UmsOptions _options;
        private readonly UmsTokenStore _tokens;
        private readonly LoginTicketStore _tickets;
        private readonly TimeProvider _time;
        private readonly ILogger<AccountService> _logger;

        public AccountService(UmsClient ums, UmsOptions options, UmsTokenStore tokens, LoginTicketStore tickets, TimeProvider time, ILogger<AccountService> logger)
        {
            _ums = ums;
            _options = options;
            _tokens = tokens;
            _tickets = tickets;
            _time = time;
            _logger = logger;
        }

        public async Task<LoginResult> SignInAsync(string email, string password, ClientInfo client, CancellationToken ct = default)
        {
            try
            {
                var r = await _ums.LoginAsync(email.Trim(), password, _options.AppCode, client.Ip, client.UserAgent, ct);
                _tokens.Save(r);
                var user = new PortalUser(r.SessionId, r.UserId, r.TenantId, r.EmpCode, r.FullName, r.Email, r.IdleTimeoutMinutes, _time.GetUtcNow());
                _logger.LogInformation("Credentials verified by UMS for {EmpCode}{Ended}", r.EmpCode, r.EndedOtherSessions ? " (session on another device ended)" : "");
                return new LoginResult(LoginStatus.Success, Ticket: _tickets.Issue(user, r.EndedOtherSessions));
            }
            catch (UmsCallException ex) when (ex.Status != null)
            {
                _logger.LogInformation("Sign-in refused by UMS: {Code}", ex.Code);
                return new LoginResult(LoginStatus.Failed, ex.Message);
            }
            catch (UmsCallException ex)
            {
                _logger.LogWarning(ex, "UMS unavailable during sign-in");
                return new LoginResult(LoginStatus.ServiceUnavailable, "Sign-in is temporarily unavailable (UMS cannot be reached). Please try again shortly.");
            }
        }
    }

    /// <summary>Runs on every HTTP request with the auth cookie: absolute lifetime and ended sessions always, UMS every RevalidationSeconds.</summary>
    internal static class CookieValidator
    {
        private const string LastCheckedKey = ".ams.checked";

        public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
        {
            if (context.Principal is not { Identity.IsAuthenticated: true } principal) return;
            var services = context.HttpContext.RequestServices;
            var checker = services.GetRequiredService<SessionChecker>();
            var user = PortalUser.From(principal);
            if (checker.QuickCheck(user) is { } quick)
            {
                await RejectAsync(context, quick);
                return;
            }
            var settings = services.GetRequiredService<IOptions<AuthSettings>>().Value;
            var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
            if (context.Properties.Items.TryGetValue(LastCheckedKey, out var raw) && long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var unix) &&
                now - DateTimeOffset.FromUnixTimeSeconds(unix) < TimeSpan.FromSeconds(settings.RevalidationSeconds))
                return;
            string? reason;
            try
            {
                reason = await checker.CheckWithUmsAsync(user, touch: false, context.HttpContext.RequestAborted);
            }
            catch (Exception ex) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                // UMS down: let this request through and check again on the next one
                services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CookieValidator)).LogWarning(ex, "Session check failed; trying again on the next request");
                return;
            }
            if (reason != null)
            {
                await RejectAsync(context, reason);
                return;
            }
            context.Properties.Items[LastCheckedKey] = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            context.ShouldRenew = true;
        }

        private static async Task RejectAsync(CookieValidatePrincipalContext context, string reason)
        {
            context.HttpContext.Items[AuthConstants.EndReasonItem] = reason;
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(context.Scheme.Name);
        }
    }

    /// <summary>
    /// An open page makes no HTTP requests, so the cookie handler never sees it. This re-checks the circuit's user on a timer
    /// (UMS session + idle backstop); when the check fails the page becomes anonymous and the layout posts the sign-out with the reason.
    /// </summary>
    public sealed class AppRevalidatingAuthStateProvider : RevalidatingServerAuthenticationStateProvider
    {
        private const int MaxConsecutiveFailures = 5;
        private readonly IServiceScopeFactory _scopes;
        private readonly AuthSettings _settings;
        private readonly SessionEndState _endState;
        private readonly UserActivityState _activity;
        private readonly ILogger _logger;
        private int _failures;

        public AppRevalidatingAuthStateProvider(ILoggerFactory loggerFactory, IServiceScopeFactory scopes, IOptions<AuthSettings> settings,
            SessionEndState endState, UserActivityState activity) : base(loggerFactory)
        {
            _scopes = scopes;
            _settings = settings.Value;
            _endState = endState;
            _activity = activity;
            _logger = loggerFactory.CreateLogger<AppRevalidatingAuthStateProvider>();
        }

        public PortalUser? CurrentUser { get; private set; }

        protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(Math.Max(5, _settings.RevalidationSeconds));

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var state = base.GetAuthenticationStateAsync();
            if (state.IsCompletedSuccessfully) CurrentUser = PortalUser.From(state.Result.User);
            return state;
        }

        protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
        {
            var user = PortalUser.From(state.User);
            if (user == null) return true;
            CurrentUser = user;
            if (_endState.Reason != null) return false;   // an API call already learned that the session ended
            // server-side idle backstop (the browser monitor normally signs out first, with a warning)
            if (_activity.IdleFor >= TimeSpan.FromMinutes(user.IdleMinutes)) return End("idle");
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var reason = await scope.ServiceProvider.GetRequiredService<SessionChecker>().CheckWithUmsAsync(user, touch: false, ct);
                _failures = 0;
                return reason == null || End(reason);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // tolerate a short UMS outage instead of signing everybody out
                if (++_failures >= MaxConsecutiveFailures)
                {
                    _logger.LogError(ex, "Session check failed {Count} times in a row; signing the user out", _failures);
                    return End("expired");
                }
                _logger.LogWarning(ex, "Session check failed ({Count}/{Max})", _failures, MaxConsecutiveFailures);
                return true;
            }
        }

        private bool End(string reason)
        {
            _endState.Reason ??= reason;
            return false;
        }
    }

    /// <summary>
    /// The real HTTP requests of the auth flow (the cookie can only be written in an HTTP response): complete sign-in from a ticket,
    /// sign out (also ends the UMS session), antiforgery token for the forms, and the keep-alive used while the user is active.
    /// </summary>
    public static class AccountEndpoints
    {
        public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapPost(AuthConstants.CompleteLoginEndpoint, CompleteLoginAsync).AllowAnonymous().ExcludeFromDescription();
            app.MapPost(AuthConstants.LogoutEndpoint, LogoutAsync).AllowAnonymous().ExcludeFromDescription();
            app.MapGet(AuthConstants.AntiforgeryTokenEndpoint, (HttpContext http, IAntiforgery af) =>
            {
                var t = af.GetAndStoreTokens(http);
                http.Response.Headers.CacheControl = "no-store";
                return Results.Json(new { field = t.FormFieldName, token = t.RequestToken });
            }).AllowAnonymous().ExcludeFromDescription();
            app.MapPost(AuthConstants.KeepAliveEndpoint, KeepAliveAsync).RequireAuthorization().ExcludeFromDescription();
            return app;
        }

        private static async Task<bool> AntiforgeryOk(HttpContext http, IAntiforgery af)
        {
            try { await af.ValidateRequestAsync(http); return true; }
            catch (AntiforgeryValidationException) { return false; }
        }

        private static string LocalOrRoot(string? url) =>
            !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\") ? url : "/";

        private static async Task<IResult> CompleteLoginAsync(HttpContext http, IAntiforgery af, LoginTicketStore tickets, LoginNotices notices, ILoggerFactory lf)
        {
            // antiforgery stops "login CSRF" (another site signing the victim in to the attacker's account)
            if (!await AntiforgeryOk(http, af)) return Results.BadRequest();
            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            var returnUrl = LocalOrRoot(form["returnUrl"]);
            var ticket = tickets.Redeem(form["ticket"]);
            if (ticket == null) return Results.LocalRedirect($"{AuthConstants.LoginPath}?reason=expired");
            await http.SignInAsync(AuthConstants.Scheme, ticket.User.ToPrincipal(), new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
            if (ticket.EndedOtherSessions) notices.Add(ticket.User.SessionId);
            lf.CreateLogger("Auth").LogInformation("User {EmpCode} signed in (session {Session})", ticket.User.EmpCode, ticket.User.SessionId);
            http.Response.Headers.CacheControl = "no-store";
            return Results.LocalRedirect(returnUrl);
        }

        private static async Task<IResult> LogoutAsync(HttpContext http, IAntiforgery af, UmsTokenStore tokens, EndedSessions ended, UmsClient ums, ILoggerFactory lf)
        {
            // POST + antiforgery: a link or image on another site cannot sign users out
            if (!await AntiforgeryOk(http, af)) return Results.BadRequest();
            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            var reason = form["reason"].ToString().ToLowerInvariant() switch
            {
                "idle" => "idle", "replaced" => "replaced", "expired" => "expired", "revokedbyadmin" => "revokedbyadmin", "tokenreuse" => "tokenreuse",
                "accountdisabled" => "accountdisabled", _ => "signedout"
            };
            var logger = lf.CreateLogger("Auth");
            if (PortalUser.From(http.User) is { } user)
            {
                ended.Add(user.SessionId, reason);   // a copy of this cookie stops working at once
                // end it in UMS too, unless UMS already ended it (e.g. replaced by a newer sign-in)
                if (reason is "signedout" or "idle")
                {
                    try
                    {
                        if (await tokens.GetAccessTokenAsync(user.SessionId, http.RequestAborted) is { } token)
                            await ums.LogoutAsync(token, reason == "idle" ? "Idle" : "SignedOut", http.RequestAborted);
                    }
                    catch (Exception ex) when (!http.RequestAborted.IsCancellationRequested)
                    {
                        logger.LogWarning(ex, "Could not end the UMS session of {EmpCode}; signing out anyway", user.EmpCode);
                    }
                }
                tokens.Remove(user.SessionId);
                logger.LogInformation("User {EmpCode} signed out ({Reason})", user.EmpCode, reason);
            }
            await http.SignOutAsync(AuthConstants.Scheme);
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["Clear-Site-Data"] = "\"cache\"";
            return Results.LocalRedirect($"{AuthConstants.LoginPath}?reason={reason}");
        }

        /// <summary>Called by idle-timeout.js while the user is active: renews the cookie (sliding) and tells UMS the user is active.</summary>
        private static async Task<IResult> KeepAliveAsync(HttpContext http, SessionChecker checker)
        {
            http.Response.Headers.CacheControl = "no-store";
            try
            {
                var reason = await checker.CheckWithUmsAsync(PortalUser.From(http.User), touch: true, http.RequestAborted);
                return reason == null ? Results.NoContent() : Results.Json(new { ended = reason }, statusCode: StatusCodes.Status401Unauthorized);
            }
            catch (UmsCallException)
            {
                return Results.NoContent();   // UMS unreachable: the next keep-alive tries again
            }
        }
    }

    public static class AuthServiceCollectionExtensions
    {
        public static IServiceCollection AddPortalAuthentication(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
        {
            services.Configure<AuthSettings>(config.GetSection(AuthSettings.SectionName));
            var settings = config.GetSection(AuthSettings.SectionName).Get<AuthSettings>() ?? new AuthSettings();

            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<UmsTokenStore>();
            services.AddSingleton<EndedSessions>();
            services.AddSingleton<LoginNotices>();
            services.AddSingleton<LoginTicketStore>();
            services.AddSingleton<SessionChecker>();
            services.AddScoped<AccountService>();
            services.AddScoped<ClientInfo>();
            services.AddScoped<SessionEndState>();
            services.AddScoped<UserActivityState>();
            services.AddScoped<AuthenticationStateProvider, AppRevalidatingAuthStateProvider>();
            services.AddScoped<Application.Interfaces.IAccessTokenProvider, CircuitTokenProvider>();

            services.AddAuthentication(AuthConstants.Scheme)
                .AddCookie(AuthConstants.Scheme, o =>
                {
                    // "__Host-": the browser only accepts it Secure, Path=/ and without Domain, so a sibling sub-domain cannot plant or overwrite it
                    o.Cookie.Name = env.IsDevelopment() ? "AMS.Auth" : "__Host-AMS.Auth";
                    o.Cookie.Path = "/";
                    o.Cookie.HttpOnly = true;
                    o.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                    o.Cookie.SameSite = SameSiteMode.Lax;
                    o.LoginPath = AuthConstants.LoginPath;
                    o.LogoutPath = AuthConstants.LogoutEndpoint;
                    o.AccessDeniedPath = AuthConstants.AccessDeniedPath;
                    o.ExpireTimeSpan = TimeSpan.FromMinutes(Math.Max(5, settings.CookieLifetimeMinutes));
                    o.SlidingExpiration = true;
                    o.Events.OnValidatePrincipal = CookieValidator.ValidatePrincipalAsync;
                    o.Events.OnRedirectToLogin = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments("/account/keepalive")) ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        else if (ctx.HttpContext.Items[AuthConstants.EndReasonItem] is string reason)
                            ctx.Response.Redirect(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(ctx.RedirectUri, "reason", reason));
                        else ctx.Response.Redirect(ctx.RedirectUri);
                        return Task.CompletedTask;
                    };
                });
            services.AddCascadingAuthenticationState();
            return services;
        }
    }
}
