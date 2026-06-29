namespace Services.Repositories.Data.TaxData
{
    // A per-tenant tax rate, keyed by tax_kind so one table can hold the admission/amusement rate
    // today and the concession sales rate later. Only 'admission' is used for now. The tenant is the
    // merchant of record for the admissions it sells, so this is the rate it remits to its
    // jurisdiction; RidePass just calculates and collects it at checkout.
    public class TenantTaxRate
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string TaxKind { get; set; } = "admission";
        public int RateBps { get; set; }                       // basis points: 900 = 9.00%
        public bool PricesIncludeTax { get; set; }             // advertised price already includes tax
        public bool ServiceChargeTaxable { get; set; } = true; // is the rider service-charge share taxed
        public string? JurisdictionLabel { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
