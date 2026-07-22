import axios from 'axios'

// ── Types ────────────────────────────────────────────────────────────────────
export type ShopTrackingKind = 'pool' | 'serialized'
export type ShopItemStatus = 'available' | 'rented_out' | 'sold' | 'maintenance' | 'retired'

export interface ShopCategory {
    id: string
    name: string
    parentId: string | null
    sortOrder: number
    isActive: boolean
}

export interface ShopSupplier {
    id: string
    name: string
    contactName: string | null
    email: string | null
    phone: string | null
    notes: string | null
    isActive: boolean
}

export interface ShopTaxCategory {
    id: string
    name: string
    rateBps: number
    isDefault: boolean
    sortOrder: number
    isActive: boolean
}

export interface ShopVariant {
    id: string
    productId: string
    sku: string | null
    barcode: string | null
    size: string | null
    color: string | null
    gender: string | null
    salePriceCents: number | null
    msrpCents: number | null
    dailyRateCents: number | null
    depositCents: number
    costCents: number | null
    mpn: string | null
    trackingKind: ShopTrackingKind
    stockOnHand: number
    availableCount: number
    lowStockThreshold: number | null
    reorderPoint: number | null
    reorderLevel: number | null
    vendorPartNumber: string | null
    isActive: boolean
}

// Header aggregates for the catalog list, computed over the whole filtered set (not just the
// visible page): what the stock on hand is worth at retail and at cost, how many products are
// low, and how many units are on order but not yet received.
export interface ShopCatalogTotals {
    stockRetailValueCents: number
    stockCostValueCents: number
    lowStockCount: number
    unitsOnPo: number
}

export interface ShopProduct {
    id: string
    categoryId: string | null
    supplierId: string | null
    name: string
    description: string | null
    brand: string | null
    imageUrl: string | null
    isSellable: boolean
    isPublished: boolean
    isRentable: boolean
    isActive: boolean
    sortOrder: number
    variants: ShopVariant[]
}

export interface ShopItem {
    id: string
    variantId: string
    label: string
    serial: string | null
    notes: string | null
    status: ShopItemStatus
    acquiredCostCents: number | null
}

export interface ShopStockMovement {
    id: string
    variantId: string
    itemId: string | null
    delta: number
    reason: string
    referenceKind: string | null
    referenceId: string | null
    unitCostCents: number | null
    note: string | null
    createdAt: string
}

export interface ShopPoLine {
    id: string
    poId: string
    variantId: string
    quantityOrdered: number
    quantityReceived: number
    unitCostCents: number
}

export interface ShopPurchaseOrder {
    id: string
    supplierId: string | null
    reference: string | null
    status: 'open' | 'ordered' | 'partial' | 'received' | 'cancelled'
    notes: string | null
    orderedAt: string | null
    expectedAt: string | null
    receivedAt: string | null
    createdAt: string
    lines?: ShopPoLine[]
}

// Request shapes
export interface UpsertShopProduct {
    name: string
    description: string | null
    brand: string | null
    imageUrl: string | null
    categoryId: string | null
    supplierId: string | null
    isSellable: boolean
    isPublished: boolean
    isRentable: boolean
    isActive: boolean
    sortOrder: number
}

export interface UpsertShopVariant {
    sku: string | null
    barcode: string | null
    size: string | null
    color: string | null
    gender: string | null
    salePriceCents: number | null
    msrpCents: number | null
    dailyRateCents: number | null
    depositCents: number
    costCents: number | null
    mpn: string | null
    trackingKind: ShopTrackingKind
    lowStockThreshold: number | null
    reorderPoint: number | null
    reorderLevel: number | null
    vendorPartNumber: string | null
    isActive: boolean
}

export interface ShopReorderRow {
    variantId: string
    productId: string
    productName: string
    variantLabel: string | null
    sku: string | null
    vendorPartNumber: string | null
    supplierId: string | null
    supplierName: string | null
    available: number
    reorderPoint: number
    reorderLevel: number | null
    costCents: number | null
    suggestedQty: number
}

export interface RingUpLine {
    variantId: string
    quantity: number
    itemId?: string | null
}

export interface RingUpResult {
    saleId: string
    receiptToken: string
    status: 'paid' | 'pending'
    orderNumber?: number
    totalCents: number
    discountCents?: number
    creditAppliedCents?: number
    giftCardAppliedCents?: number
    dueCents?: number
    clientSecret?: string
    paymentIntentId?: string
    cardPresent?: boolean
}

// ── Service ──────────────────────────────────────────────────────────────────
// The signed-in rider's own shop rentals (My Passes). Lesson bikes booked online land here.
export interface MyShopRental {
    id: string
    startsAt: string
    endsAt: string
    status: string
    totalCents: number
    depositCents: number
    depositCapturedCents: number
    orderNumber: number | null
    eventId: string | null
    lines: { nameSnapshot: string; variantLabel: string | null; quantity: number }[]
}

// A condition photo on a work order or a rental. Exactly one owner id is set.
// stage: 'intake' = what arrived / went out, 'return' = what came back (rentals only,
// the evidence behind a damage capture), 'progress' = what a tech found mid-repair.
export interface ShopConditionPhoto {
    id: string
    workOrderId: string | null
    rentalId: string | null
    stage: 'intake' | 'return' | 'progress'
    imageUrl: string
    caption: string | null
    uploadedByUserId: string | null
    sortOrder: number
    createdAt: string
}

// A versioned document customers sign: the rental agreement, or authorization to do a repair.
export interface ShopAgreement {
    id: string
    kind: 'rental_agreement' | 'work_order_terms'
    version: number
    title: string
    body: string
    isActive: boolean
}

export interface ShopAgreementSignature {
    id: string
    agreementId: string
    workOrderId: string | null
    rentalId: string | null
    agreementVersion: number
    signerName: string
    signerEmail: string | null
    signatureDataUrl: string
    signedAt: string
}

// What a rental still needs before the gear can leave the counter.
export interface RentalSigner {
    signatureId: string
    riderName: string | null
    signedByParent: boolean
    parentName: string | null
    signedAtUtc: string
}

export interface RentalCheckoutReadiness {
    agreementRequired: boolean
    agreementSigned: boolean
    waiverRequired: boolean
    // True only when EVERY rider has signed, not merely one.
    waiverSigned: boolean
    ridersRequired: number
    ridersSigned: number
    ridersOutstanding: number
    signers: RentalSigner[]
    canCheckOut: boolean
}

// A saved standard repair job: its labor and parts, ready to drop onto a work order.
export interface ShopJobTemplateLine {
    id?: string
    lineKind: 'labor' | 'part'
    description: string | null
    variantId: string | null
    quantity: number
    // Labor: the rate. Parts: null resolves the variant's CURRENT price when applied.
    unitPriceCents: number | null
    // Labor: standard time (minutes) that auto-fills the estimate when the job is applied.
    estimatedMinutes?: number | null
}

export interface ShopJobTemplate {
    id: string
    name: string
    fitsNote: string | null
    notes: string | null
    isActive: boolean
    sortOrder: number
    lines: ShopJobTemplateLine[]
}

export class BikeShopService {
    private apiUrl: string
    constructor() { this.apiUrl = import.meta.env.VITE_API_ENDPOINT ?? '' }

    // Categories
    listCategories(activeOnly = false) {
        return axios.get<{ data: ShopCategory[] }>(`${this.apiUrl}/BikeShop/Categories?activeOnly=${activeOnly}`)
    }
    createCategory(req: Partial<ShopCategory>) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/BikeShop/Categories`, req)
    }
    updateCategory(id: string, req: Partial<ShopCategory>) {
        return axios.put(`${this.apiUrl}/BikeShop/Categories/${id}`, req)
    }
    deleteCategory(id: string) {
        return axios.delete(`${this.apiUrl}/BikeShop/Categories/${id}`)
    }

    // Suppliers
    listSuppliers(activeOnly = false) {
        return axios.get<{ data: ShopSupplier[] }>(`${this.apiUrl}/BikeShop/Suppliers?activeOnly=${activeOnly}`)
    }
    createSupplier(req: Partial<ShopSupplier>) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/BikeShop/Suppliers`, req)
    }
    updateSupplier(id: string, req: Partial<ShopSupplier>) {
        return axios.put(`${this.apiUrl}/BikeShop/Suppliers/${id}`, req)
    }

    // Tax categories
    listTaxCategories(activeOnly = false) {
        return axios.get<{ data: ShopTaxCategory[] }>(`${this.apiUrl}/BikeShop/TaxCategories?activeOnly=${activeOnly}`)
    }
    createTaxCategory(req: Partial<ShopTaxCategory>) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/BikeShop/TaxCategories`, req)
    }
    updateTaxCategory(id: string, req: Partial<ShopTaxCategory>) {
        return axios.put(`${this.apiUrl}/BikeShop/TaxCategories/${id}`, req)
    }

    // Products + variants
    listProducts(activeOnly = false) {
        return axios.get<{ data: ShopProduct[] }>(`${this.apiUrl}/BikeShop/Products?activeOnly=${activeOnly}`)
    }
    // One page of the catalog. `search` matches product name/brand and any variant's SKU or
    // barcode, case-insensitively, so the same box serves typing a name or scanning a code.
    // sellable/rentable split the retail catalog from the rental fleet; a product flagged both
    // appears in each.
    searchProducts(params: {
        search?: string | null
        categoryId?: string | null
        supplierId?: string | null
        activeOnly?: boolean
        sellable?: boolean | null
        rentable?: boolean | null
        lowStockOnly?: boolean
        page?: number
        pageSize?: number
    }) {
        return axios.get<{ data: { rows: ShopProduct[]; total: number; totals: ShopCatalogTotals } }>(
            `${this.apiUrl}/BikeShop/Products/Page`, { params })
    }
    getProduct(id: string) {
        return axios.get<{ data: ShopProduct }>(`${this.apiUrl}/BikeShop/Products/${id}`)
    }
    createProduct(req: UpsertShopProduct) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/BikeShop/Products`, req)
    }
    updateProduct(id: string, req: UpsertShopProduct) {
        return axios.put(`${this.apiUrl}/BikeShop/Products/${id}`, req)
    }
    createVariant(productId: string, req: UpsertShopVariant) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/BikeShop/Products/${productId}/Variants`, req)
    }
    updateVariant(id: string, req: UpsertShopVariant) {
        return axios.put(`${this.apiUrl}/BikeShop/Variants/${id}`, req)
    }

    // Serialized items
    listItems(variantId: string) {
        return axios.get<{ data: ShopItem[] }>(`${this.apiUrl}/BikeShop/Variants/${variantId}/Items`)
    }
    createItem(variantId: string, req: Partial<ShopItem>) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/BikeShop/Variants/${variantId}/Items`, req)
    }
    updateItem(id: string, req: Partial<ShopItem>) {
        return axios.put(`${this.apiUrl}/BikeShop/Items/${id}`, req)
    }

    // Stock
    adjustStock(variantId: string, req: { delta: number; note: string }) {
        return axios.post<{ data: { stockOnHand: number } }>(`${this.apiUrl}/BikeShop/Variants/${variantId}/AdjustStock`, req)
    }
    listMovements(variantId: string, limit = 100) {
        return axios.get<{ data: ShopStockMovement[] }>(`${this.apiUrl}/BikeShop/Variants/${variantId}/Movements?limit=${limit}`)
    }

    // Purchase orders
    listPurchaseOrders() {
        return axios.get<{ data: ShopPurchaseOrder[] }>(`${this.apiUrl}/BikeShop/PurchaseOrders`)
    }
    getPurchaseOrder(id: string) {
        return axios.get<{ data: ShopPurchaseOrder }>(`${this.apiUrl}/BikeShop/PurchaseOrders/${id}`)
    }
    createPurchaseOrder(req: Partial<ShopPurchaseOrder>) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/BikeShop/PurchaseOrders`, req)
    }
    // ── Reorder worklist ─────────────────────────────────────────────────────
    reorderWorklist() {
        return axios.get<{ data: ShopReorderRow[] }>(`${this.apiUrl}/BikeShop/Reorder`)
    }
    createReorderPo(req: {
        supplierId: string | null; reference?: string | null; expectedAt?: string | null
        lines: { variantId: string; quantityOrdered: number; unitCostCents: number | null }[]
    }) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/BikeShop/Reorder/PurchaseOrder`, req)
    }
    updatePurchaseOrder(id: string, req: Partial<ShopPurchaseOrder>) {
        return axios.put(`${this.apiUrl}/BikeShop/PurchaseOrders/${id}`, req)
    }
    addPurchaseOrderLine(id: string, req: { variantId: string; quantityOrdered: number; unitCostCents: number }) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/BikeShop/PurchaseOrders/${id}/Lines`, req)
    }
    receivePurchaseOrderLine(lineId: string, req: { quantity: number; serialUnits?: { label: string; serial: string | null }[] | null }) {
        return axios.post(`${this.apiUrl}/BikeShop/PurchaseOrderLines/${lineId}/Receive`, req)
    }

    // Register
    ringUp(req: { lines: RingUpLine[]; paymentMethod: 'cash' | 'card'; buyerName?: string | null; buyerEmail?: string | null; couponCode?: string | null; giftCardCode?: string | null; tipCents?: number; creditAccountId?: string | null; creditCents?: number; cardPresent?: boolean }) {
        return axios.post<{ data: RingUpResult }>(`${this.apiUrl}/BikeShopRegister/Sale`, req)
    }
    shopTerminalToken() {
        return axios.post<{ data: { secret: string; locationId: string } }>(`${this.apiUrl}/BikeShopRegister/Terminal/ConnectionToken`)
    }
    getRental(id: string) {
        return axios.get<{ data: ShopRental }>(`${this.apiUrl}/BikeShopRental/Rentals/${id}`)
    }

    // ── Job templates ─────────────────────────────────────────────────────────
    listJobTemplates(activeOnly = false) {
        return axios.get<{ data: ShopJobTemplate[] }>(
            `${this.apiUrl}/BikeShop/JobTemplates?activeOnly=${activeOnly}`)
    }
    saveJobTemplate(req: {
        id?: string | null; name: string; fitsNote?: string | null; notes?: string | null
        isActive: boolean; sortOrder: number
        lines: Array<{ lineKind: string; description?: string | null; variantId?: string | null; quantity: number; unitPriceCents?: number | null; estimatedMinutes?: number | null }>
    }) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/BikeShop/JobTemplates`, req)
    }
    deleteJobTemplate(id: string) {
        return axios.delete(`${this.apiUrl}/BikeShop/JobTemplates/${id}`)
    }
    applyJobTemplate(workOrderId: string, templateId: string) {
        return axios.post<{ data: { added: number; skipped: string[] } }>(
            `${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${workOrderId}/ApplyJobTemplate/${templateId}`)
    }

    // ── Agreements ────────────────────────────────────────────────────────────
    // Counter side (ShopCounter): read the current text and capture a signature.
    getAgreementForSigning(kind: string, params: { workOrderId?: string; rentalId?: string }) {
        const qs = new URLSearchParams()
        if (params.workOrderId) qs.set('workOrderId', params.workOrderId)
        if (params.rentalId) qs.set('rentalId', params.rentalId)
        return axios.get<{ data: { agreement: ShopAgreement | null; signatures: ShopAgreementSignature[] } }>(
            `${this.apiUrl}/BikeShopPhoto/Agreement/${kind}?${qs.toString()}`)
    }
    signAgreement(kind: string, req: {
        workOrderId?: string | null; rentalId?: string | null
        signerName: string; signerEmail?: string | null; signatureDataUrl: string
    }) {
        return axios.post<{ data: { id: string; agreementVersion: number } }>(
            `${this.apiUrl}/BikeShopPhoto/Agreement/${kind}/Sign`, req)
    }
    // ── Public rental signing (emailed link; token is the credential) ─────────
    getRentalSigning(token: string) {
        return axios.get<{ data: any }>(`${this.apiUrl}/ShopRentalSigning/${token}`)
    }
    signRentalAgreementPublic(token: string, req: { signerName: string; signerEmail?: string | null; signatureDataUrl: string }) {
        return axios.post(`${this.apiUrl}/ShopRentalSigning/${token}/SignAgreement`, req)
    }
    signRentalWaiverPublic(token: string, req: {
        firstName: string; lastName: string; email?: string | null; birthdate?: string | null
        signatureDataUrl: string; signedByParent: boolean
        parentName?: string | null; parentPhone?: string | null
    }) {
        return axios.post(`${this.apiUrl}/ShopRentalSigning/${token}/SignWaiver`, req)
    }
    sendRentalSigningLink(rentalId: string) {
        return axios.post(`${this.apiUrl}/BikeShopRental/Rentals/${rentalId}/SendSigningLink`)
    }

    // Counter capture of the track's liability waiver, for walk-ins with no account.
    signRentalWaiver(rentalId: string, req: {
        firstName: string; lastName: string; email?: string | null; birthdate?: string | null
        signatureDataUrl: string; signedByParent: boolean
        parentName?: string | null; parentPhone?: string | null
    }) {
        return axios.post(`${this.apiUrl}/BikeShopRental/Rentals/${rentalId}/SignWaiver`, req)
    }
    rentalReadiness(rentalId: string) {
        return axios.get<{ data: RentalCheckoutReadiness }>(
            `${this.apiUrl}/BikeShopRental/Rentals/${rentalId}/Readiness`)
    }
    // Admin side (CatalogManage): write the terms.
    getAgreement(kind: string) {
        return axios.get<{ data: ShopAgreement | null }>(`${this.apiUrl}/BikeShop/Agreements/${kind}`)
    }
    publishAgreement(kind: string, req: { title: string; body: string }) {
        return axios.post(`${this.apiUrl}/BikeShop/Agreements/${kind}`, req)
    }

    // ── Condition photos ──────────────────────────────────────────────────────
    listWorkOrderPhotos(workOrderId: string) {
        return axios.get<{ data: ShopConditionPhoto[] }>(`${this.apiUrl}/BikeShopPhoto/WorkOrder/${workOrderId}`)
    }
    listRentalPhotos(rentalId: string) {
        return axios.get<{ data: ShopConditionPhoto[] }>(`${this.apiUrl}/BikeShopPhoto/Rental/${rentalId}`)
    }
    uploadWorkOrderPhoto(workOrderId: string, file: File, stage = 'intake', caption?: string) {
        return this.uploadPhoto(`${this.apiUrl}/BikeShopPhoto/WorkOrder/${workOrderId}`, file, stage, caption)
    }
    uploadRentalPhoto(rentalId: string, file: File, stage = 'intake', caption?: string) {
        return this.uploadPhoto(`${this.apiUrl}/BikeShopPhoto/Rental/${rentalId}`, file, stage, caption)
    }
    private uploadPhoto(url: string, file: File, stage: string, caption?: string) {
        const form = new FormData()
        form.append('file', file)
        const qs = new URLSearchParams({ stage })
        if (caption) qs.set('caption', caption)
        return axios.post<{ data: { id: string; imageUrl: string; stage: string } }>(
            `${url}?${qs.toString()}`, form,
            { headers: { 'Content-Type': 'multipart/form-data' } })
    }
    deleteConditionPhoto(id: string) {
        return axios.delete(`${this.apiUrl}/BikeShopPhoto/${id}`)
    }

    myRentals() {
        return axios.get<{ data: MyShopRental[] }>(`${this.apiUrl}/BikeShopRental/Mine`)
    }
    refundSale(saleId: string, req: { restock: boolean; destination: 'original' | 'credit'; note?: string | null }) {
        return axios.post<{ data: { status: string; restocked: boolean; destination: string; creditedCents: number } }>(
            `${this.apiUrl}/BikeShopRegister/Sale/${saleId}/Refund`, req)
    }
    // Finalize a card sale right away (kind-agnostic; the finalizer's shop_sale branch marks it
    // paid, depletes stock, and writes the ledger). Idempotent with the webhook.
    confirmIntent(paymentIntentId: string) {
        return axios.post(`${this.apiUrl}/Payment/ConfirmIntent`, { paymentIntentId })
    }

    // ── Rentals ──────────────────────────────────────────────────────────────
    rentalAvailability(variantId: string, startsAt: string, endsAt: string) {
        return axios.get<{ data: { available: number; units: ShopItem[] } }>(
            `${this.apiUrl}/BikeShopRental/Availability?variantId=${variantId}&startsAt=${encodeURIComponent(startsAt)}&endsAt=${encodeURIComponent(endsAt)}`)
    }
    listRentals(activeOnly = true, limit = 100) {
        return axios.get<{ data: ShopRental[] }>(`${this.apiUrl}/BikeShopRental/Rentals?activeOnly=${activeOnly}&limit=${limit}`)
    }
    bookRental(req: {
        lines: { variantId: string; quantity: number; itemId?: string | null }[]
        startsAt: string; endsAt: string; paymentMethod: 'cash' | 'card'; takeDepositHold?: boolean
        // How many riders must each sign the waiver. Omit to let the server default it from the
        // largest line quantity (two bikes = two riders; a bike + helmet is still one).
        ridersRequired?: number | null
        renterName?: string | null; renterEmail?: string | null; renterPhone?: string | null
    }) {
        return axios.post<{ data: BookRentalResult }>(`${this.apiUrl}/BikeShopRental/Rentals`, req)
    }
    checkOutRental(id: string) {
        return axios.post(`${this.apiUrl}/BikeShopRental/Rentals/${id}/CheckOut`)
    }
    returnRental(id: string, req: { depositCapturedCents: number; conditionNotes?: string | null }) {
        return axios.post<{ data: { depositCapturedCents: number } }>(`${this.apiUrl}/BikeShopRental/Rentals/${id}/Return`, req)
    }
    cancelRental(id: string) {
        return axios.post(`${this.apiUrl}/BikeShopRental/Rentals/${id}/Cancel`)
    }

    // ── Work orders ──────────────────────────────────────────────────────────
    // ── Customer bikes ───────────────────────────────────────────────────────
    // Resolve a serial before creating anything: known bike (with history), a unit we sold
    // (a suggestion to accept), or unknown.
    lookupBike(serial: string) {
        return axios.get<{ data: ShopBikeLookupResult }>(
            `${this.apiUrl}/BikeShopWorkOrder/Bikes/Lookup`, { params: { serial } })
    }
    listCustomerBikes(params: { customerUserId?: string | null; phone?: string | null }) {
        return axios.get<{ data: ShopCustomerBike[] }>(
            `${this.apiUrl}/BikeShopWorkOrder/Bikes`, { params })
    }
    bikeHistory(bikeId: string) {
        return axios.get<{ data: ShopBikeHistoryRow[] }>(
            `${this.apiUrl}/BikeShopWorkOrder/Bikes/${bikeId}/History`)
    }
    // Find-or-create on serial: an existing serial updates that bike rather than forking it.
    upsertBike(req: Partial<ShopCustomerBike> & { id?: string | null }) {
        return axios.post<{ data: { id: string; displayName: string; created: boolean } }>(
            `${this.apiUrl}/BikeShopWorkOrder/Bikes`, req)
    }

    // ── Inspections: checklist templates ─────────────────────────────────────
    listInspectionTemplates() {
        return axios.get<{ data: ShopInspectionTemplate[] }>(
            `${this.apiUrl}/BikeShopInspection/Templates`)
    }
    createInspectionTemplate(req: { name: string; isActive?: boolean; makeDefault?: boolean }) {
        return axios.post<{ data: { id: string } }>(
            `${this.apiUrl}/BikeShopInspection/Templates`, req)
    }
    updateInspectionTemplate(id: string, req: { name: string; isActive: boolean; makeDefault?: boolean }) {
        return axios.put(`${this.apiUrl}/BikeShopInspection/Templates/${id}`, req)
    }
    // Omit id to add a row, supply it to edit one.
    upsertInspectionItem(templateId: string, req: {
        id?: string | null; groupLabel: string; label: string; sortOrder: number; isActive: boolean
    }) {
        return axios.post<{ data: { id: string } }>(
            `${this.apiUrl}/BikeShopInspection/Templates/${templateId}/Items`, req)
    }
    // Past inspections keep their recorded label and rating; this only changes future ones.
    deleteInspectionItem(itemId: string) {
        return axios.delete(`${this.apiUrl}/BikeShopInspection/Templates/Items/${itemId}`)
    }

    // ── Inspections: performing one ──────────────────────────────────────────
    startInspection(req: { customerBikeId: string; workOrderId?: string | null; templateId?: string | null }) {
        return axios.post<{ data: ShopInspection }>(`${this.apiUrl}/BikeShopInspection`, req)
    }
    getInspection(id: string) {
        return axios.get<{ data: ShopInspection }>(`${this.apiUrl}/BikeShopInspection/${id}`)
    }
    inspectionsForBike(bikeId: string) {
        return axios.get<{ data: ShopInspection[] }>(`${this.apiUrl}/BikeShopInspection/ForBike/${bikeId}`)
    }
    saveInspection(id: string, req: {
        status: 'draft' | 'complete'
        nextServiceDate?: string | null
        summaryNotes?: string | null
        results: { id: string; rating: ShopInspectionRating; notes?: string | null }[]
    }) {
        return axios.put<{ data: ShopInspection }>(`${this.apiUrl}/BikeShopInspection/${id}`, req)
    }

    listWorkOrders(includeClosed = false, limit = 100) {
        return axios.get<{ data: ShopWorkOrder[] }>(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders?includeClosed=${includeClosed}&limit=${limit}`)
    }
    getWorkOrder(id: string) {
        return axios.get<{ data: ShopWorkOrder }>(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${id}`)
    }
    createWorkOrder(req: UpsertShopWorkOrder) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders`, req)
    }
    updateWorkOrder(id: string, req: UpsertShopWorkOrder) {
        return axios.put(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${id}`, req)
    }
    addWorkOrderNote(id: string, body: string) {
        return axios.post<{ data: ShopWorkOrderNote }>(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${id}/Notes`, { body })
    }
    // Approve/decline a single line, or clear the decision (pending).
    setLineApproval(lineId: string, status: 'pending' | 'approved' | 'declined') {
        return axios.put(`${this.apiUrl}/BikeShopWorkOrder/WorkOrderLines/${lineId}/Approval`, { status })
    }
    approveAllLines(id: string) {
        return axios.post(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${id}/ApproveAllLines`)
    }
    // Start (or fetch) the customer visit for this order, so another bike can join it.
    ensureWorkOrderGroup(id: string) {
        return axios.post<{ data: { groupId: string } }>(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${id}/Group`)
    }
    // ── Labor timer (returns the refreshed work order) ───────────────────────
    startWorkOrderTimer(id: string) {
        return axios.post<{ data: ShopWorkOrder }>(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${id}/Timer/Start`)
    }
    stopWorkOrderTimer(id: string) {
        return axios.post<{ data: ShopWorkOrder }>(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${id}/Timer/Stop`)
    }
    setWorkOrderActualMinutes(id: string, minutes: number) {
        return axios.put<{ data: ShopWorkOrder }>(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${id}/ActualMinutes`, { minutes })
    }
    // Record (staff id) or clear (null) the QC sign-off; returns the refreshed work order.
    setWorkOrderQc(id: string, checkedByUserId: string | null) {
        return axios.put<{ data: ShopWorkOrder }>(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${id}/QcCheck`, { checkedByUserId })
    }

    // ── Work order statuses ──────────────────────────────────────────────────
    listWorkOrderStatuses() {
        return axios.get<{ data: ShopWorkOrderStatusDef[] }>(`${this.apiUrl}/BikeShopWorkOrder/WorkOrderStatuses`)
    }
    createWorkOrderStatus(req: { name: string; color: string; notifyCustomer: boolean }) {
        return axios.post<{ data: ShopWorkOrderStatusDef }>(`${this.apiUrl}/BikeShopWorkOrder/WorkOrderStatuses`, req)
    }
    updateWorkOrderStatus(id: string, req: { name: string; color: string; notifyCustomer: boolean; sortOrder: number; isActive: boolean }) {
        return axios.put<{ data: ShopWorkOrderStatusDef }>(`${this.apiUrl}/BikeShopWorkOrder/WorkOrderStatuses/${id}`, req)
    }
    setDefaultWorkOrderStatus(id: string) {
        return axios.put<{ data: ShopWorkOrderStatusDef[] }>(`${this.apiUrl}/BikeShopWorkOrder/WorkOrderStatuses/${id}/Default`)
    }
    deleteWorkOrderStatus(id: string) {
        return axios.delete(`${this.apiUrl}/BikeShopWorkOrder/WorkOrderStatuses/${id}`)
    }
    reorderWorkOrderStatuses(items: { id: string; sortOrder: number }[]) {
        return axios.post(`${this.apiUrl}/BikeShopWorkOrder/WorkOrderStatuses/Reorder`, { items })
    }
    addWorkOrderLine(id: string, req: { lineKind: 'labor' | 'part'; description?: string | null; variantId?: string | null; quantity: number; unitPriceCents?: number | null; laborHours?: number | null; estimatedMinutes?: number | null }) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${id}/Lines`, req)
    }
    removeWorkOrderLine(lineId: string) {
        return axios.delete(`${this.apiUrl}/BikeShopWorkOrder/WorkOrderLines/${lineId}`)
    }
    billWorkOrder(id: string, req: { paymentMethod: 'cash' | 'card'; tipCents: number; excessAction?: 'refund' | 'credit' | null }) {
        return axios.post<{ data: RingUpResult & {
            depositAppliedCents?: number
            depositExcessCents?: number
            excessAction?: string
            depositWasCash?: boolean
        } }>(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${id}/Bill`, req)
    }
    listTechnicians() {
        return axios.get<{ data: { id: string; name: string }[] }>(`${this.apiUrl}/BikeShopWorkOrder/Technicians`)
    }
    specialOrderOptions() {
        return axios.get<{ data: {
            pos: { id: string; reference: string | null; status: string; supplierId: string | null }[]
            suppliers: { id: string; name: string }[]
        } }>(`${this.apiUrl}/BikeShopWorkOrder/SpecialOrderOptions`)
    }
    orderWorkOrderLine(workOrderId: string, lineId: string, req: { poId?: string | null; supplierId?: string | null; unitCostCents?: number | null }) {
        return axios.post<{ data: { poId: string; poLineId: string } }>(
            `${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${workOrderId}/Lines/${lineId}/Order`, req)
    }
    setWorkOrderDeposit(id: string, depositCents: number) {
        return axios.post(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${id}/Deposit`, { depositCents })
    }
    sendWorkOrderDepositRequest(id: string) {
        return axios.post(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${id}/DepositRequest`)
    }
    recordWorkOrderCashDeposit(id: string) {
        return axios.post(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${id}/DepositCash`)
    }
    refundWorkOrderDeposit(id: string) {
        return axios.post(`${this.apiUrl}/BikeShopWorkOrder/WorkOrders/${id}/DepositRefund`)
    }

    // ── Public deposit payment link (no auth; token is the credential) ───────
    getPublicDeposit(token: string) {
        return axios.get<{ data: PublicShopDeposit }>(`${this.apiUrl}/ShopDeposit/${token}`)
    }
    payPublicDeposit(token: string) {
        return axios.post<{ data: { clientSecret: string; amountCents: number } }>(`${this.apiUrl}/ShopDeposit/${token}/Pay`)
    }

    // ── CSV import + variant matrix ──────────────────────────────────────────
    importCsv(csv: string, dryRun: boolean) {
        return axios.post<{ data: ShopImportPreview }>(`${this.apiUrl}/BikeShop/ImportCsv?dryRun=${dryRun}`, { csv })
    }
    generateVariants(productId: string, req: {
        sizes: string[]; colors: string[]; skuPrefix?: string | null
        salePriceCents?: number | null; costCents?: number | null; depositCents?: number; lowStockThreshold?: number | null
    }) {
        return axios.post<{ data: { created: number; skipped: number } }>(
            `${this.apiUrl}/BikeShop/Products/${productId}/GenerateVariants`, req)
    }

    // ── Inventory reports ────────────────────────────────────────────────────
    valuationReport() {
        return axios.get<{ data: ShopValuationRow[] }>(`${this.apiUrl}/BikeShopReport/Valuation`)
    }
    salesReport(fromUtc: string, toUtc: string) {
        return axios.get<{ data: { fromUtc: string; toUtc: string; rows: ShopSalesReportRow[] } }>(
            `${this.apiUrl}/BikeShopReport/Sales?fromUtc=${encodeURIComponent(fromUtc)}&toUtc=${encodeURIComponent(toUtc)}`)
    }
    deadStockReport(days: number) {
        return axios.get<{ data: { days: number; rows: ShopDeadStockRow[] } }>(
            `${this.apiUrl}/BikeShopReport/DeadStock?days=${days}`)
    }
    laborTimeReport(fromUtc: string, toUtc: string) {
        return axios.get<{ data: { fromUtc: string; toUtc: string; rows: ShopLaborTimeRow[] } }>(
            `${this.apiUrl}/BikeShopReport/LaborTime?fromUtc=${encodeURIComponent(fromUtc)}&toUtc=${encodeURIComponent(toUtc)}`)
    }

    // ── Customer history (profile view; by account id OR free-text email/phone) ──
    customerHistory(params: { userId?: string | null; query?: string | null; limit?: number }) {
        const qs = new URLSearchParams()
        if (params.userId) qs.set('userId', params.userId)
        if (params.query) qs.set('query', params.query)
        if (params.limit) qs.set('limit', String(params.limit))
        return axios.get<{ data: ShopCustomerHistory }>(`${this.apiUrl}/BikeShopRegister/CustomerHistory?${qs}`)
    }

    // ── Sales history + receipts ─────────────────────────────────────────────
    searchSales(params: ShopSalesQuery) {
        // Multi-selects go over the wire as repeated keys (?status=paid&status=refunded), which is
        // what the model binder expects for a List<string>.
        const qs = new URLSearchParams()
        if (params.search) qs.set('search', params.search)
        if (params.from) qs.set('from', params.from)
        if (params.to) qs.set('to', params.to)
        for (const v of params.status ?? []) qs.append('status', v)
        for (const v of params.paymentMethod ?? []) qs.append('paymentMethod', v)
        if (params.channel) qs.set('channel', params.channel)
        if (params.awaitingPickupOnly) qs.set('awaitingPickupOnly', 'true')
        if (params.workOrderOnly) qs.set('workOrderOnly', 'true')
        if (params.sortBy) qs.set('sortBy', params.sortBy)
        qs.set('sortDesc', String(params.sortDesc ?? true))
        qs.set('page', String(params.page ?? 1))
        qs.set('pageSize', String(params.pageSize ?? 25))
        return axios.get<{ data: ShopSalesPage }>(`${this.apiUrl}/BikeShopRegister/Sales?${qs}`)
    }
    markPickedUp(saleId: string) {
        return axios.post(`${this.apiUrl}/BikeShopRegister/Sale/${saleId}/PickedUp`)
    }

    // ── Public storefront (catalog is anonymous; ordering needs a signed-in rider) ──
    storeCatalog() {
        return axios.get<{ data: StoreCatalog }>(`${this.apiUrl}/ShopStore/Catalog`)
    }
    storeOrder(req: { lines: { variantId: string; quantity: number }[]; couponCode?: string | null; creditCents?: number }) {
        return axios.post<{ data: RingUpResult & { creditAppliedCents?: number; dueCents?: number } }>(
            `${this.apiUrl}/ShopStore/Order`, req)
    }
    sendReceipt(saleId: string, req: { destination: string; channel: 'email' | 'sms' }) {
        return axios.post(`${this.apiUrl}/BikeShopRegister/Sale/${saleId}/Receipt`, req)
    }

    // ── Stock takes ──────────────────────────────────────────────────────────
    listStockCounts(limit = 50) {
        return axios.get<{ data: ShopStockCount[] }>(`${this.apiUrl}/BikeShop/StockCounts?limit=${limit}`)
    }
    getStockCount(id: string) {
        return axios.get<{ data: ShopStockCount }>(`${this.apiUrl}/BikeShop/StockCounts/${id}`)
    }
    createStockCount(notes: string | null) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/BikeShop/StockCounts`, { notes })
    }
    setStockCountLine(lineId: string, countedQty: number | null) {
        return axios.put(`${this.apiUrl}/BikeShop/StockCountLines/${lineId}`, { countedQty })
    }
    completeStockCount(id: string) {
        return axios.post(`${this.apiUrl}/BikeShop/StockCounts/${id}/Complete`)
    }
    cancelStockCount(id: string) {
        return axios.post(`${this.apiUrl}/BikeShop/StockCounts/${id}/Cancel`)
    }
}

// ── Sales + stock take types ─────────────────────────────────────────────────
export interface ShopSaleLine {
    id: string
    variantId: string | null
    itemId: string | null
    quantity: number
    nameSnapshot: string
    variantLabel: string | null
    unitPriceCents: number
    discountCents: number
    taxCents: number
}

export interface ShopSale {
    id: string
    buyerUserId: string | null
    buyerEmail: string | null
    buyerName: string | null
    status: 'pending' | 'paid' | 'failed' | 'refunded'
    subtotalCents: number
    discountCents: number
    taxCents: number
    tipCents: number
    totalCents: number
    creditAppliedCents?: number
    paymentMethod: string
    orderNumber: number | null
    workOrderId: string | null
    orderChannel: 'counter' | 'online'
    pickedUpAt: string | null
    refundedAt: string | null
    refundNote: string | null
    createdAt: string
    lines: ShopSaleLine[]
}

export interface ShopSalesQuery {
    search?: string | null
    /** ISO date (yyyy-mm-dd). Inclusive both ends. */
    from?: string | null
    to?: string | null
    status?: string[]
    paymentMethod?: string[]
    channel?: 'counter' | 'online' | null
    awaitingPickupOnly?: boolean
    workOrderOnly?: boolean
    sortBy?: 'createdAt' | 'orderNumber' | 'total' | 'buyer' | 'status'
    sortDesc?: boolean
    page?: number
    pageSize?: number
}

export interface ShopSalesPage {
    rows: ShopSale[]
    /** Matching sales across every page, not just the rows returned. */
    total: number
    totals: {
        paidCents: number
        refundedCents: number
        taxCents: number
        paidCount: number
        refundedCount: number
    }
    /** Tenant-wide pickup queue; deliberately unaffected by the filters. */
    awaitingPickupCount: number
}

// ── Public storefront ────────────────────────────────────────────────────────
export interface StoreCatalog {
    categories: { id: string; name: string; sortOrder: number }[]
    products: {
        id: string; name: string; description: string | null; brand: string | null
        imageUrl: string | null; categoryId: string | null; sortOrder: number
        variants: { id: string; size: string | null; color: string | null; salePriceCents: number; trackingKind: 'pool' | 'serialized'; available: number }[]
    }[]
}

export interface ShopStockCountLine {
    id: string
    variantId: string
    expectedQty: number
    countedQty: number | null
    productName: string
    variantLabel: string | null
    sku: string | null
}

export interface ShopStockCount {
    id: string
    status: 'open' | 'completed' | 'cancelled'
    notes: string | null
    startedAt: string
    completedAt: string | null
    lines?: ShopStockCountLine[]
}

// ── Rental + work order types ────────────────────────────────────────────────
export interface ShopRentalLine {
    id: string
    variantId: string
    itemId: string | null
    quantity: number
    nameSnapshot: string
    variantLabel: string | null
    dailyRateCentsFrozen: number
    depositCentsFrozen: number
    lineAmountCents: number
}

export interface ShopRental {
    id: string
    renterUserId: string | null
    renterName: string | null
    renterEmail: string | null
    renterPhone: string | null
    startsAt: string
    endsAt: string
    status: 'pending' | 'paid' | 'out' | 'returned' | 'damaged' | 'cancelled' | 'failed'
    amountCents: number
    totalCents: number
    depositCents: number
    depositCapturedCents: number
    paymentMethod: string
    orderNumber: number | null
    checkedOutAt: string | null
    returnedAt: string | null
    conditionNotes: string | null
    createdAt: string
    lines: ShopRentalLine[]
}

export interface BookRentalResult {
    rentalId: string
    receiptToken: string
    status: 'paid' | 'pending'
    orderNumber?: number
    totalCents: number
    depositCents: number
    clientSecret?: string
    depositClientSecret?: string
}

export type ShopWorkOrderStatus = 'estimate' | 'intake' | 'awaiting_parts' | 'in_progress' | 'ready' | 'picked_up' | 'cancelled'

export interface ShopWorkOrderLine {
    id: string
    lineKind: 'labor' | 'part'
    description: string | null
    variantId: string | null
    quantity: number
    unitPriceCents: number
    /** Labor priced by time: hours and the $/hour applied. Null on flat labor and on parts. */
    laborHours: number | null
    laborRateCents: number | null
    /** Estimated time for this labor line (minutes); summed into the job estimate. */
    estimatedMinutes: number | null
    consumed: boolean
    /** Customer decision: pending | approved | declined. Declined lines aren't consumed or billed. */
    approvalStatus: 'pending' | 'approved' | 'declined'
    approvalAt: string | null
    approvalByUserId: string | null
    poLineId: string | null
    arrivedAt: string | null
}

export interface ShopWorkOrder {
    id: string
    customerUserId: string | null
    customerName: string
    customerPhone: string | null
    customerEmail: string | null
    subjectItemId: string | null
    customerBikeDesc: string | null
    customerBikeId: string | null
    /** Status code (built-in or a tenant's custom one); resolve label/color via the status defs. */
    status: string
    assignedTechUserId: string | null
    /** Accumulated worked minutes (from the timer or a manual set). */
    actualMinutes: number
    /** When the running timer started; null = stopped. */
    timerStartedAt: string | null
    /** Ties bikes dropped off together into one customer visit. Null = a solo ticket. */
    groupId: string | null
    /** Other bikes in the same visit; only populated by getWorkOrder (the detail load). */
    groupMembers?: ShopWorkOrderGroupMember[]
    intakeNotes: string | null
    /** Customer-facing note, printed on the claim tag and the bill. */
    customerNotes: string | null
    /** QC sign-off: the reviewer who checked the finished job, and when. Null until checked. */
    checkedByUserId: string | null
    checkedAt: string | null
    promisedAt: string | null
    saleId: string | null
    depositCents: number
    depositPaidAt: string | null
    depositPaymentMethod: string | null
    depositRequestSentAt: string | null
    depositRefundedCents: number
    depositRefundedAt: string | null
    createdAt: string
    lines: ShopWorkOrderLine[]
    /** Internal notes thread, newest first. Only populated by getWorkOrder (the detail load). */
    notes?: ShopWorkOrderNote[]
}

export interface ShopWorkOrderNote {
    id: string
    workOrderId: string
    body: string
    createdByUserId: string | null
    createdByName: string | null
    createdAt: string
}

export interface ShopWorkOrderGroupMember {
    id: string
    bikeLabel: string | null
    status: string
    totalCents: number
}

/** A tenant's work-order status definition (distinct from the status-code union above). */
export interface ShopWorkOrderStatusDef {
    id: string
    code: string
    name: string
    color: string
    /** estimate | open | ready | done | cancelled — the fixed system meaning. */
    behavior: 'estimate' | 'open' | 'ready' | 'done' | 'cancelled'
    notifyCustomer: boolean
    sortOrder: number
    isBuiltin: boolean
    isActive: boolean
    isDefault: boolean
}

export interface ShopImportPreview {
    dryRun: boolean
    products: number
    variants: number
    newCategories: number | string[]
    newSuppliers: number | string[]
    errors: string[]
}

export interface ShopValuationRow {
    variantId: string
    productName: string
    variantLabel: string | null
    sku: string | null
    categoryName: string | null
    trackingKind: 'pool' | 'serialized'
    onHand: number
    costCents: number | null
    salePriceCents: number | null
    costValueCents: number
    retailValueCents: number
}

export interface ShopLaborTimeRow {
    workOrderId: string
    createdAt: string
    customerName: string
    bikeLabel: string | null
    status: string
    techName: string | null
    estimatedMinutes: number
    actualMinutes: number
}

export interface ShopSalesReportRow {
    productName: string
    variantLabel: string | null
    sku: string | null
    units: number
    revenueCents: number
    cogsCents: number
}

export interface ShopDeadStockRow {
    variantId: string
    productName: string
    variantLabel: string | null
    sku: string | null
    onHand: number
    costValueCents: number
    lastSoldAt: string | null
}

export interface ShopCustomerHistory {
    sales: { id: string; createdAt: string; status: string; totalCents: number; orderNumber: number | null; paymentMethod: string; isRepair: boolean }[]
    rentals: { id: string; startsAt: string; endsAt: string; status: string; totalCents: number; depositCents: number }[]
    workOrders: { id: string; createdAt: string; status: string; customerBikeDesc: string | null; promisedAt: string | null }[]
    creditBalanceCents: number
}

export interface PublicShopDeposit {
    customerName: string
    bikeDesc: string | null
    status: string
    depositCents: number
    paid: boolean
    refunded: boolean
    cancelled: boolean
    lines: { kind: 'labor' | 'part'; description: string | null; quantity: number; unitPriceCents: number }[]
}

// A customer's bike as a record, keyed by serial where one exists so it carries repair history.
export interface ShopCustomerBike {
    id: string
    customerUserId: string | null
    customerName: string | null
    customerPhone: string | null
    serial: string | null
    brand: string | null
    model: string | null
    modelYear: number | null
    color: string | null
    size: string | null
    notes: string | null
    soldItemId: string | null
    displayName?: string
}

export interface ShopBikeHistoryRow {
    workOrderId: string
    status: string
    createdAt: string
    promisedAt: string | null
    intakeNotes: string | null
    totalCents: number
}

// Serial lookup resolves to one of three outcomes the counter must tell apart.
export interface ShopBikeLookupResult {
    match: 'known_bike' | 'sold_by_us' | 'unknown'
    bike?: ShopCustomerBike
    displayName?: string
    // Present for sold_by_us: a suggestion to accept or edit, not yet persisted.
    suggestion?: {
        serial: string | null
        brand: string | null
        model: string | null
        soldItemId: string | null
        customerUserId: string | null
        customerName: string | null
    }
    soldAt?: string | null
    history: ShopBikeHistoryRow[]
}

// ── Inspections ──────────────────────────────────────────────────────────────
// The checklist is data, not code: an MX track checks fork seals and air filters, a bike park
// checks spoke tension and bar tape, and every shop words things its own way.
export interface ShopInspectionTemplateItem {
    id: string
    templateId: string
    groupLabel: string
    label: string
    sortOrder: number
    isActive: boolean
}

export interface ShopInspectionTemplate {
    id: string
    name: string
    isDefault: boolean
    isActive: boolean
    sortOrder: number
    items: ShopInspectionTemplateItem[]
}

export type ShopInspectionRating = 'good' | 'monitor' | 'attention' | 'na'

export interface ShopInspectionResult {
    id: string
    inspectionId: string
    templateItemId: string | null
    groupLabel: string
    label: string
    rating: ShopInspectionRating
    notes: string | null
    sortOrder: number
}

export interface ShopInspection {
    id: string
    customerBikeId: string
    workOrderId: string | null
    templateId: string | null
    performedByUserId: string | null
    status: 'draft' | 'complete'
    performedAt: string
    nextServiceDate: string | null
    summaryNotes: string | null
    results: ShopInspectionResult[]
    attentionCount?: number
    monitorCount?: number
}

export interface UpsertShopWorkOrder {
    customerName: string
    customerPhone: string | null
    customerEmail: string | null
    customerUserId?: string | null
    subjectItemId?: string | null
    customerBikeDesc: string | null
    // Preferred over customerBikeDesc; the description remains the quick unstructured fallback.
    customerBikeId?: string | null
    status: string
    assignedTechUserId?: string | null
    /** Attach a new ticket to an existing customer visit (honored on create only). */
    groupId?: string | null
    intakeNotes: string | null
    customerNotes?: string | null
    promisedAt?: string | null
}
