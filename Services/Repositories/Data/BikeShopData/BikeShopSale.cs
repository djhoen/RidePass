namespace Services.Repositories.Data.BikeShopData
{
    // The retail sale: a cart of variants rung up at the register. Payment orchestration (cash /
    // card-present) and the ledger live in the controller + finalizer; this is the persisted shape.

    public class ShopTaxCategory
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public int RateBps { get; set; }        // 825 = 8.25%
        public bool IsDefault { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
    }

    public class ShopSale
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? BuyerUserId { get; set; }
        public string? BuyerEmail { get; set; }
        public string? BuyerName { get; set; }
        public string Status { get; set; } = "pending";   // pending|paid|failed|refunded
        public int SubtotalCents { get; set; }
        public int DiscountCents { get; set; }
        public int TaxCents { get; set; }
        public int TipCents { get; set; }
        public int TotalCents { get; set; }
        public bool PricesIncludeTax { get; set; }
        public string PaymentMethod { get; set; } = "stripe";   // stripe|stripe_direct|cash|voucher
        public string? StripePaymentIntentId { get; set; }
        public string? StripeConnectedAccountId { get; set; }
        public int? OrderNumber { get; set; }
        public Guid? SoldByUserId { get; set; }
        // Set when this sale bills out a work order — its parts were consumed on the bench, so
        // depletion skips it entirely.
        public Guid? WorkOrderId { get; set; }
        // Prepaid work-order deposit credited against this sale. total_cents stays the full value
        // of the job; the payment (cash or PI) collects total minus this, and the ledger entry
        // records only that remainder (the deposit booked its own 'shop_wo_deposit' entry).
        public int DepositAppliedCents { get; set; }
        // Store credit spent as a tender on this sale (and which account it came from, so a
        // failed payment or a refund can hand it back). Like the deposit, the money paths all
        // use total minus deposit minus credit.
        public int CreditAppliedCents { get; set; }
        public Guid? CreditAccountId { get; set; }
        // Gift card tender (Script0199). Unlike credit/deposit, the gift-funded portion IS
        // recognized as ledger gross at redemption (gift purchases book nothing); only the PI
        // amount shrinks by it.
        public int GiftCardAppliedCents { get; set; }
        public Guid? GiftCardId { get; set; }
        public Guid ReceiptToken { get; set; }
        // 'counter' (staff-rung) or 'online' (rider bought from the public shop page; goods are
        // collected in store, stamped by PickedUpAt).
        public string OrderChannel { get; set; } = "counter";
        public DateTime? PickedUpAt { get; set; }
        public DateTime? RefundedAt { get; set; }
        public string? RefundNote { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ShopSaleLine
    {
        public Guid Id { get; set; }
        public Guid SaleId { get; set; }
        // Null only for a labor line on a work-order bill-out sale (labor isn't a catalog item).
        public Guid? VariantId { get; set; }
        // Set when the thing sold is a specific serialized unit (a bike); NULL for pool lines.
        public Guid? ItemId { get; set; }
        public int Quantity { get; set; }
        public string NameSnapshot { get; set; } = null!;
        public string? VariantLabel { get; set; }
        public int UnitPriceCents { get; set; }
        public int DiscountCents { get; set; }
        public int TaxCents { get; set; }
        public int TaxRateBps { get; set; }
        // Unit cost at sale time (COGS snapshot, Script0197). NULL on labor lines + historic rows.
        public int? UnitCostCentsFrozen { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ShopSaleWithLines : ShopSale
    {
        public List<ShopSaleLine> Lines { get; set; } = new();
    }

    /// <summary>
    /// Everything the register needs to price one variant on a ticket: its sale price, the resolved
    /// tax rate (product's category, else the tenant default), how many are available right now, and
    /// the frozen display text. Fetched in one query at ring-up so the server prices the cart itself.
    /// </summary>
    public class ShopVariantSaleInfo
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = null!;
        public string? Size { get; set; }
        public string? Color { get; set; }
        public string? Gender { get; set; }
        public string TrackingKind { get; set; } = null!;
        public int? SalePriceCents { get; set; }
        public int? CostCents { get; set; }   // snapshotted onto the sale line for COGS reporting
        public int TaxRateBps { get; set; }
        public int Available { get; set; }
    }
}
