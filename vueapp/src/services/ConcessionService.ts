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

export interface ConcessionModifierOption {
    id: string
    name: string
    priceDeltaCents: number
    sortOrder: number
    isActive: boolean
}

export interface ConcessionModifierGroup {
    id: string
    name: string
    minSelect: number
    maxSelect: number | null
    isRequired: boolean
    sortOrder: number
    isActive: boolean
    options: ConcessionModifierOption[]
}

export interface ConcessionStation {
    id: string
    name: string
    sortOrder: number
    isActive: boolean
}

// ── Combos (shared "make it a combo" definition) ──────────────────────
export interface ConcessionComboTier {
    id: string
    name: string
    sizeLabel: string | null   // matches a component variant's size (e.g. "Large")
    priceCents: number          // upcharge added to the entree
    sortOrder: number
}

export interface ConcessionComboSlotOption {
    id: string
    componentProductId: string
    componentName: string
    isDefault: boolean          // included choice; subs are priced vs this one
    sortOrder: number
}

export interface ConcessionComboSlot {
    id: string
    name: string
    isRequired: boolean
    sortOrder: number
    options: ConcessionComboSlotOption[]
}

export interface ConcessionComboConfig {
    tiers: ConcessionComboTier[]
    slots: ConcessionComboSlot[]
}

// Admin payload to replace the tenant's combo definition wholesale.
export interface UpsertComboConfig {
    tiers: { name: string; sizeLabel: string | null; priceCents: number }[]
    slots: { name: string; isRequired: boolean; options: { componentProductId: string; isDefault: boolean }[] }[]
}

// ── Tax ───────────────────────────────────────────────────────────────
export interface ConcessionTaxCategory {
    id: string
    name: string
    rateBps: number    // basis points: 825 = 8.25%
    isDefault: boolean
    sortOrder: number
    isActive: boolean
}
export interface UpsertConcessionTaxCategory {
    name: string
    rateBps: number
    isDefault: boolean
    sortOrder: number
    isActive: boolean
}

export interface ConcessionProduct {
    id: string
    name: string
    description: string | null
    categoryId: string | null
    categoryName: string | null
    categorySortOrder: number
    priceCents: number
    imageUrl: string | null
    showInCarousel: boolean
    isActive: boolean
    sortOrder: number
    stationId: string | null
    taxCategoryId: string | null   // null = use the tenant default tax category
    inventory: number | null     // product-level stock (no-variant items); null = unlimited
    remaining: number            // -1 = unlimited / not tracked at product level
    soldOut: boolean             // unavailable now (86'd today, depleted, or all variants out)
    manuallySoldOut: boolean     // explicitly 86'd for today
    variants: ConcessionVariant[]
    modifierGroups: ConcessionModifierGroup[]
    defaultModifierOptionIds: string[]   // pre-selected on add (e.g. lettuce, tomato)
    comboAvailable: boolean      // can be upgraded via the shared "make it a combo" definition
}

export interface UpsertConcessionProduct {
    name: string
    description: string | null
    categoryId: string | null
    priceCents: number
    imageUrl: string | null
    showInCarousel: boolean
    isActive: boolean
    sortOrder: number
    stationId: string | null
    taxCategoryId: string | null
    inventory: number | null
    modifierGroupIds: string[]
    defaultModifierOptionIds: string[]
    comboAvailable: boolean
}

export interface ConcessionCategory {
    id: string
    name: string
    sortOrder: number
    isActive: boolean
}

export interface UpsertConcessionCategory {
    name: string
    sortOrder: number
    isActive: boolean
}

export interface OrderingHoursDay {
    open: boolean
    openMinute: number   // minutes from local midnight
    closeMinute: number
}

export interface OrderingSeason {
    startDate: string   // "yyyy-MM-dd", inclusive, tenant timezone
    endDate: string
}

export interface ConcessionMenuSettings {
    logoUrl: string | null
    backgroundColor: string | null
    textColor: string | null
    accentColor: string | null
    showCarousel: boolean
    carouselSeconds: number
    tipsEnabled: boolean
    prepWarnMinutes: number
    prepLateMinutes: number
    orderingHours: OrderingHoursDay[] | null   // null = always open; else 7 entries Sun..Sat
    orderingSeasons: OrderingSeason[] | null    // null/empty = open year-round; else open-season ranges
    requireEventDay: boolean                    // true = closed on days with nothing on the events calendar
    pricesIncludeTax: boolean                   // true = item prices already include tax; false = tax added on top
    // Member-perk discounts. kind 'percent' = basis points in value; 'amount' = cents in value.
    seasonPassDiscountEnabled: boolean
    seasonPassDiscountKind: 'percent' | 'amount'
    seasonPassDiscountValue: number
    loampassDiscountEnabled: boolean
    loampassDiscountKind: 'percent' | 'amount'
    loampassDiscountValue: number
    requireManagerForManualDiscount: boolean    // true = an arbitrary manual discount needs a manager PIN
    starterSeeded: boolean                      // server-managed; true once the starter catalog was loaded
    orderingOpenNow: boolean
}

// ── Online-order capacity / throttle ──────────────────────────────────
export interface ConcessionOrderingStatus {
    openNow: boolean
    quoteMinutes: number | null   // null = quotes off or ordering closed
    capReached: boolean
    pausedManual: boolean
    capacityEnabled: boolean       // throttle feature on (staff screens show the pause control)
    reason: string | null
}
export interface ConcessionOrderingCapacity {
    capacityEnabled: boolean
    basePrepMinutes: number
    maxActiveOrders: number       // 0 = no cap
    showQuoteTimes: boolean
    onlinePaused: boolean         // server-managed; toggled via pauseOrdering()
}
export type UpsertConcessionOrderingCapacity =
    Omit<ConcessionOrderingCapacity, 'onlinePaused'>

// ── Discounts & comps ────────────────────────────────────────────────
export interface ConcessionDiscountPreset {
    id: string
    name: string
    kind: 'percent' | 'amount'   // percent = basis points in value; amount = cents in value
    value: number
    isActive: boolean
    sortOrder: number
}
export interface UpsertConcessionDiscountPreset {
    name: string
    kind: 'percent' | 'amount'
    value: number
    isActive: boolean
    sortOrder: number
}

export interface ConcessionCompReason {
    id: string
    name: string
    defaultKind: 'full' | 'percent' | 'amount'
    defaultValue: number
    isActive: boolean
    sortOrder: number
}
export interface UpsertConcessionCompReason {
    name: string
    defaultKind: 'full' | 'percent' | 'amount'
    defaultValue: number
    isActive: boolean
    sortOrder: number
}

// A discount/comp the cashier applied, at the line or order level. The server recomputes the cents.
export interface ConcessionDiscountInput {
    kind: 'preset' | 'percent' | 'amount' | 'comp' | 'season_pass' | 'loampass'
    presetId?: string | null
    percent?: number | null        // basis points (1500 = 15%)
    amountCents?: number | null
    compReasonId?: string | null
    customerEmailOrPhone?: string | null
}

export interface ConcessionManagerPinResult {
    managerUserId: string
    managerName: string
}

export interface ConcessionMemberPerk {
    eligible: boolean
    kind: 'percent' | 'amount'
    value: number
    label: string
}
export interface ConcessionMemberLookup {
    found: boolean
    customerName?: string | null
    customerEmail?: string | null
    seasonPass?: ConcessionMemberPerk | null
    loampass?: ConcessionMemberPerk | null
}

export interface ConcessionCompReportRow {
    saleId: string
    orderNumber: number | null
    createdAt: string
    discountCents: number
    totalCents: number
    compReasonLabel: string | null
    cashierName: string | null
    authorizedByName: string | null
}
export interface ConcessionCompReport {
    rows: ConcessionCompReportRow[]
    totalCompCents: number
    count: number
}

export interface UpsertConcessionStation {
    name: string
    sortOrder: number
    isActive: boolean
}

export interface UpsertConcessionModifierGroup {
    name: string
    minSelect: number
    maxSelect: number | null
    isRequired: boolean
    sortOrder: number
    isActive: boolean
}

export interface UpsertConcessionModifierOption {
    name: string
    priceDeltaCents: number
    sortOrder: number
    isActive: boolean
}

// ── Cashier sale ─────────────────────────────────────────────────────
export interface ConcessionSaleLineInput {
    productId: string
    variantId: string | null
    quantity: number
    modifierOptionIds: string[]
    notes: string | null
    // "Make it a combo": chosen size tier (null = not a combo) + one option per slot.
    comboTierId?: string | null
    comboSelections?: { slotId: string; optionId: string }[]
    // Optional per-line discount/comp (server recomputes the cents).
    discount?: ConcessionDiscountInput | null
}

export interface ConcessionSaleRequest {
    items: ConcessionSaleLineInput[]
    tipCents: number
    paymentMethod: 'cash' | 'card'
    customerName?: string   // optional name on a counter order
    // Optional order-level discount/comp + the manager PIN authorizing any gated discount/comp.
    discount?: ConcessionDiscountInput | null
    managerPin?: string | null
    // Store credit as a tender (server re-verifies the balance and caps at the total).
    creditAccountId?: string
    creditCents?: number
}

export interface ConcessionSaleResult {
    saleId: string
    creditAppliedCents?: number
    dueCents?: number
    clientSecret: string | null
    paymentIntentId: string | null
    totalCents: number
    discountCents: number
    status: string
    orderNumber: number | null
}

export interface ConcessionSaleStatus {
    status: string
    orderNumber: number | null
    totalCents: number
}

// ── Kitchen / cook screen ────────────────────────────────────────────
export interface KitchenLine {
    lineId: string
    stationId: string | null
    name: string
    variantLabel: string | null
    quantity: number
    prepStatus: 'queued' | 'in_progress' | 'ready'
    notes: string | null
    added: string[]      // chosen non-default options
    removed: string[]    // standard defaults the customer removed
    standard: string[]   // default options that stayed (shown only when defaults are toggled on)
    isCombo: boolean             // the entree sold as a combo (cooked; has side/drink children)
    parentLineId: string | null  // set on a combo's component child lines
    comboTier: string | null     // tier name on the combo entree (e.g. "Large")
}

export interface KitchenOrder {
    saleId: string
    orderNumber: number | null
    fulfillmentStatus: 'active' | 'ready' | 'completed'
    customerName: string | null   // online orders only
    isRush: boolean
    queuedAtUtc: string   // when the order was paid / entered the kitchen
    lines: KitchenLine[]
}

// ── Pickup number board (in-venue display) ───────────────────────────
export interface BoardEntry {
    orderNumber: number | null
    customerName: string | null
}
export interface ConcessionBoard {
    ready: BoardEntry[]
    preparing: BoardEntry[]
}

// ── Order history (staff: cashiers + cooks) ──────────────────────────
export interface OrderSummary {
    saleId: string
    orderNumber: number | null
    status: 'paid' | 'refunded'
    fulfillmentStatus: 'active' | 'ready' | 'completed'
    paymentMethod: 'stripe' | 'stripe_direct' | 'cash'
    orderChannel: 'counter' | 'online'
    customerName: string | null
    subtotalCents: number
    tipCents: number
    taxCents: number
    pricesIncludeTax: boolean   // true = subtotal already includes tax; false = tax added on top
    discountCents: number       // total discount/comp taken off (0 = none)
    discountKind: string | null
    discountLabel: string | null
    authorizedByName: string | null   // manager who approved a comp / manual discount
    totalCents: number
    isRush: boolean
    createdAtUtc: string
    paidAtUtc: string | null
}
export interface OrderDetailLine {
    lineId: string
    name: string
    variantLabel: string | null
    quantity: number
    lineTotalCents: number
    discountCents: number
    discountLabel: string | null
    notes: string | null
    modifiers: string[]
    isCombo: boolean
    comboTier: string | null
    parentLineId: string | null
}
export interface OrderDetail extends OrderSummary {
    lines: OrderDetailLine[]
}

// ── Rider online ordering ────────────────────────────────────────────
export interface RiderOrder {
    saleId: string
    status: string                 // pending | paid | failed | refunded
    fulfillmentStatus: 'active' | 'ready' | 'completed'
    orderNumber: number | null
    totalCents: number
    createdAtUtc: string
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

// ── Inventory ─────────────────────────────────────────────────────────
export interface ConcessionInventoryItem {
    id: string
    name: string
    unit: string
    costCents: number
    onHand: number
    lowStockThreshold: number | null
    isLow: boolean
    isActive: boolean
}
export interface UpsertConcessionInventoryItem {
    name: string
    unit: string
    costCents: number
    onHand: number
    lowStockThreshold: number | null
    isActive: boolean
}
export interface ConcessionRecipeLine {
    inventoryItemId: string
    itemName: string
    unit: string
    quantity: number
}
export interface InventoryCountSummary {
    id: string
    createdAtUtc: string
    note: string | null
    varianceCents: number
}
export interface InventoryCountDetailLine {
    name: string
    unit: string
    expectedQty: number
    countedQty: number
    variance: number
    unitCostCents: number
    varianceCents: number
}
export interface InventoryCountDetail {
    id: string
    note: string | null
    totalVarianceCents: number
    lines: InventoryCountDetailLine[]
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

    // ── Categories ───────────────────────────────────────────────────
    categories() {
        return axios.get<{ data: ConcessionCategory[] }>(`${this.apiUrl}/Concession/Categories`)
    }
    categoriesAdmin() {
        return axios.get<{ data: ConcessionCategory[] }>(`${this.apiUrl}/Concession/Categories/Admin`)
    }
    // Load the editable starter catalog (categories, stations, modifier groups, sample products).
    seedStarter() {
        return axios.post(`${this.apiUrl}/Concession/SeedStarter`, {})
    }
    createCategory(req: UpsertConcessionCategory) {
        return axios.post<{ data: ConcessionCategory }>(`${this.apiUrl}/Concession/Categories`, req)
    }
    updateCategory(id: string, req: UpsertConcessionCategory) {
        return axios.put<{ data: ConcessionCategory }>(`${this.apiUrl}/Concession/Categories/${id}`, req)
    }
    removeCategory(id: string) {
        return axios.delete(`${this.apiUrl}/Concession/Categories/${id}`)
    }

    // ── Menu board settings ──────────────────────────────────────────
    menuSettings() {
        return axios.get<{ data: ConcessionMenuSettings }>(`${this.apiUrl}/Concession/MenuSettings`)
    }
    updateMenuSettings(req: Omit<ConcessionMenuSettings, 'orderingOpenNow' | 'starterSeeded'>) {
        return axios.put(`${this.apiUrl}/Concession/MenuSettings`, req)
    }

    // ── Online-order capacity / throttle ──────────────────────────────
    orderingStatus() {
        return axios.get<{ data: ConcessionOrderingStatus }>(`${this.apiUrl}/Concession/OrderingStatus`)
    }
    orderingCapacity() {
        return axios.get<{ data: ConcessionOrderingCapacity }>(`${this.apiUrl}/Concession/OrderingCapacity`)
    }
    updateOrderingCapacity(req: UpsertConcessionOrderingCapacity) {
        return axios.put(`${this.apiUrl}/Concession/OrderingCapacity`, req)
    }
    pauseOrdering(paused: boolean) {
        return axios.post<{ data: { paused: boolean } }>(`${this.apiUrl}/Concession/Ordering/Pause`, { paused })
    }

    // ── Tax categories ───────────────────────────────────────────────
    taxCategories() {
        return axios.get<{ data: ConcessionTaxCategory[] }>(`${this.apiUrl}/Concession/TaxCategories`)
    }
    createTaxCategory(req: UpsertConcessionTaxCategory) {
        return axios.post<{ data: ConcessionTaxCategory }>(`${this.apiUrl}/Concession/TaxCategories`, req)
    }
    updateTaxCategory(id: string, req: UpsertConcessionTaxCategory) {
        return axios.put<{ data: ConcessionTaxCategory }>(`${this.apiUrl}/Concession/TaxCategories/${id}`, req)
    }
    removeTaxCategory(id: string) {
        return axios.delete(`${this.apiUrl}/Concession/TaxCategories/${id}`)
    }

    // ── Discount presets ─────────────────────────────────────────────
    discountPresets() {
        return axios.get<{ data: ConcessionDiscountPreset[] }>(`${this.apiUrl}/Concession/DiscountPresets`)
    }
    createDiscountPreset(req: UpsertConcessionDiscountPreset) {
        return axios.post<{ data: ConcessionDiscountPreset }>(`${this.apiUrl}/Concession/DiscountPresets`, req)
    }
    updateDiscountPreset(id: string, req: UpsertConcessionDiscountPreset) {
        return axios.put<{ data: ConcessionDiscountPreset }>(`${this.apiUrl}/Concession/DiscountPresets/${id}`, req)
    }
    removeDiscountPreset(id: string) {
        return axios.delete(`${this.apiUrl}/Concession/DiscountPresets/${id}`)
    }

    // ── Comp reasons ─────────────────────────────────────────────────
    compReasons() {
        return axios.get<{ data: ConcessionCompReason[] }>(`${this.apiUrl}/Concession/CompReasons`)
    }
    createCompReason(req: UpsertConcessionCompReason) {
        return axios.post<{ data: ConcessionCompReason }>(`${this.apiUrl}/Concession/CompReasons`, req)
    }
    updateCompReason(id: string, req: UpsertConcessionCompReason) {
        return axios.put<{ data: ConcessionCompReason }>(`${this.apiUrl}/Concession/CompReasons/${id}`, req)
    }
    removeCompReason(id: string) {
        return axios.delete(`${this.apiUrl}/Concession/CompReasons/${id}`)
    }

    // ── Manager PIN + member discount lookup ─────────────────────────
    // Set/clear the current user's own POS manager PIN (must be a manager/admin). Empty pin clears it.
    setManagerPin(pin: string) {
        return axios.put(`${this.apiUrl}/Concession/ManagerPin`, { pin })
    }
    // Whether the signed-in user is a manager/admin and has a PIN set (drives the forced-setup prompt).
    managerPinStatus() {
        return axios.get<{ data: { isManager: boolean; hasPin: boolean } }>(`${this.apiUrl}/Concession/ManagerPin/Status`)
    }
    // Confirm a manager PIN authorizes a gated action; returns the approving manager (or 400 on a bad PIN).
    verifyManagerPin(pin: string) {
        return axios.post<{ data: ConcessionManagerPinResult }>(`${this.apiUrl}/Concession/VerifyManagerPin`, { pin })
    }
    // Look up a customer by email/phone for member-perk discounts (season pass / loampass).
    memberLookup(query: string) {
        return axios.get<{ data: ConcessionMemberLookup }>(`${this.apiUrl}/Concession/MemberLookup`, { params: { query } })
    }

    // ── Void/comp report ─────────────────────────────────────────────
    compReport(fromIso: string, toIso: string) {
        return axios.get<{ data: ConcessionCompReport }>(`${this.apiUrl}/Concession/Reports/Comps`,
            { params: { from: fromIso, to: toIso } })
    }

    // ── Admin: stations ──────────────────────────────────────────────
    listStations() {
        return axios.get<{ data: ConcessionStation[] }>(`${this.apiUrl}/Concession/Stations`)
    }
    createStation(req: UpsertConcessionStation) {
        return axios.post<{ data: ConcessionStation }>(`${this.apiUrl}/Concession/Stations`, req)
    }
    updateStation(id: string, req: UpsertConcessionStation) {
        return axios.put<{ data: ConcessionStation }>(`${this.apiUrl}/Concession/Stations/${id}`, req)
    }
    removeStation(id: string) {
        return axios.delete(`${this.apiUrl}/Concession/Stations/${id}`)
    }

    // ── Admin: modifier groups + options ─────────────────────────────
    listModifierGroups() {
        return axios.get<{ data: ConcessionModifierGroup[] }>(`${this.apiUrl}/Concession/ModifierGroups`)
    }
    createModifierGroup(req: UpsertConcessionModifierGroup) {
        return axios.post<{ data: ConcessionModifierGroup }>(`${this.apiUrl}/Concession/ModifierGroups`, req)
    }
    updateModifierGroup(id: string, req: UpsertConcessionModifierGroup) {
        return axios.put<{ data: ConcessionModifierGroup }>(`${this.apiUrl}/Concession/ModifierGroups/${id}`, req)
    }
    removeModifierGroup(id: string) {
        return axios.delete(`${this.apiUrl}/Concession/ModifierGroups/${id}`)
    }
    createOption(groupId: string, req: UpsertConcessionModifierOption) {
        return axios.post<{ data: ConcessionModifierOption }>(`${this.apiUrl}/Concession/ModifierGroups/${groupId}/Options`, req)
    }
    updateOption(groupId: string, optionId: string, req: UpsertConcessionModifierOption) {
        return axios.put<{ data: ConcessionModifierOption }>(`${this.apiUrl}/Concession/ModifierGroups/${groupId}/Options/${optionId}`, req)
    }
    removeOption(groupId: string, optionId: string) {
        return axios.delete(`${this.apiUrl}/Concession/ModifierGroups/${groupId}/Options/${optionId}`)
    }

    // ── Cashier + cook screens ───────────────────────────────────────
    items() {
        return axios.get<{ data: ConcessionProduct[] }>(`${this.apiUrl}/Concession/Items`)
    }
    activeStations() {
        return axios.get<{ data: ConcessionStation[] }>(`${this.apiUrl}/Concession/Stations/Active`)
    }
    // Quick 86 / un-86 from the POS or cook screen. Sold out applies for today only (auto-clears tomorrow).
    setSoldOut(productId: string, soldOut: boolean) {
        return axios.post<{ data: { soldOut: boolean } }>(`${this.apiUrl}/Concession/Products/${productId}/SoldOut`, { soldOut })
    }
    createSale(req: ConcessionSaleRequest) {
        return axios.post<{ data: ConcessionSaleResult }>(`${this.apiUrl}/Concession/Sale`, req)
    }
    saleStatus(id: string) {
        return axios.get<{ data: ConcessionSaleStatus }>(`${this.apiUrl}/Concession/Sale/${id}`)
    }
    // Finalize a card sale right after the reader confirms (assigns the order number without waiting on
    // the webhook). Returns { status, orderNumber }.
    finalizeCard(id: string) {
        return axios.post<{ data: { status: string; orderNumber: number | null } }>(`${this.apiUrl}/Concession/Sale/${id}/Finalize`, {})
    }
    // A refund requires a manager PIN (verified server-side), same as a comp/discount.
    refundSale(id: string, managerPin: string) {
        return axios.post(`${this.apiUrl}/Concession/Sale/${id}/Refund`, { managerPin })
    }
    // Send a receipt for a completed sale to the customer's phone (sms) or email.
    sendReceipt(saleId: string, channel: 'sms' | 'email', destination: string) {
        return axios.post(`${this.apiUrl}/Concession/Sale/${saleId}/Receipt`, { channel, destination })
    }
    completeSale(id: string) {
        return axios.post(`${this.apiUrl}/Concession/Sale/${id}/Complete`, {})
    }
    kitchen(stationId?: string | null) {
        const q = stationId ? `?stationId=${stationId}` : ''
        return axios.get<{ data: { orders: KitchenOrder[]; stats: { completedToday: number; avgPrepMinutes: number }; warnMinutes: number; lateMinutes: number } }>(`${this.apiUrl}/Concession/Kitchen${q}`)
    }
    advanceLine(lineId: string, prepStatus: 'queued' | 'in_progress' | 'ready') {
        return axios.post(`${this.apiUrl}/Concession/Kitchen/Line/${lineId}/${prepStatus}`, {})
    }
    setRush(saleId: string, rush: boolean) {
        return axios.post(`${this.apiUrl}/Concession/Sale/${saleId}/Rush`, { rush })
    }
    recallSale(saleId: string) {
        return axios.post(`${this.apiUrl}/Concession/Sale/${saleId}/Recall`, {})
    }
    recentlyCompleted() {
        return axios.get<{ data: { saleId: string; orderNumber: number | null; customerName: string | null }[] }>(`${this.apiUrl}/Concession/Kitchen/Completed`)
    }
    // Pickup number board for an in-venue display (ready vs preparing order numbers).
    board() {
        return axios.get<{ data: ConcessionBoard }>(`${this.apiUrl}/Concession/Board`)
    }
    // Order history (cashiers + cooks): list/search past orders and fetch one with its lines.
    // from/to are local dates (yyyy-MM-dd) in the tenant timezone; omit for no date bound.
    orders(params?: { q?: string; from?: string; to?: string }) {
        return axios.get<{ data: OrderSummary[] }>(`${this.apiUrl}/Concession/Orders`, { params })
    }
    order(saleId: string) {
        return axios.get<{ data: OrderDetail }>(`${this.apiUrl}/Concession/Orders/${saleId}`)
    }
    // Mint a Stripe Terminal connection token (+ ensure the tenant's reader Location). Reuses the
    // counter endpoint, which is connected-account aware for direct-charge tenants.
    terminalConnectionToken() {
        return axios.post<{ data: { secret: string; locationId: string } }>(`${this.apiUrl}/Counter/Terminal/ConnectionToken`, {})
    }

    // ── Inventory ────────────────────────────────────────────────────
    inventoryItems() {
        return axios.get<{ data: ConcessionInventoryItem[] }>(`${this.apiUrl}/Concession/Inventory/Items`)
    }
    createInventoryItem(req: UpsertConcessionInventoryItem) {
        return axios.post<{ data: ConcessionInventoryItem }>(`${this.apiUrl}/Concession/Inventory/Items`, req)
    }
    updateInventoryItem(id: string, req: UpsertConcessionInventoryItem) {
        return axios.put<{ data: ConcessionInventoryItem }>(`${this.apiUrl}/Concession/Inventory/Items/${id}`, req)
    }
    removeInventoryItem(id: string) {
        return axios.delete(`${this.apiUrl}/Concession/Inventory/Items/${id}`)
    }
    receiveStock(id: string, quantity: number) {
        return axios.post(`${this.apiUrl}/Concession/Inventory/Items/${id}/Receive`, { quantity })
    }
    getRecipe(productId: string) {
        return axios.get<{ data: ConcessionRecipeLine[] }>(`${this.apiUrl}/Concession/Products/${productId}/Recipe`)
    }
    setRecipe(productId: string, lines: { inventoryItemId: string; quantity: number }[]) {
        return axios.put(`${this.apiUrl}/Concession/Products/${productId}/Recipe`, { lines })
    }
    // Shared "make it a combo" definition (tiers + slots). GET is open to POS/rider; PUT is admin.
    getComboConfig() {
        return axios.get<{ data: ConcessionComboConfig }>(`${this.apiUrl}/Concession/Combo`)
    }
    setComboConfig(req: UpsertComboConfig) {
        return axios.put(`${this.apiUrl}/Concession/Combo`, req)
    }
    createInventoryCount(req: { note: string | null; lines: { inventoryItemId: string; countedQty: number }[] }) {
        return axios.post<{ data: { id: string } }>(`${this.apiUrl}/Concession/Inventory/Counts`, req)
    }
    inventoryCounts() {
        return axios.get<{ data: InventoryCountSummary[] }>(`${this.apiUrl}/Concession/Inventory/Counts`)
    }
    inventoryCount(id: string) {
        return axios.get<{ data: InventoryCountDetail }>(`${this.apiUrl}/Concession/Inventory/Counts/${id}`)
    }

    // ── Rider online ordering (logged-in rider; pays via the Payment Element) ──
    riderMenu() {
        return axios.get<{ data: ConcessionProduct[] }>(`${this.apiUrl}/Concession/Menu`)
    }
    placeOrder(req: ConcessionSaleRequest) {
        return axios.post<{ data: ConcessionSaleResult }>(`${this.apiUrl}/Concession/Order`, req)
    }
    myOrders() {
        return axios.get<{ data: RiderOrder[] }>(`${this.apiUrl}/Concession/MyOrders`)
    }
}
