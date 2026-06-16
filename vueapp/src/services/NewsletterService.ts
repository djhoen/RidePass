import axios from 'axios'

export interface SubscriberListItem {
    id: string
    email: string
    name: string | null
    source: string
    subscribedAtUtc: string
    unsubscribedAtUtc: string | null
}

export interface UnsubscribeStatus {
    email: string
    name: string | null
    tenantDisplayName: string
    unsubscribed: boolean
}

export interface ImportResult {
    added: number
    skipped: number
    suppressed: number
}

export class NewsletterService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    // Public
    subscribe(email: string, name: string | null) {
        return axios.post(`${this.apiUrl}/Newsletter/Subscribe`, { email, name })
    }

    getUnsubscribeStatus(token: string) {
        return axios.get<{ data: UnsubscribeStatus }>(`${this.apiUrl}/Newsletter/Unsubscribe/${token}/Status`)
    }

    unsubscribe(token: string) {
        return axios.post(`${this.apiUrl}/Newsletter/Unsubscribe/${token}`)
    }

    resubscribe(token: string) {
        return axios.post(`${this.apiUrl}/Newsletter/Resubscribe/${token}`)
    }

    // Authenticated user
    getMyStatus() {
        return axios.get<{ data: { subscribed: boolean; email: string } }>(`${this.apiUrl}/Newsletter/Me/Status`)
    }

    subscribeMe() {
        return axios.post(`${this.apiUrl}/Newsletter/Me/Subscribe`)
    }

    unsubscribeMe() {
        return axios.post(`${this.apiUrl}/Newsletter/Me/Unsubscribe`)
    }

    // Tenant admin
    listSubscribers(includeUnsubscribed = false) {
        return axios.get<{ data: SubscriberListItem[] }>(`${this.apiUrl}/Newsletter/Admin/Subscribers`, {
            params: { includeUnsubscribed },
        })
    }

    addSubscriber(email: string, name: string | null) {
        return axios.post(`${this.apiUrl}/Newsletter/Admin/Subscribers`, { email, name })
    }

    importSubscribers(rawLines: string, consentConfirmed: boolean) {
        return axios.post<{ data: ImportResult }>(`${this.apiUrl}/Newsletter/Admin/Subscribers/Import`,
            { rawLines, consentConfirmed })
    }

    deleteSubscriber(id: string) {
        return axios.delete(`${this.apiUrl}/Newsletter/Admin/Subscribers/${id}`)
    }

    getActiveCount() {
        return axios.get<{ data: { count: number } }>(`${this.apiUrl}/Newsletter/Admin/ActiveCount`)
    }
}
