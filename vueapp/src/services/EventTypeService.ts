import axios from 'axios'

export interface EventType {
    id: string
    code: string
    name: string
    color: string
    sortOrder: number
    isSystem: boolean
}

export class EventTypeService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    async list() {
        return axios.get<{ data: EventType[] }>(`${this.apiUrl}/EventType`)
    }

    async create(req: { name: string; color: string; sortOrder?: number }) {
        return axios.post(`${this.apiUrl}/EventType`, req)
    }

    async update(id: string, req: { name: string; color: string; sortOrder?: number }) {
        return axios.put(`${this.apiUrl}/EventType/${id}`, req)
    }

    async delete(id: string) {
        return axios.delete(`${this.apiUrl}/EventType/${id}`)
    }
}
