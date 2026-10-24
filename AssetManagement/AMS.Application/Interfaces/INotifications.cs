namespace AMS.Application.Interfaces
{
    // E-mail notifications, built like the other VMM applications: a template, a recipient resolver and a sender per message type,
    // composed by EmailNotifier<TModel>. Use cases only publish a model; sending happens in the background.

    public sealed class EmailMessage
    {
        public required string From { get; init; }
        public IReadOnlyList<string> To { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Cc { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Bcc { get; init; } = Array.Empty<string>();
        public string Subject { get; init; } = "";
        public string Body { get; init; } = "";
        public bool IsBodyHtml { get; init; } = true;
    }

    public sealed record EmailRecipients
    {
        public IReadOnlyList<string> To { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Cc { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Bcc { get; init; } = Array.Empty<string>();
    }

    public interface IEmailSender
    {
        Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default);
    }

    public interface IEmailTemplate<in TModel>
    {
        string BuildSubject(TModel model);
        string BuildBody(TModel model);
    }

    public interface IRecipientResolver<in TModel>
    {
        Task<EmailRecipients> ResolveAsync(TModel model, CancellationToken ct = default);
    }

    public interface IRecipientPolicy
    {
        /// <summary>Splits ";"/"," lists, validates, de-duplicates (To &gt; Cc &gt; Bcc) and applies the domain allow-list.</summary>
        EmailRecipients Filter(EmailRecipients recipients, string notificationName);
    }

    public interface INotifier<in TModel>
    {
        Task<bool> NotifyAsync(TModel model, CancellationToken ct = default);
    }

    /// <summary>Queues a notification; it is sent in the background so a slow mail server never delays the user.</summary>
    public interface INotificationPublisher
    {
        void Publish<TModel>(TModel model) where TModel : notnull;
    }
}
