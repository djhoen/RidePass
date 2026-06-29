import { loadStripe, type Stripe } from '@stripe/stripe-js'

let stripePromise: Promise<Stripe | null> | null = null
let loadedWithKey: string | null = null

/**
 * Loads Stripe.js. For a direct-charge tenant ('direct' mode), pass the tenant's connected
 * account id as `stripeAccount` so the Payment Element confirms the charge on that account
 * (the tenant is merchant of record). Cached per (publishableKey, stripeAccount) pair.
 */
export function getStripe(
    publishableKey: string | null | undefined,
    stripeAccount?: string | null,
): Promise<Stripe | null> {
    if (!publishableKey) {
        return Promise.resolve(null)
    }
    const cacheKey = stripeAccount ? `${publishableKey}::${stripeAccount}` : publishableKey
    if (!stripePromise || loadedWithKey !== cacheKey) {
        loadedWithKey = cacheKey
        stripePromise = stripeAccount
            ? loadStripe(publishableKey, { stripeAccount })
            : loadStripe(publishableKey)
    }
    return stripePromise
}
