import axios from 'axios'

export interface EventDto {
    id: string
    eventTypeId: string
    eventTypeCode: string
    eventTypeName: string
    eventTypeColor: string
    title: string
    description: string | null
    startsAtUtc: string
    endsAtUtc: string
    allDay: boolean
    capacity: number | null
    locationLabel: string | null
    status: 'scheduled' | 'cancelled'
    hasActiveTiers?: boolean
    minTicketPriceCents?: number | null
    spotsReserved?: number | null
}

export interface UpsertEventDto {
    eventTypeId: string
    title: string
    description: string | null
    startsAtUtc: string
    endsAtUtc: string
    allDay: boolean
    capacity: number | null
    locationLabel: string | null
    status: 'scheduled' | 'cancelled'
}

export class EventService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    async list(fromUtc: string, toUtc: string) {
        return axios.get<{ data: EventDto[] }>(`${this.apiUrl}/Event`, { params: { fromUtc, toUtc } })
    }

    async create(req: UpsertEventDto) {
        return axios.post(`${this.apiUrl}/Event`, req)
    }

    async update(id: string, req: UpsertEventDto) {
        return axios.put(`${this.apiUrl}/Event/${id}`, req)
    }

    async delete(id: string) {
        return axios.delete(`${this.apiUrl}/Event/${id}`)
    }

    async duplicate(id: string) {
        return axios.post(`${this.apiUrl}/Event/${id}/Duplicate`)
    }
}
