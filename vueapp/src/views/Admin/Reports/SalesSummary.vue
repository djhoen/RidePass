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
                    <div class="text-caption text-medium-emphasis">Tickets Sold</div>
                    <div class="text-h4">{{ summary.ticketsSold }}</div>
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
            <v-card-text>
                <div style="position: relative; height: 320px;">
                    <Line v-if="revenueChartData" :data="revenueChartData" :options="revenueChartOptions" />
                </div>
            </v-card-text>
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
import { ReportsService, type TenantReportSummary } from '@/services/ReportsService'
import { branding } from '@/stores/branding'

registerChartJs()

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
    switch (v) {
        case '7d':
            rangeFrom.value = t.subtract(6, 'day').format('YYYY-MM-DD')
            rangeTo.value = t.add(1, 'day').format('YYYY-MM-DD')
            break
        case '30d':
            rangeFrom.value = t.subtract(29, 'day').format('YYYY-MM-DD')
            rangeTo.value = t.add(1, 'day').format('YYYY-MM-DD')
            break
        case 'thismonth':
            rangeFrom.value = t.startOf('month').format('YYYY-MM-DD')
            rangeTo.value = t.endOf('month').add(1, 'day').format('YYYY-MM-DD')
            break
        case 'lastmonth':
            rangeFrom.value = t.subtract(1, 'month').startOf('month').format('YYYY-MM-DD')
            rangeTo.value = t.startOf('month').format('YYYY-MM-DD')
            break
        case 'ytd':
            rangeFrom.value = t.startOf('year').format('YYYY-MM-DD')
            rangeTo.value = t.add(1, 'day').format('YYYY-MM-DD')
            break
    }
    load()
}

async function load() {
    loading.value = true
    try {
        const fromUtc = dayjs.tz(rangeFrom.value + 'T00:00', tz()).utc().toISOString()
        const toUtc = dayjs.tz(rangeTo.value + 'T00:00', tz()).utc().toISOString()
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

const KIND_LABELS: Record<string, string> = {
    event_ticket: 'Event tickets',
    season_pass: 'Season passes',
    membership: 'Memberships',
    extras: 'Add-ons',
    rental: 'Rentals',
    concession: 'Concessions',
    pass: 'Day passes',
    day_pass: 'Day passes',
}
function kindLabel(kind: string): string {
    return KIND_LABELS[kind] || kind.replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase())
}
function pctOfTotal(cents: number): string {
    const total = summary.value?.totalRevenueCents || 0
    if (total <= 0) return '0%'
    return Math.round((cents / total) * 100) + '%'
}

const revenueChartData = computed(() => {
    if (!summary.value) return null
    const points = summary.value.dailyRevenue
    return {
        labels: points.map(p => p.date),
        datasets: [
            {
                label: 'Revenue ($)',
                data: points.map(p => p.revenueCents / 100),
                borderColor: '#1976D2',
                backgroundColor: 'rgba(25, 118, 210, 0.15)',
                fill: true,
                tension: 0.3,
                yAxisID: 'y',
            },
            {
                label: 'Tickets',
                data: points.map(p => p.ticketsSold),
                borderColor: '#FB8C00',
                backgroundColor: 'transparent',
                tension: 0.3,
                yAxisID: 'y1',
            },
        ],
    }
})

const revenueChartOptions = {
    responsive: true,
    maintainAspectRatio: false,
    interaction: { mode: 'index' as const, intersect: false },
    scales: {
        y: {
            beginAtZero: true,
            title: { display: true, text: 'Revenue ($)' },
            position: 'left' as const,
        },
        y1: {
            beginAtZero: true,
            title: { display: true, text: 'Count' },
            position: 'right' as const,
            grid: { drawOnChartArea: false },
        },
    },
}

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
