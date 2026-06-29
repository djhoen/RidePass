<template>
    <div>
      <v-theme-provider theme="tenantDark" with-background>
        <div class="kds d-flex flex-column">
            <div class="kds-header d-flex align-center px-4 py-2 ga-2">
                <h1 class="text-h6 font-weight-bold">Cook Screen</h1>
                <v-chip v-if="orders.length" size="small" variant="tonal">{{ orders.length }} active</v-chip>
                <v-chip v-if="stats.completedToday" size="small" variant="tonal" prepend-icon="mdi-timer-outline">
                    {{ stats.completedToday }} done today · avg {{ stats.avgPrepMinutes }}m
                </v-chip>
                <v-chip v-if="online.capacityEnabled" size="small" variant="flat"
                    :color="online.openNow ? 'success' : 'error'"
                    :prepend-icon="online.openNow ? 'mdi-cloud-check' : 'mdi-cloud-off-outline'">
                    Online: {{ online.openNow ? 'Open' : (online.pausedManual ? 'Paused' : 'Busy') }}
                </v-chip>
                <v-spacer />
                <v-btn v-if="online.capacityEnabled" variant="tonal"
                    :prepend-icon="online.pausedManual ? 'mdi-play' : 'mdi-pause'"
                    :loading="pausing" @click="toggleOnlinePause">
                    {{ online.pausedManual ? 'Resume online' : 'Pause online' }}
                </v-btn>
                <v-btn variant="tonal" :prepend-icon="showDefaults ? 'mdi-eye' : 'mdi-eye-off'" @click="toggleDefaults">
                    {{ showDefaults ? 'Hide defaults' : 'Show defaults' }}
                </v-btn>
                <v-btn variant="tonal" prepend-icon="mdi-backup-restore" @click="openRecall">Recall</v-btn>
                <v-btn variant="tonal" prepend-icon="mdi-silverware-variant" @click="open86">86 items</v-btn>
                <v-btn variant="tonal" prepend-icon="mdi-receipt-text-clock" :to="{ name: 'AdminConcessionOrders' }">History</v-btn>
                <v-btn variant="tonal" prepend-icon="mdi-point-of-sale" :to="{ name: 'AdminConcessionPos' }">POS</v-btn>
            </div>

            <v-tabs v-model="stationFilter" density="compact" show-arrows class="kds-tabs px-2">
                <v-tab :value="null">All stations</v-tab>
                <v-tab v-for="s in stations" :key="s.id" :value="s.id">{{ s.name }}</v-tab>
            </v-tabs>

            <div class="kds-board flex-grow-1">
                <div v-if="orders.length === 0" class="kds-empty">
                    <v-icon size="56" color="grey">mdi-silverware-clean</v-icon>
                    <div class="mt-2 text-medium-emphasis">No orders in the queue.</div>
                </div>
                <div v-else class="kds-grid">
                    <div v-for="o in orders" :key="o.saleId" class="ticket"
                        :class="[`ticket--${urgency(o)}`, { 'ticket--ready': o.fulfillmentStatus === 'ready' }]">
                        <div v-if="o.isRush" class="ticket__rush">RUSH</div>
                        <div class="ticket__head">
                            <div class="d-flex flex-column">
                                <span class="ticket__num">#{{ o.orderNumber ?? '—' }}</span>
                                <span v-if="o.customerName" class="ticket__cust">{{ o.customerName }}</span>
                            </div>
                            <div class="d-flex flex-column align-end ga-1">
                                <span class="ticket__time">{{ elapsed(o.queuedAtUtc) }}</span>
                                <v-btn size="x-small" :variant="o.isRush ? 'flat' : 'tonal'" :color="o.isRush ? 'error' : undefined"
                                    @click.stop="toggleRush(o)">{{ o.isRush ? 'Un-rush' : 'Rush' }}</v-btn>
                            </div>
                        </div>
                        <div class="ticket__lines">
                            <div v-for="l in displayLines(o)" :key="l.lineId"
                                class="kline"
                                :class="[`kline--${l.prepStatus}`, { 'kline--child': !!l.parentLineId }]"
                                @click="bump(o, l)">
                                <v-icon class="kline__icon">{{ prepIcon(l.prepStatus) }}</v-icon>
                                <div class="kline__body">
                                    <div class="kline__name" :class="{ done: l.prepStatus === 'ready' }">
                                        {{ l.quantity }}× {{ l.name }}<span v-if="l.variantLabel" class="kline__variant"> ({{ l.variantLabel }})</span>
                                        <span v-if="l.isCombo && l.comboTier" class="kline__combo">{{ l.comboTier }} combo</span>
                                    </div>
                                    <div v-for="m in l.removed" :key="'r' + m" class="kline__removed">NO {{ m }}</div>
                                    <div v-for="m in l.added" :key="'a' + m" class="kline__added">ADD {{ m }}</div>
                                    <div v-if="showDefaults" v-for="m in l.standard" :key="'s' + m" class="kline__standard">{{ m }}</div>
                                    <div v-if="l.notes" class="kline__note">"{{ l.notes }}"</div>
                                </div>
                            </div>
                        </div>
                        <button class="ticket__done" :disabled="o.fulfillmentStatus !== 'ready'" @click="complete(o)">
                            {{ o.fulfillmentStatus === 'ready' ? 'Picked up' : 'Preparing…' }}
                        </button>
                    </div>
                </div>
            </div>
        </div>
      </v-theme-provider>

        <!-- Recall: bring a recently completed order back onto the board -->
        <v-dialog v-model="recallDialog" max-width="420">
            <v-card class="d-flex flex-column" style="max-height: 85vh;">
                <v-card-title class="d-flex align-center">
                    <span>Recall an order</span>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="recallDialog = false" />
                </v-card-title>
                <v-divider />
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0;">
                    <div v-if="recallList.length === 0" class="text-medium-emphasis py-4 text-center">No recently completed orders.</div>
                    <v-list density="compact">
                        <v-list-item v-for="o in recallList" :key="o.saleId" class="px-0">
                            <v-list-item-title>#{{ o.orderNumber ?? '—' }}<span v-if="o.customerName"> · {{ o.customerName }}</span></v-list-item-title>
                            <template #append>
                                <v-btn size="small" variant="tonal" :loading="recalling === o.saleId" @click="recall(o.saleId)">Recall</v-btn>
                            </template>
                        </v-list-item>
                    </v-list>
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- 86 manager: toggle any item sold out for the rest of the day -->
        <v-dialog v-model="dialog86" max-width="480">
            <v-card class="d-flex flex-column" style="max-height: 85vh;">
                <v-card-title class="d-flex align-center">
                    <span>86 items (sold out today)</span>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="dialog86 = false" />
                </v-card-title>
                <v-divider />
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0;">
                    <div v-if="items86.length === 0" class="text-medium-emphasis py-4 text-center">No items on the menu.</div>
                    <v-list density="compact">
                        <v-list-item v-for="p in items86" :key="p.id" class="px-0">
                            <v-list-item-title>{{ p.name }}</v-list-item-title>
                            <v-list-item-subtitle v-if="p.soldOut && !p.manuallySoldOut">Out of stock</v-list-item-subtitle>
                            <template #append>
                                <v-btn size="small" variant="tonal"
                                    :color="p.manuallySoldOut ? 'success' : 'warning'" :loading="toggling === p.id"
                                    @click="toggle86(p)">{{ p.manuallySoldOut ? 'Un-86' : '86' }}</v-btn>
                            </template>
                        </v-list-item>
                    </v-list>
                </v-card-text>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snack.show" :color="snack.color" timeout="5000">{{ snack.text }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { ConcessionService, type KitchenOrder, type KitchenLine, type ConcessionStation, type ConcessionProduct } from '@/services/ConcessionService'
import { useRoute } from 'vue-router'
import { branding } from '@/stores/branding'
import { setHomeScreenIcon } from '@/helpers/HomeScreenIcon'

const svc = new ConcessionService()
const orders = ref<KitchenOrder[]>([])
const stats = ref({ completedToday: 0, avgPrepMinutes: 0 })
const warnMinutes = ref(5)
const lateMinutes = ref(10)
const stations = ref<ConcessionStation[]>([])
// Online-ordering throttle status (chip + pause control shown only when the feature is enabled).
const online = ref({ openNow: true, pausedManual: false, capacityEnabled: false })
const pausing = ref(false)

// Recall recently completed orders back onto the board.
const recallDialog = ref(false)
const recallList = ref<{ saleId: string; orderNumber: number | null; customerName: string | null }[]>([])
const recalling = ref<string | null>(null)
const stationFilter = ref<string | null>(null)
const snack = ref({ show: false, text: '', color: 'error' })
let timer: number | undefined

const dialog86 = ref(false)
const items86 = ref<ConcessionProduct[]>([])
const toggling = ref<string | null>(null)

// Whether the cook screen shows the standard (default) selections. Persisted per device; applies to all
// orders. Default off so only customizations show.
const showDefaults = ref(localStorage.getItem('cookShowDefaults') === '1')
function toggleDefaults() {
    showDefaults.value = !showDefaults.value
    localStorage.setItem('cookShowDefaults', showDefaults.value ? '1' : '0')
}

function flash(text: string, color: 'error' | 'success' = 'error') { snack.value = { show: true, text, color } }

async function open86() {
    dialog86.value = true
    try {
        const r = await svc.items()
        items86.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load the menu.')
    }
}

async function toggle86(p: ConcessionProduct) {
    toggling.value = p.id
    try {
        await svc.setSoldOut(p.id, !p.manuallySoldOut)
        const r = await svc.items()
        items86.value = (r.data as any).data
        flash(p.manuallySoldOut ? `"${p.name}" is back on.` : `"${p.name}" marked sold out.`, 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || `Could not update "${p.name}". Try again.`)
    } finally {
        toggling.value = null
    }
}

function prepIcon(s: string) {
    return s === 'ready' ? 'mdi-check-circle' : s === 'in_progress' ? 'mdi-progress-clock' : 'mdi-circle-outline'
}
// `now` ticks every second (client-side only, no server calls) so the age + urgency update smoothly
// between the 4s data polls. Reading now.value inside these makes the template re-render each tick.
const now = ref(Date.now())
let clockTimer: number | undefined

function ageMins(iso: string) {
    return Math.max(0, Math.floor((now.value - new Date(iso).getTime()) / 60000))
}
function elapsed(iso: string) {
    const mins = ageMins(iso)
    return mins < 1 ? 'just now' : `${mins}m`
}
// Ticket urgency drives color escalation, using the tenant's configured targets.
function urgency(o: KitchenOrder) {
    const m = ageMins(o.queuedAtUtc)
    return m >= lateMinutes.value ? 'late' : m >= warnMinutes.value ? 'warn' : 'fresh'
}

// Toggle the rush flag (optimistic), then refresh so the board re-sorts rush-first.
async function toggleRush(o: KitchenOrder) {
    const next = !o.isRush
    o.isRush = next
    try { await svc.setRush(o.saleId, next) }
    catch (err: any) { o.isRush = !next; flash(err.response?.data?.error || 'Could not update rush.') }
    refresh()
}

async function openRecall() {
    recallDialog.value = true
    try { recallList.value = (await svc.recentlyCompleted() as any).data.data }
    catch (err: any) { flash(err.response?.data?.error || 'Could not load completed orders.') }
}
async function recall(saleId: string) {
    recalling.value = saleId
    try {
        await svc.recallSale(saleId)
        recallList.value = recallList.value.filter(o => o.saleId !== saleId)
        await refresh()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not recall the order.')
    } finally {
        recalling.value = null
    }
}

const route = useRoute()

onMounted(async () => {
    // Launched from a per-station home-screen icon? Pre-filter to that station.
    const sid = route.query.stationId
    if (typeof sid === 'string' && sid) stationFilter.value = sid
    try {
        stations.value = (await svc.activeStations() as any).data.data
    } catch { /* stations optional; All view still works */ }
    updateHomeIcon()
    await refresh()
    timer = window.setInterval(refresh, 4000)
    clockTimer = window.setInterval(() => { now.value = Date.now() }, 1000)
})
onUnmounted(() => {
    if (timer) window.clearInterval(timer)
    if (clockTimer) window.clearInterval(clockTimer)
})
watch(stationFilter, () => { updateHomeIcon(); refresh() })

// Make "Add to Home Screen" pin a Cook icon that reopens this queue chromeless. When a station tab is
// selected, the pinned icon is scoped to that station, so a cook can have a per-station icon.
function updateHomeIcon() {
    const station = stations.value.find(s => s.id === stationFilter.value)
    const title = station ? `Cook: ${station.name}` : `${branding.displayName || 'RidePass'} Cook`
    const startPath = '/Admin/ConcessionKitchen' + (stationFilter.value ? `?stationId=${stationFilter.value}` : '')
    setHomeScreenIcon({ title, iconUrl: '/icon-cook.png', startPath })
}

async function refresh() {
    try {
        const r = await svc.kitchen(stationFilter.value)
        const data = (r.data as any).data
        orders.value = data.orders
        stats.value = data.stats ?? stats.value
        if (data.warnMinutes) warnMinutes.value = data.warnMinutes
        if (data.lateMinutes) lateMinutes.value = data.lateMinutes
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not refresh the kitchen queue.')
    }
    // Online-ordering status rides along with the kitchen poll (best-effort).
    try {
        const s = (await svc.orderingStatus() as any).data.data
        online.value = { openNow: s.openNow, pausedManual: s.pausedManual, capacityEnabled: s.capacityEnabled }
    } catch { /* keep last known */ }
}

async function toggleOnlinePause() {
    pausing.value = true
    try {
        await svc.pauseOrdering(!online.value.pausedManual)
        await refresh()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not change online ordering. Please try again.')
    } finally {
        pausing.value = false
    }
}

// Render order: top-level lines, each combo's component children nested under it. A component whose
// combo parent isn't in this list (e.g. when filtered to one station, the drink shows but the grill
// entree doesn't) is promoted to top-level so it still renders and can be started/bumped.
function displayLines(o: KitchenOrder): KitchenLine[] {
    const present = new Set(o.lines.map(l => l.lineId))
    const tops = o.lines.filter(l => !l.parentLineId || !present.has(l.parentLineId))
    const out: KitchenLine[] = []
    for (const t of tops) {
        out.push(t)
        if (t.isCombo) out.push(...o.lines.filter(l => l.parentLineId === t.lineId))
    }
    return out
}

async function bump(order: KitchenOrder, line: KitchenLine) {
    const next = line.prepStatus === 'queued' ? 'in_progress' : line.prepStatus === 'in_progress' ? 'ready' : 'queued'
    try {
        await svc.advanceLine(line.lineId, next)
        line.prepStatus = next   // optimistic; the next poll reconciles order status
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not update the item.')
    }
}

async function complete(order: KitchenOrder) {
    try {
        await svc.completeSale(order.saleId)
        orders.value = orders.value.filter(o => o.saleId !== order.saleId)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not mark the order picked up.')
    }
}
</script>

<style scoped>
.kds { min-height: calc(100dvh - 64px); }
.kds-header { flex: 0 0 auto; border-bottom: 1px solid rgba(255, 255, 255, 0.08); }
.kds-tabs { flex: 0 0 auto; }
.kds-board { overflow-y: auto; min-height: 0; padding: 14px; }
.kds-empty { text-align: center; padding: 64px 16px; }
.kds-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(264px, 1fr)); gap: 14px; align-items: start; }

.ticket {
    display: flex;
    flex-direction: column;
    border-radius: 14px;
    overflow: hidden;
    background: #1e2530;
    border: 1px solid rgba(255, 255, 255, 0.08);
}
.ticket__head { display: flex; align-items: center; justify-content: space-between; padding: 10px 14px; }
.ticket__num { font-size: 1.7rem; font-weight: 800; line-height: 1; color: #fff; }
.ticket__cust { font-size: 0.95rem; color: #fff; opacity: 0.85; margin-top: 2px; }
.ticket__rush { background: #c62828; color: #fff; font-weight: 800; text-align: center; padding: 3px 0; font-size: 0.8rem; letter-spacing: 0.1em; }
.ticket__time { font-size: 0.95rem; font-weight: 700; padding: 3px 12px; border-radius: 999px; }
.ticket--fresh .ticket__head { background: #233246; }
.ticket--fresh .ticket__time { background: rgba(67, 160, 71, 0.25); color: #9be7a0; }
.ticket--warn .ticket__head { background: #4a3a1a; }
.ticket--warn .ticket__time { background: rgba(251, 140, 0, 0.3); color: #ffc66e; }
.ticket--late .ticket__head { background: #4a1f1f; }
.ticket--late .ticket__time { background: rgba(229, 57, 53, 0.4); color: #ff9a96; animation: kds-pulse 1.3s ease-in-out infinite; }
.ticket--ready { outline: 2px solid #43A047; outline-offset: -1px; }
.ticket--ready .ticket__head { background: #1f3a22; }

.ticket__lines { display: flex; flex-direction: column; }
.kline { display: flex; gap: 10px; padding: 11px 14px; border-top: 1px solid rgba(255, 255, 255, 0.06); cursor: pointer; }
.kline:hover { background: rgba(255, 255, 255, 0.04); }
.kline__icon { margin-top: 1px; }
.kline--queued .kline__icon { color: #90a4ae; }
.kline--in_progress .kline__icon { color: #ffc66e; }
.kline--ready .kline__icon { color: #9be7a0; }
/* Combo entree badge + its indented component children. */
.kline__combo { margin-left: 8px; padding: 1px 7px; border-radius: 10px; font-size: 0.72rem;
    font-weight: 700; background: #5e35b1; color: #fff; vertical-align: middle; }
.kline--child { padding-left: 30px; }
.kline__name { font-size: 1.05rem; font-weight: 600; color: #fff; line-height: 1.25; }
.kline__name.done { text-decoration: line-through; opacity: 0.45; }
.kline__variant { font-weight: 400; opacity: 0.8; }
.kline__removed { font-size: 0.9rem; font-weight: 800; letter-spacing: 0.02em; color: #ff8a80; }
.kline__added { font-size: 0.9rem; font-weight: 800; letter-spacing: 0.02em; color: #80e0a0; }
.kline__standard { font-size: 0.9rem; color: #fff; }
.kline__note { font-size: 0.85rem; font-style: italic; color: #ffe6a3; }

.ticket__done {
    margin: 10px 14px 14px;
    padding: 12px;
    border: none;
    border-radius: 10px;
    font-weight: 700;
    font-size: 1rem;
    cursor: pointer;
    background: #43A047;
    color: #fff;
}
.ticket__done:disabled { background: rgba(255, 255, 255, 0.08); color: rgba(255, 255, 255, 0.4); cursor: default; }

@keyframes kds-pulse { 50% { opacity: 0.55; } }
</style>
