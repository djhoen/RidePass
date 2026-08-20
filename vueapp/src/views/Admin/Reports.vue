<template>
    <v-container fluid>
        <h1 class="text-h4 mb-4">Reporting</h1>
        <v-row>
            <!-- Left: report selector. Stays visible at all breakpoints; on small
                 screens the right column wraps below it. -->
            <v-col cols="12" md="3" lg="2">
                <v-card>
                    <v-list nav density="compact" :selected="[selected]">
                        <v-list-item v-for="r in reports" :key="r.key" :value="r.key"
                            :prepend-icon="r.icon" :title="r.title" :subtitle="r.subtitle"
                            @click="selectReport(r.key)"></v-list-item>
                    </v-list>
                </v-card>
            </v-col>

            <!-- Right: selected report. KeepAlive so range pickers / loaded data
                 survive switching between reports. -->
            <v-col cols="12" md="9" lg="10">
                <KeepAlive>
                    <component :is="activeComponent"
                        :initial-event-id="selected === 'waiver-signatures' ? activeEventId : undefined"
                        @select-event="onSelectEvent" />
                </KeepAlive>
            </v-col>
        </v-row>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import SalesSummary from './Reports/SalesSummary.vue'
import EndOfDay from './Reports/EndOfDay.vue'
import RevenueByDepartment from './Reports/RevenueByDepartment.vue'
import TaxReport from './Reports/TaxReport.vue'
import WaiverSignatures from './Reports/WaiverSignatures.vue'
import DailyEvents from './Reports/DailyEvents.vue'
import ConcessionProfitability from './Reports/ConcessionProfitability.vue'
import ConcessionComps from './Reports/ConcessionComps.vue'
import ConcessionStaff from './Reports/ConcessionStaff.vue'
import BikeShopReports from '@/components/bikeshop/ReportsTab.vue'
import { branding } from '@/stores/branding'

type ReportKey = 'sales-summary' | 'revenue-by-department' | 'end-of-day' | 'tax' | 'waiver-signatures' | 'daily-events' | 'fnb-profit' | 'comps' | 'fnb-staff' | 'bike-shop'

const allReports: { key: ReportKey; title: string; subtitle: string; icon: string }[] = [
    { key: 'sales-summary', title: 'Sales Summary', subtitle: 'Revenue, top products, top events', icon: 'mdi-chart-line' },
    { key: 'revenue-by-department', title: 'Revenue by Department', subtitle: 'Which side of the business earned it', icon: 'mdi-office-building-outline' },
    { key: 'end-of-day',    title: 'End of Day',     subtitle: 'Daily close: revenue by category, tenders, staff, cash, QuickBooks', icon: 'mdi-cash-register' },
    { key: 'tax',           title: 'Tax',            subtitle: 'Admission tax and sales tax to remit', icon: 'mdi-receipt-text-outline' },
    { key: 'waiver-signatures', title: 'Waivers', subtitle: 'Who has signed for an event',         icon: 'mdi-file-sign' },
    { key: 'daily-events',  title: 'Daily Events',  subtitle: 'All events on a chosen date',       icon: 'mdi-calendar-today' },
    { key: 'fnb-profit',    title: 'F&B Profit',     subtitle: 'Food & Beverage margin by item',    icon: 'mdi-silverware-fork-knife' },
    { key: 'fnb-staff',     title: 'F&B Staff',      subtitle: 'Food & Beverage sales by employee', icon: 'mdi-account-cash' },
    { key: 'comps',         title: 'Void / Comp',    subtitle: 'Comped F&B sales + who approved them', icon: 'mdi-cash-remove' },
    { key: 'bike-shop',     title: 'Bike Shop',      subtitle: 'Valuation, margin, dead stock',     icon: 'mdi-bike' },
]

// Feature-gated reports hide when the tenant doesn't run that side of the business, so the
// list never offers a pane that could only ever be empty.
const FNB_KEYS: ReportKey[] = ['fnb-profit', 'fnb-staff', 'comps']
const reports = computed(() => allReports.filter(r =>
    (r.key !== 'bike-shop' || branding.bikeShopEnabled)
    && (!FNB_KEYS.includes(r.key) || branding.concessionsEnabled)))

const route = useRoute()
const router = useRouter()

// Source-of-truth for the selected report is the URL — `?report=<key>` so a
// deep link from elsewhere in the app (e.g. the calendar's "Rider Report"
// button) drops the admin straight onto the right pane.
const selected = ref<ReportKey>(parseReport(route.query.report as string | undefined))
const activeEventId = ref<string | null>(parseEventId(route.query.eventId as string | undefined))

function parseReport(v: string | undefined): ReportKey {
    if (v === 'waiver-signatures' || v === 'daily-events' || v === 'sales-summary' || v === 'end-of-day'
        || v === 'revenue-by-department'
        || v === 'tax' || v === 'fnb-profit' || v === 'comps' || v === 'fnb-staff' || v === 'bike-shop') return v
    return 'sales-summary'
}
function parseEventId(v: string | undefined): string | null {
    return typeof v === 'string' && v.length > 0 ? v : null
}

const activeComponent = computed(() => {
    switch (selected.value) {
        case 'end-of-day': return EndOfDay
        case 'revenue-by-department': return RevenueByDepartment
        case 'tax': return TaxReport
        case 'waiver-signatures': return WaiverSignatures
        case 'daily-events': return DailyEvents
        case 'fnb-profit': return ConcessionProfitability
        case 'fnb-staff': return ConcessionStaff
        case 'comps': return ConcessionComps
        case 'bike-shop': return BikeShopReports
        default: return SalesSummary
    }
})

function selectReport(key: ReportKey) {
    if (selected.value === key) return
    selected.value = key
    // Preserve eventId on the URL only when it's relevant to the current report.
    const query: Record<string, string> = { report: key }
    if (key === 'waiver-signatures' && activeEventId.value) query.eventId = activeEventId.value
    router.replace({ path: route.path, query })
}

// Daily Events row click → jump to the standalone Rider Report (Admission), pre-filtered
// to that event's day + event.
function onSelectEvent(eventId: string, date?: string) {
    router.push({ path: '/Admin/RiderReport', query: date ? { date, eventId } : { eventId } })
}

// A deep link to a feature-hidden report (stale bookmark, feature turned off later) falls
// back to the summary — but only once branding has loaded, so a legit link isn't bounced
// while the feature flags are still their pre-load defaults.
watch([reports, selected, () => branding.loaded], () => {
    if (branding.loaded && !reports.value.some(r => r.key === selected.value)) {
        selectReport('sales-summary')
    }
}, { immediate: true })

// Honor external query updates (e.g. browser back/forward) without losing the
// in-memory state of the other panes.
watch(() => route.query, (q) => {
    selected.value = parseReport(q.report as string | undefined)
    activeEventId.value = parseEventId(q.eventId as string | undefined)
})

onMounted(() => {
    // Old deep links to the retired Event Riders pane land on its successor, the
    // standalone Rider Report page, keeping any eventId they carried.
    if (route.query.report === 'event-riders') {
        router.replace({ path: '/Admin/RiderReport',
            query: activeEventId.value ? { eventId: activeEventId.value } : {} })
        return
    }
    // Make sure the URL is canonical even when we landed without a ?report= param.
    if (!route.query.report) {
        router.replace({ path: route.path, query: { ...route.query, report: selected.value } })
    }
})
</script>
