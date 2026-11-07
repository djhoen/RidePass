using Services.Repositories.Data.TenantData;
using Services.Repositories.Data.WaiverData;
using Services.Repositories.Interfaces;

namespace webapi.Helpers
{
    /// <summary>
    /// The "please sign this waiver" link and email, shared by the admin Waivers screen
    /// (WaiverController) and the gate (RedemptionController), so a link sent from either reads and
    /// behaves the same.
    /// </summary>
    public static class WaiverSignRequestMail
    {
        public static string NewToken() =>
            Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

        public static string BuildSigningLink(IConfiguration config, Tenant tenant, string token)
        {
            var apex = config["App:RootDomain"] ?? "ridepass.io";
            return $"https://{tenant.Subdomain}.{apex}/SignWaiver/{token}";
        }

        /// <summary>Sends the signing email and marks the request sent. Returns false when the
        /// emailer is unconfigured or the send fails; the request stays pending so the link can be
        /// copied and handed over manually, or the send retried.</summary>
        public static async Task<bool> TrySend(Services.Helpers.ISmtpEmailer emailer, IConfiguration config,
            Tenant tenant, IWaiverSignRequestRepository requests, WaiverSignRequestRow row)
        {
            if (!emailer.IsConfigured) return false;
            var link = BuildSigningLink(config, tenant, row.Token);
            static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);
            var html =
                $"<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:480px\">" +
                $"<h2 style=\"margin:0 0 8px\">{Enc(tenant.DisplayName)}</h2>" +
                $"<p>Hi {Enc(row.RecipientName ?? "there")},</p>" +
                $"<p>Please sign our waiver before your visit. It only takes a minute, and it means " +
                $"no paperwork holds you up when you arrive:</p>" +
                $"<p style=\"margin:16px 0\"><a href=\"{link}\" style=\"background:#1976d2;color:#fff;padding:10px 18px;" +
                $"border-radius:6px;text-decoration:none\">Sign the waiver</a></p>" +
                $"<p style=\"font-size:12px;color:#666\">Or paste this link into your browser:<br/>{link}</p></div>";
            var ok = await emailer.Send(row.RecipientEmail,
                $"{tenant.DisplayName}: please sign the waiver before your visit",
                html, null, Services.Email.TenantEmailIdentity.For(tenant));
            if (ok) await requests.MarkSent(row.Id, row.TenantId);
            return ok;
        }
    }
}
