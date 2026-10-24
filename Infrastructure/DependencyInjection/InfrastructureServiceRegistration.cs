using ApplicationLayer.Interfaces;
using ApplicationLayer.Services;
using DomainLayer.Interfaces;
using Infrastructure.Database;
using Infrastructure.Events;
using Infrastructure.Identity;
using Infrastructure.Options;
using Infrastructure.Persistence;
using Infrastructure.Persistence.InMemory;
using Infrastructure.Persistence.SqlServer;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.DependencyInjection
{
    public static class InfrastructureServiceRegistration
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
        {
            // Database

            services.AddSingleton<IDbConnectionFactory>(sp => new SqlConnectionFactory(connectionString));
            services.AddScoped<IUnitOfWork, DapperUnitOfWork>();


            // Repositories (example: register per-entity or open generic)
            services.AddScoped(typeof(IReadRepository<>), typeof(DapperReadRepository<>));
            services.AddScoped(typeof(IRepository<>), typeof(DapperRepository<>));


            // Events
            services.AddScoped<IDomainEventDispatcher, DapperDomainEventDispatcher>();


            // Identity
            services.AddSingleton<JwtTokenService>();


            // Services
            //services.AddMemoryCache();
            services.AddScoped<CacheService>();
            services.AddScoped<FileStorageService>(sp => new FileStorageService(Path.Combine(Directory.GetCurrentDirectory(), "uploads")));


            //// Options: configure from configuration in program.cs
            //services.Configure<JwtOptions>(opts => { });
            //services.Configure<EmailOptions>(opts => { });


            return services;
        }

        /// <summary>
        /// Access-control persistence and services. Persistence:Provider selects "InMemory" (demo data, lost on restart)
        /// or "SqlServer" (Dapper repositories over database/001_create_access_control_schema.sql).
        /// </summary>
        public static IServiceCollection AddAccessControl(this IServiceCollection services, IConfiguration configuration)
        {
            var provider = configuration["Persistence:Provider"] ?? "InMemory";

            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                services.AddScoped<ITenantRepository, SqlTenantRepository>();
                services.AddScoped<IApplicationRepository, SqlApplicationRepository>();
                services.AddScoped<IRoleRepository, SqlRoleRepository>();
                services.AddScoped<IUserAccountRepository, SqlUserAccountRepository>();
                services.AddScoped<IPolicyRepository, SqlPolicyRepository>();
                services.AddScoped<IScheduleRepository, SqlScheduleRepository>();
                services.AddScoped<ILogRepository, SqlLogRepository>();
                services.AddScoped<IUserSessionRepository, SqlSessionRepository>();
            }
            else
            {
                services.AddSingleton(sp =>
                {
                    var seed = DemoSeed.Create();
                    DemoDataSeeder.ApplyDemoPasswords(seed, sp.GetRequiredService<IPasswordHasher>(), configuration["Persistence:DemoAdminPassword"],
                        configuration["Persistence:DemoUserPassword"]);
                    return new InMemoryUmsStore(seed);
                });
                services.AddScoped<ITenantRepository, InMemoryTenantRepository>();
                services.AddScoped<IApplicationRepository, InMemoryApplicationRepository>();
                services.AddScoped<IRoleRepository, InMemoryRoleRepository>();
                services.AddScoped<IUserAccountRepository, InMemoryUserAccountRepository>();
                services.AddScoped<IPolicyRepository, InMemoryPolicyRepository>();
                services.AddScoped<IScheduleRepository, InMemoryScheduleRepository>();
                services.AddScoped<ILogRepository, InMemoryLogRepository>();
                services.AddScoped<IUserSessionRepository, InMemorySessionRepository>();
            }

            var tenantOptions = new TenantOptions
            {
                DefaultTenantId = Guid.TryParse(configuration["Tenancy:DefaultTenantId"], out var t) ? t : DemoSeed.VmmTenantId
            };
            services.AddSingleton(tenantOptions);

            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IPasswordHasher, PasswordHasherService>();
            services.AddScoped<ICurrentTenantService, CurrentTenantService>();
            services.AddScoped<IAuditService, AuditService>();
            services.AddScoped<IAccessContextProvider, AccessContextProvider>();
            services.AddSingleton<ISessionValidator, SessionValidator>();
            services.AddSingleton<IdentityService>();
            services.AddSingleton<ITokenIssuer>(sp => sp.GetRequiredService<IdentityService>());

            return services;
        }
    }
}
