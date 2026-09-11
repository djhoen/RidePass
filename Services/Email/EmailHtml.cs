using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Services.Repositories.Data.TenantData;

namespace Services.Email
{
    /// <summary>
    /// The tenant identity an email is dressed in: logo, color, address, socials. Built once per
    /// send run from the tenant row plus its branding row; every URL already absolute.
    /// </summary>
    public sealed record EmailBranding(
        string TrackName,
        string SiteUrl,
        string? LogoUrl,
        string PrimaryColor,
        string? AddressLine,
        string? Phone,
        string? ContactEmail,
        IReadOnlyList<(string Label, string Url)> Socials,
        string? CustomFooterHtml = null)
    {
        public static EmailBranding From(Tenant tenant, TenantBranding? branding, string siteUrl)
        {
            var root = siteUrl.TrimEnd('/');
            var socials = new List<(string, string)>();
            if (!string.IsNullOrWhiteSpace(tenant.SocialFacebookUrl)) socials.Add(("Facebook", tenant.SocialFacebookUrl!));
            if (!string.IsNullOrWhiteSpace(tenant.SocialInstagramUrl)) socials.Add(("Instagram", tenant.SocialInstagramUrl!));
            if (!string.IsNullOrWhiteSpace(tenant.SocialTiktokUrl)) socials.Add(("TikTok", tenant.SocialTiktokUrl!));
            if (!string.IsNullOrWhiteSpace(tenant.SocialYoutubeUrl)) socials.Add(("YouTube", tenant.SocialYoutubeUrl!));

            var addressParts = new[] { tenant.AddressLine, JoinCity(tenant) }.Where(s => !string.IsNullOrWhiteSpace(s));
            var address = string.Join(", ", addressParts);

            return new EmailBranding(
                TrackName: tenant.DisplayName,
                SiteUrl: root,
                LogoUrl: EmailHtml.AbsoluteUrl(branding?.LogoUrl, root),
                PrimaryColor: NormalizeColor(branding?.PrimaryColor) ?? "#1976D2",
                AddressLine: string.IsNullOrWhiteSpace(address) ? null : address,
                Phone: tenant.Phone,
                ContactEmail: tenant.ContactEmail,
                Socials: socials,
                CustomFooterHtml: string.IsNullOrWhiteSpace(tenant.MarketingEmailFooterHtml) ? null : tenant.MarketingEmailFooterHtml);
        }

        private static string? JoinCity(Tenant t)
        {
            var cityRegion = string.Join(", ", new[] { t.City, t.Region }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return string.Join(" ", new[] { cityRegion, t.PostalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private static string? NormalizeColor(string? c)
            => c is not null && Regex.IsMatch(c.Trim(), "^#[0-9a-fA-F]{6}$") ? c.Trim() : null;
    }

    /// <summary>
    /// Turns editor HTML into an email. The rich text editor produces clean semantic HTML for a
    /// web page; an email client is a much worse browser, and a bare paragraph reads as a
    /// receipt rather than a newsletter. At send time, with the stored body left editor-friendly:
    ///
    ///   1. Relative image and link URLs (/uploads/..., /Events/...) are made absolute.
    ///   2. Images are capped to the column width; the body sits in a 600px centered column.
    ///   3. The editor's button node (an anchor with class rp-button) becomes a table-based
    ///      button in the track's primary color, which is the only button that survives Outlook.
    ///   4. A branded header (logo or name) and footer (address, phone, socials) wrap the body,
    ///      and the caller's compliance footer (unsubscribe) goes last.
    ///   5. Preview text is emitted as a hidden preheader so inboxes show it under the subject.
    /// </summary>
    public static class EmailHtml
    {
        private static readonly Regex ImgTag = new(@"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SrcAttr = new(@"\bsrc\s*=\s*([""'])(?<url>[^""']*)\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex StyleAttr = new(@"\bstyle\s*=\s*([""'])(?<css>[^""']*)\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RelativeHref = new(@"\bhref\s*=\s*([""'])(?<url>/[^""']*)\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ButtonAnchor = new(
            // Named backreference on purpose: .NET numbers named groups AFTER unnamed ones, so a
            // bare \2 here would point at the attrs group and the button would never match.
            @"<a\b(?<attrs>[^>]*\bclass\s*=\s*(?<q>[""'])[^""']*\brp-button\b[^""']*\k<q>[^>]*)>(?<label>.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex HrefAny = new(@"\bhref\s*=\s*([""'])(?<url>[^""']*)\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string ImgStyle = "max-width:100%;height:auto;display:block;margin:12px 0;";
        private const string FontStack = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

        /// <summary>
        /// The X-SMTPAPI header for a marketing send: unique args SendGrid copies onto every
        /// webhook event (tenant for suppression scoping, the send row id so opens and clicks
        /// come back to the right person), plus open and click tracking switched on for this
        /// message regardless of the account default.
        /// </summary>
        public static string SmtpApiHeader(Guid tenantId, string sendIdKey, Guid sendId)
        {
            var uniqueArgs = new Dictionary<string, string>
            {
                ["tenant_id"] = tenantId.ToString(),
                [sendIdKey] = sendId.ToString(),
            };
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                unique_args = uniqueArgs,
                filters = new
                {
                    clicktrack = new { settings = new { enable = 1, enable_text = 0 } },
                    opentrack = new { settings = new { enable = 1 } },
                },
            });
        }

        /// <summary>Site-relative URL to absolute; absolute and empty values pass through.</summary>
        public static string? AbsoluteUrl(string? url, string siteUrl)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var u = url.Trim();
            return u.StartsWith("/") && !u.StartsWith("//") ? siteUrl.TrimEnd('/') + u : u;
        }

        /// <summary>Absolutize URLs, cap images, and turn editor buttons into email buttons.</summary>
        public static string PrepareBody(string html, string siteUrl, string? buttonColor = null)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var root = siteUrl.TrimEnd('/');
            var color = buttonColor ?? "#1976D2";

            var s = ImgTag.Replace(html, m =>
            {
                var tag = m.Value;
                tag = SrcAttr.Replace(tag, sm =>
                {
                    var url = sm.Groups["url"].Value;
                    return url.StartsWith("/") && !url.StartsWith("//") ? $"src=\"{root}{url}\"" : sm.Value;
                }, 1);
                tag = StyleAttr.IsMatch(tag)
                    ? StyleAttr.Replace(tag, sm => $"style=\"{ImgStyle}{sm.Groups["css"].Value}\"", 1)
                    : tag.Insert(4, $" style=\"{ImgStyle}\"");
                return tag;
            });

            // Buttons before generic hrefs so the button's own href is absolutized once, inside.
            s = ButtonAnchor.Replace(s, m =>
            {
                var hrefMatch = HrefAny.Match(m.Groups["attrs"].Value);
                var href = hrefMatch.Success ? AbsoluteUrl(hrefMatch.Groups["url"].Value, root) ?? "#" : "#";
                var label = m.Groups["label"].Value;
                return Button(label, href, color);
            });

            return RelativeHref.Replace(s, hm => $"href=\"{root}{hm.Groups["url"].Value}\"");
        }

        /// <summary>A bulletproof button: a table cell with a background, which every client honors.</summary>
        public static string Button(string labelHtml, string href, string color) =>
            "<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"margin:16px 0\"><tr>"
            + $"<td style=\"border-radius:6px;background:{color}\">"
            + $"<a href=\"{WebUtility.HtmlEncode(href)}\" target=\"_blank\" rel=\"noopener\" "
            + $"style=\"display:inline-block;padding:12px 24px;font-family:{FontStack};font-size:16px;font-weight:600;"
            + $"color:#ffffff;text-decoration:none;border-radius:6px;background:{color}\">{labelHtml}</a>"
            + "</td></tr></table>";

        /// <summary>The bare 600px column, for callers that want no branding (kept for compatibility).</summary>
        public static string Wrap(string bodyHtml) =>
            $"<div style=\"margin:0 auto;max-width:600px;padding:16px;font-family:{FontStack};font-size:16px;line-height:1.5;color:#111827\">"
            + bodyHtml + "</div>";

        /// <summary>
        /// The full email: preheader, branded header, prepared body, branded footer, then the
        /// caller's compliance footer (unsubscribe line for marketing; nothing for a test).
        /// </summary>
        public static string Compose(string editorHtml, string? previewText, EmailBranding brand, string? complianceFooterHtml)
        {
            var sb = new StringBuilder(editorHtml.Length + 2048);
            sb.Append("<div style=\"background:#f3f4f6;padding:24px 8px\">");

            if (!string.IsNullOrWhiteSpace(previewText))
            {
                // Hidden preheader, padded with non-breaking spaces so the inbox snippet does not
                // run on into the body text.
                sb.Append("<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;font-size:1px;line-height:1px\">")
                  .Append(WebUtility.HtmlEncode(previewText.Trim()))
                  .Append(string.Concat(Enumerable.Repeat("&nbsp;&zwnj;", 60)))
                  .Append("</div>");
            }

            sb.Append($"<div style=\"margin:0 auto;max-width:600px;background:#ffffff;border-radius:8px;overflow:hidden;font-family:{FontStack};font-size:16px;line-height:1.5;color:#111827\">");

            // Header: logo if there is one, else the name in the brand color.
            sb.Append("<div style=\"padding:20px 24px;text-align:center;border-bottom:1px solid #e5e7eb\">");
            if (!string.IsNullOrWhiteSpace(brand.LogoUrl))
            {
                sb.Append($"<a href=\"{brand.SiteUrl}\" style=\"text-decoration:none\"><img src=\"{WebUtility.HtmlEncode(brand.LogoUrl)}\" alt=\"{WebUtility.HtmlEncode(brand.TrackName)}\" style=\"max-height:64px;max-width:220px;height:auto;width:auto\"></a>");
            }
            else
            {
                sb.Append($"<a href=\"{brand.SiteUrl}\" style=\"font-size:22px;font-weight:700;color:{brand.PrimaryColor};text-decoration:none\">{WebUtility.HtmlEncode(brand.TrackName)}</a>");
            }
            sb.Append("</div>");

            sb.Append("<div style=\"padding:24px\">")
              .Append(PrepareBody(editorHtml, brand.SiteUrl, brand.PrimaryColor))
              .Append("</div>");

            // The tenant's own footer (hours, a tagline, a legal line) sits between the body and
            // the address block, prepared the same way as the body so links and images behave.
            if (!string.IsNullOrWhiteSpace(brand.CustomFooterHtml))
            {
                sb.Append("<div style=\"padding:0 24px 20px;font-size:14px;line-height:1.5;color:#374151\">")
                  .Append(PrepareBody(brand.CustomFooterHtml, brand.SiteUrl, brand.PrimaryColor))
                  .Append("</div>");
            }

            // Footer: who sent it and how to reach them. CAN-SPAM wants a physical address on
            // marketing mail, which is why the track's address is here and not optional.
            sb.Append("<div style=\"padding:16px 24px;border-top:1px solid #e5e7eb;font-size:12px;line-height:1.6;color:#6b7280;text-align:center\">")
              .Append($"<div style=\"font-weight:600;color:#374151\">{WebUtility.HtmlEncode(brand.TrackName)}</div>");
            if (brand.AddressLine is not null) sb.Append($"<div>{WebUtility.HtmlEncode(brand.AddressLine)}</div>");
            var contact = new List<string>();
            if (!string.IsNullOrWhiteSpace(brand.Phone)) contact.Add(WebUtility.HtmlEncode(brand.Phone));
            if (!string.IsNullOrWhiteSpace(brand.ContactEmail)) contact.Add($"<a href=\"mailto:{WebUtility.HtmlEncode(brand.ContactEmail)}\" style=\"color:#6b7280\">{WebUtility.HtmlEncode(brand.ContactEmail)}</a>");
            if (contact.Count > 0) sb.Append("<div>").Append(string.Join(" &middot; ", contact)).Append("</div>");
            if (brand.Socials.Count > 0)
            {
                sb.Append("<div style=\"margin-top:6px\">")
                  .Append(string.Join(" &middot; ", brand.Socials.Select(s =>
                      $"<a href=\"{WebUtility.HtmlEncode(s.Url)}\" style=\"color:{brand.PrimaryColor};text-decoration:none\">{WebUtility.HtmlEncode(s.Label)}</a>")))
                  .Append("</div>");
            }
            if (!string.IsNullOrWhiteSpace(complianceFooterHtml)) sb.Append(complianceFooterHtml);
            sb.Append("</div></div></div>");
            return sb.ToString();
        }
    }
}
