// Per-email tiered pricing for campaign sends. Charged by monthly volume; rates drop as
// volume climbs, calibrated to land just under Mailchimp for a typical track's send volume.
// Rates are cents per email (fractional cents are fine, they're summed across the send).
//
// This is the single source of truth for the in-app cost estimate. When server-side
// metering/billing is built, it should use the same schedule.
export const EMAIL_PRICE_TIERS: { upTo: number; centsPerEmail: number }[] = [
    { upTo: 2_000, centsPerEmail: 1.0 },
    { upTo: 10_000, centsPerEmail: 0.6 },
    { upTo: 50_000, centsPerEmail: 0.4 },
    { upTo: Infinity, centsPerEmail: 0.25 },
]

// Cost in cents for `count` emails, walking the tiers cumulatively (the first 2,000 are
// billed at the first rate, the next slice at the second, and so on).
export function estimateEmailCostCents(count: number): number {
    let remaining = Math.max(0, Math.trunc(count))
    let floor = 0
    let cents = 0
    for (const t of EMAIL_PRICE_TIERS) {
        if (remaining <= 0) break
        const slice = Math.min(remaining, t.upTo - floor)
        cents += slice * t.centsPerEmail
        remaining -= slice
        floor = t.upTo
    }
    return cents
}

export function formatEmailCost(count: number): string {
    return `$${(estimateEmailCostCents(count) / 100).toFixed(2)}`
}
