using System.Text.RegularExpressions;

namespace Services.Email
{
    /// <summary>
    /// Makes editor HTML safe to send as an email. The rich text editor produces clean semantic
    /// HTML for a web page; an email client is a much worse browser. Two things it gets wrong
    /// without help, both fixed here at send time so the stored body stays editor-friendly:
    ///
    ///   1. Relative image URLs (/uploads/...) resolve against nothing in an inbox, so they are
    ///      made absolute against the tenant's site root.
    ///   2. Images render at natural size, so a 2000px photo blows the layout out. Every img
    ///      gets an inline max-width, and the whole body sits in a 600px column, which is the
    ///      width every client renders predictably.
    /// </summary>
    public static class EmailHtml
    {
        private static readonly Regex ImgTag = new(@"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SrcAttr = new(@"\bsrc\s*=\s*([""'])(?<url>[^""']*)\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex StyleAttr = new(@"\bstyle\s*=\s*([""'])(?<css>[^""']*)\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HrefAttr = new(@"\bhref\s*=\s*([""'])(?<url>/[^""']*)\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string ImgStyle = "max-width:100%;height:auto;display:block;margin:12px 0;";

        /// <summary>Absolutize site-relative image and link URLs and cap image width.</summary>
        public static string PrepareBody(string html, string baseUrl)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var root = baseUrl.TrimEnd('/');

            var fixedImgs = ImgTag.Replace(html, m =>
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

            // Site-relative links (the editor's link dialog accepts "/Events/...") need a host too.
            return HrefAttr.Replace(fixedImgs, hm => $"href=\"{root}{hm.Groups["url"].Value}\"");
        }

        /// <summary>
        /// The outer shell: a centered 600px column with a readable default font, so a plain
        /// paragraph from the editor looks intentional in Gmail, Outlook, and Apple Mail alike.
        /// </summary>
        public static string Wrap(string bodyHtml) =>
            "<div style=\"margin:0 auto;max-width:600px;padding:16px;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;font-size:16px;line-height:1.5;color:#111827\">"
            + bodyHtml
            + "</div>";

        /// <summary>PrepareBody then Wrap, the usual call from a sender.</summary>
        public static string ForEmail(string editorHtml, string baseUrl) => Wrap(PrepareBody(editorHtml, baseUrl));
    }
}
