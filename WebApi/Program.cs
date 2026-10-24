using ApplicationLayer.Behaviors;
using ApplicationLayer.Features.Users.Commands.Create;
using ApplicationLayer.Interfaces;
using FluentValidation;
using Infrastructure.DependencyInjection;
using Infrastructure.Identity;
using Infrastructure.Options;
using Infrastructure.Persistence;
using VMM.Mapper;
using VMM.Mediator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using WebApi.Middleware;



var builder = WebApplication.CreateBuilder(args);

// --------------------- Configuration & Options ---------------------
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                     // re-add the environment file so appsettings.Development.json still overrides appsettings.json
                     .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                     .AddEnvironmentVariables()
                     .AddCommandLine(args);

// bind strongly-typed options
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));

// convenience: read JwtOptions now for JWT setup
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var persistence = builder.Configuration["Persistence:Provider"] ?? "InMemory";
var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? (persistence.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                           ? throw new InvalidOperationException("Connection string 'Default' not found.")
                           : string.Empty);
// HS256 needs a key of at least 256 bits; fail at startup rather than on the first login
if (Encoding.UTF8.GetByteCount(jwtOptions.Secret) < 32)
    throw new InvalidOperationException("Jwt:Secret must be at least 32 characters. Set it via user-secrets or an environment variable.");

// --------------------- Core Services ---------------------
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// in-house mediator (BuildingBlocks/VMM.Mediator): handlers from the API and application assemblies
builder.Services.AddMediator(cfg =>
{
    cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
    cfg.RegisterServicesFromAssembly(typeof(CreateUserCommand).Assembly);
});
// in-house object mapper (BuildingBlocks/VMM.Mapper): profiles are validated at start-up
builder.Services.AddObjectMapper(typeof(CreateUserCommand).Assembly);

// FluentValidation
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
builder.Services.AddValidatorsFromAssembly(typeof(CreateUserCommand).Assembly);

builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<DomainLayer.Interfaces.IUserPolicyService, ApplicationLayer.Services.UserPolicyService>();

// --------------------- Infrastructure & access control ---------------------
builder.Services.AddSingleton(jwtOptions); // JwtTokenService takes JwtOptions directly
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddAccessControl(builder.Configuration);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
        // a token is only as good as its sign-in session: signed out, replaced by a sign-in on another device, idle or revoked → 401
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                if (!Guid.TryParse(ctx.Principal?.FindFirst(IdentityService.SessionClaim)?.Value, out var sid))
                {
                    ctx.Fail("The token is not bound to a sign-in session.");
                    return;
                }
                var reason = await ctx.HttpContext.RequestServices.GetRequiredService<ISessionValidator>().CheckAsync(sid, ctx.HttpContext.RequestAborted);
                if (reason != DomainLayer.Entities.SessionEndReason.None)
                {
                    ctx.HttpContext.Items[SessionEndHeader] = reason.ToString();
                    ctx.Fail($"Session ended: {reason}");
                }
            },
            OnChallenge = ctx =>
            {
                // clients show why (e.g. "Replaced": signed in on another device)
                if (ctx.HttpContext.Items[SessionEndHeader] is string reason) ctx.Response.Headers[SessionEndHeader] = reason;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// browser-based clients on other origins (the Blazor Server admin portal calls the API server-side and needs no CORS)
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options => options.AddPolicy("AdminPanel", policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders(Program.SessionEndHeader)));


// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
//builder.Services.AddOpenApi();
// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "User Management API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Use format: Bearer {your token}"
    });
});

var app = builder.Build();

// optional: fill an empty SQL Server database with the Vishal Mega Mart demo data
if (persistence.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) && app.Configuration.GetValue("Persistence:SeedDemoData", false))
    await DemoDataSeeder.SeedIfEmptyAsync(app.Services, app.Configuration["Persistence:DemoAdminPassword"], app.Configuration["Persistence:DemoUserPassword"]);

app.UseMiddleware<ExceptionLoggingMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    //app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "User Management API v1");
        options.RoutePrefix = string.Empty;
    });
}

app.UseHttpsRedirection();
app.UseCors("AdminPanel");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
//app.MapGet("/", () => "API is running...");
app.Run();

public partial class Program
{
    /// <summary>Response header naming why the caller's session ended (SessionEndReason).</summary>
    public const string SessionEndHeader = "X-Session-End";
}
