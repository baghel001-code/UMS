using AMS.Application.Interfaces;
using AMS.Application.Notifications;
using AMS.Application.Services;
using AMS.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;
using System.Threading.Channels;

namespace AMS.Infrastructure.Email
{
    /// <summary>"EmailServerSettings" section.</summary>
    public sealed class EmailServerSettings
    {
        public const string SectionName = "EmailServerSettings";
        public bool Enabled { get; set; }
        public string SmtpServer { get; set; } = "";
        public int SmtpPort { get; set; } = 25;
        public string? SmtpUser { get; set; }
        public string? SmtpPwd { get; set; }
        public bool EnableSsl { get; set; }
        /// <summary>When set, messages are written as .eml files to this folder instead of being sent (development).</summary>
        public string? PickupDirectory { get; set; }
    }

    public sealed class SmtpEmailSender : IEmailSender
    {
        private readonly EmailServerSettings _settings;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IOptions<EmailServerSettings> settings, ILogger<SmtpEmailSender> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (!_settings.Enabled)
            {
                _logger.LogInformation("E-mail disabled; not sending '{Subject}' to {To}", message.Subject, string.Join(";", message.To));
                return false;
            }
            using var mail = new MailMessage { From = new MailAddress(message.From), Subject = message.Subject, Body = message.Body, IsBodyHtml = message.IsBodyHtml };
            foreach (var a in message.To) mail.To.Add(a);
            foreach (var a in message.Cc) mail.CC.Add(a);
            foreach (var a in message.Bcc) mail.Bcc.Add(a);

            using var client = new SmtpClient();
            if (!string.IsNullOrWhiteSpace(_settings.PickupDirectory))
            {
                Directory.CreateDirectory(_settings.PickupDirectory);
                client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
                client.PickupDirectoryLocation = Path.GetFullPath(_settings.PickupDirectory);
            }
            else
            {
                client.Host = _settings.SmtpServer;
                client.Port = _settings.SmtpPort;
                client.EnableSsl = _settings.EnableSsl;
                if (!string.IsNullOrWhiteSpace(_settings.SmtpUser)) client.Credentials = new NetworkCredential(_settings.SmtpUser, _settings.SmtpPwd);
            }
            await client.SendMailAsync(mail, ct);
            _logger.LogInformation("E-mail '{Subject}' sent to {To}", message.Subject, string.Join(";", message.To));
            return true;
        }
    }

    /// <summary>In-process queue: use cases publish, <see cref="NotificationWorker"/> sends in its own scope.</summary>
    public sealed class NotificationQueue : INotificationPublisher
    {
        private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel =
            Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });

        public ChannelReader<Func<IServiceProvider, CancellationToken, Task>> Reader => _channel.Reader;

        public void Publish<TModel>(TModel model) where TModel : notnull =>
            _channel.Writer.TryWrite((sp, ct) => sp.GetRequiredService<INotifier<TModel>>().NotifyAsync(model, ct));
    }

    public sealed class NotificationWorker : BackgroundService
    {
        private readonly NotificationQueue _queue;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<NotificationWorker> _logger;

        public NotificationWorker(NotificationQueue queue, IServiceScopeFactory scopes, ILogger<NotificationWorker> logger)
        {
            _queue = queue;
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    await work(scope.ServiceProvider, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Notification failed");
                }
            }
        }
    }

    /// <summary>Sends the asset desk digest once a day at Notifications:DailyDigestTime (IST) for every tenant with AMS data.</summary>
    public sealed class DailyDigestService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly NotificationOptions _options;
        private readonly IBusinessClock _clock;
        private readonly ILogger<DailyDigestService> _logger;

        public DailyDigestService(IServiceScopeFactory scopes, IOptions<NotificationOptions> options, IBusinessClock clock, ILogger<DailyDigestService> logger)
        {
            _scopes = scopes;
            _options = options.Value;
            _clock = clock;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.DailyDigestEnabled || string.IsNullOrWhiteSpace(_options.AssetDeskEmail)) return;
            var at = TimeSpan.TryParse(_options.DailyDigestTime, out var t) ? t : new TimeSpan(8, 30, 0);
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = _clock.Now;
                var next = now.Date + at;
                if (next <= now) next = next.AddDays(1);
                await Task.Delay(next - now, stoppingToken);
                await SendAsync(stoppingToken);
            }
        }

        public async Task SendAsync(CancellationToken ct)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var builder = scope.ServiceProvider.GetRequiredService<DailyDigestBuilder>();
                var notifier = scope.ServiceProvider.GetRequiredService<INotifier<DailyDigestModel>>();
                foreach (var tenant in await scope.ServiceProvider.GetRequiredService<SetupRepository>().TenantsAsync())
                {
                    var digest = await builder.BuildAsync(tenant, ct);
                    if (!digest.IsEmpty) await notifier.NotifyAsync(digest, ct);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Daily digest failed");
            }
        }
    }
}
