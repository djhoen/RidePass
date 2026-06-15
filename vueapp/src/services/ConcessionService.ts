import axios from 'axios'

export const CONCESSION_CATEGORIES = [
    { value: 'food', label: 'Food', icon: 'mdi-food' },
    { value: 'drink', label: 'Drink', icon: 'mdi-cup' },
    { value: 'swag', label: 'Swag', icon: 'mdi-tshirt-crew' },
    { value: 'other', label: 'Other', icon: 'mdi-package-variant' },
] as const

export function categoryLabel(c: string): string {
    return CONCESSION_CATEGORIES.find(x => x.value === c)?.label ?? 'Other'
}
export function categoryIcon(c: string): string {
    return CONCESSION_CATEGORIES.find(x => x.value === c)?.icon ?? 'mdi-package-variant'
}

export interface ConcessionVariant {
    id: string
    productId: string
    size: string | null
    color: string | null
    priceCents: number | null   // null = inherit product price
    imageUrl: string | null
    inventory: number | null     // null = unlimited
    sold: number
    remaining: number            // -1 if unlimited
    isActive: boolean
    sortOrder: number
}

export interface ConcessionProduct {
    id: string
    name: string
    description: string | null
    category: string
    priceCents: number
    imageUrl: string | null
    isActive: boolean
    sortOrder: number
    variants: ConcessionVariant[]
}

export interface UpsertConcessionProduct {
    name: string
    description: string | null
    category: string
    priceCents: number
    imageUrl: string | null
    isActive: boolean
    sortOrder: number
}

export interface UpsertConcessionVariant {
    size: string | null
    color: string | null
    priceCents: number | null
    imageUrl: string | null
    inventory: number | null
    isActive: boolean
    sortOrder: number
}

export class ConcessionService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    // ── Admin: products ──────────────────────────────────────────────
    listForAdmin() {
        return axios.get<{ data: ConcessionProduct[] }>(`${this.apiUrl}/Concession/Products/Admin`)
    }
    create(req: UpsertConcessionProduct) {
        return axios.post<{ data: ConcessionProduct }>(`${this.apiUrl}/Concession/Products`, req)
    }
    update(id: string, req: UpsertConcessionProduct) {
        return axios.put<{ data: ConcessionProduct }>(`${this.apiUrl}/Concession/Products/${id}`, req)
    }
    remove(id: string) {
        return axios.delete(`${this.apiUrl}/Concession/Products/${id}`)
    }
    reorder(items: { id: string; sortOrder: number }[]) {
        return axios.post(`${this.apiUrl}/Concession/Products/Reorder`, { items })
    }
    uploadImage(file: File) {
        const form = new FormData()
        form.append('file', file)
        return axios.post<{ data: { imageUrl: string } }>(`${this.apiUrl}/Concession/Image`, form, {
            headers: { 'Content-Type': 'multipart/form-data' },
        })
    }

    // ── Admin: variants ──────────────────────────────────────────────
    createVariant(productId: string, req: UpsertConcessionVariant) {
        return axios.post<{ data: ConcessionVariant }>(`${this.apiUrl}/Concession/Products/${productId}/Variants`, req)
    }
    updateVariant(productId: string, variantId: string, req: UpsertConcessionVariant) {
        return axios.put<{ data: ConcessionVariant }>(`${this.apiUrl}/Concession/Products/${productId}/Variants/${variantId}`, req)
    }
    removeVariant(productId: string, variantId: string) {
        return axios.delete(`${this.apiUrl}/Concession/Products/${productId}/Variants/${variantId}`)
    }
}
