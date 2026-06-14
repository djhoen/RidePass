<template>
    <v-container>
        <h1 class="text-h4 mb-4">Analytics</h1>

        <div class="d-flex align-center mb-4 flex-wrap ga-3">
            <v-select v-model="analyticsPreset" :items="analyticsPresetOptions" label="Range" density="compact"
                hide-details style="max-width: 200px" @update:model-value="applyAnalyticsPreset"></v-select>
            <v-text-field v-model="analyticsFrom" type="date" label="From" density="compact" hide-details
                style="max-width: 160px" @change="analyticsPreset = 'custom'"></v-text-field>
            <v-text-field v-model="analyticsTo" type="date" label="To" density="compact" hide-details
                style="max-width: 160px" @change="analyticsPreset = 'custom'"></v-text-field>
            <v-spacer></v-spacer>
            <v-btn color="primary" :loading="loadingAnalytics" @click="loadAnalytics">Refresh</v-btn>
        </div>

        <v-row v-if="analytics" class="mb-4">
            <v-col cols="12" sm="6" md="3">
                <v-card><v-card-text>
                    <div class="text-caption text-medium-emphasis">Platform Revenue</div>
                    <div class="text-h4">${{ (analytics.totalRevenueCents / 100).toFixed(2) }}</div>
                </v-card-text></v-card>
            </v-col>
            <v-col cols="12" sm="6" md="3">
                <v-card><v-card-text>
                    <div class="text-caption text-medium-emphasis">Tenants</div>
                    <div class="text-h4">{{ analytics.activeTenants }} / {{ analytics.totalTenants }}</div>
                    <div class="text-caption text-medium-emphasis">active / total</div>
                </v-card-text></v-card>
            </v-col>
            <v-col cols="12" sm="6" md="3">
                <v-card><v-card-text>
                    <div class="text-caption text-medium-emphasis">Passes / Tickets</div>
                    <div class="text-h4">{{ analytics.passesSold }} / {{ analytics.ticketsSold }}</div>
                </v-card-text></v-card>
            </v-col>
            <v-col cols="12" sm="6" md="3">
                <v-card><v-card-text>
                    <div class="text-caption text-medium-emphasis">Refunds / Disputes</div>
                    <div class="text-h4">{{ analytics.refundedCount }} / {{ analytics.disputedCount }}</div>
                </v-card-text></v-card>
            </v-col>
        </v-row>

        <v-card class="mb-4" v-if="analytics">
            <v-card-title>Daily Revenue (UTC)</v-card-title>
            <v-card-text>
                <div style="position: relative; height: 320px;">
                    <Line v-if="platformRevenueChart" :data="platformRevenueChart" :options="revenueChartOptions" />
                </div>
            </v-card-text>
        </v-card>

        <v-card class="mb-4" v-if="analytics">
            <v-card-title>Revenue by Tenant</v-card-title>
            <v-card-text>
                <div v-if="analytics.tenantBreakdown.length === 0" class="text-medium-emphasis">No tenant sales.</div>
                <div v-else style="position: relative; height: 320px;">
                    <Bar :data="tenantChart" :options="horizontalBarOptions" />
                </div>
            </v-card-text>
        </v-card>

        <v-card v-if="analytics">
            <v-card-title>Tenant Breakdown</v-card-title>
            <v-table density="compact">
                <thead>
                    <tr>
                        <th @click="sortBreakdown('displayName')" style="cursor: pointer">Tenant</th>
                        <th @click="sortBreakdown('subdomain')" style="cursor: pointer">Subdomain</th>
                        <th @click="sortBreakdown('revenueCents')" style="cursor: pointer; width: 140px">Revenue</th>
                        <th @click="sortBreakdown('passesSold')" style="cursor: pointer; width: 100px">Passes</th>
                        <th @click="sortBreakdown('ticketsSold')" style="cursor: pointer; width: 100px">Tickets</th>
                        <th @click="sortBreakdown('refundedCount')" style="cursor: pointer; width: 100px">Refunds</th>
                        <th @click="sortBreakdown('disputedCount')" style="cursor: pointer; width: 100px">Disputes</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="r in sortedBreakdown" :key="r.tenantId">
                        <td>{{ r.displayName }}</td>
                        <td><code>{{ r.subdomain }}</code></td>
                        <td>${{ (r.revenueCents / 100).toFixed(2) }}</td>
                        <td>{{ r.passesSold }}</td>
                        <td>{{ r.ticketsSold }}</td>
                        <td>{{ r.refundedCount }}</td>
                        <td>{{ r.disputedCount }}</td>
                    </tr>
                    <tr v-if="analytics.tenantBreakdown.length === 0">
                        <td colspan="7" class="text-center text-medium-emphasis py-8">No tenants.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { Line, Bar } from 'vue-chartjs'
import { ReportsService, type PlatformAnalyticsSummary, type TenantBreakdownRow } from '@/services/ReportsService'
import { registerChartJs } from '@/helpers/ChartSetup'

registerChartJs()

const reportsService = new ReportsService()

const analytics = ref<PlatformAnalyticsSummary | null>(null)
const loadingAnalytics = ref(false)
const analyticsPresetOptions = [
    { title: 'Last 7 days', value: '7d' },
    { title: 'Last 30 days', value: '30d' },
    { title: 'This month', value: 'thismonth' },
    { title: 'Year to date', value: 'ytd' },
    { title: 'Custom', value: 'custom' },
]
const analyticsPreset = ref('30d')
const _today = dayjs()
const analyticsFrom = ref(_today.subtract(29, 'day').format('YYYY-MM-DD'))
const analyticsTo = ref(_today.add(1, 'day').format('YYYY-MM-DD'))

const breakdownSortKey = ref<keyof TenantBreakdownRow>('revenueCents')
const breakdownSortDir = ref<'asc' | 'desc'>('desc')

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(loadAnalytics)

function applyAnalyticsPreset(v: string) {
    const t = dayjs()
    switch (v) {
        case '7d':
            analyticsFrom.value = t.subtract(6, 'day').format('YYYY-MM-DD')
            analyticsTo.value = t.add(1, 'day').format('YYYY-MM-DD')
            break
        case '30d':
            analyticsFrom.value = t.subtract(29, 'day').format('YYYY-MM-DD')
            analyticsTo.value = t.add(1, 'day').format('YYYY-MM-DD')
            break
        case 'thismonth':
            analyticsFrom.value = t.startOf('month').format('YYYY-MM-DD')
            analyticsTo.value = t.endOf('month').add(1, 'day').format('YYYY-MM-DD')
            break
        case 'ytd':
            analyticsFrom.value = t.startOf('year').format('YYYY-MM-DD')
            analyticsTo.value = t.add(1, 'day').format('YYYY-MM-DD')
            break
    }
    loadAnalytics()
}

async function loadAnalytics() {
    loadingAnalytics.value = true
    try {
        const fromUtc = dayjs.utc(analyticsFrom.value).toISOString()
        const toUtc = dayjs.utc(analyticsTo.value).toISOString()
        const r = await reportsService.getPlatformAnalytics(fromUtc, toUtc)
        analytics.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load analytics.', 'error')
    } finally {
        loadingAnalytics.value = false
    }
}

function sortBreakdown(key: keyof TenantBreakdownRow) {
    if (breakdownSortKey.value === key) {
        breakdownSortDir.value = breakdownSortDir.value === 'asc' ? 'desc' : 'asc'
    } else {
        breakdownSortKey.value = key
        breakdownSortDir.value = 'desc'
    }
}

const sortedBreakdown = computed<TenantBreakdownRow[]>(() => {
    if (!analytics.value) return []
    const rows = [...analytics.value.tenantBreakdown]
    const key = breakdownSortKey.value
    const dir = breakdownSortDir.value === 'asc' ? 1 : -1
    rows.sort((a: any, b: any) => {
        const av = a[key]; const bv = b[key]
        if (typeof av === 'number' && typeof bv === 'number') return (av - bv) * dir
        return String(av).localeCompare(String(bv)) * dir
    })
    return rows
})

const platformRevenueChart = computed(() => {
    if (!analytics.value) return null
    const points = analytics.value.dailyRevenue
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
                label: 'Passes',
                data: points.map(p => p.passesSold),
                borderColor: '#43A047',
                backgroundColor: 'transparent',
                tension: 0.3,
                yAxisID: 'y1',
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
        y: { beginAtZero: true, position: 'left' as const, title: { display: true, text: 'Revenue ($)' } },
        y1: { beginAtZero: true, position: 'right' as const, title: { display: true, text: 'Count' }, grid: { drawOnChartArea: false } },
    },
}

const tenantChart = computed(() => {
    if (!analytics.value) return { labels: [], datasets: [] }
    const rows = [...analytics.value.tenantBreakdown]
        .sort((a, b) => b.revenueCents - a.revenueCents)
        .slice(0, 15)
    return {
        labels: rows.map(r => r.displayName),
        datasets: [{
            label: 'Revenue ($)',
            data: rows.map(r => r.revenueCents / 100),
            backgroundColor: '#1976D2',
        }],
    }
})

const horizontalBarOptions = {
    indexAxis: 'y' as const,
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    scales: { x: { beginAtZero: true } },
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
