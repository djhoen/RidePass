import axios from 'axios'

export interface EventType {
    id: string
    code: string
    name: string
    color: string
    imageUrl: string | null
    sortOrder: number
    isSystem: boolean
    allowLoampassRedemption: boolean
    /** The survey asked as the rider self-assessment at checkout (Script0318); null = none. */
    assessmentSurveyId?: string | null
}

export class EventTypeService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    async list() {
        return axios.get<{ data: EventType[] }>(`${this.apiUrl}/EventType`)
    }

    async create(req: { name: string; color: string; imageUrl?: string | null; sortOrder?: number }) {
        return axios.post(`${this.apiUrl}/EventType`, req)
    }

    async update(id: string, req: { name: string; color: string; imageUrl?: string | null; sortOrder?: number }) {
        return axios.put(`${this.apiUrl}/EventType/${id}`, req)
    }

    async delete(id: string) {
        return axios.delete(`${this.apiUrl}/EventType/${id}`)
    }

    async uploadImage(file: File) {
        const form = new FormData()
        form.append('file', file)
        return axios.post<{ data: { imageUrl: string } }>(`${this.apiUrl}/EventType/Image`, form, {
            headers: { 'Content-Type': 'multipart/form-data' },
        })
    }

    async reorder(items: { id: string; sortOrder: number }[]) {
        return axios.post(`${this.apiUrl}/EventType/Reorder`, { items })
    }

    async setAssessmentSurvey(id: string, surveyId: string | null) {
        return axios.put(`${this.apiUrl}/EventType/${id}/AssessmentSurvey`, { surveyId })
    }

    async setLoampassRedemption(id: string, allow: boolean) {
        return axios.put(`${this.apiUrl}/EventType/${id}/LoampassRedemption`, { allow })
    }
}
