using System.Net.Mail;
using System.Net;
using System.Text.RegularExpressions;

namespace Services.Helpers
{
    public class EmailHelper
    {
        // TODO: Move credentials to appsettings.json or environment variables
        private static string FromPassword = "YOUR_APP_PASSWORD";
        private static string FromAddress = "noreply@yourdomain.com";
        private static string DisplayName = "Your App Name";

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var addr = new MailAddress(email);
                return addr.Address == email && addr.Host.Contains(".");
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static async Task SendBccEmail(List<string> bccEmails, string subject, string body)
        {
            var invalidEmails = new List<string>();

            try
            {
                using (MailMessage mail = new MailMessage())
                {
                    mail.From = new MailAddress(FromAddress, DisplayName);

                    foreach (string email in bccEmails ?? new List<string>())
                    {
                        if (IsValidEmail(email))
                        {
                            mail.Bcc.Add(email);
                        }
                        else
                        {
                            invalidEmails.Add(email);
                        }
                    }

                    mail.Subject = subject ?? string.Empty;
                    mail.Body = body ?? string.Empty;
                    mail.IsBodyHtml = true;

                    using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587))
                    {
                        smtp.Credentials = new NetworkCredential(FromAddress, FromPassword);
                        smtp.EnableSsl = true;
                        smtp.Timeout = 30000;
                        await smtp.SendMailAsync(mail);
                    }
                }
            }
            catch (SmtpException smtpEx)
            {
                throw new InvalidOperationException("Failed to send email due to SMTP error.", smtpEx);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("An unexpected error occurred while sending email.", ex);
            }
        }

        public static async Task SendEmail(string toEmail, string subject, string body)
        {
            try
            {
                using (MailMessage mail = new MailMessage())
                {
                    mail.From = new MailAddress(FromAddress, DisplayName);
                    mail.To.Add(toEmail);
                    mail.Subject = subject;
                    mail.Body = body;
                    mail.IsBodyHtml = true;

                    using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587))
                    {
                        smtp.Credentials = new NetworkCredential(FromAddress, FromPassword);
                        smtp.EnableSsl = true;
                        await smtp.SendMailAsync(mail);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error
            }
        }

        public static bool IsValid(string email)
        {
            string regex = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
            return Regex.IsMatch(email, regex, RegexOptions.IgnoreCase);
        }
    }
}
