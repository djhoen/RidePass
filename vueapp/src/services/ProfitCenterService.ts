import axios from 'axios'

export interface ProfitCenter {
    id: string
    name: string
    sortOrder: number
    /** #RRGGBB this center is drawn in on every screen and chart. */
    color: string
    revenueKeys: string[]
}

export interface ProfitCenterPalette {
    /** Recommended swatches, in the order new centers are assigned them. */
    swatches: string[]
    /** Reserved for the all-revenue series; offered as taken, not pickable. */
    totalSeriesColor: string
}

export interface RevenueStream {
    key: string
    label: string
    /** Where this stream reports when unassigned (its built-in department's label). */
    defaultCenterLabel: string
}

export interface EventRouting {
    id: string
    name: string
    /** Null = the default (event ticket & gate revenue). */
    revenueKey: string | null
}

export interface ProfitCentersResponse {
    usingDefaults: boolean
    centers: ProfitCenter[]
    streams: RevenueStream[]
    eventTypes: EventRouting[]
    eventRoutingOptions: RevenueStream[]
    palette: ProfitCenterPalette
}

export class ProfitCenterService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    get() {
        return axios.get<{ data: ProfitCentersResponse }>(`${this.apiUrl}/ProfitCenters`)
    }

    /** Omitting the color lets the server assign the next unused palette slot. */
    create(name: string, color?: string | null) {
        return axios.post<{ data: ProfitCenter }>(
            `${this.apiUrl}/ProfitCenters`, { name, color: color ?? null })
    }

    /** Name and color save together; passing a null color keeps the current one. */
    update(id: string, name: string, color?: string | null) {
        return axios.put(`${this.apiUrl}/ProfitCenters/${id}`, { name, color: color ?? null })
    }

    remove(id: string) {
        return axios.delete(`${this.apiUrl}/ProfitCenters/${id}`)
    }

    reorder(items: { id: string; sortOrder: number }[]) {
        return axios.post(`${this.apiUrl}/ProfitCenters/Reorder`, { items })
    }

    /** Bulk save: every stream with its center id, or null to fall back to the built-in group. */
    saveAssignments(assignments: { revenueKey: string; profitCenterId: string | null }[]) {
        return axios.put(`${this.apiUrl}/ProfitCenters/Assignments`, { assignments })
    }

    seedDefaults() {
        return axios.post(`${this.apiUrl}/ProfitCenters/SeedDefaults`)
    }

    setEventRouting(eventTypeId: string, revenueKey: string | null) {
        return axios.put(`${this.apiUrl}/ProfitCenters/EventRouting/${eventTypeId}`, { revenueKey })
    }
}
