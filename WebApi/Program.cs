using ApplicationLayer.Behaviors;
using ApplicationLayer.Interfaces;
using FluentValidation;
using Infrastructure.Identity;
using Infrastructure.Options;
using MediatR;
using Microsoft.OpenApi;
using System.Reflection;



var builder = WebApplication.CreateBuilder(args);

// --------------------- Configuration & Options ---------------------
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                     .AddEnvironmentVariables()
                     .AddCommandLine(args);

// bind strongly-typed options
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));

// convenience: read JwtOptions now for JWT setup
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
//var connectionString = builder.Configuration.GetConnectionString("Default")
//                       ?? builder.Configuration["ConnectionStrings:Default"]
//                       ?? throw new InvalidOperationException("Connection string 'Default' not found.");

// --------------------- Core Services ---------------------
builder.Services.AddControllers();

builder.Services.AddMediatR(cfg =>
{
    // Register from the current assembly and also attempt application assembly (uncomment and change type if needed)
    cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
    // cfg.RegisterServicesFromAssembly(typeof(CreateUserCommand).Assembly); // <- enable if available
});

// FluentValidation
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>)); // adjust namespace
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();


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
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
//app.MapGet("/", () => "API is running...");
app.Run();
