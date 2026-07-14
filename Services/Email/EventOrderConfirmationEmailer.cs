using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.Helpers;
using Services.Repositories.Interfaces;

namespace Services.Email
{
    /// <summary>
    /// Sends the rider's confirmation for an EVENT order, from whichever path completed the sale:
    /// a Stripe payment, a $0 reward voucher, a gift card that covered the cart, a Loam Pass credit,
    /// or a cash sale at the counter. Every one of those is a real admission the rider has to be able
    /// to present at the gate, so every one of them gets the same email with the same QR.
    ///
    /// One email per (event, purchaser), not per ticket row: a rider buying a gate fee plus three race
    /// classes gets a single message listing everything they hold for that event, with one QR. That
    /// matches how the gate reads a scan (scan-once-redeem-many), and it's why the body is rebuilt
    /// from the purchaser's whole event scope rather than from just the rows in this transaction.
    ///
    /// Best-effort by contract: a dead SMTP host or a bounced address must never fail a paid purchase,
    /// so everything here is wrapped and logged rather than thrown. When SMTP isn't configured at all
    /// (ISmtpEmailer.IsConfigured == false) this is a silent no-op, which is the current default.
    /// </summary>
    public interface IEventOrderConfirmationEmailer
    {
        /// <summary>Confirms the event orders these just-paid tickets belong to.</summary>
        Task SendForTickets(Guid tenantId, IReadOnlyList<Guid> ticketIds);

        /// <summary>Confirms the event orders these just-paid add-ons belong to. For carts that are
        /// add-ons only (a spectator gate fee, camping); a cart that also has tickets is confirmed
        /// via SendForTickets, whose email already lists the add-ons.</summary>
        Task SendForExtras(Guid tenantId, IReadOnlyList<Guid> extraIds);
    }

    public class EventOrderConfirmationEmailer : IEventOrderConfirmationEmailer
    {
        private readonly ISmtpEmailer _emailer;
        private readonly IEmailSuppressionRepository _suppression;
        private readonly ITenantRepository _tenants;
        private readonly IEventRepository _events;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly IEventTicketTierRepository _tiers;
        private readonly IEventExtraRepository _extras;
        private readonly IConfiguration _config;
        private readonly ILogger<EventOrderConfirmationEmailer> _logger;

        public EventOrderConfirmationEmailer(
            ISmtpEmailer emailer,
            IEmailSuppressionRepository suppression,
            ITenantRepository tenants,
            IEventRepository events,
            IEventTicketPurchaseRepository tickets,
            IEventTicketTierRepository tiers,
            IEventExtraRepository extras,
            IConfiguration config,
            ILogger<EventOrderConfirmationEmailer> logger)
        {
            _emailer = emailer;
            _suppression = suppression;
            _tenants = tenants;
            _events = events;
            _tickets = tickets;
            _tiers = tiers;
            _extras = extras;
            _config = config;
            _logger = logger;
        }

        public async Task SendForTickets(Guid tenantId, IReadOnlyList<Guid> ticketIds)
        {
            if (!_emailer.IsConfigured || ticketIds.Count == 0) return;
            try
            {
                var orders = new Dictionary<string, OrderKey>();
                foreach (var id in ticketIds.Distinct())
                {
                    var ticket = await _tickets.GetById(id, tenantId);
                    if (ticket is null) continue;
                    var tier = await _tiers.GetById(ticket.TierId, tenantId);
                    if (tier is null) continue;
                    AddOrder(orders, tier.EventId, ticket.PurchaserUserId, ticket.PurchaserEmail, ticket.PurchaserName);
                }
                foreach (var order in orders.Values) await SendOne(tenantId, order);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send event order confirmation for tickets in tenant {TenantId}", tenantId);
            }
        }

        public async Task SendForExtras(Guid tenantId, IReadOnlyList<Guid> extraIds)
        {
            if (!_emailer.IsConfigured || extraIds.Count == 0) return;
            try
            {
                var orders = new Dictionary<string, OrderKey>();
                foreach (var id in extraIds.Distinct())
                {
                    var extra = await _extras.GetPurchase(id);
                    // Counter merch has no event, so there's no event order to confirm.
                    if (extra is null || extra.TenantId != tenantId || extra.EventId is null) continue;
                    AddOrder(orders, extra.EventId.Value, extra.PurchaserUserId, extra.PurchaserEmail, extra.PurchaserName);
                }
                foreach (var order in orders.Values) await SendOne(tenantId, order);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send event order confirmation for add-ons in tenant {TenantId}", tenantId);
            }
        }

        private static void AddOrder(Dictionary<string, OrderKey> orders,
            Guid eventId, Guid? purchaserUserId, string? email, string? name)
        {
            if (string.IsNullOrWhiteSpace(email)) return;   // nothing to send to (walk-up cash sale with no email)
            var key = $"{eventId}|{purchaserUserId?.ToString() ?? email.Trim().ToLowerInvariant()}";
            if (orders.ContainsKey(key)) return;
            orders[key] = new OrderKey
            {
                EventId = eventId,
                PurchaserUserId = purchaserUserId,
                PurchaserEmail = email.Trim(),
                PurchaserName = string.IsNullOrWhiteSpace(name) ? "rider" : name,
            };
        }

        private async Task SendOne(Guid tenantId, OrderKey order)
        {
            // A hard-bounced address only inflates the account-wide bounce rate. Marketing opt-outs
            // don't block a transactional receipt, which is why this asks for marketing: false.
            if (await _suppression.IsSuppressed(order.PurchaserEmail, tenantId, marketing: false)) return;

            var tenant = await _tenants.GetById(tenantId);
            var ev = await _events.GetById(order.EventId, tenantId);
            if (tenant is null || ev is null) return;

            // Rebuild the whole order from the purchaser's event scope, so the email says what they
            // hold for this event rather than only what this one transaction added.
            var tickets = (await _tickets.ListByEventForPurchaser(
                    order.EventId, tenantId, order.PurchaserUserId, order.PurchaserEmail))
                .Where(t => t.Status == "paid" || t.Status == "redeemed")
                .ToList();
            var extras = (await _extras.ListByEventForPurchaser(
                    order.EventId, tenantId, order.PurchaserUserId, order.PurchaserEmail))
                .Where(x => x.Status == "paid" || x.Status == "redeemed")
                .ToList();
            if (tickets.Count == 0 && extras.Count == 0) return;

            var apex = _config["App:RootDomain"] ?? "ridepass.io";
            var baseUrl = $"https://{tenant.Subdomain}.{apex}";

            // The QR is the gate's anchor: one scan surfaces every ticket and add-on this purchaser
            // holds for this event, so a single code in the email admits the whole party.
            var anchorToken = tickets.Count > 0
                ? tickets[0].RedemptionToken
                : extras[0].RedemptionToken;
            var qrUrl = $"{baseUrl}/api/Qr/{anchorToken}";

            var rows = new List<string>();
            foreach (var t in tickets)
            {
                var who = $"{t.RiderFirstName} {t.RiderLastName}".Trim();
                var label = string.IsNullOrWhiteSpace(who)
                    ? Enc(t.TierName)
                    : $"{Enc(t.TierName)} <span style=\"color:#666\">({Enc(who)})</span>";
                rows.Add(Row(label, 1, t.AmountCents));
            }
            foreach (var x in extras)
            {
                rows.Add(Row(Enc(x.ProductName), x.Quantity, x.AmountCents));
            }
            var totalCents = tickets.Sum(t => t.AmountCents) + extras.Sum(x => x.AmountCents);

            // Registration (rider details + a required waiver) is what the gate blocks on. When any
            // ticket in this order is still unfinished, the email leads with the link to finish it,
            // rather than letting the rider find out at the gate on race morning.
            var incomplete = tickets.Where(t => !t.RegistrationComplete).ToList();
            var registrationLine = incomplete.Count == 0 ? string.Empty
                : $@"<p style=""padding:12px; border:1px solid #e0a800; background:#fff8e1; border-radius:6px"">
<strong>Action needed before you ride.</strong> {(incomplete.Count == 1 ? "One entry on this order still needs" : $"{incomplete.Count} entries on this order still need")}
rider details and a signed waiver.
<a href=""{baseUrl}/FinishRegistration/{anchorToken}"">Finish registration now</a> so you can go straight to the gate.</p>";

            var accountLine = order.PurchaserUserId is null
                ? $@"<p>Want to manage your entries, check in faster next time, and get race-day alerts?
<a href=""{baseUrl}/SignUp?email={Uri.EscapeDataString(order.PurchaserEmail)}"">Create your free account</a>.</p>"
                : $@"<p>You can also find this order on your account at <a href=""{baseUrl}/User/Upcoming"">My Upcoming Events</a>.</p>";

            var whenLine = ev.AllDay
                ? $"{ev.StartsAt:dddd, MMMM d, yyyy}"
                : $"{ev.StartsAt:dddd, MMMM d, yyyy} at {ev.StartsAt:h:mm tt}";

            var html = $@"<p>Hi {Enc(FirstName(order.PurchaserName))},</p>
<p>You're in for <strong>{Enc(ev.Title)}</strong> at <strong>{Enc(tenant.DisplayName)}</strong>.</p>
<p>{Enc(whenLine)}{(string.IsNullOrWhiteSpace(ev.LocationLabel) ? "" : $" &middot; {Enc(ev.LocationLabel!)}")}</p>
{registrationLine}
<table cellpadding=""6"" cellspacing=""0"" style=""border-collapse:collapse; margin:12px 0"">
{string.Join("\n", rows)}
<tr><td style=""border-top:2px solid #333""><strong>Total</strong></td>
    <td style=""border-top:2px solid #333""></td>
    <td style=""border-top:2px solid #333; text-align:right""><strong>${(totalCents / 100m):0.00}</strong></td></tr>
</table>
<p>Show this QR at the gate. It checks in everything on this order:</p>
<p><img src=""{qrUrl}"" alt=""Your QR code"" width=""240"" height=""240"" style=""border:1px solid #ddd; padding:6px; background:#fff"" /></p>
<p>If your email client doesn't show the image, open <a href=""{qrUrl}"">this link</a> on your phone and it'll display the QR.</p>
{accountLine}";

            // From the track, not from RidePass: the rider bought from them, replies reach them.
            await _emailer.Send(order.PurchaserEmail,
                $"{tenant.DisplayName}: you're in for {ev.Title}",
                html, null, TenantEmailIdentity.For(tenant));
        }

        private static string Row(string label, int qty, int amountCents) =>
            $@"<tr><td style=""border-bottom:1px solid #eee"">{label}</td>
    <td style=""border-bottom:1px solid #eee; color:#666"">{(qty > 1 ? $"x{qty}" : "")}</td>
    <td style=""border-bottom:1px solid #eee; text-align:right"">${(amountCents / 100m):0.00}</td></tr>";

        private static string Enc(string value) => System.Net.WebUtility.HtmlEncode(value);

        private static string FirstName(string name) =>
            name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "rider";

        private class OrderKey
        {
            public Guid EventId { get; set; }
            public Guid? PurchaserUserId { get; set; }
            public string PurchaserEmail { get; set; } = null!;
            public string PurchaserName { get; set; } = null!;
        }
    }
}
