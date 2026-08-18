<template>
    <div class="eod">
        <div class="d-flex align-center mb-6 flex-wrap ga-3 no-print">
            <h2 class="text-h5">End of Day</h2>
            <v-spacer></v-spacer>

            <v-btn icon variant="text" density="comfortable" :disabled="loading" @click="stepDay(-1)">
                <v-icon>mdi-chevron-left</v-icon>
                <v-tooltip activator="parent" location="bottom">Previous day</v-tooltip>
            </v-btn>
            <v-text-field v-model="businessDate" type="date" label="Business date" density="compact" hide-details
                style="max-width: 180px" @change="load"></v-text-field>
            <v-btn icon variant="text" density="comfortable" :disabled="loading || atToday" @click="stepDay(1)">
                <v-icon>mdi-chevron-right</v-icon>
                <v-tooltip activator="parent" location="bottom">Next day</v-tooltip>
            </v-btn>

            <v-btn variant="text" :disabled="loading" @click="goToday">Today</v-btn>
            <v-btn color="primary" :loading="loading" @click="load">Refresh</v-btn>
            <v-btn variant="outlined" prepend-icon="mdi-printer" :disabled="loading || !report" @click="print">
                Print
            </v-btn>
            <v-btn variant="outlined" prepend-icon="mdi-file-delimited-outline"
                :loading="exporting" :disabled="loading || !report" @click="exportCsv">
                Export CSV
            </v-btn>
        </div>

        <!-- Print-only header: the toolbar above is hidden on paper, so the page still has to say
             which venue and which day it closes. -->
        <div class="print-only mb-4">
            <div class="text-h6">{{ branding.displayName }} &mdash; End of Day</div>
            <div class="text-body-2">{{ prettyDate }} ({{ report?.timezone || branding.timezone }})</div>
        </div>

        <v-alert v-if="errorText" type="error" variant="tonal" class="mb-4 no-print" :text="errorText"></v-alert>

        <template v-if="loading && !report">
            <v-row class="mb-4">
                <v-col v-for="n in 4" :key="n" cols="12" sm="6" md="3">
                    <v-skeleton-loader type="card"></v-skeleton-loader>
                </v-col>
            </v-row>
            <v-skeleton-loader type="table"></v-skeleton-loader>
        </template>

        <template v-else-if="report">
            <v-alert v-if="isEmptyDay" type="info" variant="tonal" class="mb-4"
                text="No activity on this date. Nothing was sold, refunded or counted."></v-alert>

            <!-- Headline tiles -->
            <v-row class="mb-2">
                <v-col cols="12" sm="6" md="3">
                    <v-card><v-card-text>
                        <div class="text-caption text-medium-emphasis">Net sales</div>
                        <div class="text-h4">{{ money(report.totals.netSalesCents) }}</div>
                        <div class="text-caption text-medium-emphasis">
                            {{ report.totals.transactionCount }} sales, {{ report.totals.refundCount }} refunds
                        </div>
                    </v-card-text></v-card>
                </v-col>
                <v-col cols="12" sm="6" md="3">
                    <v-card><v-card-text>
                        <div class="text-caption text-medium-emphasis">Tax collected</div>
                        <div class="text-h4">{{ money(report.totals.taxCents) }}</div>
                        <div class="text-caption text-medium-emphasis">owed to the jurisdiction</div>
                    </v-card-text></v-card>
                </v-col>
                <v-col cols="12" sm="6" md="3">
                    <v-card><v-card-text>
                        <div class="text-caption text-medium-emphasis">Tips</div>
                        <div class="text-h4">{{ money(report.totals.tipsCents) }}</div>
                        <div class="text-caption text-medium-emphasis">owed to staff</div>
                    </v-card-text></v-card>
                </v-col>
                <v-col cols="12" sm="6" md="3">
                    <v-card><v-card-text>
                        <div class="text-caption text-medium-emphasis">Net to you</div>
                        <div class="text-h4">{{ money(report.totals.netToTenantCents) }}</div>
                        <div class="text-caption text-medium-emphasis">
                            after {{ money(report.totals.stripeFeesCents + report.totals.ridepassFeesCents) }} in fees
                        </div>
                    </v-card-text></v-card>
                </v-col>
            </v-row>

            <!-- Revenue by category -->
            <v-card class="mb-4">
                <v-card-title class="text-subtitle-1">Revenue by category</v-card-title>
                <v-card-subtitle class="pb-2">
                    The same buckets the QuickBooks journal entry uses, so the two reconcile line for line.
                </v-card-subtitle>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th>Category</th>
                            <th class="text-right" style="width: 80px">Sales</th>
                            <th class="text-right" style="width: 90px">Refunds</th>
                            <th class="text-right" style="width: 120px">Gross</th>
                            <th class="text-right" style="width: 120px">Refunded</th>
                            <th class="text-right" style="width: 110px">Tax</th>
                            <th class="text-right" style="width: 110px">Tips</th>
                            <th class="text-right" style="width: 130px">Net revenue</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="line in report.revenue" :key="line.key">
                            <td>{{ line.label }}</td>
                            <td class="text-right">{{ line.saleCount }}</td>
                            <td class="text-right">{{ line.refundCount }}</td>
                            <td class="text-right">{{ money(line.grossCents) }}</td>
                            <td class="text-right">{{ money(line.refundCents) }}</td>
                            <td class="text-right">{{ money(line.taxCents) }}</td>
                            <td class="text-right">{{ money(line.tipCents) }}</td>
                            <td class="text-right">{{ money(line.netRevenueCents) }}</td>
                        </tr>
                        <tr v-if="report.revenue.length === 0">
                            <td colspan="8" class="text-center text-medium-emphasis py-4">No sales on this date.</td>
                        </tr>
                    </tbody>
                    <tfoot v-if="report.revenue.length">
                        <tr class="font-weight-bold">
                            <td>Total</td>
                            <td class="text-right">{{ report.totals.transactionCount }}</td>
                            <td class="text-right">{{ report.totals.refundCount }}</td>
                            <td class="text-right">{{ money(report.totals.grossSalesCents) }}</td>
                            <td class="text-right">{{ money(report.totals.refundsCents) }}</td>
                            <td class="text-right">{{ money(report.totals.taxCents) }}</td>
                            <td class="text-right">{{ money(report.totals.tipsCents) }}</td>
                            <td class="text-right">{{ money(report.totals.netRevenueCents) }}</td>
                        </tr>
                    </tfoot>
                </v-table>
            </v-card>

            <v-row>
                <!-- Tenders -->
                <v-col cols="12" md="6">
                    <v-card class="mb-4">
                        <v-card-title class="text-subtitle-1">Tenders</v-card-title>
                        <v-card-subtitle class="pb-2">
                            How the day was paid for. A sale split across a gift card and a card counts in both.
                        </v-card-subtitle>
                        <v-table density="compact">
                            <thead>
                                <tr>
                                    <th>Tender</th>
                                    <th class="text-right" style="width: 90px">Count</th>
                                    <th class="text-right" style="width: 130px">Amount</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr v-for="t in report.tenders" :key="t.method">
                                    <td>{{ t.label }}</td>
                                    <td class="text-right">{{ t.count }}</td>
                                    <td class="text-right">{{ money(t.amountCents) }}</td>
                                </tr>
                            </tbody>
                        </v-table>
                    </v-card>
                </v-col>

                <!-- Other movements: only shown when something actually moved -->
                <v-col cols="12" md="6">
                    <v-card v-if="otherMovements.length" class="mb-4">
                        <v-card-title class="text-subtitle-1">Other movements</v-card-title>
                        <v-card-subtitle class="pb-2">
                            Money that changed hands without being revenue.
                        </v-card-subtitle>
                        <v-table density="compact">
                            <tbody>
                                <tr v-for="m in otherMovements" :key="m.label">
                                    <td>{{ m.label }}</td>
                                    <td class="text-right">{{ money(m.cents) }}</td>
                                </tr>
                            </tbody>
                        </v-table>
                    </v-card>
                </v-col>
            </v-row>

            <!-- Staff -->
            <v-card class="mb-4">
                <v-card-title class="text-subtitle-1">Sales by staff</v-card-title>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th>Name</th>
                            <th class="text-right" style="width: 90px">Sales</th>
                            <th class="text-right" style="width: 100px">Refunds</th>
                            <th class="text-right" style="width: 130px">Gross</th>
                            <th class="text-right" style="width: 130px">Cash</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="s in report.staff" :key="s.userId">
                            <td>{{ s.name }}</td>
                            <td class="text-right">{{ s.saleCount }}</td>
                            <td class="text-right">{{ s.refundCount }}</td>
                            <td class="text-right">{{ money(s.grossCents) }}</td>
                            <td class="text-right">{{ money(s.cashCents) }}</td>
                        </tr>
                        <tr v-if="report.staff.length === 0">
                            <td colspan="5" class="text-center text-medium-emphasis py-4">
                                No sales were attributed to a staff member on this date.
                            </td>
                        </tr>
                    </tbody>
                </v-table>
            </v-card>

            <!-- Cash: the whole card disappears when the track took no cash and opened no drawer -->
            <v-card v-if="showCash" class="mb-4">
                <v-card-title class="text-subtitle-1">Cash</v-card-title>
                <v-card-text class="pb-0">
                    <v-row dense>
                        <v-col cols="6" md="3">
                            <div class="text-caption text-medium-emphasis">Cash sales</div>
                            <div class="text-h6">{{ money(report.cash.cashSalesCents) }}</div>
                        </v-col>
                        <v-col cols="6" md="3">
                            <div class="text-caption text-medium-emphasis">Opening floats</div>
                            <div class="text-h6">{{ money(report.cash.openingFloatCents) }}</div>
                        </v-col>
                        <v-col cols="6" md="3">
                            <div class="text-caption text-medium-emphasis">Worker counted</div>
                            <div class="text-h6">{{ money(report.cash.workerCountedCents) }}</div>
                        </v-col>
                        <v-col cols="6" md="3">
                            <div class="text-caption text-medium-emphasis">Manager counted</div>
                            <div class="text-h6">{{ money(report.cash.managerCountedCents) }}</div>
                        </v-col>
                    </v-row>
                </v-card-text>

                <template v-if="report.cash.sessions.length">
                    <v-card-subtitle class="pt-4 pb-2">Sessions</v-card-subtitle>
                    <v-table density="compact">
                        <thead>
                            <tr>
                                <th>Worker</th>
                                <th>Event</th>
                                <th class="text-right" style="width: 130px">Opening float</th>
                                <th style="width: 110px">Status</th>
                                <th style="width: 150px">Opened</th>
                                <th style="width: 150px">Closed</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="s in report.cash.sessions" :key="s.id">
                                <td>{{ s.userName }}</td>
                                <td>{{ s.eventTitle || '-' }}</td>
                                <td class="text-right">{{ money(s.openingFloatCents) }}</td>
                                <td>{{ s.status }}</td>
                                <td>{{ formatTenantDateTime(s.openedAtUtc) }}</td>
                                <td>{{ s.closedAtUtc ? formatTenantDateTime(s.closedAtUtc) : '-' }}</td>
                            </tr>
                        </tbody>
                    </v-table>
                </template>

                <template v-if="report.cash.turnIns.length">
                    <v-card-subtitle class="pt-4 pb-2">Turn-ins</v-card-subtitle>
                    <v-table density="compact">
                        <thead>
                            <tr>
                                <th>Worker</th>
                                <th>Manager</th>
                                <th class="text-right" style="width: 120px">Counted</th>
                                <th class="text-right" style="width: 130px">Manager count</th>
                                <th class="text-right" style="width: 120px">Variance</th>
                                <th style="width: 110px">Status</th>
                                <th style="width: 150px">Submitted</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="t in report.cash.turnIns" :key="t.id">
                                <td>{{ t.workerName }}</td>
                                <td>{{ t.managerName || '-' }}</td>
                                <td class="text-right">{{ money(t.workerCountedCents) }}</td>
                                <td class="text-right">
                                    {{ t.managerCountedCents === null ? '-' : money(t.managerCountedCents) }}
                                </td>
                                <td class="text-right">
                                    {{ t.varianceCents === null ? '-' : money(t.varianceCents) }}
                                </td>
                                <td>{{ t.status }}</td>
                                <td>{{ formatTenantDateTime(t.submittedAtUtc) }}</td>
                            </tr>
                        </tbody>
                    </v-table>
                </template>
            </v-card>

            <!-- QuickBooks -->
            <v-card class="mb-4">
                <v-card-title class="text-subtitle-1">QuickBooks</v-card-title>
                <v-card-text>
                    <div class="d-flex align-center flex-wrap ga-3">
                        <v-icon :color="qbo.color">{{ qbo.icon }}</v-icon>
                        <div>
                            <div>{{ qbo.text }}</div>
                            <div v-if="report.quickBooks.syncedAtUtc" class="text-caption text-medium-emphasis">
                                Posted {{ formatTenantDateTime(report.quickBooks.syncedAtUtc) }}
                            </div>
                        </div>
                        <v-spacer></v-spacer>
                        <v-btn class="no-print" variant="text" color="primary"
                            to="/Admin/Settings/QuickBooks">{{ qbo.linkText }}</v-btn>
                    </div>
                </v-card-text>
            </v-card>
        </template>

        <v-snackbar v-model="snackbar" color="error" :timeout="6000">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { ReportsService, type EndOfDayReport } from '@/services/ReportsService'
import { formatTenantDateTime, tenantWallClockNow } from '@/helpers/TenantTime'
import { branding } from '@/stores/branding'

const service = new ReportsService()

const report = ref<EndOfDayReport | null>(null)
const loading = ref(false)
const exporting = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
// Kept apart from the snackbar so a failed load leaves a persistent explanation on the page
// rather than a message that vanishes after a few seconds.
const errorText = ref('')

// "Today" means today AT THE TRACK. An admin in Denver closing a New Hampshire park's day must
// get the park's date, not their laptop's.
function tenantToday(): string { return tenantWallClockNow('YYYY-MM-DD') }

const businessDate = ref(tenantToday())

const atToday = computed(() => businessDate.value >= tenantToday())
// business_date is a DATE, not an instant, so it is formatted directly and never run through
// the UTC-to-tenant converters (that would shift it a day for western timezones).
const prettyDate = computed(() => dayjs(businessDate.value).format('dddd, MMMM D, YYYY'))

const isEmptyDay = computed(() => {
    const r = report.value
    if (!r) return false
    return r.revenue.length === 0
        && r.cash.sessions.length === 0
        && r.cash.turnIns.length === 0
        && r.totals.giftCardsSoldCents === 0
})

const showCash = computed(() => {
    const c = report.value?.cash
    if (!c) return false
    return c.sessions.length > 0 || c.turnIns.length > 0 || c.cashSalesCents !== 0
})

// Everything that moved money without being revenue. Zero rows are dropped so a track that
// sells no gift cards and takes no deposits never sees the section at all.
const otherMovements = computed(() => {
    const t = report.value?.totals
    if (!t) return []
    return [
        { label: 'Gift cards sold', cents: t.giftCardsSoldCents },
        { label: 'Gift cards redeemed', cents: t.giftCardsRedeemedCents },
        { label: 'Deposits collected', cents: t.depositsCollectedCents },
        { label: 'Deposits released', cents: t.depositsReleasedCents },
        { label: 'Chargebacks lost', cents: t.disputeLossCents },
        { label: 'Chargeback fees', cents: t.disputeFeeCents },
        { label: 'RidePass messaging charges', cents: t.platformChargesCents },
    ].filter(m => m.cents !== 0)
})

const qbo = computed(() => {
    const q = report.value?.quickBooks
    if (!q || !q.connected) {
        return {
            icon: 'mdi-link-variant-off', color: 'grey',
            text: 'QuickBooks is not connected, so this day was not posted to your books.',
            linkText: 'Connect QuickBooks',
        }
    }
    switch (q.status) {
        case 'success':
            return {
                icon: 'mdi-check-circle', color: 'success',
                text: `Posted to QuickBooks as ${q.docNumber || 'a journal entry'}.`,
                linkText: 'Sync history',
            }
        case 'failed':
            return {
                icon: 'mdi-alert-circle', color: 'error',
                text: `Failed to post to QuickBooks: ${q.lastError || 'the sync did not say why.'}`,
                linkText: 'Retry in settings',
            }
        case 'no_activity':
            return {
                icon: 'mdi-minus-circle-outline', color: 'grey',
                text: 'Nothing to post to QuickBooks: this day had no accounting activity.',
                linkText: 'Sync history',
            }
        case 'disabled':
            return {
                icon: 'mdi-pause-circle-outline', color: 'warning',
                text: 'QuickBooks sync is switched off for this date, so it will not be posted.',
                linkText: 'QuickBooks settings',
            }
        default:
            return {
                icon: 'mdi-clock-outline', color: 'info',
                text: 'Pending. This day posts to QuickBooks once it has closed at the track.',
                linkText: 'Sync history',
            }
    }
})

function money(cents: number) {
    const sign = cents < 0 ? '-' : ''
    return `${sign}$${(Math.abs(cents) / 100).toFixed(2)}`
}

function stepDay(days: number) {
    businessDate.value = dayjs(businessDate.value).add(days, 'day').format('YYYY-MM-DD')
    load()
}

function goToday() {
    businessDate.value = tenantToday()
    load()
}

async function load() {
    if (!businessDate.value) {
        errorText.value = 'Pick a business date to close.'
        return
    }
    loading.value = true
    errorText.value = ''
    try {
        const r = await service.getEndOfDay(businessDate.value)
        report.value = (r.data as any).data
    } catch (err: any) {
        // Leave the previous day's numbers on screen rather than blanking them, but say plainly
        // that what is shown is stale and why the new date did not load.
        errorText.value = err.response?.data?.error
            || `Could not load the End of Day report for ${businessDate.value}. Check your connection and press Refresh.`
        snackbarText.value = errorText.value
        snackbar.value = true
    } finally {
        loading.value = false
    }
}

function print() {
    window.print()
}

async function exportCsv() {
    exporting.value = true
    try {
        const { blob, filename } = await service.downloadEndOfDayCsv(businessDate.value)
        const url = URL.createObjectURL(blob)
        const a = document.createElement('a')
        a.href = url
        a.download = filename
        document.body.appendChild(a)
        a.click()
        a.remove()
        URL.revokeObjectURL(url)
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error
            || `Could not export the End of Day CSV for ${businessDate.value}. Try again in a moment.`
        snackbar.value = true
    } finally {
        exporting.value = false
    }
}

onMounted(load)
</script>

<style scoped>
.print-only { display: none; }
</style>

<!-- Unscoped on purpose: a print stylesheet has to reach the app chrome (nav drawer, app bar,
     footer) that this component does not own.
     Every rule is inside @media print AND gated on body:has(.eod), so it applies only while this
     report is actually on screen. That matters because a non-scoped SFC style stays in the
     document once the component has loaded: without the :has() gate, printing any OTHER admin
     page after visiting this one would come out with the navigation missing. The gate also
     unloads itself correctly under the Reporting hub's KeepAlive, which detaches the pane's
     element when you switch reports. -->
<style>
@media print {
    body:has(.eod) .v-navigation-drawer,
    body:has(.eod) .v-app-bar,
    body:has(.eod) .v-footer,
    body:has(.eod) .v-snackbar,
    body:has(.eod) .no-print {
        display: none !important;
    }
    body:has(.eod) .v-main {
        padding: 0 !important;
    }
    .eod .print-only { display: block !important; }
    /* Flat cards: shadows and tinted surfaces waste toner and print as grey blocks. */
    .eod .v-card {
        box-shadow: none !important;
        border: 1px solid #ccc;
        break-inside: avoid;
        page-break-inside: avoid;
    }
    .eod .v-table {
        font-size: 11px;
    }
    .eod {
        color: #000;
    }
}
</style>
