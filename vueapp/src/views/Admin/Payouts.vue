<template>
    <v-container>
        <h1 class="text-h4 mb-4">Payouts</h1>
        <p class="text-body-2 text-medium-emphasis mb-4">
            Your sales, fees, and the payouts you've received from RidePass.
        </p>

        <v-row class="mb-4" v-if="balance">
            <v-col cols="12" sm="6" md="3">
                <v-card><v-card-text>
                    <div class="text-caption text-medium-emphasis">Available balance</div>
                    <div class="text-h4">${{ (balance.availableBalanceCents / 100).toFixed(2) }}</div>
                    <div class="text-caption text-medium-emphasis">unpaid net to you</div>
                </v-card-text></v-card>
            </v-col>
            <v-col cols="12" sm="6" md="3">
                <v-card><v-card-text>
                    <div class="text-caption text-medium-emphasis">This month gross</div>
                    <div class="text-h4">${{ (balance.currentMonthGrossCents / 100).toFixed(2) }}</div>
                </v-card-text></v-card>
            </v-col>
            <v-col cols="12" sm="6" md="3">
                <v-card><v-card-text>
                    <div class="text-caption text-medium-emphasis">Lifetime gross</div>
                    <div class="text-h4">${{ (balance.lifetimeGrossCents / 100).toFixed(2) }}</div>
                </v-card-text></v-card>
            </v-col>
            <v-col cols="12" sm="6" md="3">
                <v-card><v-card-text>
                    <div class="text-caption text-medium-emphasis">Lifetime paid out</div>
                    <div class="text-h4">${{ (balance.lifetimePaidOutCents / 100).toFixed(2) }}</div>
                </v-card-text></v-card>
            </v-col>
        </v-row>

        <v-card class="mb-4">
            <v-card-title>Payouts</v-card-title>
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Period</th>
                        <th>Status</th>
                        <th class="text-right">Net paid</th>
                        <th>Reference</th>
                        <th>Date paid</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="p in payouts" :key="p.id" style="cursor: pointer" @click="openPayout(p)">
                        <td>{{ formatDateOnly(p.periodStartUtc) }} – {{ formatDateOnly(p.periodEndUtc) }}</td>
                        <td>
                            <v-chip size="x-small" :color="payoutStatusColor(p.status)">{{ p.status }}</v-chip>
                        </td>
                        <td class="text-right">${{ (p.netPaidCents / 100).toFixed(2) }}</td>
                        <td><code v-if="p.externalReference">{{ p.externalReference }}</code><span v-else class="text-medium-emphasis">—</span></td>
                        <td>{{ p.payoutDateUtc ? formatDate(p.payoutDateUtc) : '—' }}</td>
                    </tr>
                    <tr v-if="!loading && payouts.length === 0">
                        <td colspan="5" class="text-center text-medium-emphasis py-8">No payouts yet.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-card>
            <v-card-title>Recent transactions (last 200)</v-card-title>
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Date</th>
                        <th>Kind</th>
                        <th class="text-right">Gross</th>
                        <th class="text-right">Stripe fee</th>
                        <th class="text-right">RidePass cut</th>
                        <th class="text-right">Net to you</th>
                        <th>Status</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="e in ledger" :key="e.id">
                        <td>{{ formatDate(e.occurredAtUtc) }}</td>
                        <td>
                            <v-chip size="x-small" :color="entryKindColor(e.entryKind)">
                                {{ formatEntryKind(e.entryKind) }}
                            </v-chip>
                            <div v-if="e.memo" class="text-caption text-medium-emphasis mt-1">{{ e.memo }}</div>
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
                    <tr v-if="!loading && ledger.length === 0">
                        <td colspan="7" class="text-center text-medium-emphasis py-8">No transactions yet.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- Payout detail dialog -->
        <v-dialog v-model="payoutDialog" max-width="900" scrollable>
            <v-card v-if="selectedPayout">
                <v-card-title>
                    Payout {{ formatDateOnly(selectedPayout.periodStartUtc) }} – {{ formatDateOnly(selectedPayout.periodEndUtc) }}
                </v-card-title>
                <v-card-text style="max-height: 70vh">
                    <v-row class="mb-4">
                        <v-col cols="6" md="3"><div class="text-caption">Status</div><div>{{ selectedPayout.status }}</div></v-col>
                        <v-col cols="6" md="3"><div class="text-caption">Date paid</div><div>{{ selectedPayout.payoutDateUtc ? formatDate(selectedPayout.payoutDateUtc) : '—' }}</div></v-col>
                        <v-col cols="6" md="3"><div class="text-caption">Reference</div><div><code v-if="selectedPayout.externalReference">{{ selectedPayout.externalReference }}</code><span v-else>—</span></div></v-col>
                        <v-col cols="6" md="3"><div class="text-caption">Net paid</div><div><strong>${{ (selectedPayout.netPaidCents / 100).toFixed(2) }}</strong></div></v-col>
                    </v-row>
                    <v-table density="compact">
                        <thead>
                            <tr>
                                <th>Date</th>
                                <th>Kind</th>
                                <th class="text-right">Gross</th>
                                <th class="text-right">Stripe</th>
                                <th class="text-right">RidePass</th>
                                <th class="text-right">Net</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="e in selectedEntries" :key="e.id">
                                <td>{{ formatDate(e.occurredAtUtc) }}</td>
                                <td>
                                    <v-chip size="x-small" :color="entryKindColor(e.entryKind)">
                                        {{ formatEntryKind(e.entryKind) }}
                                    </v-chip>
                                    <div v-if="e.memo" class="text-caption text-medium-emphasis mt-1">{{ e.memo }}</div>
                                </td>
                                <td class="text-right">${{ (e.grossCents / 100).toFixed(2) }}</td>
                                <td class="text-right">${{ (e.stripeFeeCents / 100).toFixed(2) }}</td>
                                <td class="text-right">${{ (e.ridepassCutCents / 100).toFixed(2) }}</td>
                                <td class="text-right"><strong>${{ (e.netToTenantCents / 100).toFixed(2) }}</strong></td>
                            </tr>
                        </tbody>
                    </v-table>
                </v-card-text>
                <v-card-actions>
                    <v-btn prepend-icon="mdi-download" :loading="downloadingCsv" @click="downloadCsv">
                        Download CSV
                    </v-btn>
                    <v-spacer></v-spacer>
                    <v-btn @click="payoutDialog = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { TenantPayoutService } from '@/services/TenantPayoutService'
import type { TenantBalanceSummary, LedgerEntry, PayoutSummary } from '@/services/SuperAdminService'
import { branding } from '@/stores/branding'

const service = new TenantPayoutService()

const balance = ref<TenantBalanceSummary | null>(null)
const payouts = ref<PayoutSummary[]>([])
const ledger = ref<LedgerEntry[]>([])
const loading = ref(false)

const payoutDialog = ref(false)
const selectedPayout = ref<PayoutSummary | null>(null)
const selectedEntries = ref<LedgerEntry[]>([])
const downloadingCsv = ref(false)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(async () => {
    loading.value = true
    try {
        const [b, p, l] = await Promise.all([
            service.getBalance(),
            service.listPayouts(),
            service.listLedger(),
        ])
        balance.value = (b.data as any).data
        payouts.value = (p.data as any).data
        ledger.value = (l.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load payouts.', 'error')
    } finally {
        loading.value = false
    }
})

async function downloadCsv() {
    if (!selectedPayout.value) return
    downloadingCsv.value = true
    try {
        await service.downloadPayoutCsv(selectedPayout.value.id)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to download CSV.', 'error')
    } finally {
        downloadingCsv.value = false
    }
}

async function openPayout(p: PayoutSummary) {
    selectedPayout.value = p
    selectedEntries.value = []
    payoutDialog.value = true
    try {
        const r = await service.getPayout(p.id)
        const data = (r.data as any).data
        selectedPayout.value = data.payout
        selectedEntries.value = data.entries
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load payout detail.', 'error')
    }
}

function formatDate(utc: string): string {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('YYYY-MM-DD HH:mm')
}

function formatDateOnly(utc: string): string {
    return dayjs.utc(utc).format('YYYY-MM-DD')
}

// Human-readable label per entry kind. Falls back to the raw key so unknown
// future kinds still render something instead of empty.
function formatEntryKind(kind: string): string {
    switch (kind) {
        case 'sale': return 'Sale'
        case 'refund': return 'Refund'
        case 'dispute_loss': return 'Dispute'
        case 'adjustment': return 'Adjustment'
        case 'sms_charge': return 'SMS charge'
        default: return kind
    }
}

// Sales are positive (primary). sms_charge is a routine deduction, not a
// problem (info). Refunds / disputes are negative outcomes (warning).
function entryKindColor(kind: string): string {
    switch (kind) {
        case 'sale': return 'primary'
        case 'sms_charge': return 'info'
        case 'adjustment': return 'grey'
        default: return 'warning'
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

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
