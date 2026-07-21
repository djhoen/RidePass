namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>The Intuit consent URL to send the browser to. Mirrors the Stripe Connect onboarding shape.</summary>
    public class QuickBooksConnectResponse
    {
        public string AuthorizationUrl { get; set; } = null!;
    }
}
