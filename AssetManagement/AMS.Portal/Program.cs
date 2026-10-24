using AMS.Infrastructure;
using AMS.Portal.Auth;
using AMS.Portal.Components;
using AMS.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using MudBlazor;
using MudBlazor.Services;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);
var logPath = builder.Configuration["Logging:FilePath"] ?? "Logs/";
builder.Host.UseSerilog((ctx, lc) => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message}{NewLine}{Exception}")
    .WriteTo.Logger(l => l.Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Information).WriteTo.File($"{logPath}Info/info-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30))
    .WriteTo.Logger(l => l.Filter.ByIncludingOnly(e => e.Level >= LogEventLevel.Warning).WriteTo.File($"{logPath}Errors/error-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 90)));

// the portal is a client: UMS for identity and sessions, the AMS API (IAPICall) for every piece of data
builder.Services.AddAmsPortalInfrastructure(builder.Configuration);
builder.Services.AddPortalAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddAuthorization(o =>
{
    // every page needs a signed-in user except the framework's static files and the sign-in page
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAssertion(ctx =>
        ctx.User.Identity?.IsAuthenticated == true ||
        (ctx.Resource is HttpContext http && (http.Request.Path.StartsWithSegments("/_framework") || http.Request.Path.StartsWithSegments("/_blazor")
            || http.Request.Path.StartsWithSegments("/_content") || http.Request.Path.StartsWithSegments("/css") || http.Request.Path.StartsWithSegments("/js")
            || http.Request.Path.StartsWithSegments("/favicon.ico")))).Build();
});

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices(c =>
{
    c.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    c.SnackbarConfiguration.VisibleStateDuration = 6000;
});
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<Ui>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    var h = context.Response.Headers;
    h["X-Frame-Options"] = "SAMEORIGIN";
    h["X-Content-Type-Options"] = "nosniff";
    h["Referrer-Policy"] = "strict-origin";
    h.Remove("Server");
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapAccountEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
app.Run();
