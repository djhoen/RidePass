using Microsoft.Extensions.Configuration;

namespace Services.QuickBooks
{
    /// <summary>
    /// Intuit app credentials and endpoints. Values come from env vars in production
    /// (QuickBooks__ClientId / QuickBooks__ClientSecret / ...) and dotnet user-secrets in dev, /// never appsettings.json, same rule as the Stripe keys.
    /// </summary>
    public class QuickBooksOptions
    {
        public string? ClientId { get; }
        public string? ClientSecret { get; }
        /// <summary>Must exactly match a redirect URI registered on the Intuit app, or the callback 400s.</summary>
        public string? RedirectUri { get; }
        /// <summary>"sandbox" or "production". Picks the API host; the OAuth hosts are shared.</summary>
        public string Environment { get; }

        public QuickBooksOptions(IConfiguration config)
        {
            ClientId     = NullIfEmpty(config["QuickBooks:ClientId"]);
            ClientSecret = NullIfEmpty(config["QuickBooks:ClientSecret"]);
            RedirectUri  = NullIfEmpty(config["QuickBooks:RedirectUri"]);
            Environment  = NullIfEmpty(config["QuickBooks:Environment"]) ?? "sandbox";
        }

        public bool IsConfigured =>
            !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret) && !string.IsNullOrEmpty(RedirectUri);

        public bool IsProduction => string.Equals(Environment, "production", StringComparison.OrdinalIgnoreCase);

        /// <summary>Where the tenant's browser goes to grant access.</summary>
        public const string AuthorizeUrl = "https://appcenter.intuit.com/connect/oauth2";
        /// <summary>Code-for-token and refresh both POST here.</summary>
        public const string TokenUrl = "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer";
        public const string RevokeUrl = "https://developer.api.intuit.com/v2/oauth2/tokens/revoke";
        /// <summary>Accounting scope only. We never ask for payments or payroll.</summary>
        public const string Scope = "com.intuit.quickbooks.accounting";

        public string ApiBaseUrl => IsProduction
            ? "https://quickbooks.api.intuit.com/v3/company"
            : "https://sandbox-quickbooks.api.intuit.com/v3/company";

        private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
