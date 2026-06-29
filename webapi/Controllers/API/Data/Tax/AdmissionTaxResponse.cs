namespace webapi.Controllers.API.Data.Tax
{
    // The tenant's event admission/amusement tax settings. RateBps 0 = no tax.
    public class AdmissionTaxResponse
    {
        public int RateBps { get; set; }
        public bool PricesIncludeTax { get; set; }
        public bool ServiceChargeTaxable { get; set; }
        public string? JurisdictionLabel { get; set; }
        public bool IsActive { get; set; }
    }
}
