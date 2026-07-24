// Per-email tiered pricing for campaign sends. Charged by monthly volume; rates drop as
// volume climbs, calibrated to land just under Mailchimp for a typical track's send volume.
// Rates are cents per email (fractional cents are fine, they're summed across the send).
//
// This is the single source of truth for the in-app cost estimate. When server-side
// metering/billing is built, it should use the same schedule.
// Calibrated to Mailchimp Essentials 2026 contact-tier anchors (a list emailed ~once a
// month): $26.50 @ 1k, $45 @ 2.5k, $75 @ 5k, $110 @ 10k. Cumulative cost lands $0.50-$1.50
// under each anchor: $25 @ 1k, $44.50 @ 2.5k, $74.50 @ 5k, $109.50 @ 10k.
export const EMAIL_PRICE_TIERS: { upTo: number; centsPerEmail: number }[] = [
    { upTo: 1_000, centsPerEmail: 2.5 },
    { upTo: 2_500, centsPerEmail: 1.3 },
    { upTo: 5_000, centsPerEmail: 1.2 },
    { upTo: 10_000, centsPerEmail: 0.7 },
    { upTo: 50_000, centsPerEmail: 0.5 },
    { upTo: Infinity, centsPerEmail: 0.3 },
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
