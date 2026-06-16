import axios from 'axios'

export interface BlogPostImageDto {
    id: string
    imageUrl: string
    caption: string | null
    sortOrder: number
}

export interface BlogPostListItem {
    id: string
    title: string
    slug: string
    status: 'draft' | 'published'
    isFeatured: boolean
    mainImageUrl: string | null
    excerpt: string | null
    imageCount: number
    publishedAtUtc: string | null
    createdAtUtc: string
    updatedAtUtc: string
}

export interface BlogPostDetail {
    id: string
    title: string
    slug: string
    excerpt: string | null
    bodyHtml: string | null
    mainImageUrl: string | null
    status: 'draft' | 'published'
    isFeatured: boolean
    publishedAtUtc: string | null
    createdAtUtc: string
    updatedAtUtc: string
    images: BlogPostImageDto[]
}

export interface PublicBlogListItem {
    title: string
    slug: string
    excerpt: string | null
    mainImageUrl: string | null
    publishedAtUtc: string | null
}

export interface UpsertBlogPostRequest {
    title: string
    slug?: string | null
    excerpt?: string | null
    bodyHtml?: string | null
    mainImageUrl?: string | null
    status: 'draft' | 'published'
}

export class BlogService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    // ── Public (tenant resolved by subdomain; 404 when the blog is turned off) ──
    listPublic() {
        return axios.get<{ data: PublicBlogListItem[] }>(`${this.apiUrl}/Blog`)
    }

    getBySlug(slug: string) {
        return axios.get<{ data: BlogPostDetail }>(`${this.apiUrl}/Blog/${encodeURIComponent(slug)}`)
    }

    getFeatured() {
        return axios.get<{ data: BlogPostDetail | null }>(`${this.apiUrl}/Blog/Featured`)
    }

    // ── Admin (blog.manage) ──
    listAdmin() {
        return axios.get<{ data: BlogPostListItem[] }>(`${this.apiUrl}/Blog/Admin`)
    }

    getAdmin(id: string) {
        return axios.get<{ data: BlogPostDetail }>(`${this.apiUrl}/Blog/Admin/${id}`)
    }

    create(req: UpsertBlogPostRequest) {
        return axios.post<{ data: BlogPostDetail }>(`${this.apiUrl}/Blog`, req)
    }

    update(id: string, req: UpsertBlogPostRequest) {
        return axios.put<{ data: BlogPostDetail }>(`${this.apiUrl}/Blog/${id}`, req)
    }

    delete(id: string) {
        return axios.delete(`${this.apiUrl}/Blog/${id}`)
    }

    setFeatured(id: string, featured: boolean) {
        return axios.put<{ data: BlogPostDetail }>(`${this.apiUrl}/Blog/${id}/Featured`, { featured })
    }

    // Main cover image — upload first, then save the returned URL on the post.
    uploadMainImage(file: File) {
        const form = new FormData()
        form.append('file', file)
        return axios.post<{ data: { imageUrl: string } }>(`${this.apiUrl}/Blog/Image`, form, {
            headers: { 'Content-Type': 'multipart/form-data' },
        })
    }

    // Gallery ("several other images") for an existing post.
    addImage(postId: string, file: File, caption: string | null, sortOrder: number) {
        const form = new FormData()
        form.append('file', file)
        if (caption) form.append('caption', caption)
        form.append('sortOrder', String(sortOrder))
        return axios.post<{ data: BlogPostImageDto }>(`${this.apiUrl}/Blog/${postId}/Images`, form, {
            headers: { 'Content-Type': 'multipart/form-data' },
        })
    }

    updateImage(imageId: string, caption: string | null) {
        return axios.put<{ data: BlogPostImageDto }>(`${this.apiUrl}/Blog/Images/${imageId}`, { caption })
    }

    deleteImage(imageId: string) {
        return axios.delete(`${this.apiUrl}/Blog/Images/${imageId}`)
    }

    reorderImages(postId: string, items: { id: string; sortOrder: number }[]) {
        return axios.post(`${this.apiUrl}/Blog/${postId}/Images/Reorder`, { items })
    }
}
