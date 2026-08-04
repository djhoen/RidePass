// Staff-side state for the bike shop's customer-facing display, shared as a module singleton so
// the register, the rentals screen, and the signing dialogs all see the same pairing. The display
// tablet keeps its own session id under a DIFFERENT localStorage key ('shopDisplayId'), so running
// both screens on one device stays safe.
import { ref, computed } from 'vue'
import {
    BikeShopService, type ShopDisplayState, type ShopDisplaySignRequest, type ShopDisplaySignResponse,
} from '@/services/BikeShopService'

const svc = new BikeShopService()
const STORAGE_KEY = 'shopPosDisplayId'

export const shopDisplayId = ref(localStorage.getItem(STORAGE_KEY) || '')
export const shopDisplayPaired = computed(() => !!shopDisplayId.value)

let lastPushed = ''

export function idleShopDisplayState(): ShopDisplayState {
    return { status: 'idle', lines: [], subtotalCents: 0, note: null, sign: null }
}

export async function pairShopDisplay(code: string): Promise<void> {
    const d = ((await svc.shopDisplayByCode(code.trim())) as any).data.data
    shopDisplayId.value = d.id
    localStorage.setItem(STORAGE_KEY, d.id)
    lastPushed = ''   // force the next push through so the display leaves its welcome screen
    await pushShopDisplayState(idleShopDisplayState())
}

export function unpairShopDisplay(): void {
    // Best-effort: send the display back to welcome before forgetting it.
    if (shopDisplayId.value) svc.updateShopDisplayState(shopDisplayId.value, idleShopDisplayState()).catch(() => { /* best-effort */ })
    shopDisplayId.value = ''
    localStorage.removeItem(STORAGE_KEY)
}

// Push a snapshot, deduplicated. Throws on failure so callers decide whether it matters (a cart
// mirror shrugs; a signature request must tell the cashier).
export async function pushShopDisplayState(state: ShopDisplayState): Promise<void> {
    if (!shopDisplayId.value) return
    const json = JSON.stringify(state)
    if (json === lastPushed) return
    await svc.updateShopDisplayState(shopDisplayId.value, state)
    lastPushed = json
}

// Ask the customer to read + sign a document on their screen. Resolves with their response, or
// null when cancelled. `cancel()` also returns the display to the given fallback state.
export interface SignatureRequestHandle {
    requestId: string
    promise: Promise<ShopDisplaySignResponse | null>
    cancel: () => void
}

export function requestSignatureOnDisplay(
    doc: Omit<ShopDisplaySignRequest, 'requestId'>,
    fallbackState?: ShopDisplayState,
): SignatureRequestHandle {
    const requestId = crypto.randomUUID()
    let cancelled = false
    let timer: number | undefined

    const promise = new Promise<ShopDisplaySignResponse | null>((resolve, reject) => {
        (async () => {
            try {
                await pushShopDisplayState({
                    status: 'sign', lines: [], subtotalCents: 0, note: null,
                    sign: { ...doc, requestId },
                })
            } catch (err) { reject(err); return }

            const poll = async () => {
                if (cancelled) { resolve(null); return }
                try {
                    const d = ((await svc.shopDisplay(shopDisplayId.value)) as any).data.data
                    if (d.responseJson) {
                        const resp = JSON.parse(d.responseJson) as ShopDisplaySignResponse
                        // Only the outstanding request counts; anything else is stale.
                        if (resp.requestId === requestId) { resolve(resp); return }
                    }
                } catch { /* transient; keep polling */ }
                timer = window.setTimeout(poll, 1000)
            }
            poll()
        })()
    })

    return {
        requestId,
        promise,
        cancel: () => {
            cancelled = true
            if (timer) window.clearTimeout(timer)
            pushShopDisplayState(fallbackState ?? idleShopDisplayState()).catch(() => { /* best-effort */ })
        },
    }
}
