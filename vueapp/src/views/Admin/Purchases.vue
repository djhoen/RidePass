<template>
    <v-container>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h1 class="text-h4">Purchases</h1>
            <v-spacer></v-spacer>
            <v-text-field v-model="rangeFrom" type="date" label="From" density="compact" hide-details style="max-width: 180px"></v-text-field>
            <v-text-field v-model="rangeTo" type="date" label="To" density="compact" hide-details style="max-width: 180px"></v-text-field>
            <v-select v-model="statusFilter" :items="statusOptions" label="Status" density="compact" hide-details clearable style="max-width: 160px"></v-select>
        </div>

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
                        <th style="width: 180px">When</th>
                        <th style="width: 130px">Kind</th>
                        <th>Purchaser</th>
                        <th>Item</th>
                        <th style="width: 110px">Amount</th>
                        <th style="width: 120px">Status</th>
                        <th style="width: 120px"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="p in purchases" :key="p.kind + ':' + p.id">
                        <td>{{ formatWhen(p.createdAt) }}</td>
                        <td>
                            <v-chip size="small">{{ kindLabel(p.kind) }}</v-chip>
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
                            <v-btn v-if="canRefund(p)" size="small" color="error" variant="tonal"
                                @click="openRefund(p)">Refund</v-btn>
                            <v-btn v-else-if="canCancel(p)" size="small" color="error" variant="tonal"
                                @click="openCancel(p)">Cancel</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && purchases.length === 0">
                        <td colspan="7" class="text-center text-medium-emphasis py-8">No purchases in this range.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

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

        <v-dialog v-model="refundDialog" max-width="520">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Refund purchase</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="refundDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div v-if="refundTarget" class="mb-3">
                        <div class="text-caption text-medium-emphasis">Purchaser</div>
                        <div>{{ refundTarget.purchaserName }} — {{ refundTarget.purchaserEmail }}</div>
                        <div class="text-caption text-medium-emphasis mt-2">Item</div>
                        <div>{{ refundTarget.productName }} ({{ kindLabel(refundTarget.kind) }})</div>
                    </div>
                    <v-text-field v-model.number="refundDollars" type="number" min="0" step="0.01" prefix="$"
                        label="Refund amount" density="compact"
                        hint="Defaults to the full amount; set per your refund policy. Loam Pass credit entries show $0 and return the credit instead."
                        persistent-hint></v-text-field>
                    <v-textarea v-model="refundReason" label="Reason (optional)" rows="2" density="compact"
                        class="mt-4" hide-details></v-textarea>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" :disabled="refunding" @click="refundDialog = false">Close</v-btn>
                    <v-btn color="error" :loading="refunding" @click="confirmRefund">Refund</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
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

const purchases = ref<PurchaseRow[]>([])
const loading = ref(false)

const disputes = ref<TenantDisputeListItem[]>([])

const cancelDialog = ref(false)
const cancelTarget = ref<PurchaseRow | null>(null)
const cancelReason = ref('')
const cancelling = ref(false)

const refundDialog = ref(false)
const refundTarget = ref<PurchaseRow | null>(null)
const refundReason = ref('')
const refundDollars = ref<number>(0)
const refunding = ref(false)
const REFUNDABLE_KINDS = ['pass', 'event_ticket', 'season_pass', 'membership', 'event_extra']

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(async () => {
    await load()
    await loadDisputes()
})

async function loadDisputes() {
    try {
        const r = await service.listDisputes()
        disputes.value = (r.data as any).data
    } catch {
        // silent — disputes are nice-to-have on this page
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
    loading.value = true
    try {
        const fromUtc = dayjs.tz(rangeFrom.value + 'T00:00', tz()).utc().toISOString()
        const toUtc = dayjs.tz(rangeTo.value + 'T00:00', tz()).utc().toISOString()
        const r = await service.listPurchasesForAdmin({
            fromUtc,
            toUtc,
            status: statusFilter.value || undefined,
        })
        purchases.value = (r.data as any).data
    } finally {
        loading.value = false
    }
}

// Auto-reload whenever any filter changes. Date pickers fire on commit (not on
// every keystroke) and v-select fires on selection, so a plain watcher is fine
// — no debounce needed.
watch([rangeFrom, rangeTo, statusFilter], () => { load() })

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
    pass: 'Day Pass',
    event_ticket: 'Ticket',
    event_extra: 'Add-on',
    season_pass: 'Season Pass',
    membership: 'Membership',
    gift_card: 'Gift Card',
    rental: 'Rental',
    concession: 'Concession',
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
    return p.kind === 'pass' || p.kind === 'event_ticket'
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
        // Dispatch to the right cancel endpoint per kind. Other kinds are
        // filtered out by canCancel(), so this switch only needs to cover
        // 'pass' and 'event_ticket' today.
        if (t.kind === 'event_ticket') {
            await service.cancelTicket(t.id, reason)
        } else {
            await service.cancelPass(t.id, reason)
        }
        cancelDialog.value = false
        snackbarText.value = 'Purchase cancelled. Refund queued for super-admin.'
        snackbarColor.value = 'success'
        snackbar.value = true
        await load()
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
    return p.status === 'paid'
        && REFUNDABLE_KINDS.includes(p.kind)
        && authHelper.hasPermission(Perm.SalesRefund)
}

function openRefund(p: PurchaseRow) {
    refundTarget.value = p
    refundReason.value = ''
    refundDollars.value = Math.round(p.amountCents) / 100
    refundDialog.value = true
}

async function confirmRefund() {
    if (!refundTarget.value) return
    refunding.value = true
    try {
        const t = refundTarget.value
        const reason = refundReason.value.trim().length > 0 ? refundReason.value.trim() : null
        const amountCents = Number.isFinite(refundDollars.value)
            ? Math.max(0, Math.round(refundDollars.value * 100))
            : null
        await service.refund(t.kind, t.id, amountCents, reason)
        refundDialog.value = false
        snackbarText.value = 'Refund processed.'
        snackbarColor.value = 'success'
        snackbar.value = true
        await load()
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Failed to process refund.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        refunding.value = false
    }
}
</script>
