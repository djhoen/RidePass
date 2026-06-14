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
    // Tenant's hero image (`/uploads/...` relative path). Joined to the API
    // origin client-side. Null = card renders a colored placeholder.
    heroImageUrl: string | null
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
    eventTypeCode: string
    eventTypeName: string
    eventTypeColor: string
    // Event cover image when set; falls back to the event type's image.
    // Both come back as relative `/uploads/...` paths and must be joined to
    // the API origin client-side.
    imageUrl: string | null
    eventTypeImageUrl: string | null
}

export interface DiscoverQuery {
    lat?: number | null
    lng?: number | null
    radiusKm?: number | null
    q?: string | null
    fromUtc?: string | null
    toUtc?: string | null
    // System event-type codes to include (e.g. ['race','open_ride']). Omitted /
    // empty = no type filter.
    eventTypeCodes?: string[] | null
    // Restrict to these tracks. Omitted / empty = all tracks.
    tenantIds?: string[] | null
}

// A selectable event type for the Events filter modal.
export interface EventTypeOption {
    code: string
    name: string
    color: string
}

// IP-based geolocation probe result. countryCode is ISO alpha-2 ("US") or null
// when the lookup couldn't resolve a country.
export interface GeoLocateResult {
    countryCode: string | null
    latitude: number | null
    longitude: number | null
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
            // Serialize arrays as repeated keys without `[]` brackets
            // (eventTypeCodes=a&eventTypeCodes=b) so ASP.NET model binding fills
            // the string[] / Guid[] action params.
            paramsSerializer: { indexes: null },
        })
    }

    // Selectable event types. `onlyCodes` restricts the result to an allow-list
    // (the apex page passes its 3 permitted codes).
    listEventTypes(onlyCodes?: string[]) {
        return axios.get<{ data: EventTypeOption[] }>(`${this.apiUrl}/Discover/EventTypes`, {
            params: onlyCodes && onlyCodes.length > 0 ? { onlyCodes } : {},
            paramsSerializer: { indexes: null },
        })
    }

    geoLocate(debugCountry?: string) {
        return axios.get<{ data: GeoLocateResult }>(`${this.apiUrl}/Discover/GeoLocate`, {
            params: debugCountry ? { debugCountry } : {},
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
