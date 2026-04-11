using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using webapi.Models;
using Services.Helpers;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PaymentController : ControllerBase
    {
        public PaymentController()
        {
        }

        [Authorize]
        [HttpPost("CreateCheckoutSession")]
        public async Task<IActionResult> CreateCheckoutSession([FromBody] CheckoutSessionRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                // TODO: Initialize Stripe client with API key from configuration
                // TODO: Create Stripe checkout session with line items from request
                // TODO: Set success and cancel URLs
                // TODO: Attach customer/user metadata to the session
                // TODO: Return the session ID and URL to the client

                throw new NotImplementedException("Stripe checkout session creation not yet implemented.");
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [HttpPost("WebhookHandler")]
        public async Task<IActionResult> WebhookHandler()
        {
            try
            {
                // TODO: Read the request body as a string
                // TODO: Retrieve Stripe webhook signing secret from configuration
                // TODO: Validate the webhook signature using Stripe.net EventUtility.ConstructEvent
                // TODO: Handle event types:
                //   - checkout.session.completed: fulfill the order
                //   - payment_intent.succeeded: update payment status
                //   - payment_intent.payment_failed: handle failure
                // TODO: Return OK to acknowledge receipt of the event

                throw new NotImplementedException("Stripe webhook handler not yet implemented.");
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }
    }
}
