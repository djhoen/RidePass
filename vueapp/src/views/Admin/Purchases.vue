<template>
    <v-container>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h1 class="text-h4">Purchases</h1>
            <v-spacer></v-spacer>
            <v-text-field v-model="emailQuery" label="Email" density="compact" hide-details clearable
                prepend-inner-icon="mdi-email-search-outline" style="max-width: 200px"></v-text-field>
            <v-text-field v-model="orderIdQuery" label="Order ID" density="compact" hide-details clearable
                prepend-inner-icon="mdi-magnify" style="max-width: 170px"></v-text-field>
            <v-text-field v-model="rangeFrom" type="date" label="From" density="compact" hide-details style="max-width: 170px"></v-text-field>
            <v-text-field v-model="rangeTo" type="date" label="To" density="compact" hide-details style="max-width: 170px"></v-text-field>
            <v-select v-model="statusFilter" :items="statusOptions" label="Status" density="compact" hide-details clearable style="max-width: 150px"></v-select>
        </div>

        <v-progress-linear v-if="searching" indeterminate color="primary" class="mb-2"></v-progress-linear>
        <v-alert v-if="searchError" type="error" variant="tonal" density="compact" class="mb-2">{{ searchError }}</v-alert>
        <div v-if="serverResults !== null" class="text-caption text-medium-emphasis mb-2 d-flex align-center ga-1">
            <v-icon icon="mdi-information-outline" size="14"></v-icon>
            No matches in the selected dates. Showing all-time results for your search.
        </div>

        <!-- Disputes carry evidence-due deadlines, so if the check fails say so rather than hiding it. -->
        <v-alert v-if="disputesError" type="warning" variant="tonal" density="compact" class="mb-4">{{ disputesError }}</v-alert>

        <v-card v-if="disputes.length > 0" class="mb-4" color="red-lighten-5">
            <v-card-title class="d-flex align-center">
                <v-icon icon="mdi-alert-circle" color="error" class="mr-2"></v-icon>
                Active Disputes ({{ disputes.length }})
            </v-card-title>
            <v-card-subtitle>
                Submit evidence in your Stripe Dashboard. RidePass staff have been notified.
            </v-card-subtitle>
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Item</th>
                        <th>Purchaser</th>
                        <th style="width: 110px">Amount</th>
                        <th style="width: 140px">Reason</th>
                        <th style="width: 200px">Status</th>
                        <th style="width: 180px">Evidence Due</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="d in disputes" :key="d.id">
                        <td>{{ d.itemName || '—' }}</td>
                        <td>
                            <div>{{ d.purchaserName || '—' }}</div>
                            <div class="text-caption text-medium-emphasis">{{ d.purchaserEmail }}</div>
                        </td>
                        <td>${{ (d.amountCents / 100).toFixed(2) }}</td>
                        <td>{{ d.reason || '—' }}</td>
                        <td>
                            <v-chip size="small" :color="disputeStatusColor(d.status)">{{ d.status }}</v-chip>
                        </td>
                        <td>
                            <span v-if="d.evidenceDueByUtc" :class="evidenceDueClass(d.evidenceDueByUtc)">
                                {{ formatWhen(d.evidenceDueByUtc) }}
                            </span>
                            <span v-else class="text-medium-emphasis">—</span>
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 160px">Date</th>
                        <th style="width: 120px">Order ID</th>
                        <th>Purchaser</th>
                        <th>Item</th>
                        <th style="width: 110px">Amount</th>
                        <th style="width: 110px">Status</th>
                        <th style="width: 110px"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="p in displayRows" :key="p.kind + ':' + p.id">
                        <td>{{ formatWhen(p.createdAt) }}</td>
                        <td>
                            <span v-if="orderRef(p)" class="text-medium-emphasis"
                                :title="p.redemptionToken || p.id">{{ orderRef(p) }}</span>
                        </td>
                        <td>
                            <div>{{ p.purchaserName }}</div>
                            <div class="text-caption text-medium-emphasis">{{ p.purchaserEmail }}</div>
                        </td>
                        <td>{{ p.productName }}</td>
                        <td>${{ (p.amountCents / 100).toFixed(2) }}</td>
                        <td>
                            <v-chip size="small" :color="statusColor(p.status)">{{ p.status }}</v-chip>
                        </td>
                        <td>
                            <v-btn size="small" variant="tonal" prepend-icon="mdi-receipt-text-outline"
                                @click="openDetails(p)">Details</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && !loadError && !searching && displayRows.length === 0">
                        <td colspan="7" class="text-center text-medium-emphasis py-8">
                            {{ searchError ? 'Search failed — see the message above.'
                                : hasQuery ? 'No orders match your search.' : 'No purchases in this range.' }}
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="detailsDialog" max-width="680">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Order details</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="detailsDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div v-if="detailsLoading" class="text-center py-6">
                        <v-progress-circular indeterminate color="primary"></v-progress-circular>
                    </div>
                    <v-alert v-else-if="detailsError" type="error" variant="tonal">{{ detailsError }}</v-alert>
                    <div v-else-if="detailsItems.length === 0" class="text-medium-emphasis py-4">
                        No items found for this order.
                    </div>
                    <template v-else>
                        <div class="d-flex flex-wrap ga-6 mb-4">
                            <div>
                                <div class="text-caption text-medium-emphasis">Order ID</div>
                                <div :title="detailsAnchor?.redemptionToken || detailsAnchor?.id">
                                    {{ detailsAnchor ? orderRef(detailsAnchor) : '' }}
                                </div>
                            </div>
                            <div>
                                <div class="text-caption text-medium-emphasis">Date</div>
                                <div>{{ detailsAnchor ? formatWhen(detailsAnchor.createdAt) : '' }}</div>
                            </div>
                            <div>
                                <div class="text-caption text-medium-emphasis">Purchaser</div>
                                <div>{{ detailsItems[0].purchaserName || '—' }}</div>
                                <div class="text-caption text-medium-emphasis">{{ detailsItems[0].purchaserEmail }}</div>
                            </div>
                            <v-spacer></v-spacer>
                            <div class="text-right">
                                <div class="text-caption text-medium-emphasis">Order total</div>
                                <div class="text-h6">${{ (orderTotalCents / 100).toFixed(2) }}</div>
                            </div>
                        </div>

                        <v-table density="compact">
                            <thead>
                                <tr>
                                    <th>Item</th>
                                    <th style="width: 130px">Kind</th>
                                    <th style="width: 110px">Amount</th>
                                    <th style="width: 120px">Status</th>
                                    <th style="width: 150px"></th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr v-for="it in detailsItems" :key="it.kind + ':' + it.id">
                                    <td>{{ it.productName }}</td>
                                    <td><v-chip size="x-small">{{ kindLabel(it.kind) }}</v-chip></td>
                                    <td>${{ (it.amountCents / 100).toFixed(2) }}</td>
                                    <td><v-chip size="small" :color="statusColor(it.status)">{{ it.status }}</v-chip></td>
                                    <td class="text-right">
                                        <v-btn v-if="canCancel(it) && !canRefund(it)" size="x-small" color="error"
                                            variant="tonal" @click="openCancel(it)">Cancel</v-btn>
                                    </td>
                                </tr>
                            </tbody>
                        </v-table>
                    </template>
                </v-card-text>
                <v-card-actions>
                    <v-btn v-if="orderHasRefundable" color="error" variant="tonal" prepend-icon="mdi-cash-refund"
                        @click="openRefund">Refund</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="detailsDialog = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="cancelDialog" max-width="520">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Cancel purchase</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="cancelDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="mb-3">
                        Cancelling will mark this purchase as cancelled and queue a refund request for a
                        RidePass super-admin to process. This can't be undone.
                    </p>
                    <div v-if="cancelTarget" class="mb-3">
                        <div class="text-caption text-medium-emphasis">Purchaser</div>
                        <div>{{ cancelTarget.purchaserName }} — {{ cancelTarget.purchaserEmail }}</div>
                        <div class="text-caption text-medium-emphasis mt-2">Product</div>
                        <div>{{ cancelTarget.productName }} — ${{ (cancelTarget.amountCents / 100).toFixed(2) }}</div>
                    </div>
                    <v-textarea v-model="cancelReason" label="Reason (optional)" rows="3" density="compact"
                        hide-details></v-textarea>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" :disabled="cancelling" @click="cancelDialog = false">Close</v-btn>
                    <v-btn color="error" :loading="cancelling" @click="confirmCancel">Cancel purchase</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="refundDialog" max-width="580">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Refund</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="refundDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div class="text-caption text-medium-emphasis mb-3">
                        Select the items to refund. Each selected line is refunded in full, including the service fee.
                    </div>
                    <v-table density="compact">
                        <thead>
                            <tr>
                                <th style="width: 44px"></th>
                                <th>Item</th>
                                <th style="width: 110px">Amount</th>
                                <th style="width: 120px">Status</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="it in refundCandidates" :key="lineKey(it)">
                                <td>
                                    <v-checkbox v-model="refundSelected" :value="lineKey(it)" color="error"
                                        :disabled="onlyOneRefundable" density="compact" hide-details></v-checkbox>
                                </td>
                                <td>
                                    <div>{{ it.productName }}</div>
                                    <div class="text-caption text-medium-emphasis">{{ kindLabel(it.kind) }}</div>
                                </td>
                                <td>${{ (it.amountCents / 100).toFixed(2) }}</td>
                                <td><v-chip size="small" :color="statusColor(it.status)">{{ it.status }}</v-chip></td>
                            </tr>
                        </tbody>
                    </v-table>

                    <v-switch v-if="canOverride && anySelectedRedeemed" v-model="refundForceCheckedIn" color="warning"
                        density="compact" hide-details class="mt-2"
                        label="Force refund items that are checked in / already used"></v-switch>
                    <div v-if="canOverride && anySelectedRedeemed && refundForceCheckedIn"
                        class="text-caption text-warning mb-2">
                        A selected item has been used or checked in. Refunding will reverse it anyway.
                    </div>

                    <v-textarea v-model="refundReason" label="Reason (optional)" rows="2" density="compact"
                        class="mt-4" hide-details></v-textarea>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" :disabled="refunding" @click="refundDialog = false">Close</v-btn>
                    <v-btn color="error" :loading="refunding" :disabled="refundSelected.length === 0"
                        @click="confirmRefund">
                        Refund {{ refundSelected.length }} {{ refundSelected.length === 1 ? 'item' : 'items' }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import dayjs from 'dayjs'
import { PassService, type PurchaseRow, type TenantDisputeListItem } from '@/services/PassService'
import { branding } from '@/stores/branding'
import authHelper from '@/helpers/AuthHelper'
import { Perm } from '@/helpers/TenantPermissions'

const service = new PassService()

const today = dayjs()
const rangeFrom = ref(today.startOf('month').format('YYYY-MM-DD'))
const rangeTo = ref(today.endOf('month').add(1, 'day').format('YYYY-MM-DD'))
const statusFilter = ref<string | null>(null)
const statusOptions = ['pending', 'paid', 'failed', 'cancelled', 'refunded', 'redeemed']

// Fuzzy search. We filter the already-loaded (date-bounded) list first; only when that
// yields nothing do we hit the server for an all-time match (see evaluateSearch).
const emailQuery = ref('')
const orderIdQuery = ref('')
// null = not in server mode (show the client-filtered list); an array = all-time results.
const serverResults = ref<PurchaseRow[] | null>(null)
const searching = ref(false)
// Persistent (not just a toast) so a failed all-time search isn't read as "order doesn't exist".
const searchError = ref<string | null>(null)
// Monotonic request tokens: a response only applies if it's still the latest of its kind, so a
// slower older response can't overwrite newer data (out-of-order responses).
let loadSeq = 0
let searchSeq = 0

const purchases = ref<PurchaseRow[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)

const hasQuery = computed(() => emailQuery.value.trim() !== '' || orderIdQuery.value.trim() !== '')

// Client-side filter of the loaded range by email and/or order id (id, redemption token).
// Dashes/#/spaces are stripped so the rider's "Order #3FA85F64" matches the stored token.
const filtered = computed(() => {
    const e = emailQuery.value.trim().toLowerCase()
    const o = orderIdQuery.value.trim().toLowerCase().replace(/[#\s-]/g, '')
    if (!e && !o) return purchases.value
    return purchases.value.filter(p => {
        const emailOk = !e || (p.purchaserEmail ?? '').toLowerCase().includes(e)
        const hay = ((p.id ?? '') + '|' + (p.redemptionToken ?? '')).toLowerCase().replace(/-/g, '')
        const orderOk = !o || hay.includes(o)
        return emailOk && orderOk
    })
})

const displayRows = computed(() => serverResults.value ?? filtered.value)

// Short, copyable order reference matching the rider-facing "Order #". Prefers the
// redemption token (what the customer sees); falls back to the internal id.
function orderRef(p: PurchaseRow): string {
    const raw = (p.redemptionToken || p.id || '').replace(/-/g, '')
    return raw ? '#' + raw.slice(0, 8).toUpperCase() : ''
}

const disputes = ref<TenantDisputeListItem[]>([])
const disputesError = ref<string | null>(null)

const cancelDialog = ref(false)
const cancelTarget = ref<PurchaseRow | null>(null)
const cancelReason = ref('')
const cancelling = ref(false)

const refundDialog = ref(false)
const refundReason = ref('')
const refunding = ref(false)
const refundForceCheckedIn = ref(false)
// Row-selection refund: the order's refundable lines and which the user picked ("kind:id" keys).
const refundCandidates = ref<PurchaseRow[]>([])
const refundSelected = ref<string[]>([])
const REFUNDABLE_KINDS = ['event_ticket', 'season_pass', 'membership', 'event_extra']

function lineKey(p: PurchaseRow): string { return p.kind + ':' + p.id }
// A lone refundable line stays locked-on (can't be deselected).
const onlyOneRefundable = computed(() => refundCandidates.value.length === 1)
const anySelectedRedeemed = computed(() =>
    refundCandidates.value.some(c => refundSelected.value.includes(lineKey(c)) && c.status === 'redeemed'))

// Order details modal: the clicked row (anchor) plus every line in its order.
const detailsDialog = ref(false)
const detailsLoading = ref(false)
const detailsError = ref('')
const detailsAnchor = ref<PurchaseRow | null>(null)
const detailsItems = ref<PurchaseRow[]>([])
const orderTotalCents = computed(() => detailsItems.value.reduce((sum, i) => sum + i.amountCents, 0))
const orderHasRefundable = computed(() => detailsItems.value.some(canRefund))

// Elevated: refund a checked-in/used purchase and refund whole orders (tenant_admin + manager).
const canOverride = computed(() => authHelper.hasPermission(Perm.SalesRefundOverride))

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error' | 'warning'>('success')

onMounted(async () => {
    await load()
    await loadDisputes()
})

async function loadDisputes() {
    disputesError.value = null
    try {
        const r = await service.listDisputes()
        disputes.value = (r.data as any).data
    } catch (err: any) {
        // Not silent: the banner carries evidence-due deadlines, so a hidden failure could cost a
        // dispute by default. Surface a warning so staff know to refresh rather than assume none.
        disputesError.value = err.response?.data?.error || 'Couldn’t check for active disputes. Refresh to retry.'
    }
}

function disputeStatusColor(status: string): string {
    switch (status) {
        case 'needs_response':
        case 'warning_needs_response':
            return 'error'
        case 'under_review':
        case 'warning_under_review':
            return 'warning'
        case 'won': return 'success'
        case 'lost': return 'grey'
        default: return 'default'
    }
}

function evidenceDueClass(dueUtc: string): string {
    const hoursRemaining = dayjs.utc(dueUtc).diff(dayjs.utc(), 'hour')
    if (hoursRemaining <= 0) return 'text-error'
    if (hoursRemaining <= 48) return 'text-warning'
    return ''
}

function tz() { return branding.timezone || 'UTC' }

async function load() {
    // Rapid filter changes can leave two loads in flight; only the latest may apply its result,
    // so a slower older response can't clobber newer data.
    const seq = ++loadSeq
    loading.value = true
    loadError.value = null
    try {
        const fromUtc = dayjs.tz(rangeFrom.value + 'T00:00', tz()).utc().toISOString()
        const toUtc = dayjs.tz(rangeTo.value + 'T00:00', tz()).utc().toISOString()
        const r = await service.listPurchasesForAdmin({
            fromUtc,
            toUtc,
            status: statusFilter.value || undefined,
        })
        if (seq !== loadSeq) return
        purchases.value = (r.data as any).data
    } catch (err: any) {
        if (seq !== loadSeq) return
        const msg = err.response?.data?.error ?? 'Couldn’t load purchases. Refresh to try again.'
        loadError.value = msg
        snackbarText.value = msg
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        if (seq === loadSeq) {
            loading.value = false
            // A fresh load changes the candidate set; re-run any active search against it.
            if (hasQuery.value) evaluateSearch()
        }
    }
}

// Auto-reload whenever any filter changes. Date pickers fire on commit (not on
// every keystroke) and v-select fires on selection, so a plain watcher is fine
// — no debounce needed.
watch([rangeFrom, rangeTo, statusFilter], () => { load() })

// Decide where the search results come from: if the loaded range already contains a
// match, show that (client-side, instant); otherwise query the DB across all time.
async function evaluateSearch() {
    searchError.value = null
    if (!hasQuery.value) { serverResults.value = null; return }
    if (filtered.value.length > 0) { serverResults.value = null; return }
    await serverSearch()
}

async function serverSearch() {
    // Debounced keystrokes + load()'s re-run can overlap; only the latest search applies its result.
    const seq = ++searchSeq
    searching.value = true
    searchError.value = null
    try {
        const r = await service.listPurchasesForAdmin({
            // Keep the status filter; the controller drops the date window when searching.
            status: statusFilter.value || undefined,
            email: emailQuery.value.trim() || undefined,
            orderId: orderIdQuery.value.trim().replace(/^#/, '') || undefined,
        })
        if (seq !== searchSeq) return
        serverResults.value = (r.data as any).data
    } catch (err: any) {
        if (seq !== searchSeq) return
        // Fall back to the client-filtered list (null), not a fake empty array — an empty array reads
        // as "this order doesn't exist" during a support call. Keep a persistent error so the admin
        // knows the SEARCH failed, not that the order is missing.
        serverResults.value = null
        const msg = err.response?.data?.error ?? 'Couldn’t search all-time purchases. Try again.'
        searchError.value = msg
        snackbarText.value = msg
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        if (seq === searchSeq) searching.value = false
    }
}

// Typing fires per keystroke, so debounce before deciding whether to hit the server.
let searchTimer: ReturnType<typeof setTimeout> | null = null
watch([emailQuery, orderIdQuery], () => {
    if (searchTimer) clearTimeout(searchTimer)
    searchTimer = setTimeout(() => evaluateSearch(), 300)
})

function formatWhen(utc: string): string {
    // Friendly 12-hour format ("May 14, 5:30 PM") — matches the dashboard's
    // recent-purchases panel so the same row reads the same in both places.
    return dayjs.utc(utc).tz(tz()).format('MMM D, h:mm A')
}

function statusColor(status: string): string {
    switch (status) {
        case 'paid': return 'success'
        case 'pending': return 'warning'
        case 'failed': return 'error'
        case 'cancelled': return 'orange'
        case 'refunded': return 'grey'
        case 'redeemed': return 'primary'
        default: return 'default'
    }
}

// Pretty labels for the v_recent_sales discriminator.
const KIND_LABELS: Record<string, string> = {
    event_ticket: 'Ticket',
    event_extra: 'Add-on',
    season_pass: 'Season Pass',
    membership: 'Membership',
    gift_card: 'Gift Card',
    concession: 'Food & Beverage',
}
function kindLabel(kind: string): string {
    return KIND_LABELS[kind] ?? kind
}

// Which kinds have an admin-cancel endpoint today. Other kinds (gift card,
// rental, season pass, membership, etc.) still need to be cancelled via their
// dedicated admin flows, so we hide the inline Cancel button rather than wire
// it to a 404.
function canCancel(p: PurchaseRow): boolean {
    if (p.status !== 'paid') return false
    return p.kind === 'event_ticket'
}

function openCancel(p: PurchaseRow) {
    cancelTarget.value = p
    cancelReason.value = ''
    cancelDialog.value = true
}

async function confirmCancel() {
    if (!cancelTarget.value) return
    cancelling.value = true
    try {
        const reason = cancelReason.value.trim().length > 0 ? cancelReason.value.trim() : null
        const t = cancelTarget.value
        // Only event tickets are self-cancellable here (canCancel() filters the rest).
        await service.cancelTicket(t.id, reason)
        cancelDialog.value = false
        snackbarText.value = 'Purchase cancelled. Refund queued for super-admin.'
        snackbarColor.value = 'success'
        snackbar.value = true
        await load()
        await refreshDetails()
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Failed to cancel purchase.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        cancelling.value = false
    }
}

// Direct tenant refund (needs sales.refund). Covers the kinds with refund wiring; gift cards
// excluded. Riders without the permission fall back to the legacy Cancel-and-queue button.
function canRefund(p: PurchaseRow): boolean {
    // A checked-in (redeemed) purchase is only refundable by an override-holder.
    const refundableStatus = p.status === 'paid' || (p.status === 'redeemed' && canOverride.value)
    return refundableStatus
        && REFUNDABLE_KINDS.includes(p.kind)
        && authHelper.hasPermission(Perm.SalesRefund)
}

async function openDetails(p: PurchaseRow) {
    detailsAnchor.value = p
    detailsItems.value = []
    detailsError.value = ''
    detailsDialog.value = true
    detailsLoading.value = true
    try {
        const r = await service.adminOrderDetails(p.kind, p.id)
        detailsItems.value = (r.data as any).data
    } catch (err: any) {
        detailsError.value = err.response?.data?.error ?? 'Couldn’t load this order. Close and try again.'
    } finally {
        detailsLoading.value = false
    }
}

// Re-pull the open order's lines after a refund/cancel so their statuses update in place.
// Best-effort: the main list already reloaded and the action's own toast reported the result,
// so on failure we keep the existing rows rather than blanking the modal.
async function refreshDetails() {
    if (!detailsDialog.value || !detailsAnchor.value) return
    try {
        const r = await service.adminOrderDetails(detailsAnchor.value.kind, detailsAnchor.value.id)
        detailsItems.value = (r.data as any).data
    } catch {
        // keep the current rows; the action result was already surfaced to the user
    }
}

// One Refund button on the order: open the picker with every refundable line pre-selected
// (so "refund everything" is one click; deselect rows to refund a subset).
function openRefund() {
    refundCandidates.value = detailsItems.value.filter(canRefund)
    refundSelected.value = refundCandidates.value.map(lineKey)
    refundReason.value = ''
    // Default the override on if any candidate is checked in (only effective when the toggle
    // shows, i.e. the user holds the override permission).
    refundForceCheckedIn.value = refundCandidates.value.some(c => c.status === 'redeemed')
    refundDialog.value = true
}

async function confirmRefund() {
    const lines = refundCandidates.value
        .filter(c => refundSelected.value.includes(lineKey(c)))
        .map(c => ({ kind: c.kind, id: c.id }))
    if (lines.length === 0) return
    refunding.value = true
    try {
        const reason = refundReason.value.trim().length > 0 ? refundReason.value.trim() : null
        const res = await service.refundLines(lines, reason, refundForceCheckedIn.value)
        const { refundedCount, totalCents, errors } = res.data.data
        const dollars = (totalCents / 100).toLocaleString(undefined, { style: 'currency', currency: 'USD' })
        const itemWord = refundedCount === 1 ? 'item' : 'items'
        if (errors && errors.length > 0) {
            snackbarText.value = `Refunded ${refundedCount} ${itemWord} (${dollars}); ${errors.length} could not be refunded.`
            snackbarColor.value = 'warning'
        } else {
            snackbarText.value = `Refunded ${refundedCount} ${itemWord} (${dollars}).`
            snackbarColor.value = 'success'
        }
        refundDialog.value = false
        snackbar.value = true
        await load()
        await refreshDetails()
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Failed to process refund.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        refunding.value = false
    }
}
</script>
