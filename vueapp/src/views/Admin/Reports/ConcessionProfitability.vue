<template>
    <div>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h2 class="text-h5">F&amp;B Profitability</h2>
            <v-spacer></v-spacer>
            <v-select v-model="preset" :items="presetOptions" label="Range" density="compact" hide-details
                style="max-width: 200px" @update:model-value="applyPreset"></v-select>
            <v-text-field v-model="rangeFrom" type="date" label="From" density="compact" hide-details
                style="max-width: 160px" @change="preset = 'custom'"></v-text-field>
            <v-text-field v-model="rangeTo" type="date" label="To" density="compact" hide-details
                style="max-width: 160px" @change="preset = 'custom'"></v-text-field>
            <v-btn color="primary" :loading="loading" @click="load">Refresh</v-btn>
            <v-btn variant="outlined" prepend-icon="mdi-file-delimited-outline"
                :disabled="loading || !report" @click="exportCsv">Export CSV</v-btn>
        </div>

        <template v-if="report">
            <!-- KPI cards -->
            <v-row class="mb-2">
                <v-col cols="12" sm="6" md="3">
                    <v-card><v-card-text>
                        <div class="text-caption text-medium-emphasis">Net sales</div>
                        <div class="text-h4">{{ money(report.netSalesCents) }}</div>
                        <div class="text-caption text-medium-emphasis">pre-tax item revenue</div>
                    </v-card-text></v-card>
                </v-col>
                <v-col cols="12" sm="6" md="3">
                    <v-card><v-card-text>
                        <div class="text-caption text-medium-emphasis">Cost of goods</div>
                        <div class="text-h4">{{ money(report.cogsCents) }}</div>
                        <div class="text-caption text-medium-emphasis">theoretical, from recipes</div>
                    </v-card-text></v-card>
                </v-col>
                <v-col cols="12" sm="6" md="3">
                    <v-card><v-card-text>
                        <div class="text-caption text-medium-emphasis">Gross profit</div>
                        <div class="text-h4">{{ money(report.grossProfitCents) }}</div>
                        <div class="text-caption text-medium-emphasis">{{ report.marginPct.toFixed(1) }}% margin</div>
                    </v-card-text></v-card>
                </v-col>
                <v-col cols="12" sm="6" md="3">
                    <v-card><v-card-text>
                        <div class="text-caption text-medium-emphasis">Orders</div>
                        <div class="text-h4">{{ report.orderCount }}</div>
                        <div class="text-caption text-medium-emphasis">{{ money(report.avgOrderValueCents) }} avg</div>
                    </v-card-text></v-card>
                </v-col>
            </v-row>
            <v-row class="mb-4">
                <v-col cols="12" sm="6" md="3">
                    <v-card variant="tonal"><v-card-text>
                        <div class="text-caption text-medium-emphasis">Gross sales</div>
                        <div class="text-h6">{{ money(report.grossSalesCents) }}</div>
                        <div class="text-caption text-medium-emphasis">incl. tax + tips</div>
                    </v-card-text></v-card>
                </v-col>
                <v-col cols="12" sm="6" md="3">
                    <v-card variant="tonal"><v-card-text>
                        <div class="text-caption text-medium-emphasis">Tax collected</div>
                        <div class="text-h6">{{ money(report.taxCents) }}</div>
                    </v-card-text></v-card>
                </v-col>
                <v-col cols="12" sm="6" md="3">
                    <v-card variant="tonal"><v-card-text>
                        <div class="text-caption text-medium-emphasis">Tips</div>
                        <div class="text-h6">{{ money(report.tipsCents) }}</div>
                    </v-card-text></v-card>
                </v-col>
                <v-col cols="12" sm="6" md="3">
                    <v-card variant="tonal"><v-card-text>
                        <div class="text-caption text-medium-emphasis">Refunds</div>
                        <div class="text-h6">{{ report.refundedCount }} · {{ money(report.refundedAmountCents) }}</div>
                    </v-card-text></v-card>
                </v-col>
            </v-row>

            <!-- Sales by hour (daypart) -->
            <v-card class="mb-4">
                <v-card-title>Sales by hour ({{ branding.timezone }})</v-card-title>
                <v-card-text>
                    <div v-if="report.hours.length === 0" class="text-medium-emphasis">No sales in range.</div>
                    <div v-else style="position: relative; height: 280px;">
                        <Bar :data="hourChartData" :options="hourChartOptions" />
                    </div>
                </v-card-text>
            </v-card>

            <!-- By item -->
            <v-card class="mb-4">
                <v-card-title>By item</v-card-title>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th>Item</th>
                            <th style="width: 90px" class="text-right">Qty</th>
                            <th style="width: 120px" class="text-right">Revenue</th>
                            <th style="width: 120px" class="text-right">COGS</th>
                            <th style="width: 120px" class="text-right">Profit</th>
                            <th style="width: 100px" class="text-right">Margin</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="(i, idx) in report.items" :key="idx">
                            <td>{{ i.name }}</td>
                            <td class="text-right">{{ i.qtySold }}</td>
                            <td class="text-right">{{ money(i.revenueCents) }}</td>
                            <td class="text-right">{{ money(i.cogsCents) }}</td>
                            <td class="text-right">{{ money(i.profitCents) }}</td>
                            <td class="text-right">{{ i.revenueCents > 0 ? i.marginPct.toFixed(1) + '%' : '—' }}</td>
                        </tr>
                        <tr v-if="report.items.length === 0">
                            <td colspan="6" class="text-center text-medium-emphasis py-4">No items sold in range.</td>
                        </tr>
                    </tbody>
                </v-table>
                <div class="text-caption text-medium-emphasis px-4 py-2">
                    Combo components show their cost with no separate revenue; the combo's revenue sits on the entrée.
                    COGS uses each item's current recipe cost.
                </div>
            </v-card>

            <!-- By category + payment method -->
            <v-row>
                <v-col cols="12" md="7">
                    <v-card>
                        <v-card-title>By category</v-card-title>
                        <v-table density="compact">
                            <thead>
                                <tr>
                                    <th>Category</th>
                                    <th style="width: 120px" class="text-right">Revenue</th>
                                    <th style="width: 120px" class="text-right">COGS</th>
                                    <th style="width: 120px" class="text-right">Profit</th>
                                    <th style="width: 100px" class="text-right">Margin</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr v-for="(c, idx) in report.categories" :key="idx">
                                    <td>{{ c.category }}</td>
                                    <td class="text-right">{{ money(c.revenueCents) }}</td>
                                    <td class="text-right">{{ money(c.cogsCents) }}</td>
                                    <td class="text-right">{{ money(c.profitCents) }}</td>
                                    <td class="text-right">{{ c.revenueCents > 0 ? c.marginPct.toFixed(1) + '%' : '—' }}</td>
                                </tr>
                                <tr v-if="report.categories.length === 0">
                                    <td colspan="5" class="text-center text-medium-emphasis py-4">—</td>
                                </tr>
                            </tbody>
                        </v-table>
                    </v-card>
                </v-col>
                <v-col cols="12" md="5">
                    <v-card>
                        <v-card-title>By payment method</v-card-title>
                        <v-table density="compact">
                            <thead>
                                <tr>
                                    <th>Method</th>
                                    <th style="width: 90px" class="text-right">Sales</th>
                                    <th style="width: 130px" class="text-right">Amount</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr v-for="(p, idx) in report.payments" :key="idx">
                                    <td>{{ paymentLabel(p.method) }}</td>
                                    <td class="text-right">{{ p.count }}</td>
                                    <td class="text-right">{{ money(p.amountCents) }}</td>
                                </tr>
                                <tr v-if="report.payments.length === 0">
                                    <td colspan="3" class="text-center text-medium-emphasis py-4">—</td>
                                </tr>
                            </tbody>
                        </v-table>
                    </v-card>
                </v-col>
            </v-row>
        </template>

        <v-snackbar v-model="snackbar" color="error" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { Bar } from 'vue-chartjs'
import { registerChartJs } from '@/helpers/ChartSetup'
import { ReportsService, type ConcessionProfitabilityReport } from '@/services/ReportsService'
import { branding } from '@/stores/branding'
import { downloadCsvSections, csvMoney, type CsvSection } from '@/helpers/csv'

registerChartJs()

const service = new ReportsService()

const presetOptions = [
    { title: 'Today', value: 'today' },
    { title: 'Last 7 days', value: '7d' },
    { title: 'Last 30 days', value: '30d' },
    { title: 'This month', value: 'thismonth' },
    { title: 'Last month', value: 'lastmonth' },
    { title: 'Custom', value: 'custom' },
]
const preset = ref<string>('7d')

const today = dayjs()
const rangeFrom = ref(today.subtract(6, 'day').format('YYYY-MM-DD'))
const rangeTo = ref(today.format('YYYY-MM-DD'))

const report = ref<ConcessionProfitabilityReport | null>(null)
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')

onMounted(load)

function tz() { return branding.timezone || 'UTC' }
function money(cents: number) { return `$${(cents / 100).toFixed(2)}` }

function applyPreset(v: string) {
    const t = dayjs()
    switch (v) {
        case 'today':
            rangeFrom.value = t.format('YYYY-MM-DD')
            rangeTo.value = t.format('YYYY-MM-DD')
            break
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
    }
    load()
}

async function load() {
    loading.value = true
    try {
        if (rangeFrom.value && rangeTo.value && rangeFrom.value > rangeTo.value) {
            snackbarText.value = '"From" must be on or before "To".'
            snackbar.value = true
            return
        }
        const fromUtc = dayjs.tz(rangeFrom.value + 'T00:00', tz()).utc().toISOString()
        // rangeTo is inclusive; add a day for the exclusive upper bound the query expects.
        const toUtc = dayjs.tz(rangeTo.value + 'T00:00', tz()).add(1, 'day').utc().toISOString()
        const r = await service.getConcessionProfitability(fromUtc, toUtc)
        report.value = (r.data as any).data
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Failed to load the F&B profitability report.'
        snackbar.value = true
    } finally {
        loading.value = false
    }
}

const PAYMENT_LABELS: Record<string, string> = {
    cash: 'Cash',
    stripe: 'Card',
    stripe_direct: 'Card (direct)',
}
function paymentLabel(m: string): string {
    return PAYMENT_LABELS[m] || m.replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase())
}

function hourLabel(h: number): string {
    if (h === 0) return '12a'
    if (h === 12) return '12p'
    return h < 12 ? `${h}a` : `${h - 12}p`
}

const hourChartData = computed(() => {
    const rows = report.value?.hours ?? []
    return {
        labels: rows.map(r => hourLabel(r.hour)),
        datasets: [{
            label: 'Net sales ($)',
            data: rows.map(r => r.revenueCents / 100),
            backgroundColor: '#43A047',
        }],
    }
})

const hourChartOptions = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    scales: { y: { beginAtZero: true, title: { display: true, text: 'Net sales ($)' } } },
}

function exportCsv() {
    const r = report.value
    if (!r) return
    const sections: CsvSection[] = [
        {
            title: 'F&B profitability',
            headers: ['Field', 'Value'],
            rows: [
                ['From', rangeFrom.value],
                ['To', rangeTo.value],
                ['Net sales', csvMoney(r.netSalesCents)],
                ['Gross sales', csvMoney(r.grossSalesCents)],
                ['Tax', csvMoney(r.taxCents)],
                ['Tips', csvMoney(r.tipsCents)],
                ['Cost of goods', csvMoney(r.cogsCents)],
                ['Gross profit', csvMoney(r.grossProfitCents)],
                ['Margin %', r.marginPct.toFixed(1)],
                ['Orders', r.orderCount],
                ['Avg order value', csvMoney(r.avgOrderValueCents)],
                ['Refunds', r.refundedCount],
                ['Refunded amount', csvMoney(r.refundedAmountCents)],
            ],
        },
        {
            title: 'By item',
            headers: ['Item', 'Qty sold', 'Revenue', 'COGS', 'Profit', 'Margin %'],
            rows: r.items.map(i => [i.name, i.qtySold, csvMoney(i.revenueCents), csvMoney(i.cogsCents),
                csvMoney(i.profitCents), i.marginPct.toFixed(1)]),
        },
        {
            title: 'By category',
            headers: ['Category', 'Revenue', 'COGS', 'Profit', 'Margin %'],
            rows: r.categories.map(c => [c.category, csvMoney(c.revenueCents), csvMoney(c.cogsCents),
                csvMoney(c.profitCents), c.marginPct.toFixed(1)]),
        },
        {
            title: 'By payment method',
            headers: ['Method', 'Count', 'Amount'],
            rows: r.payments.map(p => [paymentLabel(p.method), p.count, csvMoney(p.amountCents)]),
        },
        {
            title: 'By hour',
            headers: ['Hour', 'Orders', 'Net sales'],
            rows: r.hours.map(h => [hourLabel(h.hour), h.orderCount, csvMoney(h.revenueCents)]),
        },
    ]
    downloadCsvSections(`fnb-profit-${rangeFrom.value}-to-${rangeTo.value}.csv`, sections)
}
</script>
