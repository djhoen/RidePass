import axios from 'axios'

export interface EmailTemplateItem {
    id: string
    name: string
    subject: string | null
    previewText: string | null
    bodyHtml: string
    updatedAtUtc: string
}

export interface UpsertEmailTemplateRequest {
    name: string
    subject: string | null
    previewText: string | null
    bodyHtml: string
}

/** Saved email bodies a track starts campaigns and automation emails from. */
export class EmailTemplateService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    list() {
        return axios.get<{ data: EmailTemplateItem[] }>(`${this.apiUrl}/EmailTemplate`)
    }

    create(req: UpsertEmailTemplateRequest) {
        return axios.post<{ data: EmailTemplateItem }>(`${this.apiUrl}/EmailTemplate`, req)
    }

    update(id: string, req: UpsertEmailTemplateRequest) {
        return axios.put<{ data: EmailTemplateItem }>(`${this.apiUrl}/EmailTemplate/${id}`, req)
    }

    remove(id: string) {
        return axios.delete(`${this.apiUrl}/EmailTemplate/${id}`)
    }
}
