import axios from 'axios'

export interface ConversationListItem {
    id: string
    customerPhone: string
    lastMessageAtUtc: string
    lastInboundAtUtc: string | null
    lastReadAtUtc: string | null
    status: 'active' | 'archived'
    unread: boolean
    optedOut: boolean
    customerUserId: string | null
    customerName: string | null
}

export interface MessageDto {
    id: string
    direction: 'inbound' | 'outbound'
    body: string
    status: string
    numSegments: number | null
    errorCode: string | null
    errorMessage: string | null
    createdAtUtc: string
}

export interface ConversationDetail {
    id: string
    customerPhone: string
    lastMessageAtUtc: string
    lastInboundAtUtc: string | null
    lastReadAtUtc: string | null
    status: 'active' | 'archived'
    optedOut: boolean
    customerUserId: string | null
    customerName: string | null
    messages: MessageDto[]
}

export class InboxService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    list(includeArchived = false) {
        return axios.get<{ data: ConversationListItem[] }>(
            `${this.apiUrl}/TenantConversation`,
            { params: { includeArchived } })
    }

    get(id: string) {
        return axios.get<{ data: ConversationDetail }>(
            `${this.apiUrl}/TenantConversation/${id}`)
    }

    reply(id: string, body: string) {
        return axios.post<{ data: { sent: boolean } }>(
            `${this.apiUrl}/TenantConversation/${id}/Reply`, { body })
    }

    markRead(id: string) {
        return axios.post(`${this.apiUrl}/TenantConversation/${id}/MarkRead`)
    }

    setStatus(id: string, status: 'active' | 'archived') {
        return axios.post(`${this.apiUrl}/TenantConversation/${id}/Status`, { status })
    }
}
