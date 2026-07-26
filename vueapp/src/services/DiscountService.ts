import axios from 'axios'

/** Where a discount may be applied. Mirrors DiscountSurfaces in the API and the CHECK in
 *  Script0251; these are the ledger's own source_kind values. */
export type DiscountSurface =
    'event_ticket' | 'extras' | 'season_pass' | 'membership'
    | 'concession' | 'shop_sale' | 'shop_rental'

/** Labels for the surface pickers. Kept here so every screen names them identically. */
export const DISCOUNT_SURFACES: { value: DiscountSurface; title: string }[] = [
    { value: 'event_ticket', title: 'Event tickets' },
    { value: 'extras', title: 'Event add-ons' },
    { value: 'season_pass', title: 'Season passes' },
    { value: 'membership', title: 'Memberships' },
    { value: 'concession', title: 'Food & drink' },
    { value: 'shop_sale', title: 'Bike shop' },
    { value: 'shop_rental', title: 'Rentals' },
]

export interface DiscountPreset {
    id: string
    name: string
    kind: 'percent' | 'amount'
    /** Basis points when percent (1000 = 10%), cents when amount. */
    value: number
    surfaces: DiscountSurface[]
    requiresManager: boolean
    isActive: boolean
    sortOrder: number
    /** Server-rendered "10% off" / "$2.00 off", so every screen labels it the same. */
    label: string
}

export interface UpsertDiscountPreset {
    name: string
    kind: 'percent' | 'amount'
    value: number
    surfaces: DiscountSurface[]
    requiresManager: boolean
    isActive: boolean
    sortOrder: number
}

export class DiscountService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    /** Every discount, active and inactive. Requires settings.manage. */
    list() {
        return axios.get<{ data: DiscountPreset[] }>(`${this.apiUrl}/Discount`)
    }

    /** Active discounts a given counter may offer. Any counter permission. */
    forSurface(surface: DiscountSurface) {
        return axios.get<{ data: DiscountPreset[] }>(`${this.apiUrl}/Discount/For/${surface}`)
    }

    create(req: UpsertDiscountPreset) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/Discount`, req)
    }

    update(id: string, req: UpsertDiscountPreset) {
        return axios.put(`${this.apiUrl}/Discount/${id}`, req)
    }

    remove(id: string) {
        return axios.delete(`${this.apiUrl}/Discount/${id}`)
    }
}
