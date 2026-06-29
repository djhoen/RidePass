namespace Services.Payments
{
    // Per-tenant admission tax settings, loaded once per checkout and passed into the pure math below
    // so we never hit the DB per ticket. None = no tax (no row configured, or a 0% rate).
    public record AdmissionTaxConfig(int RateBps, bool PricesIncludeTax, bool ServiceChargeTaxable)
    {
        public static readonly AdmissionTaxConfig None = new(0, false, false);
        public bool Applies => RateBps > 0;
    }

    // TaxCents is the tax portion contained in AmountToChargeCents. We store amount_cents
    // tax-inclusive: in on-top mode the charge grows by the tax; in inclusive mode the charge is
    // unchanged and TaxCents is the portion backed out of the advertised price.
    public record AdmissionTaxResult(int TaxCents, int AmountToChargeCents);

    // Admission/amusement tax math for a single ticket. The taxable base is the post-discount ticket
    // price plus, when the tenant marks the service charge taxable, the rider's service-charge share
    // (a mandatory fee is usually part of the taxable admission charge). Rounding is per ticket so the
    // sum of rows matches the Stripe charge and the receipts to the cent.
    public static class AdmissionTax
    {
        // basePriceCents: post-discount ticket price (the tier portion).
        // riderFeeCents: the rider's service-charge share already folded into the pre-tax amount.
        public static AdmissionTaxResult Compute(int basePriceCents, int riderFeeCents, AdmissionTaxConfig cfg)
        {
            var preTaxAmount = basePriceCents + riderFeeCents;
            if (cfg is null || !cfg.Applies || preTaxAmount <= 0)
            {
                return new AdmissionTaxResult(0, preTaxAmount < 0 ? 0 : preTaxAmount);
            }

            var taxBase = cfg.ServiceChargeTaxable ? preTaxAmount : basePriceCents;
            if (taxBase <= 0)
            {
                return new AdmissionTaxResult(0, preTaxAmount);
            }

            if (cfg.PricesIncludeTax)
            {
                // The advertised price already contains the tax; back it out, charge is unchanged.
                var net = (int)Math.Round((decimal)taxBase * 10000m / (10000 + cfg.RateBps), MidpointRounding.AwayFromZero);
                var taxIncl = taxBase - net;
                return new AdmissionTaxResult(taxIncl, preTaxAmount);
            }

            // Added on top of the advertised price.
            var tax = (int)Math.Round((decimal)taxBase * cfg.RateBps / 10000m, MidpointRounding.AwayFromZero);
            return new AdmissionTaxResult(tax, preTaxAmount + tax);
        }
    }
}
