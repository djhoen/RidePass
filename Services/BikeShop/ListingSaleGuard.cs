using Services.Audit;
using Services.Payments;
using Services.Repositories.Interfaces;

namespace Services.BikeShop
{
    /// <summary>
    /// Frees a serialized unit from sales that died while naming it, so the unit can be listed and
    /// sold again. uk_shop_sale_line_item is unique over EVERY sale line that names a unit, paid or
    /// not, so a register card sale that was declined, an online checkout that was abandoned, or a
    /// sale that was refunded each leave a line that would make the next sale of that bike fail.
    ///
    /// For each such line: when the sale still has a PaymentIntent that could be confirmed (anything
    /// but paid / refunded, see ListingRules.MustCancelIntent) the intent is cancelled at Stripe first,
    /// and the line is let go only once Stripe confirms 'canceled'. 'succeeded' means the buyer paid and
    /// the unit is theirs (AlreadyPaid); anything else keeps the line (Blocked). Only item_id is nulled;
    /// the sale, its lines and its money stay as history. Every detach is written to the audit log.
    ///
    /// Callers: ShopListingController.Create (before listing), and the register and the online Order
    /// right before they create a sale with the unit. It makes Stripe calls, so it never runs inside a
    /// database transaction.
    /// </summary>
    public interface IListingSaleGuard
    {
        /// <summary>Released when no dead line names the unit any more; AlreadyPaid / Blocked otherwise.</summary>
        Task<ReservationRelease> FreeUnit(Guid itemId, Guid tenantId, string context, CancellationToken ct = default);

        /// <summary>Make sure this PaymentIntent can never be confirmed: Released when there is none
        /// or Stripe confirms it cancelled; AlreadyPaid when it succeeded; Blocked otherwise.</summary>
        Task<ReservationRelease> MakeUnpayable(string? saleStatus, string? paymentIntentId, string? connectedAccountId, CancellationToken ct = default);

        /// <summary>Null item_id on a dead sale's line naming the unit, and audit it. Rows detached.</summary>
        Task<int> DetachLine(Guid saleId, string saleStatus, Guid itemId, Guid tenantId, string context);
    }

    public class ListingSaleGuard : IListingSaleGuard
    {
        private readonly IShopListingRepository _listings;
        private readonly IPaymentProvider _payments;
        private readonly IAuditLogger _audit;

        public ListingSaleGuard(IShopListingRepository listings, IPaymentProvider payments, IAuditLogger audit)
        {
            _listings = listings;
            _payments = payments;
            _audit = audit;
        }

        public async Task<ReservationRelease> FreeUnit(Guid itemId, Guid tenantId, string context, CancellationToken ct = default)
        {
            var dead = await _listings.ListDeadSaleLinesForItem(itemId, tenantId);
            var result = ReservationRelease.Released;
            foreach (var line in dead)
            {
                var safe = await MakeUnpayable(line.SaleStatus, line.StripePaymentIntentId, line.StripeConnectedAccountId, ct);
                if (safe == ReservationRelease.AlreadyPaid) return ReservationRelease.AlreadyPaid;
                if (safe == ReservationRelease.Blocked) { result = ReservationRelease.Blocked; continue; }
                await DetachLine(line.SaleId, line.SaleStatus, itemId, tenantId, context);
            }
            return result;
        }

        public async Task<ReservationRelease> MakeUnpayable(string? saleStatus, string? paymentIntentId,
            string? connectedAccountId, CancellationToken ct = default)
        {
            if (!ListingRules.MustCancelIntent(saleStatus, paymentIntentId)) return ReservationRelease.Released;
            string? reported;
            try
            {
                reported = await _payments.CancelPaymentIntentAsync(paymentIntentId!, connectedAccountId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                reported = null;   // unreachable: nothing is known to be safe
            }
            return ListingRules.CancelOutcome(reported);
        }

        public async Task<int> DetachLine(Guid saleId, string saleStatus, Guid itemId, Guid tenantId, string context)
        {
            var n = await _listings.DetachDeadSaleLine(saleId, itemId, tenantId);
            if (n > 0)
            {
                await _audit.Log(
                    "shop.listing.sale_line_detached",
                    $"Freed a bike from a {saleStatus} sale so it can be sold again",
                    targetKind: "shop_sale",
                    targetId: saleId,
                    tenantId: tenantId,
                    metadata: new { itemId, saleStatus, context, lines = n });
            }
            return n;
        }
    }
}
