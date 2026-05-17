import axios from 'axios'

export interface EventSubscriptionStatus {
    subscribed: boolean
    email: string | null
    phone: string | null
    notifyEmail: boolean
    notifySms: boolean
    tenantDisplayName: string
}

export class EventSubscriptionService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    subscribe(body: { email: string; phone: string | null; notifyEmail: boolean; notifySms: boolean }) {
        return axios.post(`${this.apiUrl}/EventSubscription`, body)
    }
    statusByEmail(email: string) {
        return axios.get<{ data: EventSubscriptionStatus }>(`${this.apiUrl}/EventSubscription/Status`, { params: { email } })
    }
    mine() {
        return axios.get<{ data: EventSubscriptionStatus }>(`${this.apiUrl}/EventSubscription/Mine`)
    }
    unsubscribeStatus(token: string) {
        return axios.get<{ data: EventSubscriptionStatus }>(`${this.apiUrl}/EventSubscription/Unsubscribe/${token}/Status`)
    }
    unsubscribe(token: string) {
        return axios.post(`${this.apiUrl}/EventSubscription/Unsubscribe/${token}`)
    }
    resubscribe(token: string) {
        return axios.post(`${this.apiUrl}/EventSubscription/Resubscribe/${token}`)
    }
}
