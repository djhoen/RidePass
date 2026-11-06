import axios, { type AxiosProgressEvent } from 'axios'

// Used & ex-rental sales: a fleet unit (shop_item) leaves the rental fleet and goes on the sales
// floor / online as a listing (shop_item_listing). Admin side is `api/ShopListing` (ShopCounter;
// product condition needs CatalogManage); the public store reads `api/ShopStoreUsed` anonymously
// and orders signed in. Shapes mirror the backend DTOs one-to-one; keep them in step.

// ── Types ────────────────────────────────────────────────────────────────────
export type ListingConditionGrade = 'excellent' | 'good' | 'fair'
export type ListingState = 'active' | 'reserved' | 'sold' | 'unlisted'
export type ProductCondition = 'new' | 'used'

export interface ListingPhoto {
    id: string
    url: string
    caption: string | null
    sortOrder: number
}

export interface ShopListing {
    id: string
    itemId: string
    title: string
    productId: string
    productName: string
    variantId: string
    variantLabel: string | null
    unitLabel: string
    serial: string | null
    askingPriceCents: number
    conditionGrade: ListingConditionGrade
    conditionNotes: string | null
    published: boolean
    listedAtUtc: string
    listedByName: string | null
    unlistedAtUtc: string | null
    state: ListingState
    soldAtUtc: string | null
    soldSaleId: string | null
    soldOrderNumber: number | null
    acquiredCostCents: number | null
    photos: ListingPhoto[]
    /** First listing photo, else the product image. */
    coverUrl: string | null
}

export interface UpsertListingBody {
    askingPriceCents: number
    conditionGrade: ListingConditionGrade
    conditionNotes: string | null
    /** Omit / null to keep the server default "{product} {variant} · {unit}". */
    title?: string | null
    /** false = counter only. */
    published: boolean
}
export interface CreateListingBody extends UpsertListingBody { itemId: string }

/** What the register's unit picker needs per unit: `GET ShopListing/ForItems?ids=`. */
export interface ListingForItem {
    listingId: string
    askingPriceCents: number
    conditionGrade: ListingConditionGrade
    title: string
}

export interface PublicListingPhoto { url: string; caption: string | null }
export interface PublicListing {
    id: string
    title: string
    productName: string
    variantLabel: string | null
    categoryName: string | null
    askingPriceCents: number
    conditionGrade: ListingConditionGrade
    conditionNotes: string | null
    photos: PublicListingPhoto[]
    coverUrl: string | null
    specs: { size: string | null; color: string | null }
    reserved: boolean
}
export interface PublicListings {
    listings: PublicListing[]
    /** Sellable, published products flagged condition 'used': the store badges + filters on these. */
    usedProductIds: string[]
}

/** Same shape the store's `POST ShopStore/Order` returns (one line, no coupon / credit in v1). */
export interface UsedOrderResult {
    saleId: string
    receiptToken?: string
    status: 'paid' | 'pending'
    orderNumber?: number | null
    totalCents: number
    dueCents?: number
    clientSecret?: string | null
    paymentIntentId?: string
}

export const CONDITION_GRADES: { value: ListingConditionGrade; title: string; hint: string }[] = [
    { value: 'excellent', title: 'Excellent', hint: 'Like new; light cosmetic wear at most.' },
    { value: 'good', title: 'Good', hint: 'Normal wear from rental use; fully serviced.' },
    { value: 'fair', title: 'Fair', hint: 'Visible wear or cosmetic damage; rides fine.' },
]

export function gradeLabel(g: ListingConditionGrade | string | null | undefined): string {
    return CONDITION_GRADES.find(c => c.value === g)?.title ?? (g ? String(g) : '')
}

/** Unit status is owned by the listing while it's on the floor; the status select never offers it. */
export const ITEM_STATUS_FOR_SALE = 'for_sale'

/**
 * The backend ships in parallel with this UI. Until its endpoints exist every read 404s, which
 * must read as "nothing listed" rather than an error: no used section on /Shop, an info line on
 * the For sale tab, an unchanged Items dialog and register.
 */
export function isMissingEndpoint(e: unknown): boolean {
    const status = (e as { response?: { status?: number } })?.response?.status
    return status === 404
}

/** Server replies are wrapped in `{ data: ... }`; tolerate a bare payload while the backend settles. */
function unwrap<T>(body: unknown): T {
    const b = body as { data?: T }
    return (b && typeof b === 'object' && 'data' in b ? b.data : body) as T
}

// ── Service ──────────────────────────────────────────────────────────────────
export class ShopListingService {
    private apiUrl: string

    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    // ── Admin (ShopCounter) ────────────────────────────────────────────────
    async list(status: 'active' | 'sold' | 'unlisted' | 'all' = 'active'): Promise<ShopListing[]> {
        const r = await axios.get(`${this.apiUrl}/ShopListing?status=${status}`)
        const d = unwrap<{ listings?: ShopListing[] } | ShopListing[]>(r.data)
        return Array.isArray(d) ? d : (d?.listings ?? [])
    }
    async create(body: CreateListingBody): Promise<ShopListing> {
        const r = await axios.post(`${this.apiUrl}/ShopListing`, body)
        return unwrap<ShopListing>(r.data)
    }
    async update(id: string, body: UpsertListingBody): Promise<ShopListing> {
        const r = await axios.put(`${this.apiUrl}/ShopListing/${id}`, body)
        return unwrap<ShopListing>(r.data)
    }
    /** Unlist: the unit returns to the rental fleet (`available`). 409 when sold or live-reserved. */
    async unlist(id: string): Promise<ShopListing | null> {
        const r = await axios.post(`${this.apiUrl}/ShopListing/${id}/Unlist`, {})
        // A bare OK (no DTO) is fine: callers already know the listing and just flip its state.
        const d = unwrap<ShopListing | null>(r.data)
        return d && typeof d === 'object' && 'id' in d ? d : null
    }
    uploadPhoto(listingId: string, file: File, caption?: string | null, onProgress?: (pct: number) => void) {
        const form = new FormData()
        form.append('file', file)
        if (caption) form.append('caption', caption)
        return axios.post<{ data: ListingPhoto }>(`${this.apiUrl}/ShopListing/${listingId}/Photos`, form, {
            headers: { 'Content-Type': 'multipart/form-data' },
            onUploadProgress: (ev: AxiosProgressEvent) => {
                if (onProgress && ev.total) onProgress(Math.round((ev.loaded / ev.total) * 100))
            },
        })
    }
    deletePhoto(listingId: string, photoId: string) {
        return axios.delete(`${this.apiUrl}/ShopListing/${listingId}/Photos/${photoId}`)
    }
    reorderPhotos(listingId: string, photoIds: string[]) {
        return axios.put(`${this.apiUrl}/ShopListing/${listingId}/Photos/Order`, { photoIds })
    }
    /** CatalogManage: the used badge + filter on the store, "Used" pill in Inventory. */
    setProductCondition(productId: string, condition: ProductCondition) {
        return axios.put(`${this.apiUrl}/ShopListing/Products/${productId}/Condition`, { condition })
    }
    /** Active listings keyed by unit id, for the register's unit picker. Empty map for no ids. */
    async forItems(itemIds: string[]): Promise<Record<string, ListingForItem>> {
        if (itemIds.length === 0) return {}
        const r = await axios.get(`${this.apiUrl}/ShopListing/ForItems`, { params: { ids: itemIds.join(',') } })
        return unwrap<Record<string, ListingForItem>>(r.data) ?? {}
    }

    // ── Public store ───────────────────────────────────────────────────────
    async publicListings(): Promise<PublicListings> {
        const r = await axios.get(`${this.apiUrl}/ShopStoreUsed/Listings`)
        const d = unwrap<Partial<PublicListings>>(r.data)
        return { listings: d?.listings ?? [], usedProductIds: d?.usedProductIds ?? [] }
    }
    async publicListing(id: string): Promise<PublicListing> {
        const r = await axios.get(`${this.apiUrl}/ShopStoreUsed/Listings/${id}`)
        return unwrap<PublicListing>(r.data)
    }
    /** Signed in. 409 = someone else is mid-checkout on this unit (20-minute reservation). */
    async order(id: string): Promise<UsedOrderResult> {
        const r = await axios.post(`${this.apiUrl}/ShopStoreUsed/Listings/${id}/Order`, {})
        return unwrap<UsedOrderResult>(r.data)
    }
}
