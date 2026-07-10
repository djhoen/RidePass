import axios from 'axios'

export interface PageListItem {
    id: string
    title: string
    slug: string
    status: 'draft' | 'published'
    showInNav: boolean
    navLabel: string | null
    sortOrder: number
    heroImageUrl: string | null
    publishedAtUtc: string | null
    createdAtUtc: string
    updatedAtUtc: string
}

export interface PageDetail {
    id: string
    title: string
    slug: string
    bodyHtml: string | null
    heroImageUrl: string | null
    status: 'draft' | 'published'
    showInNav: boolean
    navLabel: string | null
    sortOrder: number
    publishedAtUtc: string | null
    createdAtUtc: string
    updatedAtUtc: string
}

export interface PublicPageResponse {
    title: string
    slug: string
    bodyHtml: string | null
    heroImageUrl: string | null
    publishedAtUtc: string | null
}

export interface UpsertPageRequest {
    title: string
    slug?: string | null
    bodyHtml?: string | null
    heroImageUrl?: string | null
    status: 'draft' | 'published'
    showInNav: boolean
    navLabel?: string | null
    sortOrder: number
}

export class PageService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    // ── Public (tenant resolved by subdomain; 404 when the page doesn't exist / isn't published) ──
    getBySlug(slug: string) {
        return axios.get<{ data: PublicPageResponse }>(`${this.apiUrl}/Page/${encodeURIComponent(slug)}`)
    }

    // ── Admin (settings.manage) ──
    list() {
        return axios.get<{ data: PageListItem[] }>(`${this.apiUrl}/Page/Admin`)
    }

    getAdmin(id: string) {
        return axios.get<{ data: PageDetail }>(`${this.apiUrl}/Page/Admin/${id}`)
    }

    create(req: UpsertPageRequest) {
        return axios.post<{ data: PageDetail }>(`${this.apiUrl}/Page`, req)
    }

    update(id: string, req: UpsertPageRequest) {
        return axios.put<{ data: PageDetail }>(`${this.apiUrl}/Page/${id}`, req)
    }

    remove(id: string) {
        return axios.delete(`${this.apiUrl}/Page/${id}`)
    }

    reorder(items: { id: string; sortOrder: number }[]) {
        return axios.put(`${this.apiUrl}/Page/Reorder`, { items })
    }

    // Hero image and inline-body images share the same upload endpoint.
    uploadImage(file: File) {
        const form = new FormData()
        form.append('file', file)
        return axios.post<{ data: { imageUrl: string } }>(`${this.apiUrl}/Page/Image`, form, {
            headers: { 'Content-Type': 'multipart/form-data' },
        })
    }
}
