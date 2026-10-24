using AMS.Api.Infrastructure;
using AMS.Application;
using AMS.Infrastructure;
using AMS.Infrastructure.Persistence;
using AMS.Infrastructure.Ums;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;
using System.Text;
using System.Text.Json.Serialization;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();   // catches start-up errors

var builder = WebApplication.CreateBuilder(args);

// ---- logging: console + daily files per level (Logs/Info, Logs/Warnings, Logs/Errors)
var logPath = builder.Configuration["Logging:FilePath"] ?? "Logs/";
builder.Host.UseSerilog((ctx, lc) => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message}{NewLine}{Exception}")
    .WriteTo.Logger(l => l.Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Information)
        .WriteTo.File($"{logPath}Info/info-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30))
    .WriteTo.Logger(l => l.Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Warning)
        .WriteTo.File($"{logPath}Warnings/warning-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30))
    .WriteTo.Logger(l => l.Filter.ByIncludingOnly(e => e.Level >= LogEventLevel.Error)
        .WriteTo.File($"{logPath}Errors/error-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 90)));

// ---- clean architecture: use cases (Application) + adapters (Infrastructure: Dapper/SQL Server, UMS, SMTP)
builder.Services.AddAmsApplication();
builder.Services.AddAmsApiInfrastructure(builder.Configuration);

builder.Services.AddControllers(o => o.Filters.Add<AmsExceptionFilter>())
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = null;   // keep property names as declared (same as the other VMM APIs)
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// ---- authentication: UMS access tokens only (signed by UMS, issued for application AMS, bound to a UMS session)
var ums = builder.Configuration.GetSection(UmsOptions.SectionName).Get<UmsOptions>() ?? new UmsOptions();
if (Encoding.UTF8.GetByteCount(ums.JwtSecret) < 32)
    throw new InvalidOperationException("Ums:JwtSecret must be the UMS JWT secret (at least 32 characters). Set it via user-secrets or an environment variable.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = ums.JwtIssuer,
            ValidateAudience = true, ValidAudience = ums.JwtAudience,
            ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ums.JwtSecret)),
            ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
        };
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = ctx =>
            {
                // a token UMS issued for another application (e.g. the UMS portal) is not accepted here
                if (!string.Equals(ctx.Principal?.FindFirst("app")?.Value, ums.AppCode, StringComparison.OrdinalIgnoreCase) || ctx.Principal?.FindFirst("sid") == null)
                    ctx.Fail($"The token was not issued for {ums.AppCode}.");
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "VMM Asset Management API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header,
        Description = "UMS access token (sign in at UMS /api/v1/auth/login with appCode AMS)"
    });
});

var app = builder.Build();

// development: create/upgrade the database and load demo data for the UMS demo tenant
var db = app.Services.GetRequiredService<AmsDatabaseOptions>();
if (db.AutoMigrate)
{
    await app.Services.GetRequiredService<DatabaseInitializer>().InitialiseAsync(Path.Combine(AppContext.BaseDirectory, "database"));
    if (db.DemoTenantId is { } tenant) await app.Services.GetRequiredService<DemoDataSeeder>().SeedIfEmptyAsync(tenant);
}

app.Use(async (context, next) =>
{
    var h = context.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h.Remove("Server");
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
app.Run();

public partial class Program { }
