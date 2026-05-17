import axios from 'axios'

export interface FeedbackDto {
    id: string
    name: string
    email: string
    rating: number | null
    body: string
    status: 'new' | 'addressed' | 'dismissed'
    adminNotes: string | null
    userId: string | null
    actionedByUserId: string | null
    actionedAtUtc: string | null
    createdAtUtc: string
}

export interface FeedbackListResponse {
    items: FeedbackDto[]
    total: number
}

export interface SubmitFeedbackRequest {
    name: string
    email: string
    rating: number | null
    body: string
}

export class FeedbackService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    submit(req: SubmitFeedbackRequest) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/Feedback`, req)
    }

    listAdmin(params: { status?: string | null; limit?: number; offset?: number } = {}) {
        return axios.get<{ data: FeedbackListResponse }>(`${this.apiUrl}/Feedback/Admin`, { params })
    }

    updateStatus(id: string, status: 'new' | 'addressed' | 'dismissed', adminNotes: string | null) {
        return axios.put<{ data: FeedbackDto }>(`${this.apiUrl}/Feedback/Admin/${id}/Status`, { status, adminNotes })
    }
}
