<!--
    Rental Board: a day timeline of the rental fleet. One row per bookable resource (a serialized
    bike, or a pool variant's whole bucket), bars for what's reserved, empty track for what isn't.

    The question this answers is the one the Rentals page couldn't: not "how many are free between
    these two datetimes" but "at 2pm today, which bikes are on the rack". Drag across free track to
    book that exact resource for that exact window; click a bar to work the rental it belongs to.

    All clock math is in the TENANT's timezone. Timestamps are stored UTC and staff read the track's
    own clock, so every boundary is built with dayjs.tz(..., branding.timezone) and every label goes
    through tenantDayjs.

    A panel, not a page: it lives as the first tab of the Rentals screen, which owns the heading and
    the New rental button. `openBlankBooking` is exposed so that button can drive this panel's
    booking dialog rather than the tab hosting a second copy of it.
-->
<template>
    <div>
        <!-- ── Controls ─────────────────────────────────────────────────── -->
        <v-card class="pa-3 mb-4">
            <div class="d-flex ga-3 align-center flex-wrap">
                <div class="d-flex align-center ga-1">
                    <v-tooltip text="Previous day" location="top">
                        <template #activator="{ props }">
                            <v-btn v-bind="props" icon="mdi-chevron-left" variant="text" size="small"
                                @click="shiftDay(-1)"></v-btn>
                        </template>
                    </v-tooltip>
                    <v-text-field v-model="selectedDate" type="date" density="compact" hide-details
                        style="max-width: 170px"></v-text-field>
                    <v-tooltip text="Next day" location="top">
                        <template #activator="{ props }">
                            <v-btn v-bind="props" icon="mdi-chevron-right" variant="text" size="small"
                                @click="shiftDay(1)"></v-btn>
                        </template>
                    </v-tooltip>
                    <v-btn size="small" variant="tonal" :disabled="isToday" @click="goToday">Today</v-btn>
                </div>

                <v-select v-model="categoryId" :items="categoryOptions" item-title="title" item-value="value"
                    label="Product type" density="compact" hide-details style="max-width: 210px"></v-select>

                <v-text-field v-model="search" density="compact" hide-details clearable
                    prepend-inner-icon="mdi-magnify" label="Search name, SKU or serial"
                    style="max-width: 260px"></v-text-field>

                <v-select v-model.number="startHour" :items="hourOptions" label="From" density="compact"
                    hide-details style="max-width: 120px"></v-select>
                <v-select v-model.number="endHour" :items="endHourOptions" label="Until" density="compact"
                    hide-details style="max-width: 120px"></v-select>
                <v-btn size="small" variant="tonal" :disabled="isFullDay" @click="showFullDay">24 hours</v-btn>

                <v-spacer></v-spacer>
                <v-tooltip text="Reload the board" location="top">
                    <template #activator="{ props }">
                        <v-btn v-bind="props" icon="mdi-refresh" variant="text" size="small"
                            :loading="loading" @click="load"></v-btn>
                    </template>
                </v-tooltip>
            </div>

            <div v-if="!loadError && !loading" class="d-flex ga-2 mt-3 flex-wrap align-center">
                <v-chip size="small" variant="tonal">
                    {{ visibleResources.length }} resource{{ visibleResources.length === 1 ? '' : 's' }}
                </v-chip>
                <v-chip v-if="isToday" size="small" :color="availableNow > 0 ? 'success' : 'error'" variant="tonal">
                    {{ availableNow }} of {{ totalCapacity }} available right now
                </v-chip>
                <v-chip size="small" variant="tonal">
                    {{ visibleSegments.length }} booking{{ visibleSegments.length === 1 ? '' : 's' }} in view
                </v-chip>
                <span class="text-caption text-medium-emphasis">
                    Drag across empty track to book. Click a booking to work it.
                </span>
            </div>
        </v-card>

        <!-- ── States ───────────────────────────────────────────────────── -->
        <!-- A failed load must never render as an empty board: an empty board reads as
             "everything is free", which is exactly the wrong thing to tell the counter. -->
        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">
            {{ loadError }}
            <template #append>
                <v-btn size="small" variant="text" @click="load">Retry</v-btn>
            </template>
        </v-alert>
        <v-card v-else-if="loading && !board" class="pa-10 text-center">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </v-card>
        <v-card v-else-if="groups.length === 0" class="pa-8 text-center text-medium-emphasis">
            <template v-if="search || categoryId">
                Nothing in the fleet matches those filters.
            </template>
            <template v-else>
                No rental products yet. Flag a product as rentable and give its variants a daily rate
                on the <router-link to="/Admin/BikeShop">Inventory</router-link> page.
            </template>
        </v-card>

        <!-- ── Timeline ─────────────────────────────────────────────────── -->
        <v-card v-else class="board-card">
            <div class="board-scroll">
                <div class="board-inner">
                    <!-- Hour ruler -->
                    <div class="board-head">
                        <div class="row-label head-label">
                            {{ tenantDate.format('ddd, MMM D') }}
                        </div>
                        <div class="row-track head-track">
                            <div v-for="h in hourMarks" :key="h.at" class="hour-mark"
                                :style="{ left: h.leftPct + '%' }">
                                <span class="hour-label">{{ h.label }}</span>
                            </div>
                        </div>
                    </div>

                    <template v-for="g in groups" :key="g.key">
                        <div class="group-head" @click="toggleGroup(g.key)">
                            <v-icon size="small" class="mr-1">
                                {{ collapsed.has(g.key) ? 'mdi-chevron-right' : 'mdi-chevron-down' }}
                            </v-icon>
                            <strong>{{ g.productName }}</strong>
                            <span v-if="g.categoryName" class="text-caption text-medium-emphasis ml-2">
                                {{ g.categoryName }}
                            </span>
                            <v-spacer></v-spacer>
                            <span class="text-caption text-medium-emphasis">
                                {{ g.rows.length }} row{{ g.rows.length === 1 ? '' : 's' }}
                            </span>
                        </div>

                        <template v-if="!collapsed.has(g.key)">
                            <div v-for="r in g.rows" :key="r.id" class="board-row"
                                :class="{ 'row-blocked': !isBookable(r) }">
                                <div class="row-label">
                                    <div class="text-body-2 text-truncate">{{ rowTitle(r) }}</div>
                                    <div class="text-caption text-medium-emphasis text-truncate">
                                        {{ rowSubtitle(r) }}
                                    </div>
                                </div>

                                <div class="row-track" :ref="el => registerTrack(r.id, el)"
                                    @mousedown="onTrackDown($event, r)">
                                    <div v-for="h in hourMarks" :key="h.at" class="hour-line"
                                        :style="{ left: h.leftPct + '%' }"></div>

                                    <!-- Serialized: one bar per reservation on this unit. -->
                                    <template v-if="r.trackingKind === 'serialized'">
                                        <v-tooltip v-for="b in barsFor(r)" :key="b.segment.lineId"
                                            :text="barTooltip(b.segment)" location="top">
                                            <template #activator="{ props }">
                                                <div v-bind="props" class="bar"
                                                    :class="[`bar-${b.segment.status}`, { 'bar-clip-l': b.clipLeft, 'bar-clip-r': b.clipRight }]"
                                                    :style="{ left: b.leftPct + '%', width: b.widthPct + '%' }"
                                                    @mousedown.stop @click.stop="openRental(b.segment.rentalId)">
                                                    <span class="bar-text">{{ barLabel(b.segment) }}</span>
                                                </div>
                                            </template>
                                        </v-tooltip>
                                    </template>

                                    <!-- Pool: contiguous occupancy spans, "used of capacity". -->
                                    <template v-else>
                                        <v-tooltip v-for="s in poolSpansFor(r)" :key="s.key"
                                            :text="s.tooltip" location="top">
                                            <template #activator="{ props }">
                                                <div v-bind="props" class="bar"
                                                    :class="s.used >= r.capacity ? 'bar-full' : 'bar-partial'"
                                                    :style="{ left: s.leftPct + '%', width: s.widthPct + '%' }"
                                                    @mousedown.stop @click.stop="openSpan(s)">
                                                    <span class="bar-text">{{ s.used }}/{{ r.capacity }}</span>
                                                </div>
                                            </template>
                                        </v-tooltip>
                                    </template>

                                    <!-- Live drag preview. -->
                                    <div v-if="drag && drag.resourceId === r.id" class="ghost"
                                        :style="{ left: drag.leftPct + '%', width: drag.widthPct + '%' }">
                                        <span class="bar-text">{{ drag.label }}</span>
                                    </div>

                                    <div v-if="showNowLine" class="now-line" :style="{ left: nowLeftPct + '%' }"></div>
                                </div>

                                <div class="row-action">
                                    <v-tooltip :text="isBookable(r) ? 'Rent this' : 'On the bench, not rentable'"
                                        location="top">
                                        <template #activator="{ props }">
                                            <div v-bind="props">
                                                <v-btn icon="mdi-plus" size="x-small" variant="tonal"
                                                    :disabled="!isBookable(r)" @click="quickBook(r)"></v-btn>
                                            </div>
                                        </template>
                                    </v-tooltip>
                                </div>
                            </div>
                        </template>
                    </template>
                </div>
            </div>

            <div class="d-flex ga-4 flex-wrap align-center pa-3 text-caption text-medium-emphasis">
                <span class="legend"><i class="swatch bar-pending"></i> Pending payment</span>
                <span class="legend"><i class="swatch bar-paid"></i> Paid, not picked up</span>
                <span class="legend"><i class="swatch bar-out"></i> Out with the rider</span>
                <span class="legend"><i class="swatch bar-partial"></i> Pool partly booked</span>
                <span class="legend"><i class="swatch bar-full"></i> Pool fully booked</span>
                <span class="legend"><i class="swatch swatch-blocked"></i> On the bench</span>
            </div>
        </v-card>

        <!-- ── Rental detail, from a clicked bar ────────────────────────── -->
        <v-dialog v-model="rentalOpen" max-width="560">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Rental{{ openRentalData?.orderNumber != null ? ` #${openRentalData.orderNumber}` : '' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="rentalOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <!-- A pool span can cover several bookings at once (three helmets out to three
                         different renters). Opening one at random would hide the others, so when
                         the span is shared, pick first. -->
                    <div v-if="spanChoices.length > 1 && !openRentalData" class="mb-2">
                        <p class="text-body-2 text-medium-emphasis mb-2">
                            {{ spanChoices.length }} bookings overlap there. Which one?
                        </p>
                        <v-list density="compact">
                            <v-list-item v-for="c in spanChoices" :key="c.rentalId"
                                :title="c.renterName || c.renterEmail || 'Walk-in'"
                                :subtitle="c.subtitle" @click="openRental(c.rentalId)">
                                <template #prepend>
                                    <v-chip size="x-small" :color="statusColor(c.status)">{{ c.status }}</v-chip>
                                </template>
                            </v-list-item>
                        </v-list>
                    </div>

                    <div v-else-if="rentalLoading" class="text-center py-6">
                        <v-progress-circular indeterminate color="primary"></v-progress-circular>
                    </div>
                    <v-alert v-else-if="rentalError" type="error" variant="tonal" density="compact">
                        {{ rentalError }}
                    </v-alert>
                    <template v-else-if="openRentalData">
                        <div class="d-flex align-center ga-2 mb-3">
                            <v-chip size="small" :color="statusColor(openRentalData.status)">
                                {{ openRentalData.status }}
                            </v-chip>
                            <span class="text-body-2">{{ openRentalData.renterName || 'Walk-in' }}</span>
                            <span v-if="openRentalData.renterPhone" class="text-caption text-medium-emphasis">
                                {{ openRentalData.renterPhone }}
                            </span>
                        </div>
                        <v-table density="compact" class="mb-3">
                            <tbody>
                                <tr>
                                    <td>Window</td>
                                    <td class="text-right">
                                        {{ formatTenantDateTime(openRentalData.startsAt, 'MMM D h:mm A') }}
                                        to {{ formatTenantDateTime(openRentalData.endsAt, 'MMM D h:mm A') }}
                                    </td>
                                </tr>
                                <tr>
                                    <td>Gear</td>
                                    <td class="text-right">{{ itemsLabel(openRentalData) }}</td>
                                </tr>
                                <tr><td>Total</td><td class="text-right">{{ money(openRentalData.totalCents) }}</td></tr>
                                <tr>
                                    <td>Deposit</td>
                                    <td class="text-right">
                                        {{ money(openRentalData.depositCents) }}
                                        <span v-if="openRentalData.depositCapturedCents > 0" class="text-error">
                                            (kept {{ money(openRentalData.depositCapturedCents) }})
                                        </span>
                                    </td>
                                </tr>
                            </tbody>
                        </v-table>

                        <div class="d-flex ga-2 flex-wrap">
                            <v-btn v-if="openRentalData.status === 'paid'" color="primary" variant="tonal"
                                size="small" :loading="actionBusy" @click="checkOut(openRentalData)">
                                Check out
                            </v-btn>
                            <v-btn v-if="openRentalData.status === 'out'" color="secondary" variant="tonal"
                                size="small" @click="startReturn(openRentalData)">
                                Return
                            </v-btn>
                            <v-btn v-if="openRentalData.status === 'pending' || openRentalData.status === 'paid'"
                                variant="text" size="small" prepend-icon="mdi-draw-pen" @click="signOpen = true">
                                Agreement + waiver
                            </v-btn>
                            <v-btn variant="text" size="small" prepend-icon="mdi-camera"
                                @click="photosOpen = true">Photos</v-btn>
                            <v-spacer></v-spacer>
                            <v-btn v-if="openRentalData.status === 'pending' || openRentalData.status === 'paid'"
                                color="error" variant="text" size="small" @click="cancel(openRentalData)">
                                Cancel
                            </v-btn>
                        </div>

                        <!-- ── Staff notes ──────────────────────────────────
                             Append-only, so the person who wrote "comped, don't charge" is
                             still attached to it three weeks later. Internal: the renter
                             never sees any of this. -->
                        <v-divider class="my-3"></v-divider>
                        <div class="d-flex align-center mb-2">
                            <span class="text-subtitle-2">Staff notes</span>
                            <v-chip v-if="notes.length" size="x-small" variant="tonal" class="ml-2">
                                {{ notes.length }}
                            </v-chip>
                            <v-spacer></v-spacer>
                            <span class="text-caption text-medium-emphasis">Not shown to the renter</span>
                        </div>

                        <v-alert v-if="notesError" type="error" variant="tonal" density="compact" class="mb-2">
                            {{ notesError }}
                        </v-alert>

                        <div class="d-flex ga-2 align-start mb-3">
                            <v-textarea v-model="noteDraft" label="Add a note" rows="2" auto-grow
                                density="compact" hide-details style="flex: 1"
                                @keydown.ctrl.enter="addNote"></v-textarea>
                            <v-btn color="primary" size="small" :loading="savingNote"
                                :disabled="!noteDraft.trim()" @click="addNote">Add</v-btn>
                        </div>

                        <div v-if="notesLoading" class="text-caption text-medium-emphasis">Loading notes…</div>
                        <div v-else-if="notes.length === 0" class="text-caption text-medium-emphasis">
                            No notes yet.
                        </div>
                        <div v-for="n in notes" :key="n.id" class="rental-note">
                            <div class="text-body-2">{{ n.body }}</div>
                            <div class="text-caption text-medium-emphasis">
                                {{ n.createdByName || 'Staff' }} · {{ formatNoteAt(n.createdAt) }}
                            </div>
                        </div>
                    </template>
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- ── Agreement + waiver ──────────────────────────────────────── -->
        <v-dialog v-model="signOpen" max-width="560">
            <v-card v-if="openRentalData">
                <v-card-title class="d-flex align-center">
                    <span class="text-body-1">Before check out</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="signOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-caption text-medium-emphasis mb-3">
                        Signed on this device at pickup. Gear can't be checked out until both the
                        agreement and the waiver are signed.
                    </p>
                    <RentalReadinessPanel :rental-id="openRentalData.id"
                        :renter-name="openRentalData.renterName" :renter-email="openRentalData.renterEmail" />
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- ── Condition photos ────────────────────────────────────────── -->
        <v-dialog v-model="photosOpen" max-width="720">
            <v-card v-if="openRentalData">
                <v-card-title class="d-flex align-center">
                    <span>Condition photos</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="photosOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <ConditionPhotos :rental-id="openRentalData.id" stage="intake" title="Going out"
                        hint="Photograph the gear before it leaves, especially any existing damage." />
                    <v-divider class="my-4"></v-divider>
                    <ConditionPhotos :rental-id="openRentalData.id" stage="return" title="Coming back"
                        hint="Photograph anything damaged on return." />
                    <v-divider class="my-4"></v-divider>
                    <PhotoQrPanel kind="rental" :id="openRentalData.id" />
                </v-card-text>
            </v-card>
        </v-dialog>

        <BookRentalDialog v-model="bookOpen" :rentable-variants="rentableVariants" :preset="bookPreset"
            @booked="refresh" @notify="flash" />

        <ReturnRentalDialog v-model="returnOpen" :rental="returning" @returned="onReturned" />

        <v-snackbar v-model="snackbar" :color="snackColor" :timeout="4000">{{ snackText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onActivated, onBeforeUnmount } from 'vue'
import dayjs from 'dayjs'
import { formatTenantDateTime, tenantDayjs } from '@/helpers/TenantTime'
import {
    BikeShopService,
    type ShopRental,
    type ShopRentalBoard,
    type ShopRentalBoardResource,
    type ShopRentalBoardSegment,
    type ShopRentalNote,
} from '@/services/BikeShopService'
import BookRentalDialog, { type BookRentalPreset, type RentableVariantOption } from '@/components/bikeshop/BookRentalDialog.vue'
import ReturnRentalDialog from '@/components/bikeshop/ReturnRentalDialog.vue'
import RentalReadinessPanel from '@/components/bikeshop/RentalReadinessPanel.vue'
import ConditionPhotos from '@/components/bikeshop/ConditionPhotos.vue'
import PhotoQrPanel from '@/components/bikeshop/PhotoQrPanel.vue'
import { branding } from '@/stores/branding'
import { useConfirm } from '@/composables/useConfirm'

const service = new BikeShopService()
const confirm = useConfirm()

const snackbar = ref(false); const snackText = ref(''); const snackColor = ref<'success' | 'error'>('success')
function flash(t: string, c: 'success' | 'error' = 'success') { snackText.value = t; snackColor.value = c; snackbar.value = true }
function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
function tz(): string { return branding.timezone || 'UTC' }

// ── The viewed window ──────────────────────────────────────────────────────
// Every boundary is a tenant-timezone wall-clock instant. dayjs.tz() parses the naive string in
// that zone, so "2026-07-24T06:00" is 6am AT THE TRACK regardless of where the admin is sitting.
const selectedDate = ref(tenantDayjs(new Date()).format('YYYY-MM-DD'))
const startHour = ref(6)
const endHour = ref(20)
const search = ref('')
const categoryId = ref<string | null>(null)

const hourOptions = Array.from({ length: 24 }, (_, h) => ({ title: hourLabel(h), value: h }))
// "Until" tops out at 24 (midnight the next morning), which "From" can never be.
const endHourOptions = Array.from({ length: 24 }, (_, i) => ({ title: hourLabel(i + 1), value: i + 1 }))
function hourLabel(h: number): string {
    if (h === 0) return '12 AM'
    if (h === 24) return '12 AM (+1)'
    if (h === 12) return '12 PM'
    return h < 12 ? `${h} AM` : `${h - 12} PM`
}

const tenantDate = computed(() => dayjs.tz(`${selectedDate.value}T00:00:00`, tz()))
const viewStart = computed(() => tenantDate.value.add(startHour.value, 'hour'))
const viewEnd = computed(() => tenantDate.value.add(endHour.value, 'hour'))
const viewStartMs = computed(() => viewStart.value.valueOf())
const viewEndMs = computed(() => viewEnd.value.valueOf())
const spanMs = computed(() => Math.max(1, viewEndMs.value - viewStartMs.value))
const isToday = computed(() => selectedDate.value === tenantDayjs(new Date()).format('YYYY-MM-DD'))
const isFullDay = computed(() => startHour.value === 0 && endHour.value === 24)

function shiftDay(delta: number) {
    selectedDate.value = dayjs(selectedDate.value).add(delta, 'day').format('YYYY-MM-DD')
}
function goToday() { selectedDate.value = tenantDayjs(new Date()).format('YYYY-MM-DD') }
function showFullDay() { startHour.value = 0; endHour.value = 24 }

// An inverted range would divide by a negative span and draw bars backwards. Push the other end
// rather than refusing, so the control can never be left in a broken state.
watch(startHour, h => { if (endHour.value <= h) endHour.value = Math.min(24, h + 1) })
watch(endHour, h => { if (h <= startHour.value) startHour.value = Math.max(0, h - 1) })

/** Percent across the visible track for an instant, unclamped. */
function pctFor(ms: number): number { return ((ms - viewStartMs.value) / spanMs.value) * 100 }

const hourMarks = computed(() => {
    // One label per hour while that stays readable, otherwise every other or every third hour.
    const hours = endHour.value - startHour.value
    const step = hours <= 14 ? 1 : hours <= 20 ? 2 : 3
    const marks: { at: number; leftPct: number; label: string }[] = []
    for (let h = startHour.value; h < endHour.value; h += step) {
        marks.push({
            at: h,
            leftPct: ((h - startHour.value) / hours) * 100,
            label: hourLabel(h % 24).replace(' (+1)', ''),
        })
    }
    return marks
})

// ── Now line ───────────────────────────────────────────────────────────────
const nowMs = ref(Date.now())
let nowTimer: number | undefined
const showNowLine = computed(() => nowMs.value >= viewStartMs.value && nowMs.value <= viewEndMs.value)
const nowLeftPct = computed(() => pctFor(nowMs.value))

// ── Data ───────────────────────────────────────────────────────────────────
const board = ref<ShopRentalBoard | null>(null)
const loading = ref(false)
const loadError = ref('')

// Stepping through days fast (or an hour change that nudges the other end) puts several loads in
// flight at once. Only the newest may write: an older response landing last would paint yesterday's
// bars under today's date, which is how staff hand out a bike that is actually reserved.
let loadSeq = 0

async function load() {
    const seq = ++loadSeq
    const forDate = tenantDate.value.format('MMM D')
    loading.value = true
    loadError.value = ''
    try {
        // Fetched WIDER than it is drawn. The board's pre-drag availability check (usedOver) can
        // only reason about the reservations it has been told about, and a window the user can
        // create is not bounded by the window on screen: a plain click extends two hours past the
        // click, and the dialog's From/Until fields are freely editable afterwards. Fetching only
        // the visible hours meant a bike booked at 20:00 looked free to a 19:00 drag, the drag
        // sailed through, and the server refused the booking a form-fill later. A day of padding
        // either side costs a handful of rows and closes that blind spot.
        //
        // Nothing downstream needs to change: bars outside the view clip to nothing in toBar,
        // pool spans are computed only at boundaries inside the view, and visibleSegments filters
        // on overlap for its count.
        const r = await service.rentalBoard(
            viewStart.value.subtract(1, 'day').toISOString(),
            viewEnd.value.add(1, 'day').toISOString(),
            categoryId.value)
        if (seq !== loadSeq) return
        board.value = r.data.data
    } catch (e: any) {
        if (seq !== loadSeq) return
        // Blank the board too, for the same reason: an empty board is honest about not knowing,
        // a stale one is not.
        board.value = null
        loadError.value = e.response?.data?.error
            || `Could not load the rental board for ${forDate}. Check your connection and retry.`
    } finally {
        if (seq === loadSeq) loading.value = false
    }
}

// Category options survive a filtered load because the server computes them over the whole fleet.
const categoryOptions = computed(() => [
    { title: 'All product types', value: null as string | null },
    ...(board.value?.categories ?? []).map(c => ({ title: c.name, value: c.id as string | null })),
])

const allResources = computed(() => board.value?.resources ?? [])
const allSegments = computed(() => board.value?.segments ?? [])

// Search is client-side over the already-loaded fleet: a rental fleet is tens of rows, and
// re-querying on every keystroke would flash the whole board.
const visibleResources = computed(() => {
    const term = search.value?.trim().toLowerCase()
    if (!term) return allResources.value
    return allResources.value.filter(r =>
        r.productName.toLowerCase().includes(term)
        || (r.brand ?? '').toLowerCase().includes(term)
        || (r.sku ?? '').toLowerCase().includes(term)
        || (r.unitLabel ?? '').toLowerCase().includes(term)
        || (r.serial ?? '').toLowerCase().includes(term)
        || variantLabel(r).toLowerCase().includes(term))
})

/** Segments that belong to a currently visible row (the API also returns lines for filtered-out
 *  fleet, which must not inflate the "bookings in view" count). */
const visibleSegments = computed(() => {
    const items = new Set(visibleResources.value.map(r => r.itemId).filter(Boolean) as string[])
    const pools = new Set(visibleResources.value.filter(r => !r.itemId).map(r => r.variantId))
    return allSegments.value.filter(s => {
        if (!(s.itemId ? items.has(s.itemId) : pools.has(s.variantId))) return false
        // Overlap with the DRAWN window, not the fetched one. The fetch is padded a day either
        // side so the drag check can see conflicts off-screen; counting those here would report
        // bookings the user cannot see on a chip that says "in view".
        return new Date(s.startsAt).getTime() < viewEndMs.value
            && new Date(s.endsAt).getTime() > viewStartMs.value
    })
})

const groups = computed(() => {
    const map = new Map<string, { key: string; productName: string; categoryName: string | null; rows: ShopRentalBoardResource[] }>()
    for (const r of visibleResources.value) {
        const g = map.get(r.productId)
            ?? { key: r.productId, productName: r.productName, categoryName: r.categoryName, rows: [] }
        g.rows.push(r)
        map.set(r.productId, g)
    }
    return [...map.values()].sort((a, b) => a.productName.localeCompare(b.productName))
})

const collapsed = ref<Set<string>>(new Set())
function toggleGroup(key: string) {
    const next = new Set(collapsed.value)
    if (next.has(key)) next.delete(key); else next.add(key)
    collapsed.value = next
}

function variantLabel(r: ShopRentalBoardResource): string {
    return [r.size, r.color, r.gender].filter(Boolean).join(' / ')
}
function rowTitle(r: ShopRentalBoardResource): string {
    if (r.itemId) return r.unitLabel || r.productName
    return variantLabel(r) || r.productName
}
function rowSubtitle(r: ShopRentalBoardResource): string {
    const bits: string[] = []
    if (r.itemId) {
        if (variantLabel(r)) bits.push(variantLabel(r))
        if (r.serial) bits.push(r.serial)
        if (r.itemStatus === 'maintenance') bits.push('on the bench')
    } else {
        bits.push(`pool of ${r.capacity}`)
        if (r.sku) bits.push(r.sku)
    }
    bits.push(`${money(r.dailyRateCents)}/day`)
    return bits.join(' · ')
}
/** A unit on the bench holds no reservation but must not be handed out. */
function isBookable(r: ShopRentalBoardResource): boolean {
    return r.itemStatus !== 'maintenance' && r.capacity > 0
}

// ── Bars ───────────────────────────────────────────────────────────────────
interface Bar {
    segment: ShopRentalBoardSegment
    leftPct: number
    widthPct: number
    clipLeft: boolean
    clipRight: boolean
}

function barsFor(r: ShopRentalBoardResource): Bar[] {
    return allSegments.value
        .filter(s => s.itemId && s.itemId === r.itemId)
        .map(s => toBar(s))
        .filter(Boolean) as Bar[]
}

function toBar(s: ShopRentalBoardSegment): Bar | null {
    const start = new Date(s.startsAt).getTime()
    const end = new Date(s.endsAt).getTime()
    const left = Math.max(0, pctFor(start))
    const right = Math.min(100, pctFor(end))
    if (right <= left) return null
    return {
        segment: s,
        leftPct: left,
        widthPct: right - left,
        // Multi-day rentals run past the edges of a one-day view; mark the cut so a bar that
        // stops at 8pm isn't read as a rental that ends at 8pm.
        clipLeft: start < viewStartMs.value,
        clipRight: end > viewEndMs.value,
    }
}

function barLabel(s: ShopRentalBoardSegment): string {
    const who = s.renterName || s.renterEmail || 'Walk-in'
    return s.orderNumber != null ? `#${s.orderNumber} ${who}` : who
}
function barTooltip(s: ShopRentalBoardSegment): string {
    const who = s.renterName || s.renterEmail || 'Walk-in'
    const win = `${formatTenantDateTime(s.startsAt, 'MMM D h:mm A')} to ${formatTenantDateTime(s.endsAt, 'MMM D h:mm A')}`
    const qty = s.quantity > 1 ? ` ×${s.quantity}` : ''
    // When the gear actually left matters at the counter: a booking that says 10am but was picked
    // up at 11:20 is a different conversation from one still sitting on the rack.
    const out = s.checkedOutAt ? ` · picked up ${formatTenantDateTime(s.checkedOutAt, 'h:mm A')}` : ''
    return `${who} · ${s.nameSnapshot}${qty} · ${win} · ${s.status}${out}`
}

// ── Pool occupancy ─────────────────────────────────────────────────────────
// A pool row is one bucket, so it can't draw one bar per booking without them stacking on top of
// each other. Instead sweep the reservation boundaries and draw contiguous "used of capacity"
// spans: the counter reads how many helmets are spoken for at any moment, not who has which.
interface PoolSpan {
    key: string
    leftPct: number
    widthPct: number
    used: number
    rentalIds: string[]
    tooltip: string
}

function poolSpansFor(r: ShopRentalBoardResource): PoolSpan[] {
    const segs = allSegments.value.filter(s => !s.itemId && s.variantId === r.variantId)
    if (segs.length === 0) return []

    const bounds = new Set<number>([viewStartMs.value, viewEndMs.value])
    for (const s of segs) {
        const a = new Date(s.startsAt).getTime()
        const b = new Date(s.endsAt).getTime()
        if (a > viewStartMs.value && a < viewEndMs.value) bounds.add(a)
        if (b > viewStartMs.value && b < viewEndMs.value) bounds.add(b)
    }
    const points = [...bounds].sort((a, b) => a - b)

    const raw: { from: number; to: number; used: number; rentalIds: string[] }[] = []
    for (let i = 0; i < points.length - 1; i++) {
        const from = points[i]
        const to = points[i + 1]
        const mid = from + (to - from) / 2
        const covering = segs.filter(s =>
            new Date(s.startsAt).getTime() <= mid && new Date(s.endsAt).getTime() > mid)
        const used = covering.reduce((sum, s) => sum + s.quantity, 0)
        if (used > 0) raw.push({ from, to, used, rentalIds: covering.map(s => s.rentalId) })
    }

    // Merge neighbours at the same level so an unchanged occupancy reads as one block.
    const merged: typeof raw = []
    for (const span of raw) {
        const prev = merged[merged.length - 1]
        if (prev && prev.to === span.from && prev.used === span.used) {
            prev.to = span.to
            prev.rentalIds = [...new Set([...prev.rentalIds, ...span.rentalIds])]
        } else {
            merged.push({ ...span })
        }
    }

    return merged.map(s => ({
        key: `${r.id}:${s.from}`,
        leftPct: Math.max(0, pctFor(s.from)),
        widthPct: Math.min(100, pctFor(s.to)) - Math.max(0, pctFor(s.from)),
        used: s.used,
        rentalIds: s.rentalIds,
        tooltip: `${s.used} of ${r.capacity} out · `
            + `${formatTenantDateTime(new Date(s.from).toISOString(), 'h:mm A')} to `
            + `${formatTenantDateTime(new Date(s.to).toISOString(), 'h:mm A')} · `
            + `${s.rentalIds.length} booking${s.rentalIds.length === 1 ? '' : 's'}`,
    }))
}

/** How many units of a resource are spoken for over [from, to). */
function usedOver(r: ShopRentalBoardResource, from: number, to: number): number {
    const segs = allSegments.value.filter(s =>
        r.itemId ? s.itemId === r.itemId : (!s.itemId && s.variantId === r.variantId))
    let peak = 0
    // Peak, not average: a window is only bookable if capacity holds at its busiest moment.
    const bounds = new Set<number>([from])
    for (const s of segs) {
        const a = new Date(s.startsAt).getTime()
        if (a > from && a < to) bounds.add(a)
    }
    for (const point of bounds) {
        const mid = point + 1
        const used = segs
            .filter(s => new Date(s.startsAt).getTime() <= mid && new Date(s.endsAt).getTime() > mid)
            .reduce((sum, s) => sum + s.quantity, 0)
        if (used > peak) peak = used
    }
    return peak
}

const totalCapacity = computed(() =>
    visibleResources.value.filter(isBookable).reduce((sum, r) => sum + r.capacity, 0))
const availableNow = computed(() =>
    visibleResources.value.filter(isBookable).reduce((sum, r) => {
        const used = usedOver(r, nowMs.value, nowMs.value + 1)
        return sum + Math.max(0, r.capacity - used)
    }, 0))

// ── Drag to book ───────────────────────────────────────────────────────────
// Bookings snap to a quarter hour: staff think in "10 to 12", and a pixel-exact 10:07 start would
// be noise on the receipt.
const SNAP_MINUTES = 15
const DEFAULT_CLICK_HOURS = 2

const tracks = new Map<string, HTMLElement>()
function registerTrack(id: string, el: any) {
    if (el) tracks.set(id, el as HTMLElement); else tracks.delete(id)
}

interface DragState {
    resourceId: string
    resource: ShopRentalBoardResource
    anchorMs: number
    fromMs: number
    toMs: number
    leftPct: number
    widthPct: number
    label: string
    moved: boolean
}
const drag = ref<DragState | null>(null)

function msFromClientX(el: HTMLElement, clientX: number): number {
    const rect = el.getBoundingClientRect()
    const ratio = Math.min(1, Math.max(0, (clientX - rect.left) / rect.width))
    const raw = viewStartMs.value + ratio * spanMs.value
    const snap = SNAP_MINUTES * 60_000
    return Math.round(raw / snap) * snap
}

function onTrackDown(ev: MouseEvent, r: ShopRentalBoardResource) {
    if (ev.button !== 0) return
    if (!isBookable(r)) {
        flash(`${rowTitle(r)} is on the bench and can't be rented until it's back in service.`, 'error')
        return
    }
    const el = tracks.get(r.id)
    if (!el) return
    ev.preventDefault()
    const at = msFromClientX(el, ev.clientX)
    drag.value = {
        resourceId: r.id, resource: r, anchorMs: at, fromMs: at, toMs: at,
        leftPct: pctFor(at), widthPct: 0, label: '', moved: false,
    }
    window.addEventListener('mousemove', onDragMove)
    window.addEventListener('mouseup', onDragUp)
}

function onDragMove(ev: MouseEvent) {
    const d = drag.value
    if (!d) return
    const el = tracks.get(d.resourceId)
    if (!el) return
    const at = msFromClientX(el, ev.clientX)
    const from = Math.min(d.anchorMs, at)
    const to = Math.max(d.anchorMs, at)
    d.fromMs = from
    d.toMs = to
    d.moved = to > from
    d.leftPct = Math.max(0, pctFor(from))
    d.widthPct = Math.min(100, pctFor(to)) - d.leftPct
    d.label = to > from ? `${fmtClock(from)} to ${fmtClock(to)}` : ''
}

function onDragUp() {
    window.removeEventListener('mousemove', onDragMove)
    window.removeEventListener('mouseup', onDragUp)
    const d = drag.value
    drag.value = null
    if (!d) return

    // A plain click (no drag) means "book this one, starting here" with a sensible default length
    // that staff can still edit in the dialog.
    let from = d.fromMs
    let to = d.moved ? d.toMs : from + DEFAULT_CLICK_HOURS * 3_600_000
    if (to <= from) to = from + SNAP_MINUTES * 60_000

    // Don't open a dialog the server is certain to reject: re-check the drawn span against the
    // bars already on the board.
    const used = usedOver(d.resource, from, to)
    if (used >= d.resource.capacity) {
        flash(`${rowTitle(d.resource)} is already booked across that window. Pick an open gap.`, 'error')
        return
    }

    bookPreset.value = {
        startsAt: localInput(from),
        endsAt: localInput(to),
        variantId: d.resource.variantId,
        itemId: d.resource.itemId ?? undefined,
    }
    bookOpen.value = true
}

function quickBook(r: ShopRentalBoardResource) {
    // The button path for touch screens and keyboards: start at the next quarter hour inside the
    // view, or at the start of the view when looking at another day.
    const snap = SNAP_MINUTES * 60_000
    const base = isToday.value && nowMs.value > viewStartMs.value && nowMs.value < viewEndMs.value
        ? Math.ceil(nowMs.value / snap) * snap
        : viewStartMs.value
    bookPreset.value = {
        startsAt: localInput(base),
        endsAt: localInput(base + DEFAULT_CLICK_HOURS * 3_600_000),
        variantId: r.variantId,
        itemId: r.itemId ?? undefined,
    }
    bookOpen.value = true
}

function openBlankBooking() {
    bookPreset.value = null
    bookOpen.value = true
}

// The host page owns the "New rental" button (it sits in the page header, above the tabs), so it
// drives this panel's dialog rather than mounting a second one that would fight over the same
// Stripe element.
defineExpose({ openBlankBooking })

/** A UTC instant as the tenant-local "YYYY-MM-DDTHH:mm" a datetime-local input expects. */
function localInput(ms: number): string {
    return tenantDayjs(new Date(ms)).format('YYYY-MM-DDTHH:mm')
}
function fmtClock(ms: number): string {
    return tenantDayjs(new Date(ms)).format('h:mm A')
}

// ── Booking dialog ─────────────────────────────────────────────────────────
const bookOpen = ref(false)
const bookPreset = ref<BookRentalPreset | null>(null)

// The dialog's picker is fed from the board payload rather than the catalog endpoints, which sit
// behind CatalogManage while this page only requires ShopCounter. One entry per rentable variant,
// so pool buckets appear once and a serialized product appears once (not once per unit).
const rentableVariants = computed<RentableVariantOption[]>(() => {
    const byVariant = new Map<string, RentableVariantOption>()
    for (const r of allResources.value) {
        if (byVariant.has(r.variantId)) continue
        const label = variantLabel(r)
        byVariant.set(r.variantId, {
            id: r.variantId,
            title: `${r.productName}${label ? ` (${label})` : ''} — ${money(r.dailyRateCents)}/day`,
            name: r.productName,
            trackingKind: r.trackingKind,
            dailyRateCents: r.dailyRateCents,
            depositCents: r.depositCents,
        })
    }
    return [...byVariant.values()].sort((a, b) => a.title.localeCompare(b.title))
})

// Anything that changes a booking is announced, so the sibling All Bookings tab doesn't go stale
// behind a rental the user just created, checked out, returned, or cancelled from the board.
// Reloading the board alone would leave the other tab showing the world as it was.
const emit = defineEmits<{ (e: 'changed'): void }>()

async function refresh() {
    await load()
    emit('changed')
}

// ── Rental detail from a bar ───────────────────────────────────────────────
const rentalOpen = ref(false)
const rentalLoading = ref(false)
const rentalError = ref('')
const openRentalData = ref<ShopRental | null>(null)
const actionBusy = ref(false)
const signOpen = ref(false)
const photosOpen = ref(false)

/** Bookings a clicked pool span covers, when it covers more than one. */
interface SpanChoice {
    rentalId: string
    renterName: string | null
    renterEmail: string | null
    status: string
    subtitle: string
}
const spanChoices = ref<SpanChoice[]>([])

/**
 * A pool span is an occupancy level, not a single booking: "3 of 8 out" can be three renters. Open
 * the one booking directly when that's all there is, otherwise show the list so none is hidden.
 */
function openSpan(span: PoolSpan) {
    const segs = span.rentalIds.map(id => allSegments.value.find(s => s.rentalId === id)).filter(Boolean) as ShopRentalBoardSegment[]
    if (segs.length <= 1) {
        spanChoices.value = []
        if (segs[0]) openRental(segs[0].rentalId)
        return
    }
    spanChoices.value = segs.map(s => ({
        rentalId: s.rentalId,
        renterName: s.renterName,
        renterEmail: s.renterEmail,
        status: s.status,
        subtitle: `${s.nameSnapshot}${s.quantity > 1 ? ' ×' + s.quantity : ''} · `
            + `${formatTenantDateTime(s.startsAt, 'MMM D h:mm A')} to ${formatTenantDateTime(s.endsAt, 'MMM D h:mm A')}`,
    }))
    openRentalData.value = null
    rentalError.value = ''
    rentalLoading.value = false
    rentalOpen.value = true
}

// ── Staff notes on a booking ─────────────────────────────────────────────────
// Append-only thread, internal. Kept beside the rental rather than folded into
// conditionNotes, which is the single how-it-came-back record written at return.
const notes = ref<ShopRentalNote[]>([])
const notesLoading = ref(false)
const notesError = ref<string | null>(null)
const noteDraft = ref('')
const savingNote = ref(false)

function formatNoteAt(iso: string) {
    return dayjs(iso).format('MMM D, h:mm a')
}

async function loadNotes(rentalId: string) {
    notesLoading.value = true
    notesError.value = null
    try {
        const r = await service.listRentalNotes(rentalId)
        notes.value = r.data.data
    } catch (e: any) {
        notesError.value = e.response?.data?.error
            || 'Could not load the notes for this booking. Reopen it to try again.'
    } finally {
        notesLoading.value = false
    }
}

async function addNote() {
    const body = noteDraft.value.trim()
    const rentalId = openRentalData.value?.id
    if (!body || !rentalId) return
    savingNote.value = true
    notesError.value = null
    try {
        const r = await service.addRentalNote(rentalId, body)
        // Prepend rather than refetch: the thread is newest-first and the server
        // returns the row it just wrote, author name included.
        notes.value = [r.data.data, ...notes.value]
        noteDraft.value = ''
    } catch (e: any) {
        notesError.value = e.response?.data?.error
            || 'Could not save the note. It has not been added; try again.'
    } finally {
        savingNote.value = false
    }
}

async function openRental(rentalId: string) {
    spanChoices.value = []
    rentalOpen.value = true
    rentalLoading.value = true
    rentalError.value = ''
    openRentalData.value = null
    notes.value = []
    noteDraft.value = ''
    notesError.value = null
    try {
        const r = await service.getRental(rentalId)
        openRentalData.value = r.data.data
        // Notes are secondary: load them after the rental so a note failure never
        // stops the booking itself from opening.
        loadNotes(rentalId)
    } catch (e: any) {
        rentalError.value = e.response?.data?.error
            || 'Could not open that rental. Close this and try again, or find it on the bookings list.'
    } finally {
        rentalLoading.value = false
    }
}

function statusColor(s: string) {
    return s === 'paid' ? 'primary' : s === 'out' ? 'indigo' : s === 'returned' ? 'success'
        : s === 'damaged' ? 'warning' : s === 'pending' ? 'grey' : 'error'
}
function itemsLabel(r: ShopRental): string {
    return r.lines.map(l => `${l.nameSnapshot}${l.quantity > 1 ? ' ×' + l.quantity : ''}`).join(', ')
}

async function checkOut(r: ShopRental) {
    actionBusy.value = true
    try {
        await service.checkOutRental(r.id)
        flash('Checked out — gear is on its way.')
        rentalOpen.value = false
        await refresh()
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not check out this rental. It may still need a signature.', 'error')
    } finally { actionBusy.value = false }
}

const returnOpen = ref(false)
const returning = ref<ShopRental | null>(null)
function startReturn(r: ShopRental) {
    returning.value = r
    returnOpen.value = true
}
async function onReturned(capturedCents: number) {
    flash(capturedCents > 0
        ? `Returned — ${money(capturedCents)} kept from the deposit.`
        : 'Returned — deposit released in full.')
    rentalOpen.value = false
    await refresh()
}

async function cancel(r: ShopRental) {
    const ok = await confirm({
        title: 'Cancel this rental?',
        message: r.status === 'paid'
            ? 'The deposit hold is released. The paid fee is NOT auto-refunded — refund it separately if owed.'
            : 'The pending booking is discarded.',
        confirmText: 'Cancel rental',
    })
    if (!ok) return
    try {
        await service.cancelRental(r.id)
        flash('Rental cancelled.')
        rentalOpen.value = false
        await refresh()
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not cancel this rental. Please try again.', 'error')
    }
}

// ── Lifecycle ──────────────────────────────────────────────────────────────
// Any change to the requested window or the server-side filter is a refetch; search is local.
watch([selectedDate, startHour, endHour, categoryId], load)

onMounted(() => {
    load()
    nowTimer = window.setInterval(() => { nowMs.value = Date.now() }, 60_000)
})

// The host keeps this panel alive across tab switches, so coming back does NOT remount it and
// would otherwise show whatever the board looked like when you left. On a gate screen a stale
// board is worse than a slow one: it can show a bike as free that someone booked five minutes ago.
//
// onActivated also fires on the initial mount, right after onMounted has already kicked a load.
// The flag skips exactly that one, rather than inferring it from whether data has arrived yet.
let activatedBefore = false
onActivated(() => {
    if (activatedBefore) load()
    activatedBefore = true
})

onBeforeUnmount(() => {
    if (nowTimer) window.clearInterval(nowTimer)
    window.removeEventListener('mousemove', onDragMove)
    window.removeEventListener('mouseup', onDragUp)
})
</script>

<style scoped>
.board-card { overflow: hidden; }
/* The grid scrolls sideways on narrow screens; the page body never does. */
.board-scroll { overflow-x: auto; }
.board-inner { min-width: 820px; }

.board-head,
.board-row {
    display: flex;
    align-items: stretch;
}
.board-head {
    position: sticky;
    top: 0;
    z-index: 3;
    background: rgb(var(--v-theme-surface));
    border-bottom: 1px solid rgba(var(--v-theme-on-surface), 0.12);
}

.row-label {
    flex: 0 0 240px;
    width: 240px;
    padding: 6px 12px;
    position: sticky;
    left: 0;
    z-index: 2;
    background: rgb(var(--v-theme-surface));
    border-right: 1px solid rgba(var(--v-theme-on-surface), 0.12);
    overflow: hidden;
}
.head-label { display: flex; align-items: center; font-weight: 600; }

.row-track {
    position: relative;
    flex: 1 1 auto;
    min-height: 40px;
    cursor: crosshair;
}
.head-track { min-height: 30px; }

.row-action {
    flex: 0 0 44px;
    display: flex;
    align-items: center;
    justify-content: center;
    border-left: 1px solid rgba(var(--v-theme-on-surface), 0.12);
}

.board-row {
    border-bottom: 1px solid rgba(var(--v-theme-on-surface), 0.08);
}
.board-row:hover .row-track { background: rgba(var(--v-theme-primary), 0.04); }

/* A unit on the bench: hatched track, and the cursor says so before the click does. */
.row-blocked .row-track {
    cursor: not-allowed;
    background: repeating-linear-gradient(
        45deg,
        rgba(var(--v-theme-on-surface), 0.05),
        rgba(var(--v-theme-on-surface), 0.05) 6px,
        transparent 6px,
        transparent 12px);
}

.group-head {
    display: flex;
    align-items: center;
    padding: 6px 12px;
    background: rgba(var(--v-theme-on-surface), 0.04);
    border-bottom: 1px solid rgba(var(--v-theme-on-surface), 0.12);
    cursor: pointer;
    position: sticky;
    left: 0;
}

.hour-mark { position: absolute; top: 0; bottom: 0; }
.hour-label {
    position: absolute;
    top: 6px;
    left: 4px;
    font-size: 11px;
    white-space: nowrap;
    color: rgba(var(--v-theme-on-surface), 0.6);
}
.hour-line {
    position: absolute;
    top: 0;
    bottom: 0;
    width: 1px;
    background: rgba(var(--v-theme-on-surface), 0.08);
}

.bar {
    position: absolute;
    top: 6px;
    bottom: 6px;
    border-radius: 4px;
    padding: 0 6px;
    display: flex;
    align-items: center;
    cursor: pointer;
    overflow: hidden;
    min-width: 4px;
}
.bar-text {
    font-size: 11px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    color: #fff;
}
/* Squared-off end = the rental continues past this day. */
.bar-clip-l { border-top-left-radius: 0; border-bottom-left-radius: 0; border-left: 3px solid rgba(255, 255, 255, 0.7); }
.bar-clip-r { border-top-right-radius: 0; border-bottom-right-radius: 0; border-right: 3px solid rgba(255, 255, 255, 0.7); }

.bar-pending { background: #78909c; }
.bar-paid    { background: rgb(var(--v-theme-primary)); }
.bar-out     { background: #3949ab; }
.bar-partial { background: #f9a825; }
.bar-full    { background: #c62828; }

.ghost {
    position: absolute;
    top: 6px;
    bottom: 6px;
    border-radius: 4px;
    background: rgba(var(--v-theme-primary), 0.35);
    border: 1px dashed rgb(var(--v-theme-primary));
    display: flex;
    align-items: center;
    padding: 0 6px;
    pointer-events: none;
}
.ghost .bar-text { color: rgb(var(--v-theme-on-surface)); }

.now-line {
    position: absolute;
    top: 0;
    bottom: 0;
    width: 2px;
    background: #e53935;
    pointer-events: none;
    z-index: 1;
}

.legend { display: inline-flex; align-items: center; gap: 6px; }
.swatch {
    display: inline-block;
    width: 14px;
    height: 10px;
    border-radius: 2px;
}
.swatch-blocked {
    background: repeating-linear-gradient(
        45deg,
        rgba(var(--v-theme-on-surface), 0.35),
        rgba(var(--v-theme-on-surface), 0.35) 3px,
        transparent 3px,
        transparent 6px);
}

.rental-note {
    padding: 6px 0;
    border-top: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
}
.rental-note:first-of-type { border-top: none; }
</style>
