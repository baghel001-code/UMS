using AMS.Application.Interfaces;
using AMS.Application.Notifications;
using AMS.Infrastructure.Api;
using AMS.Infrastructure.Email;
using AMS.Infrastructure.Persistence;
using AMS.Infrastructure.Ums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMS.Infrastructure
{
    public static class DependencyInjection
    {
        private static UmsOptions AddUms(IServiceCollection services, IConfiguration config)
        {
            var ums = config.GetSection(UmsOptions.SectionName).Get<UmsOptions>() ?? new UmsOptions();
            if (string.IsNullOrWhiteSpace(ums.BaseUrl)) throw new InvalidOperationException("Ums:BaseUrl is not configured.");
            services.AddSingleton(ums);
            services.AddHttpClient<UmsClient>(c =>
            {
                c.BaseAddress = new Uri(ums.BaseUrl.TrimEnd('/') + "/");
                c.Timeout = TimeSpan.FromSeconds(20);
            });
            return ums;
        }

        /// <summary>
        /// AMS API side: Dapper repositories over SQL Server stored procedures (ConnectionStrings:Ams), the UMS guard/directory for the
        /// caller of the current request, SMTP e-mail with a background queue and the daily digest.
        /// </summary>
        public static IServiceCollection AddAmsApiInfrastructure(this IServiceCollection services, IConfiguration config)
        {
            var db = new AmsDatabaseOptions
            {
                ConnectionString = config.GetConnectionString("Ams") ?? throw new InvalidOperationException("ConnectionStrings:Ams is not configured."),
                AutoMigrate = config.GetValue("Ams:Database:AutoMigrate", false),
                DemoTenantId = Guid.TryParse(config["Ams:Database:DemoTenantId"], out var t) ? t : null,
            };
            services.AddSingleton(db);
            services.AddSingleton<DapperHelper>();
            services.AddSingleton<DatabaseInitializer>();
            services.AddSingleton<DemoDataSeeder>();
            services.AddScoped<SetupRepository>();
            services.AddScoped<ISetupRepository>(sp => sp.GetRequiredService<SetupRepository>());
            services.AddScoped<IAssetRepository, AssetRepository>();
            services.AddScoped<IMaintenanceRepository, MaintenanceRepository>();
            services.AddScoped<IRequestRepository, RequestRepository>();
            services.AddScoped<ILicenseRepository, LicenseRepository>();
            services.AddScoped<IStockRepository, StockRepository>();
            services.AddScoped<IHistoryRepository, HistoryRepository>();

            AddUms(services, config);
            services.AddMemoryCache();
            services.AddHttpContextAccessor();
            services.AddSingleton(new TrustedForwarders(config.GetSection("Ums:TrustedForwarders").Get<string[]>()));
            services.AddScoped<ICurrentActor, HttpCurrentActor>();
            services.AddScoped<IAccessGuard, UmsAccessGuard>();
            services.AddScoped<IPeopleDirectory, UmsPeopleDirectory>();

            services.Configure<NotificationOptions>(config.GetSection(NotificationOptions.SectionName));
            services.Configure<EmailServerSettings>(config.GetSection(EmailServerSettings.SectionName));
            services.AddScoped<IEmailSender, SmtpEmailSender>();
            services.AddSingleton<NotificationQueue>();
            services.AddSingleton<INotificationPublisher>(sp => sp.GetRequiredService<NotificationQueue>());
            services.AddHostedService<NotificationWorker>();
            services.AddSingleton<DailyDigestService>();
            services.AddHostedService(sp => sp.GetRequiredService<DailyDigestService>());
            return services;
        }

        /// <summary>
        /// Web portal side: the UMS client (sign-in, refresh, keep-alive, sign-out) and the API-call service that talks to the AMS API.
        /// The portal supplies <see cref="IAccessTokenProvider"/> (its signed-in user's tokens). It has no database access.
        /// </summary>
        public static IServiceCollection AddAmsPortalInfrastructure(this IServiceCollection services, IConfiguration config)
        {
            AddUms(services, config);
            var api = config.GetSection(AmsApiOptions.SectionName).Get<AmsApiOptions>() ?? new AmsApiOptions();
            if (string.IsNullOrWhiteSpace(api.BaseUrl)) throw new InvalidOperationException("AmsApi:BaseUrl is not configured.");
            services.AddSingleton(api);
            services.AddHttpClient<IAPICall, ApiCallService>(c =>
            {
                c.BaseAddress = new Uri(api.BaseUrl.TrimEnd('/') + "/");
                c.Timeout = TimeSpan.FromSeconds(Math.Max(5, api.TimeoutSeconds));
            });
            return services;
        }
    }
}
