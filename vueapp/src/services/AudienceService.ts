import axios from 'axios'
import type { CampaignAudienceOptions } from './CampaignService'

export type AudienceBase = 'everyone' | 'subscribers' | 'customers'
export type AudienceMatch = 'all' | 'any'
export type AudienceRuleKind =
    | 'event_purchased' | 'event_type_purchased' | 'pass_holder' | 'pass_expiring'
    | 'abandoned_cart' | 'postal_code' | 'state' | 'city'

export interface AudienceRule {
    kind: AudienceRuleKind
    /** "is not": exclude instead of include. */
    negate: boolean
    ids: string[]
    values: string[]
    days: number | null
    activeOnly: boolean
    fromUtc: string | null
    toUtc: string | null
    /** Read-only sentence from the API ("bought a ticket to Spring Camp"). */
    label?: string | null
}

export interface AudienceDefinition {
    base: AudienceBase
    match: AudienceMatch
    rules: AudienceRule[]
}

export interface AudienceItem {
    id: string
    name: string
    description: string | null
    isSample: boolean
    definition: AudienceDefinition
    summary: string
    memberCount: number
    usedByCampaigns: number
    usedByAutomations: number
    updatedAt: string
}

export interface AudiencePreview {
    count: number
    suppressed: number
    /** How many have a phone on their account (the reach of a text). */
    withPhone: number
    summary: string
    sample: { email: string; name: string | null }[]
}

export interface UpsertAudienceRequest {
    name: string
    description: string | null
    definition: AudienceDefinition
}

export function emptyRule(kind: AudienceRuleKind = 'event_purchased'): AudienceRule {
    return {
        kind, negate: false, ids: [], values: [],
        days: kind === 'abandoned_cart' ? 1 : kind === 'pass_expiring' ? 30 : null,
        activeOnly: kind === 'pass_holder', fromUtc: null, toUtc: null,
    }
}

export class AudienceService {
    private apiUrl: string
    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    list() {
        return axios.get<{ data: AudienceItem[] }>(`${this.apiUrl}/Audience`)
    }

    get(id: string) {
        return axios.get<{ data: AudienceItem }>(`${this.apiUrl}/Audience/${id}`)
    }

    options() {
        return axios.get<{ data: CampaignAudienceOptions }>(`${this.apiUrl}/Audience/Options`)
    }

    preview(definition: AudienceDefinition) {
        return axios.post<{ data: AudiencePreview }>(`${this.apiUrl}/Audience/Preview`, { definition })
    }

    create(req: UpsertAudienceRequest) {
        return axios.post<{ data: AudienceItem }>(`${this.apiUrl}/Audience`, req)
    }

    update(id: string, req: UpsertAudienceRequest) {
        return axios.put<{ data: AudienceItem }>(`${this.apiUrl}/Audience/${id}`, req)
    }

    delete(id: string) {
        return axios.delete(`${this.apiUrl}/Audience/${id}`)
    }

    /** Adds the starter audiences this track does not have yet. */
    addSamples() {
        return axios.post<{ data: { added: number } }>(`${this.apiUrl}/Audience/Samples`)
    }
}
