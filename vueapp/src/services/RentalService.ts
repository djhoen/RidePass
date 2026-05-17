import axios from 'axios'

export interface RentalProduct {
    id: string
    name: string
    description: string | null
    imageUrl: string | null
    dailyRateCents: number
    depositCents: number
    trackingKind: 'pool' | 'per_item'
    inventoryPool: number | null
    requiresWaiver: boolean
    riderPaidServiceChargeBps: number
    isActive: boolean
    sortOrder: number
    perItemTotal: number | null
    perItemAvailable: number | null
}

export interface UpsertRentalProduct {
    name: string
    description: string | null
    imageUrl: string | null
    dailyRateCents: number
    depositCents: number
    trackingKind: 'pool' | 'per_item'
    inventoryPool: number | null
    requiresWaiver: boolean
    riderPaidServiceChargeBps: number
    isActive: boolean
    sortOrder: number
}

export interface RentalItem {
    id: string
    productId: string
    label: string
    serial: string | null
    notes: string | null
    status: 'available' | 'maintenance' | 'retired'
}

export interface UpsertRentalItem {
    label: string
    serial: string | null
    notes: string | null
    status: 'available' | 'maintenance' | 'retired'
}

export interface BuyRentalRequest {
    productId: string
    startDate: string  // YYYY-MM-DD
    endDate: string    // YYYY-MM-DD
    quantity: number
    couponCode?: string | null
    giftCardCode?: string | null
}

export interface BuyRentalResponse {
    purchaseId: string
    redemptionToken: string
    clientSecret: string
    amountCents: number
    rentalFeeCents: number
    depositCents: number
    riderServiceChargeCents: number
    giftCardAppliedCents: number
}

export interface MyRental {
    id: string
    redemptionToken: string
    productName: string
    startDate: string
    endDate: string
    quantity: number
    amountCents: number
    depositCents: number
    status: string
    createdAtUtc: string
}

export class RentalService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    listActive() {
        return axios.get<{ data: RentalProduct[] }>(`${this.apiUrl}/Rental/Products`)
    }
    listForAdmin() {
        return axios.get<{ data: RentalProduct[] }>(`${this.apiUrl}/Rental/Products/Admin`)
    }
    createProduct(req: UpsertRentalProduct) {
        return axios.post<{ data: RentalProduct }>(`${this.apiUrl}/Rental/Products`, req)
    }
    updateProduct(id: string, req: UpsertRentalProduct) {
        return axios.put<{ data: RentalProduct }>(`${this.apiUrl}/Rental/Products/${id}`, req)
    }
    deleteProduct(id: string) {
        return axios.delete(`${this.apiUrl}/Rental/Products/${id}`)
    }
    reorderProducts(items: { id: string; sortOrder: number }[]) {
        return axios.post(`${this.apiUrl}/Rental/Products/Reorder`, { items })
    }

    listItems(productId: string) {
        return axios.get<{ data: RentalItem[] }>(`${this.apiUrl}/Rental/Products/${productId}/Items`)
    }
    createItem(productId: string, req: UpsertRentalItem) {
        return axios.post<{ data: RentalItem }>(`${this.apiUrl}/Rental/Products/${productId}/Items`, req)
    }
    updateItem(id: string, req: UpsertRentalItem) {
        return axios.put<{ data: RentalItem }>(`${this.apiUrl}/Rental/Items/${id}`, req)
    }
    deleteItem(id: string) {
        return axios.delete(`${this.apiUrl}/Rental/Items/${id}`)
    }

    buy(req: BuyRentalRequest) {
        return axios.post<{ data: BuyRentalResponse }>(`${this.apiUrl}/Rental/Buy`, req)
    }

    listMine() {
        return axios.get<{ data: MyRental[] }>(`${this.apiUrl}/Rental/Mine`)
    }

    listForCounter(params: { fromUtc?: string; toUtc?: string; status?: string }) {
        return axios.get<{ data: CounterRental[] }>(`${this.apiUrl}/Rental/Counter`, { params })
    }
    markOut(id: string, body?: { items?: PerItemConditionInput[] }) {
        return axios.post(`${this.apiUrl}/Rental/Counter/${id}/MarkOut`, body ?? {})
    }
    markReturned(id: string, body: { conditionNotes?: string | null; depositCapturedCents: number; items?: PerItemConditionInput[] }) {
        return axios.post(`${this.apiUrl}/Rental/Counter/${id}/MarkReturned`, body)
    }

    // Maintenance windows
    listMaintenance(itemId: string) {
        return axios.get<{ data: MaintenanceWindow[] }>(`${this.apiUrl}/Rental/Items/${itemId}/Maintenance`)
    }
    addMaintenance(itemId: string, body: UpsertMaintenance) {
        return axios.post<{ data: MaintenanceWindow }>(`${this.apiUrl}/Rental/Items/${itemId}/Maintenance`, body)
    }
    updateMaintenance(id: string, body: UpsertMaintenance) {
        return axios.put<{ data: MaintenanceWindow }>(`${this.apiUrl}/Rental/Maintenance/${id}`, body)
    }
    deleteMaintenance(id: string) {
        return axios.delete(`${this.apiUrl}/Rental/Maintenance/${id}`)
    }
}

export interface PerItemConditionInput {
    purchaseItemId: string
    photoDataUrl?: string | null
    notes?: string | null
}

export interface AssignedRentalItem {
    purchaseItemId: string
    itemId: string
    label: string | null
    checkoutPhotoDataUrl: string | null
    checkoutNotes: string | null
    returnPhotoDataUrl: string | null
    returnNotes: string | null
}

export interface CounterRental {
    id: string
    redemptionToken: string
    productId: string
    productName: string
    purchaserName: string
    purchaserEmail: string
    startDate: string
    endDate: string
    quantity: number
    amountCents: number
    depositCents: number
    depositCapturedCents: number
    status: string
    checkedOutAtUtc: string | null
    returnedAtUtc: string | null
    assignedItems: AssignedRentalItem[]
}

export interface MaintenanceWindow {
    id: string
    itemId: string
    startsAtDate: string
    endsAtDate: string
    reason: string | null
}

export interface UpsertMaintenance {
    startsAtDate: string
    endsAtDate: string
    reason: string | null
}
