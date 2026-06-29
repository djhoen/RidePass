namespace webapi.Controllers.API.Data.Concession
{
    // POS member-discount lookup result for an email/phone. Reports whether the customer holds an active
    // Season Pass and/or a linked LoamPass account, and the discount each perk would apply (when the
    // tenant has that perk enabled). The cashier then applies the chosen perk; the sale re-verifies it.
    public class ConcessionMemberLookupResponse
    {
        public bool Found { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public ConcessionMemberPerk? SeasonPass { get; set; }
        public ConcessionMemberPerk? Loampass { get; set; }
    }

    // An offered member perk. Eligible = the customer qualifies AND the tenant enabled this perk.
    // Kind 'percent' = bps in Value, 'amount' = cents in Value. Label is a ready-to-show description.
    public class ConcessionMemberPerk
    {
        public bool Eligible { get; set; }
        public string Kind { get; set; } = "percent";
        public int Value { get; set; }
        public string Label { get; set; } = null!;
    }
}
