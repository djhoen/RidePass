import axios from 'axios'

export interface BlackoutDto {
    id: string
    startsAtUtc: string
    endsAtUtc: string
    allDay: boolean
    reason: string | null
}

export interface UpsertBlackoutDto {
    startsAtUtc: string
    endsAtUtc: string
    allDay: boolean
    reason: string | null
}

export class BlackoutService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    async list(fromUtc: string, toUtc: string) {
        return axios.get<{ data: BlackoutDto[] }>(`${this.apiUrl}/Blackout`, { params: { fromUtc, toUtc } })
    }

    async create(req: UpsertBlackoutDto) {
        return axios.post(`${this.apiUrl}/Blackout`, req)
    }

    async update(id: string, req: UpsertBlackoutDto) {
        return axios.put(`${this.apiUrl}/Blackout/${id}`, req)
    }

    async delete(id: string) {
        return axios.delete(`${this.apiUrl}/Blackout/${id}`)
    }
}
