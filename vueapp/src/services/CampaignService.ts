import axios from 'axios'

export interface CampaignListItem {
    id: string
    subject: string
    status: 'draft' | 'scheduled' | 'sending' | 'sent' | 'failed'
    recipientCount: number
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

    send(id: string) {
        return axios.post<{ data: SendCampaignResponse }>(`${this.apiUrl}/Campaign/${id}/Send`)
    }
}
