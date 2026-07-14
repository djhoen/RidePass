using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Services.Helpers
{
    /// <summary>
    /// Who a message appears to come from. The envelope address is always the platform's
    /// authenticated FromAddress (we're DKIM-signed for ridepass.io and can't sign for a track's
    /// own domain), so the tenant's identity is carried by the display name and the Reply-To:
    /// the rider sees "Motoland" in their inbox, and hitting reply reaches the track, not a
    /// noreply mailbox. Null anywhere = fall back to the platform defaults.
    /// </summary>
    public record EmailSender(string? FromName, string? ReplyToEmail = null, string? ReplyToName = null);

    public interface ISmtpEmailer
    {
        bool IsConfigured { get; }
        Task<bool> Send(string toEmail, string subject, string htmlBody);
        // Overload that stamps extra headers (e.g. List-Unsubscribe / List-Unsubscribe-Post
        // for marketing one-click unsubscribe).
        Task<bool> Send(string toEmail, string subject, string htmlBody, IReadOnlyDictionary<string, string>? headers);
        // Overload that sends AS a tenant: their name on the From line, replies routed to them.
        // Every rider-facing email should use this, so no rider gets mail from a platform they've
        // never heard of about a track they have.
        Task<bool> Send(string toEmail, string subject, string htmlBody,
            IReadOnlyDictionary<string, string>? headers, EmailSender? sender);
    }

    /// <summary>
    /// Config-gated SMTP sender. Reads Email:Smtp:Host / Port / User / Password and Email:FromAddress / FromName.
    /// If Host or FromAddress is missing, IsConfigured is false and Send is a silent no-op so notification
    /// emission code doesn't have to special-case "email not set up yet" deployments.
    /// </summary>
    public class SmtpEmailer : ISmtpEmailer
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpEmailer> _logger;

        public bool IsConfigured { get; }

        public SmtpEmailer(IConfiguration config, ILogger<SmtpEmailer> logger)
        {
            _config = config;
            _logger = logger;
            IsConfigured = !string.IsNullOrWhiteSpace(config["Email:Smtp:Host"])
                        && !string.IsNullOrWhiteSpace(config["Email:FromAddress"]);
        }

        public Task<bool> Send(string toEmail, string subject, string htmlBody)
            => Send(toEmail, subject, htmlBody, null, null);

        public Task<bool> Send(string toEmail, string subject, string htmlBody,
            IReadOnlyDictionary<string, string>? headers)
            => Send(toEmail, subject, htmlBody, headers, null);

        public async Task<bool> Send(string toEmail, string subject, string htmlBody,
            IReadOnlyDictionary<string, string>? headers, EmailSender? sender)
        {
            if (!IsConfigured) return false;
            try
            {
                var host = _config["Email:Smtp:Host"]!;
                var port = int.TryParse(_config["Email:Smtp:Port"], out var p) ? p : 587;
                var user = _config["Email:Smtp:User"];
                var pass = _config["Email:Smtp:Password"];
                var fromAddr = _config["Email:FromAddress"]!;
                // The tenant's name when the caller supplied one, else the platform default.
                var fromName = string.IsNullOrWhiteSpace(sender?.FromName)
                    ? (_config["Email:FromName"] ?? "RidePass")
                    : sender!.FromName!;

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = true,
                    Credentials = string.IsNullOrEmpty(user) ? null : new NetworkCredential(user, pass),
                };
                using var msg = new MailMessage
                {
                    From = new MailAddress(fromAddr, fromName),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true,
                };
                msg.To.Add(toEmail);
                // Replies go to the track that actually sold the ticket. A malformed contact email
                // on the tenant record must not take the whole send down, so it's best-effort.
                if (!string.IsNullOrWhiteSpace(sender?.ReplyToEmail))
                {
                    try { msg.ReplyToList.Add(new MailAddress(sender!.ReplyToEmail!, sender.ReplyToName ?? fromName)); }
                    catch (FormatException)
                    {
                        _logger.LogWarning("Tenant reply-to '{ReplyTo}' is not a valid address; sending without it.",
                            sender!.ReplyToEmail);
                    }
                }
                if (headers is not null)
                {
                    foreach (var h in headers) msg.Headers.Add(h.Key, h.Value);
                }
                await client.SendMailAsync(msg);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send email to {Email}", toEmail);
                return false;
            }
        }
    }
}
