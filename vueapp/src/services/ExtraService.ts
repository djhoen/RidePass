import axios from 'axios'

// Default kinds — Vue picker offers these as quick-select chips. Tenants can also
// type a custom slug (e.g. "rv_hookup", "locker") which the server stores verbatim.
export const DEFAULT_EXTRA_KINDS = [
    { value: 'gate_fee',    label: 'Gate Fee',    icon: 'mdi-gate' },
    { value: 'camping',     label: 'Camping',     icon: 'mdi-tent' },
    { value: 'parking',     label: 'Parking',     icon: 'mdi-parking' },
    { value: 'pit_vehicle', label: 'Pit Vehicle', icon: 'mdi-truck' },
    // Apparel/merch sold as an event add-on (hats, shirts, etc.). Uses the size/color
    // variant editor. This is a SEPARATE catalog from F&B/concessions "swag": merch added
    // here shows only as an event add-on and never in the F&B store, and vice versa.
    { value: 'merch',       label: 'Merch',       icon: 'mdi-tshirt-crew' },
] as const

export function kindIcon(kind: string): string {
    const known = DEFAULT_EXTRA_KINDS.find(k => k.value === kind)
    return known?.icon ?? 'mdi-package-variant'
}

export function kindLabel(kind: string): string {
    const known = DEFAULT_EXTRA_KINDS.find(k => k.value === kind)
    if (known) return known.label
    // Fallback: prettify the slug — "rv_hookup" → "RV Hookup".
    return kind.split(/[-_]/).map(w => w.charAt(0).toUpperCase() + w.slice(1)).join(' ')
}

export interface ExtraProduct {
    id: string
    name: string
    description: string | null
    imageUrl: string | null
    kind: string
    priceCents: number
    riderPaidServiceChargeBps: number
    requiresWaiver: boolean
    isActive: boolean
    sortOrder: number
    expiresAt: string | null      // ISO UTC; null = no expiry
    inventory: number | null      // tenant-wide cap; null = unlimited
    sold: number                  // total units sold across events + variants
    remaining: number             // -1 if unlimited
    variants: ExtraVariant[]
}

export interface ExtraVariant {
    id: string
    productId: string
    size: string | null
    color: string | null
    gender: string | null
    sku: string | null
    tier: string | null
    description: string | null
    priceCents: number | null         // null = inherit from product
    inventory: number | null
    sold: number
    remaining: number                 // -1 if unlimited
    imageUrl: string | null
    sortOrder: number
    isActive: boolean
}

export interface UpsertExtraVariant {
    size: string | null
    color: string | null
    gender: string | null
    sku: string | null
    tier: string | null
    description: string | null
    priceCents: number | null
    inventory: number | null
    imageUrl: string | null
    sortOrder: number
    isActive: boolean
}

export interface UpsertExtraProduct {
    name: string
    description: string | null
    imageUrl: string | null
    kind: string
    priceCents: number
    riderPaidServiceChargeBps: number
    requiresWaiver: boolean
    isActive: boolean
    sortOrder: number
    expiresAt: string | null
    inventory: number | null
}

export interface BuyExtrasItem {
    productId: string
    quantity: number
    variantId?: string | null
}

export interface BuyExtrasResponse {
    purchaseIds: string[]
    clientSecret: string
    amountCents: number
    riderServiceChargeCents: number
}

export interface MyExtra {
    id: string
    redemptionToken: string
    eventId: string | null
    eventTitle: string | null
    eventStartsAtUtc: string | null
    productName: string
    kind: string
    quantity: number
    amountCents: number
    status: string
    createdAtUtc: string
}

export interface ExtraCheckInItem {
    purchaseId: string
    productName: string
    productKind: string
    purchaserName: string
    purchaserEmail: string
    quantity: number
    /** Size/colour/gender where the add-on has variants. */
    variantLabel: string | null
    amountCents: number
    status: string
    arrived: boolean
    arrivedAtUtc: string | null
    arrivedByName: string | null
    /** Null for an add-on bought at the counter with no event attached. */
    eventId: string | null
    eventTitle: string | null
    eventStartsAtUtc: string | null
    purchasedAtUtc: string
}

export interface ExtraCheckInResponse {
    items: ExtraCheckInItem[]
    totalCount: number
    arrivedCount: number
    /** True when the result hit the row cap, so the page can say so. */
    truncated: boolean
}

export interface ExtraCheckInProductOption {
    id: string
    name: string
    kind: string
    isActive: boolean
}

export class ExtraService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    // ── Check-in ─────────────────────────────────────────────────────────────
    checkInFilters() {
        return axios.get<{ data: { products: ExtraCheckInProductOption[] } }>(
            `${this.apiUrl}/Extra/CheckIn/Filters`)
    }

    /** A name/email query deliberately ignores the date window server-side. */
    checkInList(params: {
        productId?: string | null
        eventId?: string | null
        from?: string | null
        to?: string | null
        q?: string | null
        arrival?: 'arrived' | 'not_arrived' | null
    }) {
        return axios.get<{ data: ExtraCheckInResponse }>(`${this.apiUrl}/Extra/CheckIn`, { params })
    }

    setCheckIn(purchaseId: string, checkedIn: boolean) {
        return axios.put<{ data: { status: string; arrived: boolean; arrivedAtUtc: string | null } }>(
            `${this.apiUrl}/Extra/CheckIn/${purchaseId}`, { checkedIn })
    }

    listForAdmin() {
        return axios.get<{ data: ExtraProduct[] }>(`${this.apiUrl}/Extra/Products/Admin`)
    }
    listActive() {
        return axios.get<{ data: ExtraProduct[] }>(`${this.apiUrl}/Extra/Products`)
    }
    create(req: UpsertExtraProduct) {
        return axios.post<{ data: ExtraProduct }>(`${this.apiUrl}/Extra/Products`, req)
    }
    update(id: string, req: UpsertExtraProduct) {
        return axios.put<{ data: ExtraProduct }>(`${this.apiUrl}/Extra/Products/${id}`, req)
    }
    remove(id: string) {
        return axios.delete(`${this.apiUrl}/Extra/Products/${id}`)
    }

    reorderProducts(items: { id: string; sortOrder: number }[]) {
        return axios.post(`${this.apiUrl}/Extra/Products/Reorder`, { items })
    }

    buy(req: { eventId: string; items: BuyExtrasItem[] }) {
        return axios.post<{ data: BuyExtrasResponse }>(`${this.apiUrl}/Extra/Buy`, req)
    }
    listMine() {
        return axios.get<{ data: MyExtra[] }>(`${this.apiUrl}/Extra/Mine`)
    }

    // Image upload — used by both the product edit dialog and the variant editor.
    // Returns the absolute URL to store on whichever record is being edited.
    uploadImage(file: File) {
        const form = new FormData()
        form.append('file', file)
        return axios.post<{ data: { imageUrl: string } }>(`${this.apiUrl}/Extra/Image`, form, {
            headers: { 'Content-Type': 'multipart/form-data' },
        })
    }

    // ── Variants ─────────────────────────────────────────────────────────
    listVariants(productId: string) {
        return axios.get<{ data: ExtraVariant[] }>(`${this.apiUrl}/Extra/Products/${productId}/Variants`)
    }
    createVariant(productId: string, req: UpsertExtraVariant) {
        return axios.post<{ data: ExtraVariant }>(`${this.apiUrl}/Extra/Products/${productId}/Variants`, req)
    }
    updateVariant(productId: string, variantId: string, req: UpsertExtraVariant) {
        return axios.put<{ data: ExtraVariant }>(`${this.apiUrl}/Extra/Products/${productId}/Variants/${variantId}`, req)
    }
    removeVariant(productId: string, variantId: string) {
        return axios.delete(`${this.apiUrl}/Extra/Products/${productId}/Variants/${variantId}`)
    }
}
