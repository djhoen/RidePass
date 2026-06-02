import axios from 'axios'

export type UpcomingKind = 'event_ticket' | 'pass' | 'season_pass' | 'membership'

export interface UpcomingItem {
    kind: UpcomingKind
    id: string
    tenantId: string
    tenantSubdomain: string
    tenantDisplayName: string
    itemName: string
    occursAtUtc: string | null
    validToUtc: string | null
    amountCents: number
    redemptionToken: string | null
    createdAtUtc: string
}

export class UpcomingService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    list() {
        return axios.get<{ data: UpcomingItem[] }>(`${this.apiUrl}/Me/Upcoming`)
    }
}
