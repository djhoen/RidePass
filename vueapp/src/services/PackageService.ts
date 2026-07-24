import axios from 'axios'

export interface PackageTier {
    id: string
    name: string
    priceCents: number
    dayScope: 'any' | 'weekday' | 'weekend'
    afternoonOnly: boolean
    sessionCount: number
    sortOrder: number
}
export interface PackageSlot {
    id: string
    dayScope: 'any' | 'weekday' | 'weekend'
    startTime: string
    isAfternoon: boolean
    capacity: number
    instructorId: string | null
}
export interface PackageItemSizeOption {
    variantId: string
    label: string
    depositCents: number
}
export interface PackageItem {
    id: string
    itemType: 'bike' | 'gear'
    variantId: string
    quantity: number
    name: string | null
    variantLabel: string | null
    depositCents: number
    sizeOptions: PackageItemSizeOption[]
}
export interface PackageProduct {
    id: string
    name: string
    slug: string | null
    summary: string | null
    description: string | null
    heroImageUrl: string | null
    landingPublished: boolean
    includesDayTicket: boolean
    coachingMinutes: number | null
    coachingLabel: string | null
    isActive: boolean
    sortOrder: number
    insuranceOffered: boolean
    insuranceLabel: string | null
    tiers: PackageTier[]
    slots: PackageSlot[]
    items: PackageItem[]
}

export interface PackageSlotAvailability { slotId: string; startTime: string; remaining: number }
export interface PackageAvailability {
    available: boolean
    reason: string | null
    priceCents: number
    depositCents: number
    insuranceCents: number
    sessions: PackageSlotAvailability[]
}
export interface PackageBookResult {
    purchaseId: string
    status: string
    clientSecret: string | null
    depositClientSecret: string | null
    totalCents: number
    depositCents: number
}

export interface UpsertPackageRequest {
    name: string
    slug?: string | null
    summary?: string | null
    description?: string | null
    heroImageUrl?: string | null
    landingPublished: boolean
    includesDayTicket: boolean
    dayTicketEventTypeCode: string
    coachingMinutes?: number | null
    coachingLabel?: string | null
    isActive: boolean
    sortOrder: number
    validFromDate?: string | null
    validToDate?: string | null
    tiers: Omit<PackageTier, 'id'>[]
    slots: Omit<PackageSlot, 'id'>[]
    items: { itemType: 'bike' | 'gear'; variantId: string; quantity: number; sortOrder: number }[]
}

export class PackageService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    // Public
    listPublic() {
        return axios.get<{ data: PackageProduct[] }>(`${this.apiUrl}/Package`)
    }
    getLanding(slugOrId: string) {
        return axios.get<{ data: PackageProduct }>(`${this.apiUrl}/Package/Landing/${encodeURIComponent(slugOrId)}`)
    }
    availability(pkg: string, dateIso: string, tierId: string) {
        return axios.get<{ data: PackageAvailability }>(`${this.apiUrl}/Package/Availability`, {
            params: { package: pkg, date: dateIso, tierId },
        })
    }
    book(req: { packageId: string; tierId: string; rideDate: string; slotId?: string | null; bikeVariantId?: string | null; insurance?: boolean }) {
        return axios.post<{ data: PackageBookResult }>(`${this.apiUrl}/Package/Book`, req)
    }

    // Admin
    listAdmin() {
        return axios.get<{ data: PackageProduct[] }>(`${this.apiUrl}/Package/Admin`)
    }
    getAdmin(id: string) {
        return axios.get<{ data: PackageProduct }>(`${this.apiUrl}/Package/Admin/${id}`)
    }
    create(req: UpsertPackageRequest) {
        return axios.post<{ data: PackageProduct }>(`${this.apiUrl}/Package`, req)
    }
    update(id: string, req: UpsertPackageRequest) {
        return axios.put<{ data: PackageProduct }>(`${this.apiUrl}/Package/${id}`, req)
    }
    remove(id: string) {
        return axios.delete(`${this.apiUrl}/Package/${id}`)
    }
}
