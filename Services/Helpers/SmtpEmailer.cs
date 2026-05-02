using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Services.Helpers
{
    public interface ISmtpEmailer
    {
        bool IsConfigured { get; }
        Task<bool> Send(string toEmail, string subject, string htmlBody);
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

        public async Task<bool> Send(string toEmail, string subject, string htmlBody)
        {
            if (!IsConfigured) return false;
            try
            {
                var host = _config["Email:Smtp:Host"]!;
                var port = int.TryParse(_config["Email:Smtp:Port"], out var p) ? p : 587;
                var user = _config["Email:Smtp:User"];
                var pass = _config["Email:Smtp:Password"];
                var fromAddr = _config["Email:FromAddress"]!;
                var fromName = _config["Email:FromName"] ?? "RidePass";

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
