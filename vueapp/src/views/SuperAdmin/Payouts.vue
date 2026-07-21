<template>
    <v-container>
        <h1 class="text-h4 mb-4">Payouts</h1>

        <div class="d-flex align-center mb-3">
            <p class="text-caption text-medium-emphasis mb-0 mr-4">
                Per-tenant available balance and lifetime totals. Click a row for ledger detail and fee schedule.
            </p>
            <v-spacer></v-spacer>
            <v-btn variant="text" @click="loadBalances">Refresh</v-btn>
        </div>
        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Tenant</th>
                        <th class="text-right" style="width: 130px">Available</th>
                        <th class="text-right" style="width: 130px">This month</th>
                        <th class="text-right" style="width: 130px">Lifetime gross</th>
                        <th class="text-right" style="width: 110px">Stripe fees</th>
                        <th class="text-right" style="width: 130px">RidePass cut</th>
                        <th class="text-right" style="width: 130px">Paid out</th>
                        <th style="width: 200px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="b in balances" :key="b.tenantId">
                        <td>
                            <strong>{{ b.tenantDisplayName }}</strong>
                            <div class="text-caption text-medium-emphasis"><code>{{ b.tenantSubdomain }}</code></div>
                        </td>
                        <td class="text-right">${{ (b.availableBalanceCents / 100).toFixed(2) }}</td>
                        <td class="text-right">${{ (b.currentMonthGrossCents / 100).toFixed(2) }}</td>
                        <td class="text-right">${{ (b.lifetimeGrossCents / 100).toFixed(2) }}</td>
                        <td class="text-right">${{ (b.lifetimeStripeFeeCents / 100).toFixed(2) }}</td>
                        <td class="text-right">${{ (b.lifetimeRidepassCutCents / 100).toFixed(2) }}</td>
                        <td class="text-right">${{ (b.lifetimePaidOutCents / 100).toFixed(2) }}</td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" @click="openTenantDetail(b)">Detail</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loadingBalances && balances.length === 0">
                        <td colspan="8" class="text-center text-medium-emphasis py-8">No tenants yet.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- Tenant detail dialog: payouts + ledger -->
        <v-dialog v-model="detailDialog" max-width="980" scrollable>
            <v-card v-if="detailTenant">
                <v-card-title class="d-flex align-center">
                    <span>
                        {{ detailTenant.tenantDisplayName }}
                        <span class="text-medium-emphasis ml-2 text-body-2"><code>{{ detailTenant.tenantSubdomain }}</code></span>
                    </span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="detailDialog = false"></v-btn>
                </v-card-title>
                <v-card-text style="max-height: 70vh">
                    <div class="d-flex align-center mb-2">
                        <div class="text-subtitle-2">Past payouts</div>
                        <v-spacer></v-spacer>
                        <v-btn size="small" color="primary" prepend-icon="mdi-plus" @click="openCreatePayout">
                            New payout
                        </v-btn>
                    </div>
                    <v-table density="compact" class="mb-4">
                        <thead>
                            <tr>
                                <th>Period</th>
                                <th>Status</th>
                                <th class="text-right">Net paid</th>
                                <th>Reference</th>
                                <th>Date paid</th>
                                <th style="width: 180px" class="text-right"></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="p in detailPayouts" :key="p.id">
                                <td>{{ formatDateOnly(p.periodStartUtc) }} – {{ formatDateOnly(p.periodEndUtc) }}</td>
                                <td>
                                    <v-chip size="x-small" :color="payoutStatusColor(p.status)">{{ p.status }}</v-chip>
                                </td>
                                <td class="text-right">${{ (p.netPaidCents / 100).toFixed(2) }}</td>
                                <td><code v-if="p.externalReference">{{ p.externalReference }}</code><span v-else class="text-medium-emphasis">—</span></td>
                                <td>{{ p.payoutDateUtc ? formatDate(p.payoutDateUtc) : '—' }}</td>
                                <td class="text-right">
                                    <v-btn v-if="p.status === 'pending'" size="x-small" variant="tonal" color="primary"
                                        :loading="stripeSendingId === p.id" :disabled="voidingId === p.id" @click="sendPayoutViaStripe(p)">
                                        Send via Stripe
                                    </v-btn>
                                    <v-btn v-if="p.status === 'pending' || p.status === 'processing'" size="x-small" variant="text"
                                        :disabled="stripeSendingId === p.id || voidingId === p.id" @click="openMarkPaid(p)">Mark paid</v-btn>
                                    <v-btn v-if="p.status === 'pending'" size="x-small" variant="text" color="error"
                                        :loading="voidingId === p.id" :disabled="stripeSendingId === p.id" @click="voidPayout(p)">Void</v-btn>
                                    <v-btn size="x-small" variant="text" icon="mdi-download" @click="downloadPayoutCsv(p)"
                                        :title="'Download CSV'"></v-btn>
                                </td>
                            </tr>
                            <tr v-if="detailPayouts.length === 0">
                                <td colspan="6" class="text-center text-medium-emphasis py-4">No payouts yet.</td>
                            </tr>
                        </tbody>
                    </v-table>

                    <div class="text-subtitle-2 mb-2">Recent transactions (last 200)</div>
                    <v-table density="compact">
                        <thead>
                            <tr>
                                <th>Date</th>
                                <th>Kind</th>
                                <th class="text-right">Gross</th>
                                <th class="text-right">Stripe</th>
                                <th class="text-right">RidePass</th>
                                <th class="text-right">Net</th>
                                <th>Payout</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="e in detailLedger" :key="e.id">
                                <td>{{ formatDate(e.occurredAtUtc) }}</td>
                                <td>
                                    <v-chip size="x-small" :color="e.entryKind === 'sale' ? 'primary' : 'warning'">
                                        {{ e.entryKind }}<span v-if="e.sourceKind"> · {{ e.sourceKind }}</span>
                                    </v-chip>
                                </td>
                                <td class="text-right">${{ (e.grossCents / 100).toFixed(2) }}</td>
                                <td class="text-right">${{ (e.stripeFeeCents / 100).toFixed(2) }}</td>
                                <td class="text-right">${{ (e.ridepassCutCents / 100).toFixed(2) }}</td>
                                <td class="text-right"><strong>${{ (e.netToTenantCents / 100).toFixed(2) }}</strong></td>
                                <td>
                                    <span v-if="e.payoutId" class="text-success text-caption">paid out</span>
                                    <span v-else class="text-warning text-caption">pending</span>
                                </td>
                            </tr>
                            <tr v-if="detailLedger.length === 0">
                                <td colspan="7" class="text-center text-medium-emphasis py-4">No transactions yet.</td>
                            </tr>
                        </tbody>
                    </v-table>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="detailDialog = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Create payout dialog -->
        <v-dialog v-model="createPayoutDialog" max-width="560" persistent>
            <v-card v-if="detailTenant">
                <v-card-title class="d-flex align-center">
                    <span>Create payout — {{ detailTenant.tenantDisplayName }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="createPayoutDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-caption text-medium-emphasis mb-3">
                        All unpaid ledger entries with <code>occurred_at_utc</code> in the period will be batched into this payout.
                    </p>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="payoutPeriodStart" type="date" label="Period start (UTC)" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="payoutPeriodEnd" type="date" label="Period end (UTC, exclusive)" density="compact"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-text-field v-model="payoutMemo" label="Memo (optional)" density="compact" class="mt-4"></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="createPayoutDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="creatingPayout" @click="submitCreatePayout">Create</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Mark paid dialog -->
        <v-dialog v-model="markPaidDialog" max-width="520" persistent>
            <v-card v-if="markPaidTarget">
                <v-card-title class="d-flex align-center">
                    <span>Mark payout as paid</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="markPaidDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-3">
                        Net to pay: <strong>${{ (markPaidTarget.netPaidCents / 100).toFixed(2) }}</strong>
                    </p>
                    <v-text-field v-model="markPaidDate" type="date" label="Payout date (UTC)" density="compact"></v-text-field>
                    <v-text-field v-model="markPaidReference" label="External reference (transfer id / ACH trace / check #)" density="compact" class="mt-4"></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="markPaidDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="markingPaid" @click="submitMarkPaid">Mark paid</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { SuperAdminService, type TenantBalanceSummary, type LedgerEntry, type PayoutSummary } from '@/services/SuperAdminService'
import { useConfirm } from '@/composables/useConfirm'

const service = new SuperAdminService()
const confirm = useConfirm()

const balances = ref<TenantBalanceSummary[]>([])
const loadingBalances = ref(false)
const detailDialog = ref(false)
const detailTenant = ref<TenantBalanceSummary | null>(null)
const detailPayouts = ref<PayoutSummary[]>([])
const detailLedger = ref<LedgerEntry[]>([])

const createPayoutDialog = ref(false)
const creatingPayout = ref(false)
const payoutPeriodStart = ref(dayjs.utc().startOf('month').subtract(1, 'month').format('YYYY-MM-DD'))
const payoutPeriodEnd = ref(dayjs.utc().startOf('month').format('YYYY-MM-DD'))
const payoutMemo = ref('')

const markPaidDialog = ref(false)
const markingPaid = ref(false)
const markPaidTarget = ref<PayoutSummary | null>(null)
const markPaidDate = ref(dayjs.utc().format('YYYY-MM-DD'))
const markPaidReference = ref('')

const stripeSendingId = ref<string | null>(null)
const voidingId = ref<string | null>(null)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(loadBalances)

async function loadBalances() {
    loadingBalances.value = true
    try {
        const r = await service.listBalances()
        balances.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load balances.', 'error')
    } finally {
        loadingBalances.value = false
    }
}

async function openTenantDetail(b: TenantBalanceSummary) {
    detailTenant.value = b
    detailPayouts.value = []
    detailLedger.value = []
    detailDialog.value = true
    try {
        const [p, l] = await Promise.all([
            service.listPayouts(b.tenantId),
            service.listLedger(b.tenantId, undefined, undefined, 200),
        ])
        detailPayouts.value = (p.data as any).data
        detailLedger.value = (l.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load tenant detail.', 'error')
    }
}

function openCreatePayout() {
    payoutMemo.value = ''
    createPayoutDialog.value = true
}

async function submitCreatePayout() {
    if (!detailTenant.value) return
    if (payoutPeriodStart.value >= payoutPeriodEnd.value) {
        flash('The period end must be after the period start.', 'error'); return
    }
    creatingPayout.value = true
    try {
        const r = await service.createPayout(detailTenant.value.tenantId, {
            periodStartUtc: dayjs.utc(payoutPeriodStart.value).toISOString(),
            periodEndUtc: dayjs.utc(payoutPeriodEnd.value).toISOString(),
            memo: payoutMemo.value || null,
        })
        const data = (r.data as any).data
        flash(`Payout created with ${data.attachedCount} ledger entr${data.attachedCount === 1 ? 'y' : 'ies'} attached.`, 'success')
        createPayoutDialog.value = false
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to create payout.', 'error')
        return
    } finally {
        creatingPayout.value = false
    }
    await refreshAfterMoneyAction()
}

function openMarkPaid(p: PayoutSummary) {
    markPaidTarget.value = p
    markPaidDate.value = dayjs.utc().format('YYYY-MM-DD')
    markPaidReference.value = p.externalReference ?? ''
    markPaidDialog.value = true
}

async function submitMarkPaid() {
    if (!markPaidTarget.value || !detailTenant.value) return
    markingPaid.value = true
    try {
        await service.updatePayoutStatus(detailTenant.value.tenantId, markPaidTarget.value.id, {
            status: 'paid',
            payoutDateUtc: dayjs.utc(markPaidDate.value).toISOString(),
            externalReference: markPaidReference.value || null,
        })
        flash('Marked as paid.', 'success')
        markPaidDialog.value = false
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to update payout.', 'error')
        return
    } finally {
        markingPaid.value = false
    }
    await refreshAfterMoneyAction()
}

async function voidPayout(p: PayoutSummary) {
    if (!detailTenant.value || voidingId.value) return
    const ok = await confirm({
        title: 'Void this payout?',
        message: 'Attached ledger entries will become unpaid again.',
        confirmText: 'Void',
        confirmColor: 'error',
    })
    if (!ok) return
    voidingId.value = p.id
    try {
        await service.voidPayout(detailTenant.value.tenantId, p.id)
        flash('Payout voided.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to void payout.', 'error')
        return
    } finally {
        voidingId.value = null
    }
    await refreshAfterMoneyAction()
}

async function sendPayoutViaStripe(p: PayoutSummary) {
    if (!detailTenant.value) return
    const amount = `$${(p.netPaidCents / 100).toFixed(2)}`
    const ok = await confirm({
        title: 'Send via Stripe?',
        message: `Send ${amount} to this tenant via Stripe Transfer. Moves funds from the platform balance to their connected account immediately.`,
        confirmText: 'Send',
    })
    if (!ok) return
    try {
        stripeSendingId.value = p.id
        const r = await service.sendPayoutViaStripe(detailTenant.value.tenantId, p.id)
        flash(`Sent via Stripe (transfer ${r.data.data.transferId}).`, 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Stripe transfer failed.', 'error')
        return
    } finally {
        stripeSendingId.value = null
    }
    await refreshAfterMoneyAction()
}

async function downloadPayoutCsv(p: PayoutSummary) {
    if (!detailTenant.value) return
    try {
        await service.downloadPayoutCsv(detailTenant.value.tenantId, p.id)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to download CSV.', 'error')
    }
}

async function refreshDetailDialog() {
    if (!detailTenant.value) return
    const [p, l] = await Promise.all([
        service.listPayouts(detailTenant.value.tenantId),
        service.listLedger(detailTenant.value.tenantId, undefined, undefined, 200),
    ])
    detailPayouts.value = (p.data as any).data
    detailLedger.value = (l.data as any).data
}

// Refresh the dialog + balances AFTER a money action has already succeeded. It has its own
// try/catch so a refresh failure can't be caught by the money action's catch and mis-reported as
// "the transfer/payout failed" (which would tempt an operator to run the money action again).
async function refreshAfterMoneyAction() {
    try {
        await refreshDetailDialog()
        await loadBalances()
    } catch {
        flash('The action succeeded, but the view couldn’t refresh. Reload the page to see the latest state.', 'error')
    }
}

function payoutStatusColor(s: string): string {
    switch (s) {
        case 'paid': return 'success'
        case 'pending': return 'warning'
        case 'processing': return 'info'
        case 'failed': return 'error'
        case 'on_hold': return 'grey'
        default: return 'default'
    }
}

function formatDate(utc: string): string {
    return dayjs.utc(utc).format('YYYY-MM-DD HH:mm')
}

function formatDateOnly(utc: string): string {
    return dayjs.utc(utc).format('YYYY-MM-DD')
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
