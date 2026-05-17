using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.Helpers;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.WaitlistData;
using Services.Repositories.Interfaces;

namespace Services.Waitlist
{
    public interface IWaitlistPromoter
    {
        /// <summary>
        /// Promote the next waiting alternate in the (event, tier) bucket. Pre-paid
        /// alternates auto-confirm (creates the purchase row + notifies); everyone
        /// else is set to 'promoted' with a confirm token + tenant-configured deadline
        /// and texted the link. No-op if the bucket is empty.
        /// </summary>
        Task PromoteNext(Guid eventId, Guid? tierId);
    }

    public class WaitlistPromoter : IWaitlistPromoter
    {
        private readonly IEventWaitlistRepository _waitlist;
        private readonly IEventRepository _events;
        private readonly IEventTicketTierRepository _tiers;
        private readonly IEventTicketPurchaseRepository _ticketPurchases;
        private readonly IUserRepository _users;
        private readonly ITenantRepository _tenants;
        private readonly ISmsSender _sms;
        private readonly IConfiguration _config;
        private readonly ILogger<WaitlistPromoter> _logger;

        public WaitlistPromoter(
            IEventWaitlistRepository waitlist,
            IEventRepository events,
            IEventTicketTierRepository tiers,
            IEventTicketPurchaseRepository ticketPurchases,
            IUserRepository users,
            ITenantRepository tenants,
            ISmsSender sms,
            IConfiguration config,
            ILogger<WaitlistPromoter> logger)
        {
            _waitlist = waitlist;
            _events = events;
            _tiers = tiers;
            _ticketPurchases = ticketPurchases;
            _users = users;
            _tenants = tenants;
            _sms = sms;
            _config = config;
            _logger = logger;
        }

        public async Task PromoteNext(Guid eventId, Guid? tierId)
        {
            var next = await _waitlist.PeekFront(eventId, tierId);
            if (next is null) return;

            var tenant = await _tenants.GetById(next.TenantId);
            if (tenant is null) return;

            var ev = await _events.GetById(eventId, next.TenantId);
            if (ev is null) return;

            var user = await _users.GetById(next.UserId);
            if (user is null) return;

            var apex = _config["App:RootDomain"] ?? "ridepass.io";
            var origin = $"https://{tenant.Subdomain}.{apex}";

            // Pre-paid: skip the timer, create the purchase row, notify confirmation.
            // Pre-pay only happens for tier-based waitlists (UI gates pass alternates
            // off pre-pay), so tierId is guaranteed non-null on this branch.
            if (next.IsPrepaid && tierId.HasValue && !string.IsNullOrEmpty(next.PrepayPiId))
            {
                var tier = await _tiers.GetById(tierId.Value, next.TenantId);
                if (tier is null) return;

                var serviceCharge = (int)((long)tier.PriceCents * tenant.ServiceChargeBps / 10_000L);
                var purchase = new EventTicketPurchase
                {
                    TenantId = next.TenantId,
                    TierId = tier.Id,
                    PurchaserUserId = next.UserId,
                    AmountCents = next.PrepayAmountCents,
                    ServiceChargeCents = serviceCharge,
                    PaymentMethod = "stripe",
                    Status = "paid",
                    PurchaserEmail = user.Email,
                    PurchaserName = $"{user.FirstName} {user.LastName}".Trim(),
                };
                var created = await _ticketPurchases.Create(purchase);
                await _ticketPurchases.SetStripePaymentIntentId(created.Id, next.PrepayPiId!);
                await _ticketPurchases.UpdateStatus(created.Id, "paid");
                await _waitlist.MarkConfirmed(next.Id, created.Id, "event_ticket");

                if (!string.IsNullOrWhiteSpace(user.Phone))
                {
                    var msg = $"You're in for {ev.Title}! Your pre-paid {tier.Name} entry is confirmed. " +
                              $"Show your QR at {origin}/User/MyPasses";
                    await _sms.Send(user.Phone, msg);
                }
                _logger.LogInformation("Auto-confirmed pre-paid waitlist {Id} for event {EventId}", next.Id, eventId);
                return;
            }

            // Non-prepaid: set deadline + token, send promotion SMS with confirm link.
            var window = Math.Max(5, tenant.WaitlistConfirmWindowMinutes);
            var deadline = DateTime.UtcNow.AddMinutes(window);
            var token = Guid.NewGuid();
            await _waitlist.MarkPromoted(next.Id, DateTime.UtcNow, deadline, token);

            if (!string.IsNullOrWhiteSpace(user.Phone))
            {
                var confirmUrl = $"{origin}/Waitlist/Confirm/{token}";
                var label = tierId.HasValue ? "spot" : "spot";  // future: tier name in message
                var msg = $"A {label} opened at {ev.Title}! Confirm within {window} min: {confirmUrl}";
                await _sms.Send(user.Phone, msg);
            }
            _logger.LogInformation("Promoted waitlist {Id} for event {EventId}, deadline {Deadline}",
                next.Id, eventId, deadline);
        }
    }
}
