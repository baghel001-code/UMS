using ApplicationLayer.Interfaces;
using DomainLayer.Interfaces;
using Infrastructure.Database;
using Infrastructure.Events;
using Infrastructure.Identity;
using Infrastructure.Options;
using Infrastructure.Repositories;
using Infrastructure.Services;
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
    }
}
