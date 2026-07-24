<template>
    <v-container>
        <div class="d-flex align-center mb-4 ga-3 flex-wrap">
            <v-btn variant="text" prepend-icon="mdi-arrow-left" to="/Admin/BikeShop/Register">Register</v-btn>
            <h1 class="text-h5">Shop Sales</h1>
            <v-spacer></v-spacer>
            <!-- Tenant-wide queue, so it holds its count no matter how the list is filtered. -->
            <v-chip v-if="awaitingPickupCount > 0" color="warning" variant="tonal"
                prepend-icon="mdi-package-variant" style="cursor: pointer"
                @click="showAwaitingPickup">
                {{ awaitingPickupCount }} awaiting pickup
            </v-chip>
        </div>

        <!-- ── Filters ─────────────────────────────────────────────────── -->
        <v-card variant="outlined" class="mb-4 pa-3">
            <v-row dense align="center">
                <v-col cols="12" md="4">
                    <v-text-field v-model="filters.search" density="compact" hide-details clearable
                        prepend-inner-icon="mdi-magnify" label="Order #, customer, or item"
                        @update:model-value="onFilterChanged"></v-text-field>
                </v-col>
                <v-col cols="6" md="2">
                    <v-text-field v-model="filters.from" type="date" density="compact" hide-details
                        label="From" @update:model-value="onDateChanged"></v-text-field>
                </v-col>
                <v-col cols="6" md="2">
                    <v-text-field v-model="filters.to" type="date" density="compact" hide-details
                        label="To" :error="dateRangeInvalid"
                        @update:model-value="onDateChanged"></v-text-field>
                </v-col>
                <v-col cols="12" md="4">
                    <v-btn-toggle v-model="datePreset" density="compact" variant="outlined" divided
                        @update:model-value="applyPreset">
                        <v-btn value="today" size="small">Today</v-btn>
                        <v-btn value="7" size="small">7d</v-btn>
                        <v-btn value="30" size="small">30d</v-btn>
                        <v-btn value="month" size="small">Month</v-btn>
                    </v-btn-toggle>
                </v-col>
            </v-row>

            <v-row dense align="center" class="mt-1">
                <v-col cols="12" md="3">
                    <v-select v-model="filters.status" :items="statusOptions" multiple chips
                        closable-chips density="compact" hide-details label="Status"
                        @update:model-value="onFilterChanged"></v-select>
                </v-col>
                <v-col cols="12" md="3">
                    <v-select v-model="filters.paymentMethod" :items="paymentOptions" multiple chips
                        closable-chips density="compact" hide-details label="Payment"
                        @update:model-value="onFilterChanged"></v-select>
                </v-col>
                <v-col cols="12" md="2">
                    <v-select v-model="filters.channel" :items="channelOptions" clearable
                        density="compact" hide-details label="Channel"
                        @update:model-value="onFilterChanged"></v-select>
                </v-col>
                <v-col cols="12" md="4" class="d-flex align-center ga-3 flex-wrap">
                    <v-switch v-model="filters.awaitingPickupOnly" label="Awaiting pickup"
                        color="warning" hide-details density="compact"
                        @update:model-value="onFilterChanged"></v-switch>
                    <v-switch v-model="filters.workOrderOnly" label="Repairs" color="primary"
                        hide-details density="compact"
                        @update:model-value="onFilterChanged"></v-switch>
                    <v-btn v-if="hasActiveFilter" size="small" variant="text"
                        prepend-icon="mdi-filter-remove-outline" @click="clearFilters">Clear</v-btn>
                </v-col>
            </v-row>

            <div v-if="dateRangeInvalid" class="text-error text-caption mt-2">
                The From date is after the To date, so nothing can match.
            </div>
        </v-card>

        <!-- ── Totals for the whole filtered set, not the visible page ──── -->
        <div v-if="!loadError" class="d-flex ga-6 flex-wrap mb-4 px-1">
            <div>
                <div class="text-caption text-medium-emphasis">Collected</div>
                <div class="text-h6">{{ money(totals.paidCents) }}</div>
                <div class="text-caption text-medium-emphasis">{{ totals.paidCount }} paid</div>
            </div>
            <div>
                <div class="text-caption text-medium-emphasis">Refunded</div>
                <div class="text-h6" :class="totals.refundedCents > 0 ? 'text-error' : ''">
                    {{ money(totals.refundedCents) }}
                </div>
                <div class="text-caption text-medium-emphasis">{{ totals.refundedCount }} refunded</div>
            </div>
            <div>
                <div class="text-caption text-medium-emphasis">Tax collected</div>
                <div class="text-h6">{{ money(totals.taxCents) }}</div>
            </div>
            <div>
                <div class="text-caption text-medium-emphasis">Sales</div>
                <div class="text-h6">{{ total }}</div>
            </div>
        </div>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <v-card>
            <v-table density="compact">
                <thead>
                    <tr>
                        <th v-for="c in columns" :key="c.label"
                            :class="[c.align === 'right' ? 'text-right' : '', c.key ? 'sortable-col' : '']"
                            :style="c.width ? `width: ${c.width}` : ''"
                            @click="c.key && toggleSort(c.key)">
                            {{ c.label }}
                            <v-icon v-if="c.key && filters.sortBy === c.key" size="14"
                                :icon="filters.sortDesc ? 'mdi-arrow-down' : 'mdi-arrow-up'"></v-icon>
                        </th>
                        <th style="width: 150px"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-if="loading">
                        <td :colspan="columns.length + 1" class="text-center py-6">
                            <v-progress-circular indeterminate size="24" color="primary"></v-progress-circular>
                        </td>
                    </tr>
                    <tr v-else-if="sales.length === 0">
                        <td :colspan="columns.length + 1" class="text-center text-medium-emphasis py-6">
                            <template v-if="hasActiveFilter">
                                No sales match those filters.
                                <v-btn size="small" variant="text" class="ml-2" @click="clearFilters">Clear filters</v-btn>
                            </template>
                            <template v-else>No sales yet.</template>
                        </td>
                    </tr>
                    <tr v-for="s in sales" :key="s.id">
                        <td>{{ s.orderNumber ?? '—' }}</td>
                        <td class="text-caption">{{ formatDate(s.createdAt) }}</td>
                        <td>{{ s.buyerName || 'Walk-in' }}</td>
                        <td class="text-caption">
                            {{ itemsLabel(s) }}
                            <v-chip v-if="s.workOrderId" size="x-small" class="ml-1" variant="tonal">repair</v-chip>
                            <v-chip v-if="isAwaiting(s)" size="x-small" class="ml-1" color="warning" variant="tonal">pickup</v-chip>
                            <v-tooltip v-else-if="s.pickedUpAt" :text="`Collected ${formatDate(s.pickedUpAt)}`" location="top">
                                <template #activator="{ props: tp }">
                                    <v-chip v-bind="tp" size="x-small" class="ml-1" variant="tonal">picked up</v-chip>
                                </template>
                            </v-tooltip>
                            <v-chip v-else-if="s.orderChannel === 'online'" size="x-small" class="ml-1" variant="tonal">online</v-chip>
                        </td>
                        <td class="text-right">{{ money(s.totalCents) }}</td>
                        <td class="text-caption">{{ paymentLabel(s.paymentMethod) }}</td>
                        <td><v-chip size="x-small" :color="statusColor(s.status)">{{ s.status }}</v-chip></td>
                        <td class="text-right" style="white-space: nowrap">
                            <v-btn v-if="isAwaiting(s)" size="x-small" variant="tonal" color="success"
                                :loading="pickupBusyId === s.id" @click="markPickedUp(s)">Picked up</v-btn>
                            <v-tooltip text="Send receipt" location="top">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" size="x-small" variant="text" icon="mdi-receipt-text-send-outline"
                                        @click="openReceipt(s)"></v-btn>
                                </template>
                            </v-tooltip>
                            <v-tooltip v-if="s.status === 'paid'" text="Refund" location="top">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" size="x-small" variant="text" icon="mdi-cash-refund"
                                        @click="openRefund(s)"></v-btn>
                                </template>
                            </v-tooltip>
                        </td>
                    </tr>
                </tbody>
            </v-table>

            <div v-if="total > (filters.pageSize ?? 25)" class="d-flex align-center pa-3">
                <span class="text-caption text-medium-emphasis">{{ pageRangeLabel }} of {{ total }}</span>
                <v-spacer></v-spacer>
                <v-pagination v-model="filters.page" :length="pageCount" :total-visible="5"
                    density="compact" @update:model-value="reload"></v-pagination>
            </div>
        </v-card>

        <!-- ── Refund dialog ───────────────────────────────────────────── -->
        <v-dialog v-model="refundOpen" max-width="440">
            <v-card v-if="refunding">
                <v-card-title class="d-flex align-center">
                    <span>Refund sale{{ refunding.orderNumber != null ? ' #' + refunding.orderNumber : '' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="refundOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-radio-group v-model="refundDestination" hide-details density="compact" class="mb-2">
                        <v-radio value="original"
                            :label="`Back to ${refunding.paymentMethod === 'cash' ? 'cash (from the drawer)' : 'their card'}: ${money(refunding.totalCents)}`"></v-radio>
                        <v-radio value="credit" label="Keep it as store credit on their account"></v-radio>
                    </v-radio-group>
                    <p v-if="refundDestination === 'credit'" class="text-caption text-medium-emphasis mb-2">
                        Needs a customer email or account on the sale. The credit is spendable at the register right away.
                    </p>
                    <v-switch v-model="restock" color="primary" hide-details density="compact"
                        label="Items came back — return them to stock"></v-switch>
                    <v-text-field v-model="refundNote" label="Reason (optional)" density="compact" class="mt-4" hide-details></v-text-field>
                    <div v-if="refundError" class="text-error text-body-2 mt-2">{{ refundError }}</div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="refundBusy" @click="refundOpen = false">Cancel</v-btn>
                    <v-btn color="error" :loading="refundBusy" @click="doRefund">Refund {{ money(refunding.totalCents) }}</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- ── Receipt dialog ──────────────────────────────────────────── -->
        <v-dialog v-model="receiptOpen" max-width="420">
            <v-card v-if="receiptSale">
                <v-card-title class="d-flex align-center">
                    <span>Send receipt</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="receiptOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-btn-toggle v-model="receiptChannel" mandatory density="compact" class="mb-2">
                        <v-btn value="email" size="small">Email</v-btn>
                        <v-btn value="sms" size="small">Text</v-btn>
                    </v-btn-toggle>
                    <v-text-field v-model="receiptDest" :label="receiptChannel === 'sms' ? 'Phone number' : 'Email address'"
                        density="compact" class="mt-2" hide-details autofocus @keyup.enter="sendReceipt"></v-text-field>
                    <div v-if="receiptError" class="text-error text-body-2 mt-2">{{ receiptError }}</div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="receiptBusy" @click="receiptOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="receiptBusy" @click="sendReceipt">Send</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackColor" :timeout="3500">{{ snackText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, reactive, onMounted } from 'vue'
import dayjs from 'dayjs'
import { tenantDayjs } from '@/helpers/TenantTime'
import { BikeShopService, type ShopSale, type ShopSalesQuery } from '@/services/BikeShopService'

const service = new BikeShopService()
const sales = ref<ShopSale[]>([])
const loading = ref(false)
const loadError = ref('')
const total = ref(0)
const awaitingPickupCount = ref(0)
const totals = ref({ paidCents: 0, refundedCents: 0, taxCents: 0, paidCount: 0, refundedCount: 0 })

// ── Filters ────────────────────────────────────────────────────────────────
// Everything here is applied server-side. Filtering a "most recent 100" slice in the browser
// would report "no results" for a sale that exists but simply fell off the end of the list.
const DEFAULTS: ShopSalesQuery = {
    search: '', from: null, to: null, channel: null,
    awaitingPickupOnly: false, workOrderOnly: false,
    sortBy: 'createdAt', sortDesc: true, page: 1, pageSize: 25,
}
const filters = reactive<ShopSalesQuery>({ ...DEFAULTS, status: [], paymentMethod: [] })
const datePreset = ref<string | null>(null)

const statusOptions = [
    { title: 'Paid', value: 'paid' },
    { title: 'Refunded', value: 'refunded' },
    { title: 'Pending', value: 'pending' },
    { title: 'Failed', value: 'failed' },
]
const paymentOptions = [
    { title: 'Cash', value: 'cash' },
    { title: 'Card', value: 'stripe' },
    { title: 'Card (direct)', value: 'stripe_direct' },
    { title: 'Voucher', value: 'voucher' },
]
const channelOptions = [
    { title: 'In store', value: 'counter' },
    { title: 'Online', value: 'online' },
]

const columns: { key: string | null; label: string; align?: string; width?: string }[] = [
    { key: 'orderNumber', label: '#', width: '90px' },
    { key: 'createdAt', label: 'When', width: '170px' },
    { key: 'buyer', label: 'Customer' },
    { key: null, label: 'Items' },
    { key: 'total', label: 'Total', align: 'right', width: '110px' },
    { key: null, label: 'Paid', width: '120px' },
    { key: 'status', label: 'Status', width: '110px' },
]

const hasActiveFilter = computed(() =>
    !!filters.search || !!filters.from || !!filters.to
    || (filters.status?.length ?? 0) > 0 || (filters.paymentMethod?.length ?? 0) > 0
    || !!filters.channel || !!filters.awaitingPickupOnly || !!filters.workOrderOnly)

// Caught in the UI rather than sent: an inverted range can only ever return nothing, and an
// empty table reads as "no sales" instead of "your dates are backwards".
const dateRangeInvalid = computed(() =>
    !!filters.from && !!filters.to && filters.to < filters.from)

const pageCount = computed(() => Math.max(1, Math.ceil(total.value / (filters.pageSize ?? 25))))
const pageRangeLabel = computed(() => {
    if (total.value === 0) return '0'
    const from = ((filters.page ?? 1) - 1) * (filters.pageSize ?? 25) + 1
    return `${from}-${Math.min(from + sales.value.length - 1, total.value)}`
})

let searchDebounce: ReturnType<typeof setTimeout> | null = null
function onFilterChanged() {
    filters.page = 1
    if (searchDebounce) clearTimeout(searchDebounce)
    searchDebounce = setTimeout(reload, 250)
}

// Typing a date by hand no longer matches whichever preset button is lit.
function onDateChanged() {
    datePreset.value = null
    onFilterChanged()
}

function applyPreset(preset: string | null) {
    if (!preset) { filters.from = null; filters.to = null; onFilterChanged(); return }
    const today = dayjs()
    filters.to = today.format('YYYY-MM-DD')
    filters.from = preset === 'today' ? today.format('YYYY-MM-DD')
        : preset === 'month' ? today.startOf('month').format('YYYY-MM-DD')
        : today.subtract(Number(preset), 'day').format('YYYY-MM-DD')
    filters.page = 1
    void reload()
}

function resetFilters(overrides: Partial<ShopSalesQuery> = {}) {
    Object.assign(filters, DEFAULTS, { status: [], paymentMethod: [] }, overrides)
    datePreset.value = null
    void reload()
}
function clearFilters() { resetFilters() }
// Jump straight to the queue behind the header badge.
function showAwaitingPickup() { resetFilters({ awaitingPickupOnly: true }) }

function toggleSort(key: string) {
    if (filters.sortBy === key) filters.sortDesc = !filters.sortDesc
    // A new column starts descending for dates and money (newest and biggest first) but ascending
    // for names, which is what someone scanning an alphabetical list expects.
    else { filters.sortBy = key as ShopSalesQuery['sortBy']; filters.sortDesc = key !== 'buyer' }
    void reload()
}

// ── Load ───────────────────────────────────────────────────────────────────
async function reload() {
    if (dateRangeInvalid.value) return
    loading.value = true
    loadError.value = ''
    try {
        const r = await service.searchSales(filters)
        sales.value = r.data.data.rows
        total.value = r.data.data.total
        totals.value = r.data.data.totals
        awaitingPickupCount.value = r.data.data.awaitingPickupCount
    } catch (e: any) {
        loadError.value = e.response?.data?.error || 'Could not load sales. Refresh to try again.'
        // Never leave stale rows under a failed load: they would read as the filtered result.
        sales.value = []
        total.value = 0
    } finally { loading.value = false }
}
onMounted(reload)

// ── Pickup ─────────────────────────────────────────────────────────────────
const pickupBusyId = ref<string | null>(null)
const isAwaiting = (s: ShopSale) => s.orderChannel === 'online' && s.status === 'paid' && !s.pickedUpAt

async function markPickedUp(s: ShopSale) {
    pickupBusyId.value = s.id
    try {
        await service.markPickedUp(s.id)
        flash(`Order #${s.orderNumber ?? ''} handed over.`)
        await reload()
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not mark the order picked up.', 'error')
    } finally { pickupBusyId.value = null }
}

const snackbar = ref(false); const snackText = ref(''); const snackColor = ref<'success' | 'error'>('success')
function flash(t: string, c: 'success' | 'error' = 'success') { snackText.value = t; snackColor.value = c; snackbar.value = true }
function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
// The list now spans arbitrary date ranges, so an older sale has to show its year.
// Tenant timezone, not the browser's: a manager checking the till from another timezone
// must still read the track's own clock.
function formatDate(iso: string): string {
    const d = tenantDayjs(iso)
    return d.year() === dayjs().year() ? d.format('MMM D, h:mm A') : d.format('MMM D YYYY, h:mm A')
}
function itemsLabel(s: ShopSale): string {
    return s.lines.map(l => `${l.quantity > 1 ? l.quantity + '× ' : ''}${l.nameSnapshot}`).join(', ')
}
function paymentLabel(m: string): string {
    return m === 'cash' ? 'Cash' : m === 'stripe_direct' ? 'Card (direct)' : m === 'voucher' ? 'Voucher' : 'Card'
}
function statusColor(s: string) {
    return s === 'paid' ? 'success' : s === 'pending' ? 'warning' : s === 'refunded' ? 'grey' : 'error'
}

// ── Refund ─────────────────────────────────────────────────────────────────
const refundOpen = ref(false)
const refunding = ref<ShopSale | null>(null)
const refundBusy = ref(false)
const refundError = ref('')
const restock = ref(true)
const refundNote = ref('')
const refundDestination = ref<'original' | 'credit'>('original')
function openRefund(s: ShopSale) {
    refunding.value = s
    restock.value = true
    refundNote.value = ''
    refundError.value = ''
    refundDestination.value = 'original'
    refundOpen.value = true
}
async function doRefund() {
    if (!refunding.value) return
    refundBusy.value = true
    refundError.value = ''
    try {
        const r = await service.refundSale(refunding.value.id, {
            restock: restock.value,
            destination: refundDestination.value,
            note: refundNote.value.trim() || null,
        })
        refundOpen.value = false
        const credited = r.data.data.creditedCents
        flash(credited > 0
            ? `Refunded ${money(credited)} to store credit${restock.value ? ' and restocked' : ''}.`
            : `Refunded ${money(refunding.value.totalCents)}${restock.value ? ' and restocked' : ''}.`)
        await reload()
    } catch (e: any) {
        refundError.value = e.response?.data?.error || 'Could not refund this sale.'
    } finally { refundBusy.value = false }
}

// ── Receipt ────────────────────────────────────────────────────────────────
const receiptOpen = ref(false)
const receiptSale = ref<ShopSale | null>(null)
const receiptBusy = ref(false)
const receiptError = ref('')
const receiptDest = ref('')
const receiptChannel = ref<'email' | 'sms'>('email')
function openReceipt(s: ShopSale) {
    receiptSale.value = s
    receiptDest.value = s.buyerEmail ?? ''
    receiptChannel.value = 'email'
    receiptError.value = ''
    receiptOpen.value = true
}
async function sendReceipt() {
    if (!receiptSale.value) return
    const dest = receiptDest.value.trim()
    if (!dest) { receiptError.value = 'Enter where to send it.'; return }
    receiptBusy.value = true
    receiptError.value = ''
    try {
        await service.sendReceipt(receiptSale.value.id, { destination: dest, channel: receiptChannel.value })
        receiptOpen.value = false
        flash('Receipt sent.')
    } catch (e: any) {
        receiptError.value = e.response?.data?.error || 'Could not send the receipt.'
    } finally { receiptBusy.value = false }
}
</script>

<style scoped>
.sortable-col {
    cursor: pointer;
    user-select: none;
    white-space: nowrap;
}
.sortable-col:hover {
    background: rgba(var(--v-theme-on-surface), 0.04);
}
</style>
