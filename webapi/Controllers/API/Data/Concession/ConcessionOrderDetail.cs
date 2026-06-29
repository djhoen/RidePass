namespace webapi.Controllers.API.Data.Concession
{
    // Full order detail for the staff order-history view: the summary plus its line items.
    public class ConcessionOrderDetail : ConcessionOrderSummary
    {
        public List<Line> Lines { get; set; } = new();

        public class Line
        {
            public string Name { get; set; } = null!;
            public string? VariantLabel { get; set; }
            public int Quantity { get; set; }
            // NET line total (after any discount). DiscountCents is what was taken off this line (its own
            // line discount plus any allocated share of an order-level discount); Label describes it.
            public int LineTotalCents { get; set; }
            public int DiscountCents { get; set; }
            public string? DiscountLabel { get; set; }
            public string? Notes { get; set; }
            public List<string> Modifiers { get; set; } = new();
            // Combo linkage so the detail can nest a combo's component lines under it.
            public bool IsCombo { get; set; }
            public string? ComboTier { get; set; }
            public Guid? ParentLineId { get; set; }
            public Guid LineId { get; set; }
        }
    }
}
