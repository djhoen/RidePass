import axios from 'axios'

// ── Bike shop "Today": one business day of rentals and reservations, grouped by program ─────────
// Read model for the Rentals screen's Today tab. Deliberately its own service rather than another
// method on BikeShopService: that file is the rental/catalog write surface and is edited often,
// while this is a single read that the counter opens dozens of times a day.
//
// Every timestamp is UTC on the wire; the panel renders them in the tenant's timezone via
// @/helpers/TenantTime. `date` is a tenant-local calendar day, never an instant.

export type ShopDayProgramKind = 'shop_rental' | 'event_type' | 'package'

/**
 * Where a rental sits in the day, derived server-side from status + the date:
 * pickup    = paid, starts today, not yet out
 * out       = status out
 * return    = out and ends today (also counted under outNow)
 * reserved  = paid, starts after today
 * done      = returned / damaged
 * cancelled = cancelled (only present when includeCancelled was asked for)
 * unpaid    = pending payment
 */
export type ShopDayPhase = 'pickup' | 'out' | 'return' | 'reserved' | 'done' | 'cancelled' | 'unpaid'

export interface ShopDayCounts {
    pickupsDue: number
    outNow: number
    returnsDue: number
    reserved: number
    unpaid: number
    returned: number
}

export interface ShopDayItem {
    name: string
    variantLabel: string | null
    quantity: number
    /** The assigned unit ("Trek Fuel #3") for serialized gear; null while unassigned or for pool stock. */
    unitLabel: string | null
    unitSerial: string | null
    /** Not in the agreed shape today. Read defensively so the product-type filter can work the
     *  moment the server starts sending it; until then rows without it are never hidden by it. */
    categoryId?: string | null
}

export interface ShopDayRow {
    rentalId: string
    orderNumber: number | null
    status: string
    // Set by the server when a pickup or return slipped past its day; the row is still on today's sheet.
    overdue?: boolean
    startsAtUtc: string
    endsAtUtc: string
    checkedOutAtUtc: string | null
    returnedAtUtc: string | null
    customerName: string | null
    customerPhone: string | null
    customerEmail: string | null
    /** Set when the renter has an account; the panel links to /Admin/Customers/{id}. */
    customerUserId: string | null
    items: ShopDayItem[]
    /** Booked at the counter (sold_by_user_id set) or by the customer online. */
    channel: 'counter' | 'online'
    eventTitle: string | null
    eventStartsAtUtc: string | null
    packageName: string | null
    sessionStartAtUtc: string | null
    totalCents: number
    depositCents: number
    /** total minus what was paid: 0 for paid rows, the full total for pending ones. */
    balanceDueCents: number
    phase: ShopDayPhase
}

export interface ShopDayGroup {
    /** 'shop_rental' | 'event_type:<guid>' | 'package:<guid>' */
    key: string
    kind: ShopDayProgramKind
    label: string
    /** The event type's color; null for rentals and packages (the panel falls back to a neutral line). */
    color: string | null
    sortOrder: number
    counts: ShopDayCounts
    rows: ShopDayRow[]
}

export interface ShopDayProgram {
    key: string
    label: string
    kind: ShopDayProgramKind
}

export interface ShopDayProductType {
    id: string
    name: string
}

export interface ShopDay {
    /** The tenant-local calendar day this sheet covers, yyyy-MM-dd. */
    date: string
    timezone: string
    totals: ShopDayCounts
    /** Rentals first, then event types by their sort order, then packages by name. */
    groups: ShopDayGroup[]
    /** Every program with at least one row today, for the filter chips. */
    programs: ShopDayProgram[]
    /** Categories present in today's lines, for the product-type filter. */
    productTypes: ShopDayProductType[]
}

export class ShopDayService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    /**
     * GET BikeShopDay?date=yyyy-MM-dd&includeCancelled=false
     * `date` is a tenant-local calendar day. Cancelled rows are left out unless asked for;
     * failed and abandoned bookings never appear.
     */
    getDay(date: string, includeCancelled = false) {
        const q = new URLSearchParams({ date, includeCancelled: String(includeCancelled) })
        return axios.get<{ data: ShopDay }>(`${this.apiUrl}/BikeShopDay?${q.toString()}`)
    }
}
