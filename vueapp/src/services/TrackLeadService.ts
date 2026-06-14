import axios from 'axios'

export interface SubmitTrackLeadRequest {
    contactName: string
    trackName: string
    email: string
    phone?: string
    message?: string
}

export class TrackLeadService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    submit(req: SubmitTrackLeadRequest) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/TrackLead`, req)
    }
}
