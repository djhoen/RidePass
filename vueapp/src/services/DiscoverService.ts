import axios from 'axios'

export interface TrackDiscoverItem {
    tenantId: string
    subdomain: string
    displayName: string
    addressLine: string | null
    city: string | null
    region: string | null
    postalCode: string | null
    country: string | null
    latitude: number | null
    longitude: number | null
    distanceKm: number | null
    upcomingEventsCount: number
}

export interface EventDiscoverItem {
    eventId: string
    tenantId: string
    tenantSubdomain: string
    tenantDisplayName: string
    tenantCity: string | null
    tenantRegion: string | null
    latitude: number | null
    longitude: number | null
    distanceKm: number | null
    title: string
    startsAtUtc: string
    endsAtUtc: string
    locationLabel: string | null
    eventTypeName: string
    eventTypeColor: string
}

export interface DiscoverQuery {
    lat?: number | null
    lng?: number | null
    radiusKm?: number | null
    q?: string | null
    fromUtc?: string | null
    toUtc?: string | null
}

export class DiscoverService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    searchTracks(params: DiscoverQuery) {
        return axios.get<{ data: TrackDiscoverItem[] }>(`${this.apiUrl}/Discover/Tracks`, {
            params: compact(params),
        })
    }

    searchEvents(params: DiscoverQuery) {
        return axios.get<{ data: EventDiscoverItem[] }>(`${this.apiUrl}/Discover/Events`, {
            params: compact(params),
        })
    }
}

function compact(o: Record<string, any>): Record<string, any> {
    const out: Record<string, any> = {}
    for (const [k, v] of Object.entries(o)) {
        if (v !== null && v !== undefined && v !== '') out[k] = v
    }
    return out
}
