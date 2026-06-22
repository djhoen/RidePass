namespace webapi.Controllers.API.Data.Payment
{
    // Client-confirm of a just-succeeded PaymentIntent so we can finalize immediately
    // instead of waiting for the async Stripe webhook. The server re-verifies the status
    // with Stripe before finalizing, so the id is the only thing the client needs to send.
    public class ConfirmIntentRequest
    {
        public string PaymentIntentId { get; set; } = null!;
    }
}
