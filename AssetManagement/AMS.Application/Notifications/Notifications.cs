using AMS.Application.Interfaces;
using AMS.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using System.Net.Mail;

namespace AMS.Application.Notifications
{
    /// <summary>"Notifications" section of the API settings.</summary>
    public sealed class NotificationOptions
    {
        public const string SectionName = "Notifications";
        /// <summary>Sender address of every AMS e-mail.</summary>
        public string From { get; set; } = "assets-noreply@vishalmegamart.example";
        /// <summary>Shared IT asset desk mailbox: receives new asset requests and the daily digest (no roles are hard-coded).</summary>
        public string AssetDeskEmail { get; set; } = "";
        /// <summary>Address of the AMS web portal, used in links.</summary>
        public string PortalUrl { get; set; } = "";
        /// <summary>Only these e-mail domains receive mail (empty = any). Protects against typos sending data outside the company.</summary>
        public List<string> AllowedDomains { get; set; } = new();
        public bool DailyDigestEnabled { get; set; } = true;
        /// <summary>Local (IST) time of the daily digest, HH:mm.</summary>
        public string DailyDigestTime { get; set; } = "08:30";
    }

    // ------------------------------------------------------------------ models

    public sealed record AssetCheckedOutModel(AssetView Asset, Person To, string ByName, DateTime? Due, string? Note);
    public sealed record AssetCheckedInModel(AssetView Asset, string HolderName, string? HolderEmail, string ByName, string? Note);
    public sealed record RequestSubmittedModel(RequestView Request);
    public sealed record RequestDecidedModel(RequestView Request, bool Approved, string ByName, string? Note);
    public sealed record DailyDigestModel(Guid TenantId, DateTime Date, List<AssetView> OverdueCheckins, List<AssetView> AuditDue,
        List<LicenseView> LicensesExpiring, List<StockItemView> LowStock)
    {
        public bool IsEmpty => OverdueCheckins.Count + AuditDue.Count + LicensesExpiring.Count + LowStock.Count == 0;
    }

    // ------------------------------------------------------------------ notifier

    public sealed class EmailNotifier<TModel> : INotifier<TModel>
    {
        private readonly IEmailTemplate<TModel> _template;
        private readonly IRecipientResolver<TModel> _recipients;
        private readonly IRecipientPolicy _policy;
        private readonly IEmailSender _sender;
        private readonly NotificationOptions _options;
        private readonly ILogger<EmailNotifier<TModel>> _logger;

        public EmailNotifier(IEmailTemplate<TModel> template, IRecipientResolver<TModel> recipients, IRecipientPolicy policy, IEmailSender sender,
            IOptions<NotificationOptions> options, ILogger<EmailNotifier<TModel>> logger)
        {
            _template = template;
            _recipients = recipients;
            _policy = policy;
            _sender = sender;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<bool> NotifyAsync(TModel model, CancellationToken ct = default)
        {
            var name = typeof(TModel).Name;
            try
            {
                var recipients = _policy.Filter(await _recipients.ResolveAsync(model, ct), name);
                if (recipients.To.Count == 0)
                {
                    _logger.LogWarning("[{Notification}] No deliverable To recipients; e-mail not sent", name);
                    return false;
                }
                return await _sender.SendAsync(new EmailMessage
                {
                    From = _options.From, To = recipients.To, Cc = recipients.Cc, Bcc = recipients.Bcc,
                    Subject = _template.BuildSubject(model), Body = _template.BuildBody(model), IsBodyHtml = true
                }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed sending {Notification} notification", name);
                return false;
            }
        }
    }

    public sealed class RecipientPolicy : IRecipientPolicy
    {
        private readonly NotificationOptions _options;
        private readonly ILogger<RecipientPolicy> _logger;

        public RecipientPolicy(IOptions<NotificationOptions> options, ILogger<RecipientPolicy> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public EmailRecipients Filter(EmailRecipients recipients, string notificationName)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> Clean(IEnumerable<string> list) => list
                .SelectMany(x => (x ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(e =>
                {
                    if (!MailAddress.TryCreate(e, out var a)) { _logger.LogWarning("[{N}] Invalid address '{Address}' dropped", notificationName, e); return false; }
                    if (_options.AllowedDomains.Count > 0 && !_options.AllowedDomains.Contains(a.Host, StringComparer.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("[{N}] Address outside the allowed domains dropped: {Address}", notificationName, e);
                        return false;
                    }
                    return seen.Add(a.Address);
                })
                .ToList();
            return new EmailRecipients { To = Clean(recipients.To), Cc = Clean(recipients.Cc), Bcc = Clean(recipients.Bcc) };
        }
    }

    // ------------------------------------------------------------------ templates

    internal static class Mail
    {
        private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");
        public static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        public static string Date(DateTime? d) => d?.ToString("dd MMM yyyy", India) ?? "—";
        public static string Money(decimal v) => "₹" + v.ToString("N0", India);

        /// <summary>Common layout: prismatic blue header strip, white body, footer.</summary>
        public static string Layout(string title, string body, string portalUrl) =>
            "<div style=\"font-family:Segoe UI,Arial,sans-serif;max-width:640px;margin:auto;border:1px solid #dce5f5;border-radius:10px;overflow:hidden\">" +
            "<div style=\"background:linear-gradient(90deg,#0a1f5c,#1559e0 45%,#4f9bff);color:#fff;padding:16px 20px;font-size:17px;font-weight:700\">" +
            $"VMM Asset Management · {E(title)}</div><div style=\"padding:20px;color:#0f1d3d;font-size:14px;line-height:1.55\">{body}" +
            (string.IsNullOrWhiteSpace(portalUrl) ? "" : $"<p style=\"margin-top:22px\"><a href=\"{E(portalUrl)}\" style=\"background:#1559e0;color:#fff;padding:9px 16px;border-radius:6px;text-decoration:none;font-weight:600\">Open Asset Management</a></p>") +
            "<p style=\"color:#5d6b8a;font-size:12px;margin-top:24px\">This is an automated message from the VMM Asset Management System. Please do not reply.</p></div></div>";

        public static string Table(params (string Label, string? Value)[] rows) =>
            "<table style=\"border-collapse:collapse;margin:10px 0\">" +
            string.Concat(rows.Where(r => !string.IsNullOrEmpty(r.Value)).Select(r =>
                $"<tr><td style=\"padding:4px 14px 4px 0;color:#5d6b8a\">{E(r.Label)}</td><td style=\"padding:4px 0;font-weight:600\">{E(r.Value)}</td></tr>")) +
            "</table>";
    }

    public sealed class AssetCheckedOutTemplate : IEmailTemplate<AssetCheckedOutModel>
    {
        private readonly NotificationOptions _o;
        public AssetCheckedOutTemplate(IOptions<NotificationOptions> o) => _o = o.Value;
        public string BuildSubject(AssetCheckedOutModel m) => $"Asset issued to you: {m.Asset.AssetTag} {m.Asset.Name}";
        public string BuildBody(AssetCheckedOutModel m) => Mail.Layout("Asset issued", $"<p>Hello {Mail.E(m.To.FullName)},</p><p>The following asset has been checked out to you by {Mail.E(m.ByName)}:</p>" +
            Mail.Table(("Asset tag", m.Asset.AssetTag), ("Asset", m.Asset.Name), ("Model", $"{m.Asset.ManufacturerName} {m.Asset.ModelName}"), ("Serial", m.Asset.Serial),
                ("Location", m.Asset.LocationName), ("Return by", m.Due == null ? null : Mail.Date(m.Due)), ("Note", m.Note)) +
            "<p>Please take good care of it and report any damage or loss to the IT asset desk straight away.</p>", _o.PortalUrl);
    }

    public sealed class AssetCheckedInTemplate : IEmailTemplate<AssetCheckedInModel>
    {
        private readonly NotificationOptions _o;
        public AssetCheckedInTemplate(IOptions<NotificationOptions> o) => _o = o.Value;
        public string BuildSubject(AssetCheckedInModel m) => $"Asset returned: {m.Asset.AssetTag} {m.Asset.Name}";
        public string BuildBody(AssetCheckedInModel m) => Mail.Layout("Asset returned", $"<p>Hello {Mail.E(m.HolderName)},</p><p>{Mail.E(m.ByName)} has checked in the following asset from you:</p>" +
            Mail.Table(("Asset tag", m.Asset.AssetTag), ("Asset", m.Asset.Name), ("Note", m.Note)) + "<p>It is no longer issued to you.</p>", _o.PortalUrl);
    }

    public sealed class RequestSubmittedTemplate : IEmailTemplate<RequestSubmittedModel>
    {
        private readonly NotificationOptions _o;
        public RequestSubmittedTemplate(IOptions<NotificationOptions> o) => _o = o.Value;
        public string BuildSubject(RequestSubmittedModel m) => $"New asset request from {m.Request.RequestedByName}: {m.Request.AssetTag}";
        public string BuildBody(RequestSubmittedModel m) => Mail.Layout("New asset request", $"<p>{Mail.E(m.Request.RequestedByName)} ({Mail.E(m.Request.RequestedByEmpCode)}) has requested an asset:</p>" +
            Mail.Table(("Asset", $"{m.Request.AssetTag} {m.Request.AssetName}"), ("Location", m.Request.LocationCode), ("Reason", m.Request.Reason)) +
            "<p>Review it under <b>Asset Requests</b>. Who may approve is decided by UMS policy (for example, nobody can approve their own request).</p>", _o.PortalUrl);
    }

    public sealed class RequestDecidedTemplate : IEmailTemplate<RequestDecidedModel>
    {
        private readonly NotificationOptions _o;
        public RequestDecidedTemplate(IOptions<NotificationOptions> o) => _o = o.Value;
        public string BuildSubject(RequestDecidedModel m) => $"Your asset request was {(m.Approved ? "approved" : "rejected")}: {m.Request.AssetTag}";
        public string BuildBody(RequestDecidedModel m) => Mail.Layout(m.Approved ? "Request approved" : "Request rejected",
            $"<p>Hello {Mail.E(m.Request.RequestedByName)},</p><p>Your request for <b>{Mail.E(m.Request.AssetTag)} {Mail.E(m.Request.AssetName)}</b> was " +
            $"<b>{(m.Approved ? "approved" : "rejected")}</b> by {Mail.E(m.ByName)}.</p>" + Mail.Table(("Note", m.Note)) +
            (m.Approved ? "<p>The asset is now issued to you; please collect it from the IT asset desk.</p>" : ""), _o.PortalUrl);
    }

    public sealed class DailyDigestTemplate : IEmailTemplate<DailyDigestModel>
    {
        private readonly NotificationOptions _o;
        public DailyDigestTemplate(IOptions<NotificationOptions> o) => _o = o.Value;
        public string BuildSubject(DailyDigestModel m) => $"Asset desk digest {Mail.Date(m.Date)}: {m.OverdueCheckins.Count} overdue, {m.AuditDue.Count} audits, {m.LowStock.Count} low stock";

        private static string List<T>(string title, List<T> items, Func<T, string> line) => items.Count == 0 ? "" :
            $"<h3 style=\"margin:18px 0 6px;color:#0c2c80;font-size:15px\">{Mail.E(title)} ({items.Count})</h3><ul style=\"margin:0;padding-left:18px\">" +
            string.Concat(items.Take(30).Select(i => $"<li>{Mail.E(line(i))}</li>")) + (items.Count > 30 ? $"<li>… and {items.Count - 30} more</li>" : "") + "</ul>";

        public string BuildBody(DailyDigestModel m) => Mail.Layout("Daily digest", "<p>Items that need the asset desk's attention today:</p>" +
            List("Overdue check-ins", m.OverdueCheckins, a => $"{a.AssetTag} {a.Name} — {a.AssignedToName}, due {Mail.Date(a.ExpectedCheckin)}") +
            List("Audits due within 30 days", m.AuditDue, a => $"{a.AssetTag} {a.Name} at {a.LocationCode} — due {Mail.Date(a.NextAuditDate)}") +
            List("Licences expiring within 30 days", m.LicensesExpiring, l => $"{l.Name} — expires {Mail.Date(l.ExpirationDate)} ({l.Used}/{l.Seats} seats used)") +
            List("Stock at or below re-order level", m.LowStock, s => $"{s.Name} at {s.LocationCode} — {s.Available} available (re-order at {s.MinQuantity})"), _o.PortalUrl);
    }

    // ------------------------------------------------------------------ recipients

    public sealed class AssetCheckedOutRecipients : IRecipientResolver<AssetCheckedOutModel>
    {
        public Task<EmailRecipients> ResolveAsync(AssetCheckedOutModel m, CancellationToken ct = default) =>
            Task.FromResult(new EmailRecipients { To = new[] { m.To.Email } });
    }

    public sealed class AssetCheckedInRecipients : IRecipientResolver<AssetCheckedInModel>
    {
        public Task<EmailRecipients> ResolveAsync(AssetCheckedInModel m, CancellationToken ct = default) =>
            Task.FromResult(new EmailRecipients { To = m.HolderEmail == null ? Array.Empty<string>() : new[] { m.HolderEmail } });
    }

    /// <summary>New requests go to the asset desk mailbox (configuration, not a role), with the requester in copy.</summary>
    public sealed class RequestSubmittedRecipients : IRecipientResolver<RequestSubmittedModel>
    {
        private readonly NotificationOptions _o;
        public RequestSubmittedRecipients(IOptions<NotificationOptions> o) => _o = o.Value;
        public Task<EmailRecipients> ResolveAsync(RequestSubmittedModel m, CancellationToken ct = default) => Task.FromResult(new EmailRecipients
        {
            To = string.IsNullOrWhiteSpace(_o.AssetDeskEmail) ? Array.Empty<string>() : new[] { _o.AssetDeskEmail },
            Cc = m.Request.RequestedByEmail == null ? Array.Empty<string>() : new[] { m.Request.RequestedByEmail }
        });
    }

    public sealed class RequestDecidedRecipients : IRecipientResolver<RequestDecidedModel>
    {
        public Task<EmailRecipients> ResolveAsync(RequestDecidedModel m, CancellationToken ct = default) =>
            Task.FromResult(new EmailRecipients { To = m.Request.RequestedByEmail == null ? Array.Empty<string>() : new[] { m.Request.RequestedByEmail } });
    }

    public sealed class DailyDigestRecipients : IRecipientResolver<DailyDigestModel>
    {
        private readonly NotificationOptions _o;
        public DailyDigestRecipients(IOptions<NotificationOptions> o) => _o = o.Value;
        public Task<EmailRecipients> ResolveAsync(DailyDigestModel m, CancellationToken ct = default) =>
            Task.FromResult(new EmailRecipients { To = string.IsNullOrWhiteSpace(_o.AssetDeskEmail) ? Array.Empty<string>() : new[] { _o.AssetDeskEmail } });
    }
}
