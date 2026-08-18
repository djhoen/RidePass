<template>
    <div>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h2 class="text-h5">Tax</h2>
            <v-spacer></v-spacer>
            <v-select v-model="preset" :items="presetOptions" label="Range" density="compact" hide-details
                style="max-width: 200px" @update:model-value="applyPreset"></v-select>
            <v-text-field v-model="rangeFrom" type="date" label="From" density="compact" hide-details
                style="max-width: 160px" @change="preset = 'custom'"></v-text-field>
            <v-text-field v-model="rangeTo" type="date" label="To" density="compact" hide-details
                style="max-width: 160px" @change="preset = 'custom'"></v-text-field>
            <v-btn color="primary" :loading="loading" @click="load">Refresh</v-btn>
            <v-btn variant="outlined" prepend-icon="mdi-file-delimited-outline"
                :disabled="loading || !salesTax" @click="exportCsv">Export CSV</v-btn>
        </div>

        <v-alert v-if="errorText" type="error" variant="tonal" class="mb-4" :text="errorText"></v-alert>

        <v-skeleton-loader v-if="loading && !salesTax" type="card, table"></v-skeleton-loader>

        <template v-else-if="salesTax">
            <!-- Sales tax headline -->
            <v-row class="mb-2">
                <v-col cols="12" sm="6" md="3">
                    <v-card><v-card-text>
                        <div class="text-caption text-medium-emphasis">Sales tax to remit</div>
                        <div class="text-h4">{{ money(salesTax.netTaxCents) }}</div>
                        <div class="text-caption text-medium-emphasis">collected minus refunded</div>
                    </v-card-text></v-card>
                </v-col>
                <v-col cols="12" sm="6" md="3">
                    <v-card><v-card-text>
                        <div class="text-caption text-medium-emphasis">Collected</div>
                        <div class="text-h4">{{ money(salesTax.collectedTaxCents) }}</div>
                    </v-card-text></v-card>
                </v-col>
                <v-col cols="12" sm="6" md="3">
                    <v-card><v-card-text>
                        <div class="text-caption text-medium-emphasis">Refunded</div>
                        <div class="text-h4">{{ money(salesTax.refundedTaxCents) }}</div>
                    </v-card-text></v-card>
                </v-col>
                <v-col cols="12" sm="6" md="3">
                    <v-card><v-card-text>
                        <div class="text-caption text-medium-emphasis">Taxed sales</div>
                        <div class="text-h4">{{ salesTax.taxedSaleCount }}</div>
                        <div class="text-caption text-medium-emphasis">
                            {{ money(salesTax.taxableSalesCents) }} taxable
                        </div>
                    </v-card-text></v-card>
                </v-col>
            </v-row>

            <!-- Admission tax: its own jurisdiction and its own rate, so it gets its own card -->
            <v-card class="mb-4">
                <v-card-title class="text-subtitle-1">Admission tax</v-card-title>
                <v-card-subtitle class="pb-2">
                    Amusement or admission tax on event tickets. Separate from the sales tax below,
                    which covers every other revenue stream.
                </v-card-subtitle>
                <v-card-text>
                    <v-alert v-if="admissionTax && !admissionTaxConfigured" type="info" variant="tonal" class="mb-4"
                        text="No admission tax is configured for this venue. Set a rate and jurisdiction under Settings > Tax if your state or town charges one."></v-alert>
                    <v-row v-if="admissionTax" dense>
                        <v-col cols="6" md="3">
                            <div class="text-caption text-medium-emphasis">Net tax</div>
                            <div class="text-h6">{{ money(admissionTax.netTaxCents) }}</div>
                        </v-col>
                        <v-col cols="6" md="3">
                            <div class="text-caption text-medium-emphasis">Collected</div>
                            <div class="text-h6">{{ money(admissionTax.taxCollectedCents) }}</div>
                        </v-col>
                        <v-col cols="6" md="3">
                            <div class="text-caption text-medium-emphasis">Refunded</div>
                            <div class="text-h6">{{ money(admissionTax.refundedTaxCents) }}</div>
                        </v-col>
                        <v-col cols="6" md="3">
                            <div class="text-caption text-medium-emphasis">Taxable ticket sales</div>
                            <div class="text-h6">{{ money(admissionTax.taxableSalesCents) }}</div>
                        </v-col>
                        <v-col cols="6" md="3">
                            <div class="text-caption text-medium-emphasis">Taxed tickets</div>
                            <div class="text-h6">{{ admissionTax.taxedTicketCount }}</div>
                        </v-col>
                        <v-col cols="6" md="3">
                            <div class="text-caption text-medium-emphasis">Current rate</div>
                            <div class="text-h6">{{ ratePct(admissionTax.currentRateBps) }}</div>
                        </v-col>
                        <v-col cols="12" md="6">
                            <div class="text-caption text-medium-emphasis">Jurisdiction</div>
                            <div class="text-h6">{{ admissionTax.jurisdictionLabel || 'Not set' }}</div>
                        </v-col>
                    </v-row>
                </v-card-text>
            </v-card>

            <!-- Sales tax by category -->
            <v-card class="mb-4">
                <v-card-title class="text-subtitle-1">Sales tax by category</v-card-title>
                <v-card-subtitle class="pb-2">
                    Grouped by the same revenue buckets the QuickBooks journal entry uses.
                </v-card-subtitle>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th>Category</th>
                            <th class="text-right" style="width: 100px">Sales</th>
                            <th class="text-right" style="width: 140px">Taxable sales</th>
                            <th class="text-right" style="width: 130px">Collected</th>
                            <th class="text-right" style="width: 130px">Refunded</th>
                            <th class="text-right" style="width: 130px">Net tax</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="c in salesTax.byCategory" :key="c.key">
                            <td>{{ c.label }}</td>
                            <td class="text-right">{{ c.saleCount }}</td>
                            <td class="text-right">{{ money(c.taxableSalesCents) }}</td>
                            <td class="text-right">{{ money(c.collectedTaxCents) }}</td>
                            <td class="text-right">{{ money(c.refundedTaxCents) }}</td>
                            <td class="text-right">{{ money(c.taxCents) }}</td>
                        </tr>
                        <tr v-if="salesTax.byCategory.length === 0">
                            <td colspan="6" class="text-center text-medium-emphasis py-4">
                                No sales tax was collected in this range.
                            </td>
                        </tr>
                    </tbody>
                    <tfoot v-if="salesTax.byCategory.length">
                        <tr class="font-weight-bold">
                            <td>Total</td>
                            <td class="text-right">{{ salesTax.taxedSaleCount }}</td>
                            <td class="text-right">{{ money(salesTax.taxableSalesCents) }}</td>
                            <td class="text-right">{{ money(salesTax.collectedTaxCents) }}</td>
                            <td class="text-right">{{ money(salesTax.refundedTaxCents) }}</td>
                            <td class="text-right">{{ money(salesTax.netTaxCents) }}</td>
                        </tr>
                    </tfoot>
                </v-table>
            </v-card>

            <!-- Sales tax by day -->
            <v-card>
                <v-card-title class="text-subtitle-1">Sales tax by day ({{ salesTax.timezone }})</v-card-title>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th style="width: 160px">Business date</th>
                            <th class="text-right" style="width: 100px">Sales</th>
                            <th class="text-right" style="width: 140px">Taxable sales</th>
                            <th class="text-right" style="width: 130px">Collected</th>
                            <th class="text-right" style="width: 130px">Refunded</th>
                            <th class="text-right" style="width: 130px">Net tax</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="d in salesTax.byDay" :key="d.businessDate">
                            <td>{{ prettyDay(d.businessDate) }}</td>
                            <td class="text-right">{{ d.saleCount }}</td>
                            <td class="text-right">{{ money(d.taxableSalesCents) }}</td>
                            <td class="text-right">{{ money(d.collectedTaxCents) }}</td>
                            <td class="text-right">{{ money(d.refundedTaxCents) }}</td>
                            <td class="text-right">{{ money(d.taxCents) }}</td>
                        </tr>
                        <tr v-if="salesTax.byDay.length === 0">
                            <td colspan="6" class="text-center text-medium-emphasis py-4">
                                No sales tax was collected in this range.
                            </td>
                        </tr>
                    </tbody>
                </v-table>
            </v-card>
        </template>

        <v-snackbar v-model="snackbar" color="error" :timeout="6000">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import {
    ReportsService,
    type AdmissionTaxReport,
    type SalesTaxReport,
} from '@/services/ReportsService'
import { branding } from '@/stores/branding'
import { downloadCsvSections, csvMoney, type CsvSection } from '@/helpers/csv'

const service = new ReportsService()

const presetOptions = [
    { title: 'Last 7 days', value: '7d' },
    { title: 'Last 30 days', value: '30d' },
    { title: 'This month', value: 'thismonth' },
    { title: 'Last month', value: 'lastmonth' },
    { title: 'Year to date', value: 'ytd' },
    { title: 'Custom', value: 'custom' },
]
const preset = ref<string>('thismonth')

const today = dayjs()
const rangeFrom = ref(today.startOf('month').format('YYYY-MM-DD'))
const rangeTo = ref(today.format('YYYY-MM-DD'))

const salesTax = ref<SalesTaxReport | null>(null)
const admissionTax = ref<AdmissionTaxReport | null>(null)
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const errorText = ref('')

function tz() { return branding.timezone || 'UTC' }
function money(cents: number) {
    const sign = cents < 0 ? '-' : ''
    return `${sign}$${(Math.abs(cents) / 100).toFixed(2)}`
}
function ratePct(bps: number) { return `${(bps / 100).toFixed(2)}%` }
// business_date is a DATE, so it is formatted directly rather than converted from UTC.
function prettyDay(d: string) { return dayjs(d).format('ddd, MMM D, YYYY') }

// A venue with no rate AND no jurisdiction has simply never set admission tax up. A venue with a
// jurisdiction and a 0% rate has set it up and is currently exempt, which is not the same thing.
const admissionTaxConfigured = computed(() =>
    !!admissionTax.value && (admissionTax.value.currentRateBps > 0 || !!admissionTax.value.jurisdictionLabel))

function applyPreset(v: string) {
    const t = dayjs()
    switch (v) {
        case '7d':
            rangeFrom.value = t.subtract(6, 'day').format('YYYY-MM-DD')
            rangeTo.value = t.format('YYYY-MM-DD')
            break
        case '30d':
            rangeFrom.value = t.subtract(29, 'day').format('YYYY-MM-DD')
            rangeTo.value = t.format('YYYY-MM-DD')
            break
        case 'thismonth':
            rangeFrom.value = t.startOf('month').format('YYYY-MM-DD')
            rangeTo.value = t.endOf('month').format('YYYY-MM-DD')
            break
        case 'lastmonth':
            rangeFrom.value = t.subtract(1, 'month').startOf('month').format('YYYY-MM-DD')
            rangeTo.value = t.subtract(1, 'month').endOf('month').format('YYYY-MM-DD')
            break
        case 'ytd':
            rangeFrom.value = t.startOf('year').format('YYYY-MM-DD')
            rangeTo.value = t.format('YYYY-MM-DD')
            break
    }
    load()
}

async function load() {
    if (rangeFrom.value && rangeTo.value && rangeFrom.value > rangeTo.value) {
        errorText.value = '"From" must be on or before "To".'
        snackbarText.value = errorText.value
        snackbar.value = true
        return
    }
    loading.value = true
    errorText.value = ''
    try {
        const fromUtc = dayjs.tz(rangeFrom.value + 'T00:00', tz()).utc().toISOString()
        // rangeTo is inclusive; add a day for the exclusive upper bound the query expects.
        const toUtc = dayjs.tz(rangeTo.value + 'T00:00', tz()).add(1, 'day').utc().toISOString()
        // Both halves of the report are loaded together: a partial page ("sales tax loaded but
        // admission tax silently missing") would read as a venue that owes no admission tax.
        const [sales, admission] = await Promise.all([
            service.getSalesTax(fromUtc, toUtc),
            service.getAdmissionTax(fromUtc, toUtc),
        ])
        salesTax.value = (sales.data as any).data
        admissionTax.value = (admission.data as any).data
    } catch (err: any) {
        errorText.value = err.response?.data?.error
            || 'Could not load the tax report for this range. Check your connection and press Refresh.'
        snackbarText.value = errorText.value
        snackbar.value = true
    } finally {
        loading.value = false
    }
}

function exportCsv() {
    const s = salesTax.value
    if (!s) return
    const a = admissionTax.value
    const sections: CsvSection[] = [
        {
            title: 'Tax report',
            headers: ['Field', 'Value'],
            rows: [
                ['Venue', branding.displayName],
                ['From', rangeFrom.value],
                ['To', rangeTo.value],
                ['Timezone', s.timezone],
            ],
        },
        {
            title: 'Admission tax',
            headers: ['Field', 'Value'],
            rows: a ? [
                ['Net tax', csvMoney(a.netTaxCents)],
                ['Collected', csvMoney(a.taxCollectedCents)],
                ['Refunded', csvMoney(a.refundedTaxCents)],
                ['Taxable ticket sales', csvMoney(a.taxableSalesCents)],
                ['Taxed tickets', a.taxedTicketCount],
                ['Current rate', ratePct(a.currentRateBps)],
                ['Jurisdiction', a.jurisdictionLabel || 'Not set'],
            ] : [['Admission tax', 'Not available']],
        },
        {
            title: 'Sales tax by category',
            headers: ['Category', 'Sales', 'Taxable sales', 'Collected', 'Refunded', 'Net tax'],
            rows: s.byCategory.map(c => [
                c.label, c.saleCount, csvMoney(c.taxableSalesCents),
                csvMoney(c.collectedTaxCents), csvMoney(c.refundedTaxCents), csvMoney(c.taxCents),
            ]),
        },
        {
            title: 'Sales tax by day',
            headers: ['Business date', 'Sales', 'Taxable sales', 'Collected', 'Refunded', 'Net tax'],
            rows: s.byDay.map(d => [
                d.businessDate, d.saleCount, csvMoney(d.taxableSalesCents),
                csvMoney(d.collectedTaxCents), csvMoney(d.refundedTaxCents), csvMoney(d.taxCents),
            ]),
        },
    ]
    downloadCsvSections(`tax-${rangeFrom.value}-to-${rangeTo.value}.csv`, sections)
}

onMounted(load)
</script>
