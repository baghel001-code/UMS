using AMS.Core.Security;
using AMS.Core.Services;
using AMS.Infrastructure.Persistence;
using AMS.Infrastructure.Ums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMS.Infrastructure
{
    public static class DependencyInjection
    {
        /// <summary>
        /// Ams:Database:Provider = Sqlite (default, file database for development) | SqlServer, with ConnectionStrings:Ams.
        /// Ums:BaseUrl = the UMS Web API that authenticates users and authorises every action.
        /// </summary>
        public static IServiceCollection AddAms(this IServiceCollection services, IConfiguration config)
        {
            var provider = config["Ams:Database:Provider"] ?? "Sqlite";
            var cs = config.GetConnectionString("Ams") ?? "Data Source=ams.db";
            var builder = new DbContextOptionsBuilder<AmsDbContext>();
            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)) builder.UseSqlServer(cs);
            else builder.UseSqlite(cs);
            services.AddSingleton(builder.Options);
            services.AddSingleton<AmsDbContextFactory>();
            services.AddSingleton<IAmsDbContextFactory>(sp => sp.GetRequiredService<AmsDbContextFactory>());

            var ums = new UmsOptions
            {
                BaseUrl = config["Ums:BaseUrl"] ?? throw new InvalidOperationException("Ums:BaseUrl is not configured."),
                Device = config["Ums:Device"] ?? "Managed",
                Channel = config["Ums:Channel"] ?? "Web",
            };
            services.AddSingleton(ums);
            services.AddHttpClient<UmsClient>(c =>
            {
                c.BaseAddress = new Uri(ums.BaseUrl.TrimEnd('/') + "/");
                c.Timeout = TimeSpan.FromSeconds(20);
            });

            services.AddSingleton(TimeProvider.System);
            services.AddScoped<UmsSession>();
            services.AddScoped<IAccessGuard, UmsAccessGuard>();
            services.AddScoped<IPeopleDirectory, UmsPeopleDirectory>();

            services.AddScoped<AssetService>();
            services.AddScoped<MaintenanceService>();
            services.AddScoped<RequestService>();
            services.AddScoped<LicenseService>();
            services.AddScoped<InventoryService>();
            services.AddScoped<LookupService>();
            services.AddScoped<ReportService>();
            return services;
        }

        /// <summary>Creates the schema if the database is new (EnsureCreated; use EF migrations when the model starts to evolve in production).</summary>
        public static async Task InitialiseAmsDatabaseAsync(this IServiceProvider services)
        {
            await using var db = services.GetRequiredService<AmsDbContextFactory>().CreateContext(Guid.Empty);
            await db.Database.EnsureCreatedAsync();
        }
    }
}
