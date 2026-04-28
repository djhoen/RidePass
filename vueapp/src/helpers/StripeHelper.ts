import { loadStripe, type Stripe } from '@stripe/stripe-js'

let stripePromise: Promise<Stripe | null> | null = null
let loadedWithKey: string | null = null

export function getStripe(publishableKey: string | null | undefined): Promise<Stripe | null> {
    if (!publishableKey) {
        return Promise.resolve(null)
    }
    if (!stripePromise || loadedWithKey !== publishableKey) {
        loadedWithKey = publishableKey
        stripePromise = loadStripe(publishableKey)
    }
    return stripePromise
}
