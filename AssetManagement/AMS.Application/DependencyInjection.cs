using AMS.Application.Common;
using AMS.Application.Interfaces;
using AMS.Application.Notifications;
using AMS.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AMS.Application
{
    public static class DependencyInjection
    {
        /// <summary>Use cases and e-mail notifications of the AMS API (persistence, UMS and SMTP adapters come from AMS.Infrastructure).</summary>
        public static IServiceCollection AddAmsApplication(this IServiceCollection services)
        {
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IBusinessClock>(sp => new BusinessClock(sp.GetRequiredService<TimeProvider>()));

            services.AddScoped<AssetUseCases>();
            services.AddScoped<MaintenanceUseCases>();
            services.AddScoped<RequestUseCases>();
            services.AddScoped<LicenseUseCases>();
            services.AddScoped<StockUseCases>();
            services.AddScoped<SetupUseCases>();
            services.AddScoped<ReportUseCases>();
            services.AddScoped<DailyDigestBuilder>();

            // e-mail: template + recipients per message type, composed by EmailNotifier<T>
            services.AddSingleton<IRecipientPolicy, RecipientPolicy>();
            Notification<AssetCheckedOutModel, AssetCheckedOutTemplate, AssetCheckedOutRecipients>(services);
            Notification<AssetCheckedInModel, AssetCheckedInTemplate, AssetCheckedInRecipients>(services);
            Notification<RequestSubmittedModel, RequestSubmittedTemplate, RequestSubmittedRecipients>(services);
            Notification<RequestDecidedModel, RequestDecidedTemplate, RequestDecidedRecipients>(services);
            Notification<DailyDigestModel, DailyDigestTemplate, DailyDigestRecipients>(services);
            return services;
        }

        private static void Notification<TModel, TTemplate, TRecipients>(IServiceCollection services)
            where TTemplate : class, IEmailTemplate<TModel>
            where TRecipients : class, IRecipientResolver<TModel>
        {
            services.AddScoped<IEmailTemplate<TModel>, TTemplate>();
            services.AddScoped<IRecipientResolver<TModel>, TRecipients>();
            services.AddScoped<INotifier<TModel>, EmailNotifier<TModel>>();
        }
    }
}
