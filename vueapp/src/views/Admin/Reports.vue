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
                        :initial-event-id="(selected === 'event-riders' || selected === 'waiver-signatures') ? activeEventId : undefined"
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
import EventRiders from './Reports/EventRiders.vue'
import WaiverSignatures from './Reports/WaiverSignatures.vue'
import DailyEvents from './Reports/DailyEvents.vue'
import ConcessionProfitability from './Reports/ConcessionProfitability.vue'
import ConcessionComps from './Reports/ConcessionComps.vue'
import ConcessionStaff from './Reports/ConcessionStaff.vue'

type ReportKey = 'sales-summary' | 'event-riders' | 'waiver-signatures' | 'daily-events' | 'fnb-profit' | 'comps' | 'fnb-staff'

const reports: { key: ReportKey; title: string; subtitle: string; icon: string }[] = [
    { key: 'sales-summary', title: 'Sales Summary', subtitle: 'Revenue, top products, top events', icon: 'mdi-chart-line' },
    { key: 'event-riders',  title: 'Event Riders',  subtitle: 'Roll call + check-in for an event', icon: 'mdi-account-group' },
    { key: 'waiver-signatures', title: 'Waivers', subtitle: 'Who has signed for an event',         icon: 'mdi-file-sign' },
    { key: 'daily-events',  title: 'Daily Events',  subtitle: 'All events on a chosen date',       icon: 'mdi-calendar-today' },
    { key: 'fnb-profit',    title: 'F&B Profit',     subtitle: 'Food & Beverage margin by item',    icon: 'mdi-silverware-fork-knife' },
    { key: 'fnb-staff',     title: 'F&B Staff',      subtitle: 'Food & Beverage sales by employee', icon: 'mdi-account-cash' },
    { key: 'comps',         title: 'Void / Comp',    subtitle: 'Comped F&B sales + who approved them', icon: 'mdi-cash-remove' },
]

const route = useRoute()
const router = useRouter()

// Source-of-truth for the selected report is the URL — `?report=<key>` so a
// deep link from elsewhere in the app (e.g. the calendar's "Rider Report"
// button) drops the admin straight onto the right pane.
const selected = ref<ReportKey>(parseReport(route.query.report as string | undefined))
const activeEventId = ref<string | null>(parseEventId(route.query.eventId as string | undefined))

function parseReport(v: string | undefined): ReportKey {
    if (v === 'event-riders' || v === 'waiver-signatures' || v === 'daily-events' || v === 'sales-summary' || v === 'fnb-profit' || v === 'comps' || v === 'fnb-staff') return v
    return 'sales-summary'
}
function parseEventId(v: string | undefined): string | null {
    return typeof v === 'string' && v.length > 0 ? v : null
}

const activeComponent = computed(() => {
    switch (selected.value) {
        case 'event-riders': return EventRiders
        case 'waiver-signatures': return WaiverSignatures
        case 'daily-events': return DailyEvents
        case 'fnb-profit': return ConcessionProfitability
        case 'fnb-staff': return ConcessionStaff
        case 'comps': return ConcessionComps
        default: return SalesSummary
    }
})

function selectReport(key: ReportKey) {
    if (selected.value === key) return
    selected.value = key
    // Preserve eventId on the URL only when it's relevant to the current report.
    const query: Record<string, string> = { report: key }
    if ((key === 'event-riders' || key === 'waiver-signatures') && activeEventId.value) query.eventId = activeEventId.value
    router.replace({ path: route.path, query })
}

// Daily Events row click → jump to Event Riders for that event.
function onSelectEvent(eventId: string) {
    activeEventId.value = eventId
    selected.value = 'event-riders'
    router.replace({ path: route.path, query: { report: 'event-riders', eventId } })
}

// Honour external query updates (e.g. browser back/forward) without losing the
// in-memory state of the other panes.
watch(() => route.query, (q) => {
    selected.value = parseReport(q.report as string | undefined)
    activeEventId.value = parseEventId(q.eventId as string | undefined)
})

onMounted(() => {
    // Make sure the URL is canonical even when we landed without a ?report= param.
    if (!route.query.report) {
        router.replace({ path: route.path, query: { ...route.query, report: selected.value } })
    }
})
</script>
