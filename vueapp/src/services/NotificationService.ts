import axios from 'axios'

export interface AppNotification {
    id: string
    recipientUserId: string
    tenantId: string | null
    kind: string
    title: string
    body: string
    linkUrl: string | null
    isRead: boolean
    createdAt: string
    readAt: string | null
}

export class NotificationService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    list(take = 50) {
        return axios.get<{ data: AppNotification[] }>(`${this.apiUrl}/Notification`, { params: { take } })
    }

    unreadCount() {
        return axios.get<{ data: { count: number } }>(`${this.apiUrl}/Notification/UnreadCount`)
    }

    markRead(id: string) {
        return axios.post(`${this.apiUrl}/Notification/${id}/Read`)
    }

    markAllRead() {
        return axios.post(`${this.apiUrl}/Notification/ReadAll`)
    }

    getCatalog() {
        return axios.get<{ data: NotificationKindDescriptor[] }>(`${this.apiUrl}/Notification/Catalog`)
    }

    getPreferences() {
        return axios.get<{ data: NotificationPreferenceRow[] }>(`${this.apiUrl}/Notification/Preferences`)
    }

    setPreference(kind: string, emailEnabled: boolean) {
        return axios.put(`${this.apiUrl}/Notification/Preferences/${encodeURIComponent(kind)}`, { emailEnabled })
    }
}

export interface NotificationKindDescriptor {
    kind: string
    label: string
    description: string
    audiences: string[]
}

export interface NotificationPreferenceRow {
    id: string
    userId: string
    kind: string
    emailEnabled: boolean
}
