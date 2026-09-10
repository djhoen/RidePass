import axios from 'axios'

export type CampaignAudienceKind = 'subscribers' | 'event' | 'event_type' | 'pass_product'

/** Target of a non-subscriber audience. Only the field the kind needs is set. */
export interface CampaignAudienceConfig {
    eventId: string | null
    eventTypeId: string | null
    passProductId: string | null
    fromUtc: string | null
    toUtc: string | null
}

export interface CampaignAudienceOptions {
    events: { id: string; title: string; startsAtUtc: string; status: string; eventTypeName: string }[]
    eventTypes: { id: string; name: string; isActive: boolean }[]
    passProducts: { id: string; name: string; isActive: boolean }[]
}

export interface CampaignAudienceCount {
    kind: CampaignAudienceKind
    label: string
    recipients: number
    suppressed: number
}

export interface CampaignListItem {
    id: string
    subject: string
    status: 'draft' | 'scheduled' | 'sending' | 'sent' | 'failed'
    recipientCount: number
    audienceKind: CampaignAudienceKind
    audienceLabel: string
    audienceConfig: CampaignAudienceConfig
    sentAtUtc: string | null
    scheduledForUtc: string | null
    createdAtUtc: string
}

export interface CampaignDetail extends CampaignListItem {
    bodyHtml: string
    bodyText: string | null
}

export interface SendCampaignResponse {
    campaignId: string
    recipientCount: number
    status: string
    sendNotice: string | null
}

export class CampaignService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    /** Inline image for a campaign or automation body; returns the URL the editor inserts. */
    uploadImage(file: File) {
        const form = new FormData()
        form.append('file', file)
        return axios.post<{ data: { imageUrl: string } }>(`${this.apiUrl}/Campaign/Image`, form, {
            headers: { 'Content-Type': 'multipart/form-data' },
        })
    }

    audienceOptions() {
        return axios.get<{ data: CampaignAudienceOptions }>(`${this.apiUrl}/Campaign/Audience/Options`)
    }

    audienceCount(kind: CampaignAudienceKind, config: CampaignAudienceConfig) {
        const params: Record<string, string> = { kind }
        if (config.eventId) params.eventId = config.eventId
        if (config.eventTypeId) params.eventTypeId = config.eventTypeId
        if (config.passProductId) params.passProductId = config.passProductId
        if (config.fromUtc) params.fromUtc = config.fromUtc
        if (config.toUtc) params.toUtc = config.toUtc
        return axios.get<{ data: CampaignAudienceCount }>(`${this.apiUrl}/Campaign/Audience/Count`, { params })
    }

    list() {
        return axios.get<{ data: CampaignListItem[] }>(`${this.apiUrl}/Campaign`)
    }

    get(id: string) {
        return axios.get<{ data: CampaignDetail }>(`${this.apiUrl}/Campaign/${id}`)
    }

    create(req: { subject: string; bodyHtml: string; bodyText?: string | null }) {
        return axios.post<{ data: CampaignDetail }>(`${this.apiUrl}/Campaign`, req)
    }

    update(id: string, req: { subject: string; bodyHtml: string; bodyText?: string | null }) {
        return axios.put<{ data: CampaignDetail }>(`${this.apiUrl}/Campaign/${id}`, req)
    }

    delete(id: string) {
        return axios.delete(`${this.apiUrl}/Campaign/${id}`)
    }

    // scheduledForUtc (ISO) in the future schedules the send; omit/null sends now.
    send(id: string, scheduledForUtc?: string | null) {
        return axios.post<{ data: SendCampaignResponse }>(`${this.apiUrl}/Campaign/${id}/Send`,
            null, { params: scheduledForUtc ? { scheduledForUtc } : undefined })
    }

    unschedule(id: string) {
        return axios.post(`${this.apiUrl}/Campaign/${id}/Unschedule`)
    }
}
