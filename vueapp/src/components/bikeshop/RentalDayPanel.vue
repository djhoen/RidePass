<!--
    Rental Day sheet ("Today"): every rental and reservation for one business day, with customer
    names, grouped by the program it was booked through (the shop's own rentals, an event type such
    as After School or Kids Day Camp, or a package such as Find Your Ride).

    The counter's opening question is "who is coming today, and what do they need on the rack". The
    Board answers "which bikes are free at 2pm"; this answers the other half, by person rather than
    by bike, and prints as the one-page sheet that goes by the till.

    Everything is read-only here. Working a booking (check out, return, sign, cancel) happens on
    the bookings tab, which the Open action routes to with ?open=<rentalId>.

    Clock math: `date` is a tenant-local calendar day and never an instant; every timestamp on a
    row is UTC and is rendered through @/helpers/TenantTime, so staff read the track's clock
    wherever they sit.

    A panel, not a page: the Rentals screen hosts it as its first tab under keep-alive, so coming
    back to it keeps the chosen date and filters.
-->
<template>
    <div class="rdp">
        <!-- ── Controls ─────────────────────────────────────────────────── -->
        <v-card class="pa-3 mb-4 no-print">
            <div class="d-flex ga-3 align-center flex-wrap">
                <div class="d-flex align-center ga-1">
                    <v-tooltip text="Previous day" location="top">
                        <template #activator="{ props }">
                            <v-btn v-bind="props" icon="mdi-chevron-left" variant="text" size="small"
                                :disabled="loading" @click="shiftDay(-1)"></v-btn>
                        </template>
                    </v-tooltip>
                    <v-text-field v-model="selectedDate" type="date" density="compact" hide-details
                        style="max-width: 170px"></v-text-field>
                    <v-tooltip text="Next day" location="top">
                        <template #activator="{ props }">
                            <v-btn v-bind="props" icon="mdi-chevron-right" variant="text" size="small"
                                :disabled="loading" @click="shiftDay(1)"></v-btn>
                        </template>
                    </v-tooltip>
                    <v-btn size="small" variant="tonal" :disabled="isToday" @click="goToday">Today</v-btn>
                </div>

                <span class="text-body-2 day-title">{{ prettyDate }}</span>

                <v-spacer></v-spacer>

                <v-switch v-model="includeCancelled" color="primary" density="compact" hide-details
                    label="Show cancelled" class="mr-2"></v-switch>

                <v-tooltip text="Reload" location="top">
                    <template #activator="{ props }">
                        <v-btn v-bind="props" icon="mdi-refresh" variant="text" size="small"
                            :loading="loading" @click="load"></v-btn>
                    </template>
                </v-tooltip>
                <v-btn variant="outlined" size="small" prepend-icon="mdi-printer"
                    :disabled="!day" @click="print">Print</v-btn>
            </div>
        </v-card>

        <!-- Print-only header: the controls above are hidden on paper, so the sheet has to say
             which shop and which day it is for. -->
        <div class="print-only mb-3">
            <div class="text-h6">{{ branding.displayName }} &mdash; Rentals for {{ prettyDate }}</div>
            <div class="text-body-2">
                {{ day?.timezone || branding.timezone }}
                <span v-if="includeCancelled"> &middot; cancelled bookings included</span>
            </div>
        </div>

        <!-- ── States ───────────────────────────────────────────────────── -->
        <!-- The server side of this sheet ships separately. A missing endpoint must read as
             "not here yet", not as an outage, and must never take the rest of Rentals with it. -->
        <v-alert v-if="notAvailable" type="info" variant="tonal" class="mb-4">
            The Today sheet isn't available on this server yet. Everything else on Rentals works
            as usual: the Board shows what's on the rack, and All Bookings lists every rental.
        </v-alert>
        <v-alert v-else-if="loadError" type="error" variant="tonal" class="mb-4">
            {{ loadError }}
            <template #append>
                <v-btn size="small" variant="text" @click="load">Retry</v-btn>
            </template>
        </v-alert>
        <v-card v-else-if="loading && !day" class="pa-10 text-center">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </v-card>

        <template v-else-if="day">
            <!-- ── KPI strip ────────────────────────────────────────────── -->
            <!-- Whole-day numbers from the server, not the filtered view: the tiles are the
                 glance, and clicking one IS the filter. -->
            <div class="kpi-strip mb-4">
                <button v-for="t in kpiTiles" :key="t.phase" type="button" class="rp-kpi kpi-tile"
                    :class="{ 'kpi-active': phaseFilter === t.phase, 'kpi-warn': t.warn }"
                    :aria-pressed="phaseFilter === t.phase" @click="togglePhase(t.phase)">
                    <div class="rp-kpi-label mt-0">{{ t.label }}</div>
                    <div class="rp-kpi-value">{{ t.value }}</div>
                    <div class="kpi-hint">{{ t.hint }}</div>
                </button>
            </div>

            <!-- ── Filters ──────────────────────────────────────────────── -->
            <v-card v-if="!dayIsEmpty" class="pa-3 mb-4 no-print">
                <div class="d-flex ga-2 align-center flex-wrap">
                    <span class="text-caption text-medium-emphasis mr-1">Programs</span>
                    <v-chip v-for="p in day.programs" :key="p.key" size="small" label
                        :variant="programOn(p.key) ? 'tonal' : 'outlined'"
                        :class="{ 'chip-off': !programOn(p.key) }"
                        :prepend-icon="programOn(p.key) ? 'mdi-check' : undefined"
                        @click="toggleProgram(p.key)">
                        {{ p.label }}
                    </v-chip>
                </div>
                <div class="d-flex ga-3 align-center flex-wrap mt-3">
                    <v-select v-model="phaseFilter" :items="phaseOptions" item-title="title" item-value="value"
                        label="Status" density="compact" hide-details style="max-width: 200px"></v-select>
                    <v-select v-if="day.productTypes.length" v-model="productTypeId" :items="productTypeOptions"
                        item-title="title" item-value="value" label="Product type" density="compact"
                        hide-details style="max-width: 210px"></v-select>
                    <v-text-field v-model="search" density="compact" hide-details clearable
                        prepend-inner-icon="mdi-magnify" label="Name, phone, email or order #"
                        style="max-width: 280px"></v-text-field>
                    <v-btn v-if="filtersActive" size="small" variant="text" prepend-icon="mdi-filter-off"
                        @click="clearFilters">Clear</v-btn>
                    <v-spacer></v-spacer>
                    <span class="text-caption text-medium-emphasis">
                        {{ visibleRowCount }} of {{ totalRowCount }} booking{{ totalRowCount === 1 ? '' : 's' }}
                    </span>
                </div>
            </v-card>

            <!-- ── Empty states ─────────────────────────────────────────── -->
            <v-card v-if="dayIsEmpty" class="pa-8 text-center text-medium-emphasis">
                Nothing booked for {{ shortDate }}.
            </v-card>
            <v-card v-else-if="visibleGroups.length === 0" class="pa-8 text-center text-medium-emphasis no-print">
                Nothing on {{ shortDate }} matches those filters.
                <v-btn size="small" variant="text" class="ml-2" @click="clearFilters">Clear filters</v-btn>
            </v-card>

            <!-- ── One card per program ─────────────────────────────────── -->
            <v-card v-for="g in visibleGroups" :key="g.key" class="group-card mb-4"
                :style="`--group-color: ${g.color || 'var(--rp-line3)'}`">
                <div class="group-head" role="button" @click="toggleGroup(g.key)">
                    <v-icon size="small" class="mr-1 no-print">
                        {{ collapsed.has(g.key) ? 'mdi-chevron-right' : 'mdi-chevron-down' }}
                    </v-icon>
                    <span class="group-title">{{ g.label }}</span>
                    <span class="text-medium-emphasis mx-2">&middot;</span>
                    <span class="text-body-2">
                        {{ g.visible.length }} booking{{ g.visible.length === 1 ? '' : 's' }}
                        <span v-if="g.visible.length !== g.rows.length" class="text-caption text-medium-emphasis">
                            of {{ g.rows.length }}
                        </span>
                    </span>
                    <v-spacer></v-spacer>
                    <span class="text-caption text-medium-emphasis group-counts">{{ countsSummary(g.counts) }}</span>
                </div>

                <div v-show="!collapsed.has(g.key)" class="group-body">
                    <table class="day-table">
                        <thead>
                            <tr>
                                <th class="col-time">Time</th>
                                <th>Customer</th>
                                <th>Gear</th>
                                <th class="col-status">Status</th>
                                <th class="col-source">Source</th>
                                <th>Booking</th>
                                <th class="col-action no-print"></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="r in g.visible" :key="r.rentalId"
                                :class="{ 'row-done': r.phase === 'done', 'row-cancelled': r.phase === 'cancelled' }">
                                <td class="col-time tabular" data-label="Time">
                                    <div>{{ timeLabel(r) }}</div>
                                    <div v-if="movementLabel(r)" class="text-caption text-medium-emphasis">
                                        {{ movementLabel(r) }}
                                    </div>
                                </td>
                                <td data-label="Customer">
                                    <div class="customer-name">
                                        <router-link v-if="r.customerUserId && canViewCustomers"
                                            :to="`/Admin/Customers/${r.customerUserId}`">
                                            {{ r.customerName || 'Walk-in' }}
                                        </router-link>
                                        <template v-else>{{ r.customerName || 'Walk-in' }}</template>
                                        <span v-if="r.orderNumber != null" class="text-caption text-medium-emphasis ml-1">
                                            #{{ r.orderNumber }}
                                        </span>
                                    </div>
                                    <div v-if="r.customerPhone" class="text-caption text-medium-emphasis">
                                        {{ r.customerPhone }}
                                    </div>
                                </td>
                                <td data-label="Gear">
                                    <div v-for="(line, i) in gearLines(r)" :key="i" class="gear-line">
                                        {{ line.text }}
                                        <span v-if="line.unit" class="gear-unit">&middot; {{ line.unit }}</span>
                                        <span v-else class="gear-unit text-medium-emphasis">&middot; unassigned</span>
                                    </div>
                                    <div v-if="r.items.length === 0" class="text-caption text-medium-emphasis">No gear on this booking</div>
                                </td>
                                <td class="col-status" data-label="Status">
                                    <span class="rp-pill" :class="phasePill(r).cls">
                                        <v-icon v-if="r.phase === 'unpaid'" size="12">mdi-alert</v-icon>
                                        {{ phasePill(r).label }}
                                    </span>
                                </td>
                                <td class="col-source" data-label="Source">
                                    {{ r.channel === 'counter' ? 'Counter' : 'Online' }}
                                </td>
                                <td data-label="Booking">
                                    <span v-if="bookingLabel(r)">{{ bookingLabel(r) }}</span>
                                    <span v-else class="text-medium-emphasis">&mdash;</span>
                                </td>
                                <td class="col-action no-print cell-action">
                                    <v-btn size="x-small" variant="tonal" @click="openRental(r)">Open</v-btn>
                                </td>
                            </tr>
                        </tbody>
                    </table>
                </div>
            </v-card>
        </template>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onActivated } from 'vue'
import { useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { tenantDayjs, tenantWallClockNow } from '@/helpers/TenantTime'
import {
    ShopDayService,
    type ShopDay,
    type ShopDayCounts,
    type ShopDayGroup,
    type ShopDayPhase,
    type ShopDayRow,
} from '@/services/ShopDayService'
import { branding } from '@/stores/branding'
import authHelper from '@/helpers/AuthHelper'
import { Perm } from '@/helpers/TenantPermissions'

const service = new ShopDayService()
const router = useRouter()

// The customer page sits behind customers.view, which a counter-only login may not hold. A link
// the router bounces is worse than plain text.
const canViewCustomers = computed(() => authHelper.hasPermission(Perm.CustomersView))

function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }

// ── The day ────────────────────────────────────────────────────────────────
// "Today" is today AT THE TRACK. The date is a calendar day, formatted directly with dayjs and
// never run through the UTC converters (which would shift it for western timezones).
function tenantToday(): string { return tenantWallClockNow('YYYY-MM-DD') }

const selectedDate = ref(tenantToday())
const includeCancelled = ref(false)

const isToday = computed(() => selectedDate.value === tenantToday())
const prettyDate = computed(() => selectedDate.value ? dayjs(selectedDate.value).format('dddd, MMMM D, YYYY') : '')
const shortDate = computed(() => selectedDate.value ? dayjs(selectedDate.value).format('ddd, MMM D') : '')

function shiftDay(delta: number) {
    const base = selectedDate.value || tenantToday()
    selectedDate.value = dayjs(base).add(delta, 'day').format('YYYY-MM-DD')
}
function goToday() { selectedDate.value = tenantToday() }

// ── Data ───────────────────────────────────────────────────────────────────
const day = ref<ShopDay | null>(null)
const loading = ref(false)
const loadError = ref('')
const notAvailable = ref(false)

// Stepping through days quickly puts several loads in flight. Only the newest may write: an
// older response landing last would show Tuesday's names under Wednesday's date.
let loadSeq = 0

async function load() {
    // A cleared date input is a half-typed one, not a request for nothing.
    if (!selectedDate.value) return
    const seq = ++loadSeq
    const forDate = shortDate.value
    loading.value = true
    loadError.value = ''
    notAvailable.value = false
    try {
        const r = await service.getDay(selectedDate.value, includeCancelled.value)
        if (seq !== loadSeq) return
        day.value = r.data.data
    } catch (e: any) {
        if (seq !== loadSeq) return
        day.value = null
        // The endpoint ships with the backend half of this feature. Until it is deployed, say so
        // plainly rather than reporting an outage.
        if (e.response?.status === 404) {
            notAvailable.value = true
            return
        }
        loadError.value = e.response?.data?.error
            || `Could not load the day sheet for ${forDate}. Check your connection and retry.`
    } finally {
        if (seq === loadSeq) loading.value = false
    }
}

// ── Filters ────────────────────────────────────────────────────────────────
// Programs are stored as EXCLUSIONS so "all on" is the natural default and a program that first
// appears on another day shows up switched on rather than silently hidden.
const excludedPrograms = ref<Set<string>>(new Set())
function programOn(key: string): boolean { return !excludedPrograms.value.has(key) }
function toggleProgram(key: string) {
    const next = new Set(excludedPrograms.value)
    if (next.has(key)) next.delete(key); else next.add(key)
    excludedPrograms.value = next
}

// A phase filter of 'out' means "with a rider right now", which includes the ones due back today.
type PhaseFilter = ShopDayPhase | null
const phaseFilter = ref<PhaseFilter>(null)
const phaseOptions = computed(() => {
    const opts: { title: string; value: PhaseFilter }[] = [
        { title: 'All statuses', value: null },
        { title: 'Pickups due', value: 'pickup' },
        { title: 'Out now', value: 'out' },
        { title: 'Returns due', value: 'return' },
        { title: 'Reserved ahead', value: 'reserved' },
        { title: 'Unpaid', value: 'unpaid' },
        { title: 'Returned', value: 'done' },
    ]
    if (includeCancelled.value) opts.push({ title: 'Cancelled', value: 'cancelled' })
    return opts
})
function togglePhase(p: ShopDayPhase) {
    phaseFilter.value = phaseFilter.value === p ? null : p
}
function matchesPhase(r: ShopDayRow): boolean {
    const f = phaseFilter.value
    if (!f) return true
    if (f === 'out') return r.phase === 'out' || r.phase === 'return'
    return r.phase === f
}

const productTypeId = ref<string | null>(null)
const productTypeOptions = computed(() => [
    { title: 'All product types', value: null as string | null },
    ...(day.value?.productTypes ?? []).map(t => ({ title: t.name, value: t.id as string | null })),
])
// Items carry their category only once the server sends it (see ShopDayItem.categoryId). A row
// whose lines carry no category can't be judged, so it stays visible rather than vanishing on
// a filter it was never able to match.
function matchesProductType(r: ShopDayRow): boolean {
    const id = productTypeId.value
    if (!id) return true
    const known = r.items.filter(i => i.categoryId != null)
    return known.length === 0 || known.some(i => i.categoryId === id)
}

const search = ref('')
function matchesSearch(r: ShopDayRow): boolean {
    const term = search.value?.trim().toLowerCase()
    if (!term) return true
    if ((r.customerName ?? '').toLowerCase().includes(term)) return true
    if ((r.customerEmail ?? '').toLowerCase().includes(term)) return true
    if (r.orderNumber != null && String(r.orderNumber).includes(term.replace(/^#/, ''))) return true
    // Phones are typed every which way; compare digits to digits once there are enough to mean
    // something.
    const digits = term.replace(/\D/g, '')
    if (digits.length >= 3 && (r.customerPhone ?? '').replace(/\D/g, '').includes(digits)) return true
    return (r.customerPhone ?? '').toLowerCase().includes(term)
}

const filtersActive = computed(() =>
    excludedPrograms.value.size > 0 || phaseFilter.value !== null || productTypeId.value !== null || !!search.value?.trim())
function clearFilters() {
    excludedPrograms.value = new Set()
    phaseFilter.value = null
    productTypeId.value = null
    search.value = ''
}

// ── Groups ─────────────────────────────────────────────────────────────────
interface VisibleGroup extends ShopDayGroup { visible: ShopDayRow[] }

const totalRowCount = computed(() => (day.value?.groups ?? []).reduce((n, g) => n + g.rows.length, 0))
const dayIsEmpty = computed(() => !!day.value && totalRowCount.value === 0)

// Groups with nothing left after filtering disappear rather than sitting there empty.
const visibleGroups = computed<VisibleGroup[]>(() =>
    (day.value?.groups ?? [])
        .filter(g => programOn(g.key))
        .map(g => ({ ...g, visible: g.rows.filter(r => matchesPhase(r) && matchesProductType(r) && matchesSearch(r)) }))
        .filter(g => g.visible.length > 0))
const visibleRowCount = computed(() => visibleGroups.value.reduce((n, g) => n + g.visible.length, 0))

const collapsed = ref<Set<string>>(new Set())
function toggleGroup(key: string) {
    const next = new Set(collapsed.value)
    if (next.has(key)) next.delete(key); else next.add(key)
    collapsed.value = next
}

function countsSummary(c: ShopDayCounts): string {
    const bits: string[] = []
    if (c.pickupsDue) bits.push(`${c.pickupsDue} to pick up`)
    if (c.outNow) bits.push(`${c.outNow} out`)
    if (c.returnsDue) bits.push(`${c.returnsDue} due back`)
    if (c.reserved) bits.push(`${c.reserved} reserved`)
    if (c.unpaid) bits.push(`${c.unpaid} unpaid`)
    if (c.returned) bits.push(`${c.returned} returned`)
    return bits.join(' · ')
}

// ── KPI tiles ──────────────────────────────────────────────────────────────
const kpiTiles = computed(() => {
    const t = day.value?.totals
    return [
        { phase: 'pickup' as ShopDayPhase, label: 'Pickups due', value: t?.pickupsDue ?? 0, hint: 'paid, not yet out', warn: false },
        { phase: 'out' as ShopDayPhase, label: 'Out now', value: t?.outNow ?? 0, hint: 'with riders', warn: false },
        { phase: 'return' as ShopDayPhase, label: 'Returns due', value: t?.returnsDue ?? 0, hint: 'back today', warn: false },
        { phase: 'reserved' as ShopDayPhase, label: 'Reserved ahead', value: t?.reserved ?? 0, hint: 'starts after today', warn: false },
        { phase: 'unpaid' as ShopDayPhase, label: 'Unpaid', value: t?.unpaid ?? 0, hint: 'pending payment', warn: (t?.unpaid ?? 0) > 0 },
    ]
})

// ── Row formatting ─────────────────────────────────────────────────────────
/** A clock reading at the track: time only when it falls on the sheet's day, date + time otherwise
 *  (a three-day rental that started yesterday must not read as "9:00 AM today"). */
function clock(utc: string): string {
    const d = tenantDayjs(utc)
    return d.format('YYYY-MM-DD') === selectedDate.value ? d.format('h:mm A') : d.format('MMM D h:mm A')
}
function timeLabel(r: ShopDayRow): string {
    return `${clock(r.startsAtUtc)} – ${clock(r.endsAtUtc)}`
}
/** When the gear actually moved, which is a different conversation from when it was booked to. */
function movementLabel(r: ShopDayRow): string {
    if (r.returnedAtUtc) return `returned ${clock(r.returnedAtUtc)}`
    if (r.checkedOutAtUtc) return `picked up ${clock(r.checkedOutAtUtc)}`
    return ''
}
function gearLines(r: ShopDayRow): { text: string; unit: string | null }[] {
    return r.items.map(i => ({
        text: [i.name, i.variantLabel].filter(Boolean).join(' · ') + (i.quantity > 1 ? ` ×${i.quantity}` : ''),
        unit: i.unitLabel,
    }))
}
function phasePill(r: ShopDayRow): { label: string; cls: string } {
    switch (r.phase) {
        // Overdue rows carry yesterday's promise: say so loudly instead of blending in with today's.
        case 'pickup': return r.overdue ? { label: 'Pickup overdue', cls: 'rp-pill--red' } : { label: 'Pickup', cls: 'rp-pill--tenant' }
        case 'out': return { label: 'Out', cls: 'rp-pill--blue' }
        case 'return': return r.overdue ? { label: 'Return overdue', cls: 'rp-pill--red' } : { label: 'Due back', cls: 'pill-amber' }
        case 'reserved': return { label: 'Reserved', cls: '' }
        case 'done': return { label: r.status === 'damaged' ? 'Damaged' : 'Returned', cls: 'rp-pill--green' }
        case 'cancelled': return { label: 'Cancelled', cls: 'pill-muted' }
        case 'unpaid': return { label: `Unpaid ${money(r.balanceDueCents)}`, cls: 'rp-pill--red' }
        default: return { label: r.status, cls: '' }
    }
}
function bookingLabel(r: ShopDayRow): string {
    if (r.eventTitle) return r.eventStartsAtUtc ? `${r.eventTitle} · ${clock(r.eventStartsAtUtc)}` : r.eventTitle
    if (r.packageName) return r.sessionStartAtUtc ? `${r.packageName} · ${clock(r.sessionStartAtUtc)}` : r.packageName
    return ''
}

/** Working a booking happens on the bookings tab, which picks the rental up from ?open=. */
function openRental(r: ShopDayRow) {
    router.push({ path: '/Admin/BikeShop/Rentals', query: { tab: 'bookings', open: r.rentalId } })
}

function print() {
    window.print()
}

// ── Lifecycle ──────────────────────────────────────────────────────────────
watch([selectedDate, includeCancelled], load)

onMounted(load)

// The host keeps this panel alive across tab switches, so coming back does not remount it and
// would otherwise show the day as it was when you left: a pickup that has since gone out still
// reading "Pickup". onActivated also fires on the initial mount, right after onMounted has
// kicked a load; the flag skips exactly that one.
let activatedBefore = false
onActivated(() => {
    if (activatedBefore) load()
    activatedBefore = true
})
</script>

<style scoped>
.print-only { display: none; }

.day-title {
    font-weight: 600;
    white-space: nowrap;
}

/* ── KPI tiles: the house stat tile, made pressable. ─────────────────────── */
.kpi-strip {
    display: grid;
    grid-template-columns: repeat(5, minmax(0, 1fr));
    gap: 12px;
}
@media (max-width: 960px) {
    .kpi-strip { grid-template-columns: repeat(3, minmax(0, 1fr)); }
}
@media (max-width: 600px) {
    .kpi-strip { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
.kpi-tile {
    text-align: left;
    padding: 12px 14px;
    background: var(--rp-card);
    border: 1px solid var(--rp-line);
    border-radius: var(--rp-radius-sm);
    color: var(--rp-ink);
    font: inherit;
    cursor: pointer;
    transition: border-color 0.12s ease, background 0.12s ease;
}
.kpi-tile:hover { background: var(--rp-card2); border-color: var(--rp-line2); }
.kpi-tile.kpi-active {
    border-color: var(--rp-ink);
    box-shadow: inset 0 0 0 1px var(--rp-ink);
}
.kpi-tile.kpi-warn .rp-kpi-value { color: var(--rp-red-text); }
.kpi-hint {
    font-size: 11.5px;
    color: var(--rp-muted);
    margin-top: 4px;
}

.chip-off { opacity: 0.55; }

/* ── Program cards ───────────────────────────────────────────────────────── */
/* The colored left edge is the event type's color, or a neutral line for rentals and packages. */
.group-card {
    border-left: 4px solid var(--group-color);
    overflow: hidden;
}
.group-head {
    display: flex;
    align-items: center;
    padding: 10px 14px;
    background: var(--rp-panel);
    border-bottom: 1px solid var(--rp-line2);
    cursor: pointer;
    user-select: none;
}
.group-title {
    font-weight: 700;
    font-size: 15px;
}
.group-counts { white-space: nowrap; }

/* ── The table ───────────────────────────────────────────────────────────── */
.day-table {
    width: 100%;
    border-collapse: collapse;
    font-size: 13px;
}
.day-table th {
    text-align: left;
    font-size: 11px;
    font-weight: 700;
    letter-spacing: 0.04em;
    text-transform: uppercase;
    color: var(--rp-muted);
    padding: 8px 12px;
    border-bottom: 1px solid var(--rp-line);
    white-space: nowrap;
}
.day-table td {
    padding: 8px 12px;
    vertical-align: top;
    border-bottom: 1px solid var(--rp-line);
}
.day-table tbody tr:last-child td { border-bottom: none; }
.day-table tbody tr:hover td { background: var(--rp-panel2); }
.col-time { white-space: nowrap; width: 1%; }
.col-status { white-space: nowrap; width: 1%; }
.col-source { white-space: nowrap; width: 1%; }
.col-action { width: 1%; text-align: right; }
.customer-name { font-weight: 600; }
.customer-name a { color: inherit; text-decoration: underline; text-decoration-color: var(--rp-line3); }
.customer-name a:hover { text-decoration-color: currentColor; }
.gear-line { white-space: nowrap; }
.gear-unit { font-size: 12px; }
.row-done td { color: var(--rp-muted); }
.row-cancelled td { color: var(--rp-muted2); text-decoration: line-through; }
.row-cancelled td .rp-pill { text-decoration: none; }

/* Pill tints the shared stylesheet has no variant for. */
.pill-amber { background: var(--rp-amber-soft); color: var(--rp-amber); }
.pill-muted { background: var(--rp-ink-soft); color: var(--rp-muted2); }

/* ── Phone width: one stacked block per booking instead of a seven-column row. ── */
@media screen and (max-width: 700px) {
    .day-table thead { display: none; }
    .day-table,
    .day-table tbody { display: block; }
    .day-table tbody tr {
        display: block;
        padding: 8px 12px;
        border-bottom: 1px solid var(--rp-line);
    }
    .day-table tbody tr:last-child { border-bottom: none; }
    .day-table tbody tr:hover td { background: transparent; }
    .day-table td {
        display: flex;
        gap: 10px;
        padding: 2px 0;
        border: none;
        width: auto;
        white-space: normal;
    }
    .day-table td::before {
        content: attr(data-label);
        flex: 0 0 68px;
        font-size: 10.5px;
        font-weight: 700;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        color: var(--rp-muted);
        padding-top: 2px;
    }
    .day-table td.cell-action { justify-content: flex-end; padding-top: 6px; }
    .day-table td.cell-action::before { content: none; }
    .gear-line { white-space: normal; }
    .group-counts { display: none; }
}
</style>

<!-- Unscoped on purpose: a print stylesheet has to reach the app chrome (nav drawer, app bar,
     tabs, page header) that this panel does not own. Every rule is inside @media print AND
     gated on body:has(.rdp), so it applies only while this sheet is actually on screen. Without
     the gate, a non-scoped SFC style stays in the document after the component has loaded and
     printing any OTHER admin page would come out with the navigation missing. keep-alive detaches
     the panel's element when another tab is showing, which unloads the gate correctly. -->
<style>
@media print {
    body:has(.rdp) .v-navigation-drawer,
    body:has(.rdp) .v-app-bar,
    body:has(.rdp) .v-footer,
    body:has(.rdp) .v-snackbar,
    body:has(.rdp) .v-tabs,
    body:has(.rdp) .rp-pagehead,
    body:has(.rdp) .no-print {
        display: none !important;
    }
    body:has(.rdp) { background: #fff !important; }
    body:has(.rdp) .v-main { padding: 0 !important; }
    body:has(.rdp) .v-container { max-width: none !important; padding: 0 !important; }

    /* Black on white whatever theme the screen was in: retarget the tokens the panel reads. */
    .rdp {
        color: #000;
        --rp-ink: #000;
        --rp-ink2: #000;
        --rp-muted: #444;
        --rp-muted2: #666;
        --rp-line: #ccc;
        --rp-line2: #bbb;
        --rp-line3: #999;
        --rp-card: #fff;
        --rp-card2: #fff;
        --rp-panel: #fff;
        --rp-panel2: #fff;
        --rp-raise: #fff;
        --rp-ink-soft: transparent;
        --rp-amber-soft: transparent;
        --rp-amber: #000;
        --rp-red-soft: transparent;
        --rp-red-text: #000;
        --rp-blue-soft: transparent;
        --rp-blue: #000;
        --rp-green-soft: transparent;
        --rp-green: #000;
        --rp-tenant: transparent;
        --rp-on-tenant: #000;
    }
    .rdp .print-only { display: block !important; }
    /* Every group open on paper, whatever was folded on screen (beats v-show's inline display). */
    .rdp .group-body { display: block !important; }
    .rdp .v-card {
        box-shadow: none !important;
        background: #fff !important;
        color: #000 !important;
        border: 1px solid #ccc;
        break-inside: avoid;
        page-break-inside: avoid;
    }
    .rdp .group-card { border-left: 4px solid var(--group-color); }
    .rdp .group-head { background: #fff; }
    .rdp .kpi-strip { gap: 8px; }
    .rdp .kpi-tile { border: 1px solid #ccc; background: #fff; color: #000; }
    .rdp .day-table { font-size: 11px; }
    .rdp .day-table tbody tr:hover td { background: transparent; }
    .rdp .rp-pill {
        border: 1px solid #000;
        background: transparent !important;
        color: #000 !important;
    }
    .rdp .customer-name a { color: #000; text-decoration: none; }
    .rdp .row-done td, .rdp .row-cancelled td { color: #555; }
}
</style>
