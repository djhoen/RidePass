<template>
    <div>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h2 class="text-h5">Sales Summary</h2>
            <v-spacer></v-spacer>
            <v-select v-model="preset" :items="presetOptions" label="Range" density="compact" hide-details
                style="max-width: 200px" @update:model-value="applyPreset"></v-select>
            <v-text-field v-model="rangeFrom" type="date" label="From" density="compact" hide-details
                style="max-width: 160px" @change="preset = 'custom'"></v-text-field>
            <v-text-field v-model="rangeTo" type="date" label="To" density="compact" hide-details
                style="max-width: 160px" @change="preset = 'custom'"></v-text-field>
            <v-btn color="primary" :loading="loading" @click="load">Refresh</v-btn>
            <v-btn variant="outlined" prepend-icon="mdi-file-delimited-outline"
                :disabled="loading || !summary" @click="exportCsv">Export CSV</v-btn>
        </div>

        <v-row v-if="summary" class="mb-4">
            <v-col cols="12" sm="6" md="3">
                <v-card><v-card-text>
                    <div class="text-caption text-medium-emphasis">Revenue</div>
                    <div class="text-h4">${{ (summary.totalRevenueCents / 100).toFixed(2) }}</div>
                </v-card-text></v-card>
            </v-col>
            <v-col cols="12" sm="6" md="3">
                <v-card><v-card-text>
                    <div class="text-caption text-medium-emphasis">Tickets / Season passes</div>
                    <div class="text-h4">{{ summary.ticketsSold }} / {{ summary.passesSold }}</div>
                    <div class="text-caption text-medium-emphasis">sold in range</div>
                </v-card-text></v-card>
            </v-col>
            <v-col cols="12" sm="6" md="3">
                <v-card><v-card-text>
                    <div class="text-caption text-medium-emphasis">Unique Riders</div>
                    <div class="text-h4">{{ summary.uniqueRiders }}</div>
                </v-card-text></v-card>
            </v-col>
            <v-col cols="12" sm="6" md="3">
                <v-card><v-card-text>
                    <div class="text-caption text-medium-emphasis">Refunds / Disputes</div>
                    <div class="text-h4">{{ summary.refundedCount }} / {{ summary.disputedCount }}</div>
                    <div class="text-caption text-medium-emphasis">
                        ${{ (summary.refundedAmountCents / 100).toFixed(2) }} refunded
                    </div>
                </v-card-text></v-card>
            </v-col>
        </v-row>

        <v-card class="mb-4" v-if="summary">
            <v-card-title>Daily Revenue ({{ branding.timezone }})</v-card-title>
            <v-card-subtitle class="pb-2">
                Total revenue in blue, with each profit center below it. The center lines add up to
                the total.
            </v-card-subtitle>
            <v-card-text>
                <div style="position: relative; height: 340px;">
                    <Line v-if="revenueChartData" :data="revenueChartData" :options="revenueChartOptions" />
                </div>
            </v-card-text>
        </v-card>

        <!-- The table view of the same series. Three palette colors sit below 3:1 against a white
             surface, so the chart's identity must not rest on color alone; this table is that
             relief, and it doubles as the numbers behind the lines. -->
        <v-card class="mb-4" v-if="summary && summary.revenueByProfitCenter.length">
            <v-card-title>Revenue by Profit Center</v-card-title>
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Profit center</th>
                        <th style="width: 140px">Revenue</th>
                        <th style="width: 110px">% of total</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="c in summary.revenueByProfitCenter" :key="c.key">
                        <td>
                            <span class="swatch mr-2" :style="{ background: seriesColor(c.color, isDark) }"></span>
                            {{ c.label }}
                        </td>
                        <td>${{ (c.totalCents / 100).toFixed(2) }}</td>
                        <td>{{ pctOfTotal(c.totalCents) }}</td>
                    </tr>
                </tbody>
                <tfoot>
                    <tr class="font-weight-bold">
                        <td>
                            <span class="swatch mr-2" :style="{ background: totalColor }"></span>
                            Total revenue
                        </td>
                        <td>${{ (summary.totalRevenueCents / 100).toFixed(2) }}</td>
                        <td></td>
                    </tr>
                </tfoot>
            </v-table>
        </v-card>

        <v-card class="mb-4" v-if="summary && summary.revenueByType.length">
            <v-card-title>Revenue by Type</v-card-title>
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Type</th>
                        <th style="width: 120px">Sales</th>
                        <th style="width: 140px">Revenue</th>
                        <th style="width: 110px">% of total</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="r in summary.revenueByType" :key="r.kind">
                        <td>{{ kindLabel(r.kind) }}</td>
                        <td>{{ r.saleCount }}</td>
                        <td>${{ (r.revenueCents / 100).toFixed(2) }}</td>
                        <td>{{ pctOfTotal(r.revenueCents) }}</td>
                    </tr>
                </tbody>
                <tfoot>
                    <tr class="font-weight-bold">
                        <td>Total</td>
                        <td></td>
                        <td>${{ (summary.totalRevenueCents / 100).toFixed(2) }}</td>
                        <td></td>
                    </tr>
                </tfoot>
            </v-table>
        </v-card>

        <v-card class="mb-4" v-if="summary && summary.topPassProducts.length">
            <v-card-title>Top Season Passes</v-card-title>
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Pass</th>
                        <th style="width: 120px">Sold</th>
                        <th style="width: 140px">Revenue</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="p in summary.topPassProducts" :key="p.productId">
                        <td>{{ p.productName }}</td>
                        <td>{{ p.soldCount }}</td>
                        <td>${{ (p.revenueCents / 100).toFixed(2) }}</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-row v-if="summary">
            <v-col cols="12">
                <v-card>
                    <v-card-title>Top Events</v-card-title>
                    <v-card-text>
                        <div v-if="summary.topEvents.length === 0" class="text-medium-emphasis">No event sales in range.</div>
                        <div v-else style="position: relative; height: 320px;">
                            <Bar :data="eventsChartData" :options="horizontalBarOptions" />
                        </div>
                    </v-card-text>
                </v-card>
            </v-col>
        </v-row>

        <v-row v-if="summary" class="mt-2">
            <v-col cols="12">
                <v-card>
                    <v-table density="compact">
                        <thead>
                            <tr><th>Event</th><th style="width: 140px">When</th><th style="width: 100px">Sold</th><th style="width: 120px">Revenue</th></tr>
                        </thead>
                        <tbody>
                            <tr v-for="e in summary.topEvents" :key="e.eventId">
                                <td>{{ e.eventTitle }}</td>
                                <td>{{ formatDate(e.eventStartUtc) }}</td>
                                <td>{{ e.soldCount }}</td>
                                <td>${{ (e.revenueCents / 100).toFixed(2) }}</td>
                            </tr>
                            <tr v-if="summary.topEvents.length === 0">
                                <td colspan="4" class="text-center text-medium-emphasis py-4">—</td>
                            </tr>
                        </tbody>
                    </v-table>
                </v-card>
            </v-col>
        </v-row>

        <v-snackbar v-model="snackbar" color="error" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { Line, Bar } from 'vue-chartjs'
import { registerChartJs } from '@/helpers/ChartSetup'
import { useTheme } from 'vuetify'
import { ReportsService, type TenantReportSummary } from '@/services/ReportsService'
import { branding } from '@/stores/branding'
import { downloadCsvSections, csvMoney, type CsvSection } from '@/helpers/csv'
import {
    seriesColor, withAlpha, TOTAL_SERIES_COLOR, TOTAL_SERIES_COLOR_DARK,
} from '@/helpers/profitCenterColor'

registerChartJs()

// Dark mode is a real state here (a tenant can set themeMode dark), and the palette's dark steps
// are selected values, not a computed lightening: the light hexes are genuinely unreadable on the
// dark surface (violet lands at 1.88:1).
const theme = useTheme()
const isDark = computed(() => theme.current.value.dark)
const totalColor = computed(() => isDark.value ? TOTAL_SERIES_COLOR_DARK : TOTAL_SERIES_COLOR)

const service = new ReportsService()

const presetOptions = [
    { title: 'Last 7 days', value: '7d' },
    { title: 'Last 30 days', value: '30d' },
    { title: 'This month', value: 'thismonth' },
    { title: 'Last month', value: 'lastmonth' },
    { title: 'Year to date', value: 'ytd' },
    { title: 'Custom', value: 'custom' },
]
const preset = ref<string>('30d')

const today = dayjs()
const rangeFrom = ref(today.subtract(29, 'day').format('YYYY-MM-DD'))
const rangeTo = ref(today.add(1, 'day').format('YYYY-MM-DD'))

const summary = ref<TenantReportSummary | null>(null)
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')

onMounted(load)

function tz() { return branding.timezone || 'UTC' }

function applyPreset(v: string) {
    const t = dayjs()
    // rangeTo is the inclusive last day shown to the admin; load() converts it to the
    // exclusive query bound by adding a day. (Previously these set an exclusive bound that
    // displayed one day past what was intended.)
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
        const r = await service.getTenantSummary(fromUtc, toUtc)
        summary.value = (r.data as any).data
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Failed to load report.'
        snackbar.value = true
    } finally {
        loading.value = false
    }
}

function formatDate(utc: string): string {
    return dayjs.utc(utc).tz(tz()).format('YYYY-MM-DD HH:mm')
}

// Everything the page already holds, stacked into one file: revenue by type, the daily series,
// top season passes and top events. No server round trip, so there is nothing here that can fail
// after the report itself loaded.
function exportCsv() {
    const s = summary.value
    if (!s) return
    const sections: CsvSection[] = [
        {
            title: 'Sales summary',
            headers: ['Field', 'Value'],
            rows: [
                ['Venue', branding.displayName],
                ['From', rangeFrom.value],
                ['To', rangeTo.value],
                ['Timezone', tz()],
                ['Total revenue', csvMoney(s.totalRevenueCents)],
                ['Tickets sold', s.ticketsSold],
                ['Season passes sold', s.passesSold],
                ['Unique riders', s.uniqueRiders],
                ['Refunds', s.refundedCount],
                ['Refunded amount', csvMoney(s.refundedAmountCents)],
                ['Disputes', s.disputedCount],
            ],
        },
        {
            title: 'Revenue by type',
            headers: ['Type', 'Sales', 'Revenue'],
            rows: s.revenueByType.map(r => [kindLabel(r.kind), r.saleCount, csvMoney(r.revenueCents)]),
        },
        {
            title: 'Revenue by profit center',
            headers: ['Profit center', 'Revenue'],
            rows: s.revenueByProfitCenter.map(c => [c.label, csvMoney(c.totalCents)]),
        },
        {
            // One column per center beside the total, so the exported sheet carries the same
            // breakdown the chart draws rather than just its total line.
            title: 'Daily revenue',
            headers: ['Date', 'Total revenue', ...s.revenueByProfitCenter.map(c => c.label), 'Tickets'],
            rows: s.dailyRevenue.map((p, i) => [
                p.date,
                csvMoney(p.revenueCents),
                ...s.revenueByProfitCenter.map(c => csvMoney(c.points[i]?.revenueCents ?? 0)),
                p.ticketsSold,
            ]),
        },
        {
            title: 'Top season passes',
            headers: ['Pass', 'Sold', 'Revenue'],
            rows: s.topPassProducts.map(p => [p.productName, p.soldCount, csvMoney(p.revenueCents)]),
        },
        {
            title: 'Top events',
            headers: ['Event', 'When', 'Sold', 'Revenue'],
            rows: s.topEvents.map(e => [e.eventTitle, formatDate(e.eventStartUtc), e.soldCount, csvMoney(e.revenueCents)]),
        },
    ]
    downloadCsvSections(`sales-summary-${rangeFrom.value}-to-${rangeTo.value}.csv`, sections)
}

const KIND_LABELS: Record<string, string> = {
    event_ticket: 'Event tickets',
    season_pass: 'Season passes',
    membership: 'Memberships',
    extras: 'Add-ons',
    concession: 'Food & Beverage',
    pass: 'Day passes',
    day_pass: 'Day passes',
    shop_sale: 'Bike shop sales',
    shop_rental: 'Bike shop rentals',
    shop_rental_deposit: 'Rental damage charges (shop)',
    shop_wo_deposit: 'Repair deposits',
    // Balancing rows for store credit spent at checkout: negative, so credit-funded value
    // nets out of total revenue instead of counting twice.
    credit_tender: 'Store credit applied',
}
function kindLabel(kind: string): string {
    return KIND_LABELS[kind] || kind.replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase())
}
function pctOfTotal(cents: number): string {
    const total = summary.value?.totalRevenueCents || 0
    if (total <= 0) return '0%'
    return Math.round((cents / total) * 100) + '%'
}

// Total revenue plus one line per profit center, all in dollars on ONE axis.
//
// The tickets-sold line that used to share this chart is gone, and with it the second y-axis: two
// different units on two scales make the crossings meaningless (a revenue line "overtaking" a
// count line means nothing), and adding the center lines to that would have been unreadable.
// Tickets sold is still on the stat tile above and in the CSV, where a count belongs.
const revenueChartData = computed(() => {
    if (!summary.value) return null
    const points = summary.value.dailyRevenue
    const centers = summary.value.revenueByProfitCenter

    return {
        labels: points.map(p => p.date),
        datasets: [
            {
                label: 'Total revenue',
                data: points.map(p => p.revenueCents / 100),
                borderColor: totalColor.value,
                backgroundColor: withAlpha(totalColor.value, 0.10),
                borderWidth: 2,
                fill: true,
                tension: 0.3,
                pointRadius: 0,
                pointHoverRadius: 5,
                // Drawn first so the thinner center lines sit on top of its fill rather than under it.
                order: 2,
            },
            // Each center keeps ITS OWN color whatever the range or the sort: identity follows the
            // center, never its rank, so narrowing the dates never repaints the survivors.
            ...centers.map(c => ({
                label: c.label,
                data: c.points.map(p => p.revenueCents / 100),
                borderColor: seriesColor(c.color, isDark.value),
                backgroundColor: seriesColor(c.color, isDark.value),
                borderWidth: 2,
                fill: false,
                tension: 0.3,
                pointRadius: 0,
                pointHoverRadius: 5,
                order: 1,
            })),
        ],
    }
})

const revenueChartOptions = computed(() => ({
    responsive: true,
    maintainAspectRatio: false,
    // Crosshair-style hover: one tooltip listing every series for the hovered day, so two centers
    // can be compared on a date without hitting each line exactly.
    interaction: { mode: 'index' as const, intersect: false },
    plugins: {
        legend: {
            display: true,
            position: 'bottom' as const,
            labels: { usePointStyle: true, boxWidth: 8, padding: 16 },
        },
        tooltip: {
            callbacks: {
                label: (ctx: any) => `${ctx.dataset.label}: $${Number(ctx.parsed.y).toFixed(2)}`,
            },
        },
    },
    scales: {
        y: {
            beginAtZero: true,
            title: { display: true, text: 'Revenue ($)' },
            position: 'left' as const,
        },
    },
}))

const eventsChartData = computed(() => {
    if (!summary.value) return { labels: [], datasets: [] }
    const rows = summary.value.topEvents
    return {
        labels: rows.map(r => r.eventTitle),
        datasets: [{
            label: 'Revenue ($)',
            data: rows.map(r => r.revenueCents / 100),
            backgroundColor: '#43A047',
        }],
    }
})

const horizontalBarOptions = {
    indexAxis: 'y' as const,
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    scales: {
        x: { beginAtZero: true },
    },
}
</script>

<style scoped>
/* The color chip beside a label. Text keeps its own ink; the swatch carries the identity. */
.swatch {
    display: inline-block;
    width: 12px;
    height: 12px;
    border-radius: 3px;
    vertical-align: middle;
    box-shadow: inset 0 0 0 1px rgba(var(--v-theme-on-surface), 0.2);
}
</style>
