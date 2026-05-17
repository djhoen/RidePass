import axios from 'axios'

export interface CustomerSummaryDto {
    userId: string
    email: string
    firstName: string
    lastName: string
    birthdate: string | null
    lastActivityAt: string | null
    totalPurchases: number
    totalSpentCents: number
    hasWaiverSigned: boolean
}

export interface CustomerListResponseDto {
    items: CustomerSummaryDto[]
    total: number
    limit: number
    offset: number
}

export interface CustomerUserDto {
    id: string
    tenantId: string | null
    email: string
    firstName: string
    lastName: string
    role: string
    status: string
    birthdate: string | null
    emergencyContactName: string | null
    emergencyContactPhone: string | null
}

export interface CustomerPassDto {
    id: string
    productId: string
    validOnDate: string | null
    amountCents: number
    serviceChargeCents: number
    status: string
    createdAt: string
}

export interface CustomerEventTicketDto {
    id: string
    tierId: string
    amountCents: number
    status: string
    createdAt: string
}

export interface CustomerSeasonPassDto {
    id: string
    productId: string
    amountCents: number
    serviceChargeCents: number
    status: string
    validFromDate: string
    validToDate: string
    creditsRemaining: number | null
    createdAt: string
}

export interface CustomerWaiverDto {
    id: string
    waiverId: string
    waiverTitle: string
    waiverVersion: number
    signedAt: string
    ipAddress: string | null
    signatureDataUrl: string | null
    signedByParent: boolean
    parentName: string | null
    parentPhone: string | null
}

export interface CustomerDetailDto {
    user: CustomerUserDto
    passes: CustomerPassDto[]
    eventTickets: CustomerEventTicketDto[]
    seasonPasses: CustomerSeasonPassDto[]
    waiverSignatures: CustomerWaiverDto[]
}

export interface TopRiderDto {
    userId: string
    firstName: string
    lastName: string
    email: string
    days: number
    spentCents: number
}

export class CustomerService {
    private apiUrl: string

    constructor() {
        this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? ''
    }

    async list(search?: string, limit = 50, offset = 0) {
        return axios.get<{ data: CustomerListResponseDto }>(`${this.apiUrl}/Customer`, {
            params: { search, limit, offset },
        })
    }

    async getDetail(userId: string) {
        return axios.get<{ data: CustomerDetailDto }>(`${this.apiUrl}/Customer/${userId}`)
    }

    async topRiders(metric: 'days' | 'spent' = 'days', period: 'month' | 'year' = 'month', limit = 10) {
        return axios.get<{ data: TopRiderDto[] }>(`${this.apiUrl}/Customer/top-riders`, {
            params: { metric, period, limit },
        })
    }
}
