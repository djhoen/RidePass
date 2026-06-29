namespace webapi.Controllers.API.Data.Tax
{
    // Save the tenant's event admission/amusement tax. RateBps is clamped to 0..10000 server-side.
    public class UpdateAdmissionTaxRequest
    {
        public int RateBps { get; set; }
        public bool PricesIncludeTax { get; set; }
        public bool ServiceChargeTaxable { get; set; } = true;
        public string? JurisdictionLabel { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
