<template>
    <v-container>
        <div class="d-flex align-center mb-6">
            <h1 class="text-h4">Dashboard</h1>
            <v-spacer></v-spacer>
            <v-btn variant="text" prepend-icon="mdi-refresh" @click="load">Refresh</v-btn>
            <v-btn variant="tonal" prepend-icon="mdi-cog" @click="customizeOpen = true" class="ml-2">
                Customize
            </v-btn>
        </div>

        <div v-if="loading && !snapshot" class="text-center py-12">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <v-row v-else-if="snapshot">
            <v-col v-for="w in visibleWidgets" :key="w.type" :cols="12" :sm="w.sm" :md="w.md">
                <!-- KPI: Today -->
                <v-card v-if="w.type === 'kpi.today'" height="100%">
                    <v-card-text>
                        <div class="text-caption text-medium-emphasis">Today's Revenue</div>
                        <div class="text-h4">${{ (snapshot.todayRevenue!.revenueCents / 100).toFixed(2) }}</div>
                        <div class="text-caption text-medium-emphasis">
                            {{ snapshot.todayRevenue!.passesSold }} passes · {{ snapshot.todayRevenue!.ticketsSold }} tickets
                        </div>
                    </v-card-text>
                </v-card>

                <!-- KPI: Month -->
                <v-card v-else-if="w.type === 'kpi.month'" height="100%">
                    <v-card-text>
                        <div class="text-caption text-medium-emphasis">Month-to-date Revenue</div>
                        <div class="text-h4">${{ (snapshot.monthRevenue!.revenueCents / 100).toFixed(2) }}</div>
                        <div class="text-caption text-medium-emphasis">
                            {{ snapshot.monthRevenue!.passesSold }} passes · {{ snapshot.monthRevenue!.ticketsSold }} tickets
                        </div>
                    </v-card-text>
                </v-card>

                <!-- KPI: Unique riders -->
                <v-card v-else-if="w.type === 'kpi.riders'" height="100%">
                    <v-card-text>
                        <div class="text-caption text-medium-emphasis">Unique Riders (MTD)</div>
                        <div class="text-h4">{{ snapshot.uniqueRidersMonth }}</div>
                    </v-card-text>
                </v-card>

                <!-- KPI: Needs attention -->
                <v-card v-else-if="w.type === 'kpi.attention'" height="100%">
                    <v-card-text>
                        <div class="text-caption text-medium-emphasis">Needs Attention</div>
                        <div class="text-h4">{{ (snapshot.openDisputesCount ?? 0) + (snapshot.pendingRefundsCount ?? 0) }}</div>
                        <div class="text-caption text-medium-emphasis">
                            <span v-if="snapshot.openDisputesCount !== null">{{ snapshot.openDisputesCount }} disputes</span>
                            <span v-if="snapshot.openDisputesCount !== null && snapshot.pendingRefundsCount !== null"> · </span>
                            <span v-if="snapshot.pendingRefundsCount !== null">{{ snapshot.pendingRefundsCount }} refunds pending</span>
                        </div>
                    </v-card-text>
                </v-card>

                <!-- 7-day spark -->
                <v-card v-else-if="w.type === 'chart.spark7'" height="100%">
                    <v-card-title class="text-subtitle-1">Last 7 Days</v-card-title>
                    <v-card-text>
                        <div style="position: relative; height: 200px;">
                            <Line v-if="sparkData" :data="sparkData" :options="sparkOptions" />
                        </div>
                    </v-card-text>
                </v-card>

                <!-- Upcoming events -->
                <v-card v-else-if="w.type === 'events.upcoming'" height="100%">
                    <v-card-title class="text-subtitle-1 d-flex align-center">
                        Upcoming Events
                        <v-spacer></v-spacer>
                        <v-btn v-if="hasPerm('catalog.manage')" variant="text" size="small" to="/Admin/Events">View all</v-btn>
                    </v-card-title>
                    <v-card-text class="pa-0">
                        <v-list density="compact">
                            <v-list-item v-for="e in snapshot.upcomingEvents" :key="e.id">
                                <template #prepend>
                                    <v-chip size="x-small" :style="{ backgroundColor: e.eventTypeColor, color: '#fff' }"
                                        class="mr-2">{{ e.eventTypeName }}</v-chip>
                                </template>
                                <v-list-item-title>{{ e.title }}</v-list-item-title>
                                <v-list-item-subtitle>
                                    {{ formatWhen(e.startsAtUtc) }}<span v-if="e.capacity"> · capacity {{ e.capacity }}</span>
                                </v-list-item-subtitle>
                                <template #append>
                                    <v-btn v-if="hasPerm('catalog.manage')" icon="mdi-pencil" variant="text" size="small"
                                        :to="`/Admin/Events?edit=${e.id}`" title="Edit event"></v-btn>
                                    <v-btn v-if="hasPerm('reports.view')" icon="mdi-account-group" variant="text"
                                        size="small"
                                        :to="`/Admin/Reports?report=event-riders&eventId=${e.id}`"
                                        title="Rider report"></v-btn>
                                </template>
                            </v-list-item>
                            <v-list-item v-if="snapshot.upcomingEvents.length === 0">
                                <v-list-item-subtitle class="text-medium-emphasis">No upcoming events scheduled.</v-list-item-subtitle>
                            </v-list-item>
                        </v-list>
                    </v-card-text>
                </v-card>

                <!-- Recent purchases -->
                <v-card v-else-if="w.type === 'purchases.recent'" height="100%">
                    <v-card-title class="text-subtitle-1 d-flex align-center">
                        Recent Purchases
                        <v-spacer></v-spacer>
                        <v-btn variant="text" size="small" to="/Admin/Purchases">View all</v-btn>
                    </v-card-title>
                    <v-card-text class="pa-0">
                        <v-list density="compact">
                            <v-list-item v-for="p in snapshot.recentPurchases || []" :key="p.kind + ':' + p.id">
                                <v-list-item-title>
                                    {{ p.purchaserName }} — {{ p.productName }}
                                </v-list-item-title>
                                <v-list-item-subtitle>
                                    <v-chip size="x-small" class="mr-1">{{ kindLabel(p.kind) }}</v-chip>
                                    ${{ (p.amountCents / 100).toFixed(2) }} · {{ p.status }} · {{ formatWhen(p.createdAtUtc) }}
                                </v-list-item-subtitle>
                            </v-list-item>
                            <v-list-item v-if="!snapshot.recentPurchases || snapshot.recentPurchases.length === 0">
                                <v-list-item-subtitle class="text-medium-emphasis">No purchases yet.</v-list-item-subtitle>
                            </v-list-item>
                        </v-list>
                    </v-card-text>
                </v-card>

                <!-- Top Riders widget — self-contained component, fetches its own data. -->
                <TopRidersWidget v-else-if="w.type === 'top.riders'" />

                <!-- Quick actions -->
                <v-card v-else-if="w.type === 'quickactions'" height="100%">
                    <v-card-title class="text-subtitle-1">Quick Actions</v-card-title>
                    <v-card-text>
                        <div class="d-flex flex-wrap ga-2">
                            <v-btn v-if="hasPerm('catalog.manage')" size="small" variant="tonal" prepend-icon="mdi-plus"
                                to="/Admin/Events">Add Event</v-btn>
                            <v-btn v-if="hasPerm('sales.redeem')" size="small" variant="tonal" prepend-icon="mdi-qrcode-scan"
                                to="/Admin/RedeemTickets">Redeem Tickets</v-btn>
                            <v-btn v-if="hasPerm('sales.view')" size="small" variant="tonal" prepend-icon="mdi-cart-check"
                                to="/Admin/Purchases">Purchases</v-btn>
                            <v-btn v-if="hasPerm('reports.view')" size="small" variant="tonal" prepend-icon="mdi-chart-line"
                                to="/Admin/Reports">Reports</v-btn>
                            <v-btn v-if="hasPerm('users.manage')" size="small" variant="tonal" prepend-icon="mdi-account-multiple"
                                to="/Admin/Users">Users</v-btn>
                        </div>
                    </v-card-text>
                </v-card>
            </v-col>
        </v-row>

        <!-- Customize drawer -->
        <v-navigation-drawer v-model="customizeOpen" location="right" width="380" temporary>
            <v-list>
                <v-list-subheader>Customize dashboard</v-list-subheader>
                <v-list-item v-for="(w, i) in draftWidgets" :key="w.type">
                    <template #prepend>
                        <v-checkbox-btn v-model="w.visible" density="compact"></v-checkbox-btn>
                    </template>
                    <v-list-item-title>{{ catalogTitle(w.type) }}</v-list-item-title>
                    <v-list-item-subtitle class="text-caption">{{ catalogDescription(w.type) }}</v-list-item-subtitle>
                    <template #append>
                        <v-btn icon="mdi-arrow-up" variant="text" size="small" :disabled="i === 0"
                            @click="moveWidget(i, -1)"></v-btn>
                        <v-btn icon="mdi-arrow-down" variant="text" size="small" :disabled="i === draftWidgets.length - 1"
                            @click="moveWidget(i, 1)"></v-btn>
                    </template>
                </v-list-item>
            </v-list>
            <v-divider></v-divider>
            <div class="pa-3">
                <v-btn variant="text" @click="resetDraftToDefault">Reset to default</v-btn>
                <v-btn color="primary" :loading="savingConfig" @click="saveConfig" class="float-right">Save</v-btn>
            </div>
        </v-navigation-drawer>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import dayjs from 'dayjs'
import { Line } from 'vue-chartjs'
import { registerChartJs } from '@/helpers/ChartSetup'
import { DashboardService, type DashboardSnapshot, type DashboardWidgetEntry } from '@/services/DashboardService'
import authHelper from '@/helpers/AuthHelper'
import { branding } from '@/stores/branding'
import TopRidersWidget from '@/components/TopRidersWidget.vue'

registerChartJs()

const service = new DashboardService()

interface WidgetMeta {
    type: string
    title: string
    description: string
    perm: string | null   // null = always visible
    sm: number            // grid cols on small
    md: number            // grid cols on medium+
}

// Single source of truth for widget catalog.
const CATALOG: WidgetMeta[] = [
    { type: 'kpi.today',         title: "Today's Revenue",      description: 'Revenue + passes/tickets sold today.',   perm: 'reports.view',  sm: 6, md: 3 },
    { type: 'kpi.month',         title: 'Month Revenue',        description: 'Month-to-date revenue summary.',         perm: 'reports.view',  sm: 6, md: 3 },
    { type: 'kpi.riders',        title: 'Unique Riders',        description: 'Distinct riders this month.',            perm: 'reports.view',  sm: 6, md: 3 },
    { type: 'kpi.attention',     title: 'Needs Attention',      description: 'Open disputes + refund queue items.',    perm: 'disputes.view', sm: 6, md: 3 },
    { type: 'chart.spark7',      title: '7-Day Revenue',        description: 'Sparkline of the last week.',            perm: 'reports.view',  sm: 12, md: 6 },
    { type: 'events.upcoming',   title: 'Upcoming Events',      description: 'Next 5 events on the schedule.',         perm: null,            sm: 12, md: 6 },
    { type: 'purchases.recent',  title: 'Recent Purchases',     description: 'Last 5 purchases on your tenant.',       perm: 'sales.view',    sm: 12, md: 6 },
    { type: 'top.riders',        title: 'Top Riders',           description: 'Most-active waiver-signed riders this month or year.', perm: 'customers.view', sm: 12, md: 6 },
    { type: 'quickactions',      title: 'Quick Actions',        description: 'Shortcuts to the tools you use most.',   perm: null,            sm: 12, md: 6 },
]

const snapshot = ref<DashboardSnapshot | null>(null)
const widgets = ref<DashboardWidgetEntry[]>([])
const draftWidgets = ref<DashboardWidgetEntry[]>([])
const loading = ref(false)
const savingConfig = ref(false)
const customizeOpen = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

function hasPerm(p: string): boolean { return authHelper.hasPermission(p as any) }

function defaultWidgetsForRole(): DashboardWidgetEntry[] {
    // Start everyone with every widget they can see, in catalog order.
    return CATALOG
        .filter(w => !w.perm || hasPerm(w.perm))
        .map((w, idx) => ({ type: w.type, visible: true, order: idx }))
}

function catalogTitle(type: string): string {
    return CATALOG.find(w => w.type === type)?.title ?? type
}

function catalogDescription(type: string): string {
    return CATALOG.find(w => w.type === type)?.description ?? ''
}

// Render only widgets the user still has permission for, and pull the size from the catalog.
const visibleWidgets = computed(() => {
    return widgets.value
        .filter(w => {
            const meta = CATALOG.find(m => m.type === w.type)
            if (!meta) return false
            if (!w.visible) return false
            if (meta.perm && !hasPerm(meta.perm)) return false
            // Further: if the snapshot doesn't carry this widget's payload, skip it.
            if (!snapshot.value) return false
            if (w.type === 'kpi.today' && !snapshot.value.todayRevenue) return false
            if (w.type === 'kpi.month' && !snapshot.value.monthRevenue) return false
            if (w.type === 'kpi.riders' && snapshot.value.uniqueRidersMonth === null) return false
            if (w.type === 'kpi.attention'
                && snapshot.value.openDisputesCount === null
                && snapshot.value.pendingRefundsCount === null) return false
            if (w.type === 'chart.spark7' && !snapshot.value.last7Days) return false
            if (w.type === 'purchases.recent' && !snapshot.value.recentPurchases) return false
            return true
        })
        .sort((a, b) => a.order - b.order)
        .map(w => ({ ...w, ...(CATALOG.find(m => m.type === w.type)!) }))
})

const sparkData = computed(() => {
    const points = snapshot.value?.last7Days ?? []
    return {
        labels: points.map(p => dayjs(p.date).format('MMM D')),
        datasets: [{
            label: 'Revenue ($)',
            data: points.map(p => p.revenueCents / 100),
            borderColor: '#1976D2',
            backgroundColor: 'rgba(25, 118, 210, 0.15)',
            fill: true,
            tension: 0.3,
        }],
    }
})

const sparkOptions = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    scales: {
        y: { beginAtZero: true, ticks: { callback: (v: number | string) => `$${v}` } },
    },
}

function moveWidget(i: number, delta: number) {
    const next = i + delta
    if (next < 0 || next >= draftWidgets.value.length) return
    const [item] = draftWidgets.value.splice(i, 1)
    draftWidgets.value.splice(next, 0, item)
    draftWidgets.value.forEach((w, idx) => { w.order = idx })
}

function resetDraftToDefault() {
    draftWidgets.value = defaultWidgetsForRole()
}

async function saveConfig() {
    savingConfig.value = true
    try {
        draftWidgets.value.forEach((w, idx) => { w.order = idx })
        await service.saveConfig({ widgets: draftWidgets.value })
        widgets.value = JSON.parse(JSON.stringify(draftWidgets.value))
        customizeOpen.value = false
        flash('Dashboard saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to save dashboard.', 'error')
    } finally {
        savingConfig.value = false
    }
}

async function load() {
    loading.value = true
    try {
        const [snap, cfg] = await Promise.all([service.getSnapshot(), service.getConfig()])
        snapshot.value = (snap.data as any).data
        const rawConfig = (cfg.data as any).data.config as string | null
        widgets.value = resolveConfig(rawConfig)
    } catch (err: any) {
        console.error('[Dashboard] load failed', {
            url: err.config?.url,
            status: err.response?.status,
            body: err.response?.data,
            message: err.message,
        })
        const detail = err.response?.data?.error
            ?? err.response?.data?.title
            ?? err.response?.data?.detail
            ?? err.message
            ?? 'unknown error'
        flash(`Dashboard load failed (${err.response?.status ?? 'no response'}): ${detail}`, 'error')
    } finally {
        loading.value = false
    }
}

function resolveConfig(raw: string | null): DashboardWidgetEntry[] {
    if (!raw) return defaultWidgetsForRole()
    try {
        const parsed = JSON.parse(raw) as { widgets: DashboardWidgetEntry[] }
        if (!parsed?.widgets || parsed.widgets.length === 0) return defaultWidgetsForRole()
        // Merge: drop widgets no longer in catalog; append catalog widgets newly added since last save.
        const known = new Set(CATALOG.map(w => w.type))
        const kept = parsed.widgets.filter(w => known.has(w.type))
        const missing = CATALOG
            .filter(w => !kept.some(k => k.type === w.type))
            .map((w, idx) => ({ type: w.type, visible: false, order: kept.length + idx }))
        return [...kept, ...missing]
    } catch {
        return defaultWidgetsForRole()
    }
}

watch(customizeOpen, isOpen => {
    if (isOpen) {
        draftWidgets.value = JSON.parse(JSON.stringify(widgets.value))
    }
})

function formatWhen(utc: string): string {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('MMM D, h:mm A')
}

// Pretty labels for the v_recent_sales discriminator. Anything we forget to map
// falls back to the raw kind slug so we still ship something useful.
const KIND_LABELS: Record<string, string> = {
    pass: 'Day Pass',
    event_ticket: 'Ticket',
    event_extra: 'Add-on',
    season_pass: 'Season Pass',
    membership: 'Membership',
    gift_card: 'Gift Card',
    rental: 'Rental',
    concession: 'Concession',
}
function kindLabel(kind: string): string {
    return KIND_LABELS[kind] ?? kind
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onMounted(load)
</script>
