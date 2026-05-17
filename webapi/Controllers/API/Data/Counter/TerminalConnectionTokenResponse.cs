namespace webapi.Controllers.API.Data.Counter
{
    public class TerminalConnectionTokenResponse
    {
        // Short-lived (~10 min) token the mobile Stripe Terminal SDK uses to
        // authenticate. The SDK re-asks for a new one when this expires.
        public string Secret { get; set; } = null!;
        // The Location id this token is scoped to — mobile SDK uses it during
        // reader discovery to limit which readers it can connect to.
        public string LocationId { get; set; } = null!;
    }
}
