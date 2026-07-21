import axios from 'axios'

export interface EventDto {
    id: string
    eventTypeId: string
    eventTypeCode: string
    eventTypeName: string
    eventTypeColor: string
    eventTypeImageUrl: string | null
    title: string
    description: string | null
    startsAtUtc: string
    endsAtUtc: string
    allDay: boolean
    capacity: number | null
    locationLabel: string | null
    status: 'scheduled' | 'cancelled'
    allowsRiders: boolean
    allowsSpectators: boolean
    requiresRiderWaiver: boolean
    requiresSpectatorWaiver: boolean
    spectatorWaiverId: string | null
    racerWaiverId: string | null
    imageUrl: string | null
    // Per-event gate-fee heading overrides; null = inherit tenant setting.
    riderGateLabel: string | null
    spectatorGateLabel: string | null
    hasActiveTiers?: boolean
    hasSpectatorTiers?: boolean
    hasRaceEntryTiers?: boolean
    minTicketPriceCents?: number | null
    spotsReserved?: number | null
    eligiblePasses?: EligiblePass[]
    eligibleExtras?: EligibleExtra[]
    schedule?: ScheduleItem[]
    // Lessons: assigned instructors + bikes offered with the lesson (with live availability).
    instructors?: EventInstructor[]
    eligibleRentals?: EligibleRental[]
}

export interface EventInstructor {
    id: string
    name: string
    imageUrl: string | null
}

export interface EligibleRental {
    variantId: string
    name: string
    description: string | null
    imageUrl: string | null
    priceCents: number              // effective per-lesson price = override ?? daily rate (all-in)
    priceCentsOverride: number | null
    depositCents: number
    trackingKind: 'pool' | 'serialized'
    available: number               // free for THIS lesson's window
}

export interface ScheduleItem {
    time: string
    label: string
}

export interface EligiblePass {
    id: string
    name: string
    description: string | null
    priceCents: number
    requiresWaiver: boolean
    isActive: boolean
}

export interface EligibleExtra {
    productId: string
    name: string
    kind: string
    priceCents: number
    imageUrl: string | null
    inventory: number | null
    sold: number
    remaining: number   // -1 if unlimited
    requiresWaiver: boolean
    riderPaidServiceChargeBps: number   // buyer's share of the service charge (bps of tenant rate)
    variants: EligibleExtraVariant[]
}

export interface EligibleExtraVariant {
    id: string
    size: string | null
    color: string | null
    gender: string | null
    priceCents: number          // effective: variant override or product
    imageUrl: string | null     // effective: variant override or product
    inventory: number | null
    sold: number
    remaining: number           // -1 if unlimited
}

export interface EligibleExtraInput {
    productId: string
    inventory: number | null
}

export interface UpsertEventDto {
    eventTypeId: string
    title: string
    description: string | null
    startsAtUtc: string
    endsAtUtc: string
    allDay: boolean
    capacity: number | null
    locationLabel: string | null
    status: 'scheduled' | 'cancelled'
    allowsRiders: boolean
    allowsSpectators: boolean
    requiresRiderWaiver: boolean
    requiresSpectatorWaiver: boolean
    spectatorWaiverId: string | null
    racerWaiverId: string | null
    imageUrl: string | null
    riderGateLabel?: string | null
    spectatorGateLabel?: string | null
    eligiblePassProductIds?: string[]
    eligibleExtras?: EligibleExtraInput[]
    schedule?: ScheduleItem[]
    // Lessons: assigned instructor ids (server rejects an overlapping double-booking) and
    // the bikes offered with this lesson, each with an optional per-lesson price override.
    // null = leave untouched on save; [] = clear.
    instructorIds?: string[]
    eligibleRentals?: EligibleRentalInput[]
}

export interface EligibleRentalInput {
    variantId: string
    priceCentsOverride: number | null
}

export class EventService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    async list(fromUtc: string, toUtc: string) {
        return axios.get<{ data: EventDto[] }>(`${this.apiUrl}/Event`, { params: { fromUtc, toUtc } })
    }

    async getPublic(id: string) {
        return axios.get<{ data: EventDto }>(`${this.apiUrl}/Event/Public/${id}`)
    }

    async create(req: UpsertEventDto) {
        return axios.post(`${this.apiUrl}/Event`, req)
    }

    async update(id: string, req: UpsertEventDto) {
        return axios.put(`${this.apiUrl}/Event/${id}`, req)
    }

    async delete(id: string) {
        return axios.delete(`${this.apiUrl}/Event/${id}`)
    }

    async duplicate(id: string) {
        return axios.post(`${this.apiUrl}/Event/${id}/Duplicate`)
    }

    async uploadImage(file: File) {
        const form = new FormData()
        form.append('file', file)
        return axios.post<{ data: { imageUrl: string } }>(`${this.apiUrl}/Event/Image`, form, {
            headers: { 'Content-Type': 'multipart/form-data' },
        })
    }
}
