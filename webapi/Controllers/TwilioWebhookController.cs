using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.BillingData;
using Services.Repositories.Data.MessagingData;
using Services.Repositories.Interfaces;
using Services.Sms;

namespace webapi.Controllers
{
    /// <summary>
    /// Public webhook endpoints called by Twilio. NOT JWT-authenticated —
    /// authenticity is verified via the X-Twilio-Signature HMAC, keyed by the
    /// owning subaccount's auth token. The endpoint resolves the tenant from
    /// the form body (AccountSid) rather than the request subdomain.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/[controller]")]
    public class TwilioWebhookController : ControllerBase
    {
        private readonly ITenantRepository _tenants;
        private readonly ITenantBillingEventRepository _billing;
        private readonly ITenantConversationRepository _conversations;
        private readonly ITenantSmsOptOutRepository _optOuts;
        private readonly IUserRepository _users;
        private readonly ISmsPricing _pricing;
        private readonly IConfiguration _config;
        private readonly ILogger<TwilioWebhookController> _logger;

        public TwilioWebhookController(
            ITenantRepository tenants,
            ITenantBillingEventRepository billing,
            ITenantConversationRepository conversations,
            ITenantSmsOptOutRepository optOuts,
            IUserRepository users,
            ISmsPricing pricing,
            IConfiguration config,
            ILogger<TwilioWebhookController> logger)
        {
            _tenants = tenants;
            _billing = billing;
            _conversations = conversations;
            _optOuts = optOuts;
            _users = users;
            _pricing = pricing;
            _config = config;
            _logger = logger;
        }

        [HttpPost("StatusCallback")]
        public async Task<IActionResult> StatusCallback()
        {
            if (!Request.HasFormContentType) return BadRequest();
            var form = await Request.ReadFormAsync();

            var accountSid = form["AccountSid"].ToString();
            var messageSid = form["MessageSid"].ToString();
            var status = form["MessageStatus"].ToString();

            if (string.IsNullOrEmpty(accountSid) || string.IsNullOrEmpty(messageSid))
            {
                return BadRequest();
            }

            var tenant = await _tenants.GetByTwilioSubaccountSid(accountSid);
            if (tenant is null || string.IsNullOrEmpty(tenant.TwilioAuthTokenEncrypted))
            {
                // Unknown subaccount or missing creds — return 200 so Twilio
                // doesn't retry the same dead end. Log for investigation.
                _logger.LogWarning(
                    "StatusCallback for unknown/unprovisioned subaccount {Sid}; message {MessageSid}",
                    accountSid, messageSid);
                return Ok();
            }

            var authToken = EncryptionHelper.Decrypt(tenant.TwilioAuthTokenEncrypted);
            if (string.IsNullOrEmpty(authToken))
            {
                _logger.LogWarning(
                    "StatusCallback: failed to decrypt auth token for tenant {TenantId}",
                    tenant.Id);
                return Ok();
            }

            var configuredUrl = _config["Sms:Twilio:StatusCallbackUrl"];
            if (string.IsNullOrEmpty(configuredUrl))
            {
                // Without the canonical URL we'd have to reconstruct from
                // headers, which is unreliable behind a TLS-terminating proxy.
                // Fail closed: don't bill un-verified callbacks.
                _logger.LogWarning("Sms:Twilio:StatusCallbackUrl not configured; rejecting webhook");
                return Unauthorized();
            }

            var signature = Request.Headers["X-Twilio-Signature"].ToString();
            var formPairs = form.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value.ToString()));
            if (!TwilioSignatureValidator.Verify(authToken, configuredUrl, formPairs, signature))
            {
                _logger.LogWarning(
                    "StatusCallback signature failed for tenant {TenantId} message {MessageSid}",
                    tenant.Id, messageSid);
                return Unauthorized();
            }

            // Mirror Twilio's delivery status onto the tenant_message row so
            // the Inbox thread reflects the current state ("delivered" /
            // "failed" badges). Idempotent: same SID + same status is a no-op.
            // Pull num_segments + error fields when present.
            int? numSegmentsForRow = null;
            if (int.TryParse(form["NumSegments"].ToString(), out var nsRow) && nsRow > 0)
            {
                numSegmentsForRow = nsRow;
            }
            var errCode = form["ErrorCode"].ToString();
            var errMessage = form["ErrorMessage"].ToString();
            await _conversations.UpdateStatusBySid(
                messageSid, status, numSegmentsForRow,
                string.IsNullOrEmpty(errCode) ? null : errCode,
                string.IsNullOrEmpty(errMessage) ? null : errMessage);

            if (status == "delivered")
            {
                // Bill only on confirmed-delivered with both Price and
                // NumSegments present. Twilio promises NumSegments on
                // terminal-state callbacks for sms/mms.
                var priceRaw = form["Price"].ToString();
                var numSegmentsRaw = form["NumSegments"].ToString();

                if (!int.TryParse(numSegmentsRaw, out var numSegments) || numSegments <= 0)
                {
                    _logger.LogInformation(
                        "Delivered SMS {MessageSid} missing NumSegments; skipping bill",
                        messageSid);
                    return Ok();
                }

                long priceMicros = 0;
                if (!string.IsNullOrEmpty(priceRaw)
                    && decimal.TryParse(priceRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var priceDec))
                {
                    priceMicros = (long)Math.Round(Math.Abs(priceDec) * 1_000_000m);
                }

                var billedCents = numSegments * _pricing.OutboundPerSegmentCents;

                var inserted = await _billing.RecordIfNew(new TenantBillingEvent
                {
                    TenantId = tenant.Id,
                    Kind = "sms",
                    SourceTable = "sms_send",
                    SourceId = messageSid,
                    TwilioCostMicros = priceMicros,
                    BilledCents = billedCents,
                });

                if (!inserted)
                {
                    _logger.LogInformation(
                        "Duplicate StatusCallback for {MessageSid} (tenant {TenantId}); no-op",
                        messageSid, tenant.Id);
                }
            }
            else if (status == "failed" || status == "undelivered")
            {
                // Twilio either refunds or doesn't charge us for failures, so
                // we don't bill the tenant either. Log for analytics + the
                // future Billing & Usage "blocked sends" surface.
                _logger.LogInformation(
                    "SMS {MessageSid} ended {Status} for tenant {TenantId}; no charge",
                    messageSid, status, tenant.Id);
            }
            // Intermediate states (queued, sent) — silent ack.

            return Ok();
        }

        [HttpPost("IncomingSms")]
        public async Task<IActionResult> IncomingSms()
        {
            if (!Request.HasFormContentType) return BadRequest();
            var form = await Request.ReadFormAsync();

            var accountSid = form["AccountSid"].ToString();
            var messageSid = form["MessageSid"].ToString();
            var fromPhone = form["From"].ToString();
            var toPhone = form["To"].ToString();
            var body = form["Body"].ToString();

            if (string.IsNullOrEmpty(accountSid) || string.IsNullOrEmpty(messageSid)
                || string.IsNullOrEmpty(fromPhone) || string.IsNullOrEmpty(toPhone))
            {
                return BadRequest();
            }

            var tenant = await _tenants.GetByTwilioSubaccountSid(accountSid);
            if (tenant is null || string.IsNullOrEmpty(tenant.TwilioAuthTokenEncrypted))
            {
                _logger.LogWarning(
                    "IncomingSms for unknown/unprovisioned subaccount {Sid}; message {MessageSid}",
                    accountSid, messageSid);
                return Ok();
            }

            var authToken = EncryptionHelper.Decrypt(tenant.TwilioAuthTokenEncrypted);
            if (string.IsNullOrEmpty(authToken))
            {
                _logger.LogWarning(
                    "IncomingSms: failed to decrypt auth token for tenant {TenantId}",
                    tenant.Id);
                return Ok();
            }

            // Signed-against URL is the InboundSmsWebhookUrl we configured on
            // the bought number (TwilioSubaccountProvisioner.BuyNumber sets the
            // SmsUrl from this config key). Falling back to BadRequest if
            // unset closes a footgun where misconfiguration would silently
            // accept unverified webhooks.
            var configuredUrl = _config["Sms:Twilio:InboundSmsWebhookUrl"];
            if (string.IsNullOrEmpty(configuredUrl))
            {
                _logger.LogWarning("Sms:Twilio:InboundSmsWebhookUrl not configured; rejecting webhook");
                return Unauthorized();
            }

            var signature = Request.Headers["X-Twilio-Signature"].ToString();
            var formPairs = form.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value.ToString()));
            if (!TwilioSignatureValidator.Verify(authToken, configuredUrl, formPairs, signature))
            {
                _logger.LogWarning(
                    "IncomingSms signature failed for tenant {TenantId} message {MessageSid}",
                    tenant.Id, messageSid);
                return Unauthorized();
            }

            // Normalize the inbound number once: the conversation row, the
            // opt-out row, and any future cross-table joins all key off the
            // same E.164 form so downstream lookups land cleanly.
            var normalizedFrom = TwilioSmsSender.NormalizeE164(fromPhone) ?? fromPhone;

            // Reverse phone→user lookup. Hits the fn_phone_e164 expression
            // index from Script0088 so this is an index probe even at scale.
            // Returning null is fine — a customer texting from a number we
            // don't have on file just shows up as an unlinked phone in the
            // Inbox, and the link backfills if they ever update their profile
            // and text in again (FindOrCreate COALESCEs nulls forward).
            var matchedUser = await _users.GetByPhoneE164(normalizedFrom);

            // Find-or-create the conversation thread for this customer phone.
            var conversation = await _conversations.FindOrCreate(tenant.Id, normalizedFrom, customerUserId: matchedUser?.Id);

            int? numSegments = null;
            if (int.TryParse(form["NumSegments"].ToString(), out var segs) && segs > 0)
            {
                numSegments = segs;
            }

            try
            {
                await _conversations.AppendMessage(new TenantMessage
                {
                    ConversationId = conversation.Id,
                    TenantId = tenant.Id,
                    Direction = "inbound",
                    Body = body,
                    TwilioMessageSid = messageSid,
                    Status = "received",
                    NumSegments = numSegments,
                });
            }
            catch (Exception ex) when (ex.Message.Contains("ux_tenant_message_twilio_sid"))
            {
                // Twilio retried delivery of the same inbound — the unique
                // partial index on twilio_message_sid rejected the dup. Treat
                // as success so Twilio stops retrying.
                _logger.LogInformation(
                    "Duplicate IncomingSms for {MessageSid} (tenant {TenantId}); no-op",
                    messageSid, tenant.Id);
            }

            // Compliance keyword handling. Carriers and Twilio both run their
            // own STOP/START/HELP filters on US/Canada toll-free, and the
            // carrier sends a stock confirmation reply automatically — so we
            // don't return TwiML here. What we DO need is our own suppression
            // list so the outbound path can short-circuit before hitting
            // Twilio (saves the API call + failed StatusCallback), and so the
            // audit trail follows the tenant rather than their current number.
            var (keyword, canonical) = SmsKeywords.Classify(body);
            switch (keyword)
            {
                case SmsKeyword.OptOut:
                    await _optOuts.RecordOptOut(tenant.Id, normalizedFrom, canonical!);
                    _logger.LogInformation(
                        "SMS opt-out recorded for tenant {TenantId} phone {Phone} via {Keyword}",
                        tenant.Id, normalizedFrom, canonical);
                    break;
                case SmsKeyword.OptIn:
                    await _optOuts.RecordOptIn(tenant.Id, normalizedFrom, canonical!);
                    _logger.LogInformation(
                        "SMS opt-in recorded for tenant {TenantId} phone {Phone} via {Keyword}",
                        tenant.Id, normalizedFrom, canonical);
                    break;
                case SmsKeyword.Help:
                    // No state change. The carrier-side HELP responder
                    // returns the standard brand + opt-out instructions;
                    // a tenant-branded custom HELP reply is a future
                    // enhancement once we capture per-tenant help copy.
                    break;
            }

            // Empty 200 is the "no auto-reply" response Twilio accepts.
            // Returning TwiML here would let us send an immediate reply
            // (e.g., business-hours auto-responder) — not in v1 scope.
            return Ok();
        }
    }
}
