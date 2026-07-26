namespace webapi.Controllers.API.Data.Concession
{
    // Cart the cashier rang up. Each line is a product + optional chosen variant + qty, with any
    // selected structured modifiers and a free-text note. Tip + payment method apply to the whole sale.
    public class ConcessionSaleRequest
    {
        public List<SaleLine> Items { get; set; } = new();
        public int TipCents { get; set; }
        // 'cash' = paid at the counter immediately; 'card' = card-present on the reader.
        public string PaymentMethod { get; set; } = "card";
        // Optional name the cashier puts on a counter order (shows on the cook screen + order history).
        public string? CustomerName { get; set; }
        // Optional order-level discount/comp (percent/dollar/preset/comp/member perk). Server recomputes.
        public ConcessionDiscountInput? Discount { get; set; }
        // Manager PIN authorizing any PIN-gated discount/comp in this sale (manual discounts + comps). The
        // server verifies it and stamps the authorizing manager on the sale. Ignored when nothing needs it.
        public string? ManagerPin { get; set; }

        // Store credit as a tender: the account the cashier looked up and how much of its balance
        // to apply. The server re-verifies the balance and caps at the order total.
        public Guid? CreditAccountId { get; set; }
        public int CreditCents { get; set; }


        public class SaleLine
        {
            public Guid ProductId { get; set; }
            public Guid? VariantId { get; set; }
            public int Quantity { get; set; }
            // Selected modifier option ids (validated against the product's configured groups).
            public List<Guid> ModifierOptionIds { get; set; } = new();
            public string? Notes { get; set; }
            // "Make it a combo": the chosen size tier (null = not a combo) and one option per slot.
            public Guid? ComboTierId { get; set; }
            public List<ComboSelection> ComboSelections { get; set; } = new();
            // Optional per-line discount/comp. Server recomputes the cents from the catalog/config.
            public ConcessionDiscountInput? Discount { get; set; }
        }

        public class ComboSelection
        {
            public Guid SlotId { get; set; }
            public Guid OptionId { get; set; }
        }
    }
}
