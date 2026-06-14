<template>
    <v-container>
        <div class="d-flex align-center mb-4 flex-wrap ga-2">
            <h1 class="text-h4">{{ branding.displayName }} Calendar</h1>
            <v-spacer></v-spacer>
            <v-btn v-if="canManageEvents" color="primary" prepend-icon="mdi-plus" @click="openCreateEvent">
                Add Event
            </v-btn>
            <v-btn v-if="canManageEvents" variant="tonal" color="error"
                prepend-icon="mdi-calendar-remove" @click="openCreateBlackout">
                Add Blackout
            </v-btn>
            <v-btn v-if="branding.allowEventSubscriptions" variant="tonal" prepend-icon="mdi-bell-plus" @click="openSubscribe">
                {{ subscribed ? 'Notification settings' : 'Notify me of new events' }}
            </v-btn>
        </div>

        <div class="d-flex align-center mb-4 flex-wrap ga-2">
            <v-btn icon="mdi-chevron-left" variant="text" @click="prevMonth"></v-btn>
            <h2 class="text-h5 mx-2">{{ monthLabel }}</h2>
            <v-btn icon="mdi-chevron-right" variant="text" @click="nextMonth"></v-btn>
            <v-btn variant="tonal" size="small" class="ml-3" @click="goToday">Today</v-btn>
            <v-switch v-if="hasHoursConfigured" v-model="showHours" label="Show hours"
                density="compact" color="primary" hide-details
                class="ml-3 flex-grow-0"></v-switch>
            <v-spacer></v-spacer>
            <v-progress-circular v-if="loading" indeterminate size="20" width="2"></v-progress-circular>
        </div>

        <v-card>
            <div class="calendar-grid">
                <div v-for="d in weekdayLabels" :key="d" class="weekday-header">{{ d }}</div>
                <div v-for="(day, i) in days" :key="i" class="day-cell"
                    :class="{ 'other-month': !day.inMonth, 'is-today': day.isToday, 'has-blackout': !!day.blackout }"
                    @click="selectDay(day)">
                    <div class="day-top-row">
                        <span class="day-num">{{ day.dayNumber }}</span>
                        <span v-if="showHours && day.hoursLabel" class="day-hours"
                            :class="{ 'is-closed': day.hoursLabel === 'Closed' }">
                            {{ day.hoursLabel }}
                        </span>
                    </div>
                    <div v-if="day.blackout" class="blackout-chip">Closed</div>
                    <div v-for="ev in day.events.slice(0, 3)" :key="ev.id" class="event-chip"
                        :style="{ background: ev.eventTypeColor || '#1976D2' }" :title="ev.title">
                        {{ ev.title }}
                    </div>
                    <div v-if="day.events.length > 3" class="text-caption text-medium-emphasis">
                        +{{ day.events.length - 3 }} more
                    </div>
                </div>
            </div>
        </v-card>

        <p class="text-caption text-medium-emphasis mt-3">
            Times shown in {{ tz }}.
            <span v-if="branding.requireReservationForPasses">
                Some events require advance reservation — click a day to see details.
            </span>
        </p>

        <v-dialog v-model="detailOpen" max-width="640">
            <v-card v-if="selectedDay">
                <v-card-title class="d-flex align-center">
                    <span>{{ formatLong(selectedDay.date) }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="detailOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-alert v-if="selectedDay.blackout" type="error" variant="tonal" density="compact" class="mb-3">
                        Track closed{{ selectedDay.blackout.reason ? ' — ' + selectedDay.blackout.reason : '' }}
                    </v-alert>

                    <div v-for="ev in selectedDay.events" :key="ev.id" class="mb-4 event-block"
                        :class="{ 'is-selected': canManageEvents && selectedEventId === ev.id }"
                        @click="canManageEvents && (selectedEventId = ev.id)">
                        <!-- Event image banner. Per-event image wins; falls back to the
                             event-type default image. Hidden when neither is set so flat
                             colored events still read clean without an empty box. -->
                        <v-img v-if="eventCoverImage(ev)" :src="absoluteUrl(eventCoverImage(ev)!)"
                            height="160" cover class="rounded mb-3"></v-img>

                        <div class="d-flex align-center mb-1">
                            <span class="event-dot" :style="{ background: ev.eventTypeColor || '#1976D2' }"></span>
                            <strong class="ml-2 text-body-1">{{ ev.title }}</strong>
                            <v-icon v-if="canManageEvents && selectedDay.events.length > 1 && selectedEventId === ev.id"
                                size="small" color="primary" class="ml-2">mdi-check-circle</v-icon>
                        </div>
                        <div class="text-caption text-medium-emphasis">
                            <v-icon size="x-small" class="mr-1">mdi-clock-outline</v-icon>
                            <span v-if="ev.allDay">All day</span>
                            <span v-else>{{ formatTime(ev.startsAtUtc) }} – {{ formatTime(ev.endsAtUtc) }}</span>
                            <span v-if="ev.eventTypeName"> · {{ ev.eventTypeName }}</span>
                        </div>
                        <div v-if="ev.locationLabel" class="text-caption text-medium-emphasis">
                            <v-icon size="x-small" class="mr-1">mdi-map-marker-outline</v-icon>
                            {{ ev.locationLabel }}
                        </div>
                        <div v-if="ev.description" class="text-body-2 mt-2" style="white-space: pre-wrap">{{ ev.description }}</div>
                        <div v-if="hasEnded(ev)" class="text-caption text-medium-emphasis mt-2">
                            This event has already ended.
                        </div>
                        <div v-else class="d-flex ga-2 mt-2 flex-wrap">
                            <v-btn v-if="ev.hasRaceEntryTiers" :to="`/BuyTicket/${ev.id}?kind=race_entry`"
                                size="small" color="deep-orange" @click="detailOpen = false">
                                Buy Race Entry
                            </v-btn>
                            <v-btn v-if="ev.hasRaceEntryTiers" :to="`/BuySpectator/${ev.id}`"
                                size="small" color="primary" @click="detailOpen = false">
                                Buy Spectator Pass
                            </v-btn>
                            <!-- Day-pass reserve only makes sense for ride-day events without
                                 ticket tiers AND with at least one eligible pass product
                                 configured for this event. Ticketed events (race entries /
                                 spectator passes) are sold via the buttons above. -->
                            <v-btn class="bg-primary" v-if="!ev.hasActiveTiers && ev.capacity && (ev.capacity - (ev.spotsReserved ?? 0)) > 0
                                    && (ev.eligiblePasses?.length ?? 0) > 0"
                                :to="`/BuyPass?eventId=${ev.id}`" size="small" variant="tonal"
                                @click="detailOpen = false">
                                Reserve a pass
                            </v-btn>
                            <v-btn v-if="!ev.hasActiveTiers && passUsableFor(ev)" size="small" variant="tonal" color="success"
                                :loading="reservingId === ev.id" @click="reserveWithPass(ev)">
                                Reserve with my season pass
                            </v-btn>
                        </div>
                    </div>

                    <p v-if="selectedDay.events.length === 0 && !selectedDay.blackout" class="text-medium-emphasis">
                        Nothing scheduled.
                    </p>
                </v-card-text>
                <v-card-actions>
                    <template v-if="canManageEvents && selectedEvent">
                        <v-btn variant="text" prepend-icon="mdi-pencil" @click="openEditEvent(selectedEvent)">
                            Edit
                        </v-btn>
                        <v-btn variant="text" prepend-icon="mdi-content-copy" @click="openDuplicateEvent(selectedEvent)">
                            Duplicate
                        </v-btn>
                        <v-btn variant="text" prepend-icon="mdi-account-group"
                            :to="{ path: '/Admin/Reports', query: { report: 'event-riders', eventId: selectedEvent.id } }"
                            @click="detailOpen = false">
                            Rider Report
                        </v-btn>
                    </template>
                    <v-spacer></v-spacer>
                    <v-btn v-if="canManageEvents && selectedDay.blackout"
                        variant="text" prepend-icon="mdi-pencil"
                        @click="openEditBlackout(selectedDay.blackout)">
                        Edit Blackout
                    </v-btn>
                    <v-btn @click="detailOpen = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="subscribeOpen" max-width="500" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Notify me of new events</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="subscribeOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 text-medium-emphasis mb-3">
                        Get a heads-up the moment {{ branding.displayName }} adds something to the calendar.
                    </p>
                    <v-text-field v-model="subForm.email" type="email" label="Email" density="compact" required></v-text-field>
                    <v-checkbox v-model="subForm.notifyEmail" label="Email me" hide-details density="compact"></v-checkbox>
                    <v-checkbox v-model="subForm.notifySms" label="Text me" hide-details density="compact"></v-checkbox>
                    <PhoneField v-if="subForm.notifySms" v-model="subForm.phone"
                        label="Mobile number" density="compact" class="mt-2"
                        hint="Standard message rates apply. Reply STOP to opt out at any time." persistent-hint />
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="subscribeOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="subSaving" :disabled="!canSubmitSubscribe" @click="submitSubscribe">
                        {{ subscribed ? 'Save' : 'Subscribe' }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <EventDialog v-if="canManageEvents" v-model:open="eventDialogOpen"
            :event="eventToEdit" :duplicate-from="eventDuplicateFrom"
            @saved="onEventSaved" @deleted="onEventDeleted" @flash="flash" />

        <!-- Quick add / edit blackout. Defaults to a single all-day blackout starting
             today; the From / To pair lets the admin span multiple days. The full
             Admin → Blackouts page handles partial-day blackouts. -->
        <v-dialog v-if="canManageEvents" v-model="blackoutDialog" max-width="520" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ blackoutEditing ? 'Edit Blackout' : 'Add Blackout' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="blackoutDialog = false"></v-btn>
                </v-card-title>
                <v-card-subtitle>Mark a date (or range) as Closed on the calendar.</v-card-subtitle>
                <v-card-text>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="blackoutForm.startDate" type="date"
                                label="From" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="blackoutForm.endDate" type="date"
                                label="To (inclusive)" density="compact"
                                hint="Same as From for a single-day blackout."
                                persistent-hint></v-text-field>
                        </v-col>
                    </v-row>
                    <v-textarea v-model="blackoutForm.reason" label="Reason (optional)"
                        rows="2" density="compact" class="mt-6"
                        placeholder="Track maintenance, holiday, weather…"></v-textarea>
                </v-card-text>
                <v-card-actions>
                    <v-btn v-if="blackoutEditing" variant="text" color="error"
                        :loading="blackoutDeleting" @click="deleteBlackout">
                        Delete
                    </v-btn>
                    <v-spacer></v-spacer>
                    <v-btn @click="blackoutDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="blackoutSaving" :disabled="!canSaveBlackout"
                        @click="saveBlackout">
                        {{ blackoutEditing ? 'Save' : 'Add' }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="extrasDialog" max-width="640" persistent>
            <v-card v-if="extrasEvent">
                <v-card-title class="d-flex align-center">
                    <span>Add-ons for {{ extrasEvent.title }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="extrasDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-caption text-medium-emphasis mb-2">{{ formatLong(dayjs.utc(extrasEvent.startsAtUtc).tz(tz)) }}</p>
                    <ExtrasPicker :extras="eligibleExtrasForDialog" v-model="extraSelections" />
                    <v-divider class="my-3"></v-divider>
                    <div class="d-flex">
                        <strong>Total</strong>
                        <v-spacer></v-spacer>
                        <strong>${{ (extrasTotalCents / 100).toFixed(2) }}</strong>
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="extrasDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="extrasCreating" :disabled="extrasUnits === 0" @click="buyExtras">
                        Continue to Payment
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="extrasPayOpen" persistent max-width="500">
            <v-card v-if="extrasPayInFlight">
                <v-card-title class="d-flex align-center">
                    <span>Pay for add-ons</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="extrasPayOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-3">${{ (extrasPayInFlight.amountCents / 100).toFixed(2) }} for {{ extrasPayInFlight.label }}</p>
                    <div :id="extrasPaymentElementId" class="mb-4"></div>
                    <v-btn color="primary" :loading="extrasPaying" :disabled="!extrasStripeReady" @click="payExtras">
                        Pay ${{ (extrasPayInFlight.amountCents / 100).toFixed(2) }}
                    </v-btn>
                    <div v-if="extrasPaymentError" class="text-error mt-3">{{ extrasPaymentError }}</div>
                </v-card-text>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'

// Image storage returns paths relative to the API host (/uploads/<tenant>/event-<id>.png).
// On the Vite dev server those need an explicit origin prefix; same helper every other
// admin page uses.
const apiUrl: string = import.meta.env.VITE_API_ENDPOINT ?? ''
function apiOrigin(): string {
    try { return new URL(apiUrl, window.location.origin).origin } catch { return '' }
}
function absoluteUrl(url: string | null | undefined): string {
    if (!url) return ''
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}
import { EventService, type EventDto } from '@/services/EventService'
import { BlackoutService, type BlackoutDto } from '@/services/BlackoutService'
import { EventSubscriptionService } from '@/services/EventSubscriptionService'
import { SeasonPassService, type MySeasonPass } from '@/services/SeasonPassService'
import { branding } from '@/stores/branding'
import authHelper from '@/helpers/AuthHelper'
import { Perm } from '@/helpers/TenantPermissions'
import EventDialog from '@/components/EventDialog.vue'
import ExtrasPicker, { type ExtraSelection } from '@/components/ExtrasPicker.vue'
import PhoneField from '@/components/PhoneField.vue'
import { ExtraService } from '@/services/ExtraService'
import { getStripe } from '@/helpers/StripeHelper'

interface DayCell {
    date: dayjs.Dayjs
    dayNumber: number
    inMonth: boolean
    isToday: boolean
    events: EventDto[]
    blackout: BlackoutDto | null
    // Open / close strings for that weekday (e.g. "9 AM – 5 PM"), or "Closed",
    // or empty when the tenant hasn't configured hours. Driven by branding.hoursJson.
    hoursLabel: string
}

type WeekdayHours = { closed: boolean; open: string; close: string }
const weekdayKeys = ['sun', 'mon', 'tue', 'wed', 'thu', 'fri', 'sat'] as const

function formatTime12(hhmm: string | undefined): string {
    if (!hhmm) return ''
    const [h, m] = hhmm.split(':').map(Number)
    if (Number.isNaN(h) || Number.isNaN(m)) return hhmm
    const period = h >= 12 ? 'PM' : 'AM'
    const h12 = h % 12 === 0 ? 12 : h % 12
    // Drop ":00" for cleaner display in tight calendar cells.
    return m === 0
        ? `${h12} ${period}`
        : `${h12}:${m.toString().padStart(2, '0')} ${period}`
}

const eventService = new EventService()
const blackoutService = new BlackoutService()
const subscriptionService = new EventSubscriptionService()
const seasonPassService = new SeasonPassService()

const tz = computed(() => branding.timezone || 'UTC')
const canManageEvents = computed(() => authHelper.hasPermission(Perm.CatalogManage))
const weekdayLabels = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

const cursor = ref(dayjs().tz(tz.value).startOf('month'))
const events = ref<EventDto[]>([])
const blackouts = ref<BlackoutDto[]>([])
const loading = ref(false)

const detailOpen = ref(false)
const selectedDay = ref<DayCell | null>(null)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const myPasses = ref<MySeasonPass[]>([])
const reservingId = ref<string | null>(null)

const eventDialogOpen = ref(false)
const eventToEdit = ref<EventDto | null>(null)
const eventDuplicateFrom = ref<EventDto | null>(null)
const selectedEventId = ref<string | null>(null)

const selectedEvent = computed<EventDto | null>(() => {
    if (!selectedDay.value) return null
    if (selectedDay.value.events.length === 0) return null
    if (selectedDay.value.events.length === 1) return selectedDay.value.events[0]
    return selectedDay.value.events.find(e => e.id === selectedEventId.value) ?? selectedDay.value.events[0]
})

// Reset the highlighted event when the dialog opens for a different day.
watch(detailOpen, (open) => {
    if (open && selectedDay.value && selectedDay.value.events.length > 0) {
        selectedEventId.value = selectedDay.value.events[0].id
    } else if (!open) {
        selectedEventId.value = null
    }
})

function openCreateEvent() {
    eventToEdit.value = null
    eventDuplicateFrom.value = null
    eventDialogOpen.value = true
}

// ── Quick add / edit blackout ───────────────────────────────────────────────
const blackoutDialog = ref(false)
const blackoutSaving = ref(false)
const blackoutDeleting = ref(false)
// Null = creating a new blackout, BlackoutDto = editing the one currently shown.
const blackoutEditing = ref<BlackoutDto | null>(null)
const blackoutForm = ref({
    startDate: '',
    endDate: '',
    reason: '' as string | null,
})
const canSaveBlackout = computed(() => {
    const s = blackoutForm.value.startDate
    const e = blackoutForm.value.endDate
    return !!s && !!e && e >= s
})

function openCreateBlackout() {
    blackoutEditing.value = null
    const today = dayjs().tz(tz.value).format('YYYY-MM-DD')
    blackoutForm.value = { startDate: today, endDate: today, reason: '' }
    blackoutDialog.value = true
}

function openEditBlackout(b: BlackoutDto) {
    blackoutEditing.value = b
    const tzn = tz.value
    const startsTz = dayjs.utc(b.startsAtUtc).tz(tzn)
    const endsExclusiveTz = dayjs.utc(b.endsAtUtc).tz(tzn)
    // For all-day blackouts the end is the *exclusive* midnight after the last
    // covered date — subtract a day to recover the inclusive end the form expects.
    // Partial-day blackouts (created via Admin → Blackouts) get clamped to the
    // start/end calendar dates here; switch to Admin → Blackouts to edit hours.
    const endDateInclusive = b.allDay
        ? endsExclusiveTz.subtract(1, 'day').format('YYYY-MM-DD')
        : endsExclusiveTz.format('YYYY-MM-DD')
    blackoutForm.value = {
        startDate: startsTz.format('YYYY-MM-DD'),
        endDate: endDateInclusive,
        reason: b.reason ?? '',
    }
    detailOpen.value = false
    // Defer past Vuetify's overlay/scroll-lock handoff so the new dialog paints.
    setTimeout(() => { blackoutDialog.value = true }, 200)
}

async function saveBlackout() {
    if (!canSaveBlackout.value || blackoutSaving.value) return
    blackoutSaving.value = true
    try {
        const tzn = tz.value
        // All-day blackout: snap to local midnight start; end is the *exclusive*
        // midnight of the day after the last covered date so the row cleanly
        // covers full calendar days. Same convention as Admin → Blackouts.
        const startsAtUtc = dayjs.tz(blackoutForm.value.startDate + 'T00:00', tzn).utc().toISOString()
        const endsAtUtc = dayjs.tz(blackoutForm.value.endDate + 'T00:00', tzn).add(1, 'day').utc().toISOString()
        const body = {
            startsAtUtc,
            endsAtUtc,
            allDay: true,
            reason: blackoutForm.value.reason?.trim() || null,
        }
        if (blackoutEditing.value) {
            await blackoutService.update(blackoutEditing.value.id, body)
        } else {
            await blackoutService.create(body)
        }
        blackoutDialog.value = false
        await load()
        flash(blackoutEditing.value ? 'Blackout updated.' : 'Blackout added.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to save blackout.', 'error')
    } finally {
        blackoutSaving.value = false
    }
}

async function deleteBlackout() {
    if (!blackoutEditing.value || blackoutDeleting.value) return
    if (!confirm('Delete this blackout? The day will reopen on the calendar.')) return
    blackoutDeleting.value = true
    try {
        await blackoutService.delete(blackoutEditing.value.id)
        blackoutDialog.value = false
        await load()
        flash('Blackout deleted.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to delete blackout.', 'error')
    } finally {
        blackoutDeleting.value = false
    }
}

function openEditEvent(ev: EventDto) {
    eventToEdit.value = ev
    eventDuplicateFrom.value = null
    detailOpen.value = false
    eventDialogOpen.value = true
}

function openDuplicateEvent(ev: EventDto) {
    eventToEdit.value = null
    eventDuplicateFrom.value = ev
    detailOpen.value = false
    eventDialogOpen.value = true
}

async function onEventSaved(_ev: EventDto) { await load() }
async function onEventDeleted(_id: string) {
    await load()
    detailOpen.value = false
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

// ── Add-ons (extras) standalone purchase from the event modal ───────────────
const extraService = new ExtraService()
const extrasDialog = ref(false)
const extrasEvent = ref<EventDto | null>(null)
// One row per (productId, variantId|null) — matches BuyPass / BuyAdmissionFlow.
// Keeping the shape consistent across all three flows lets the same ExtrasPicker
// component drive every add-on purchase UI.
const extraSelections = ref<ExtraSelection[]>([])

const eligibleExtrasForDialog = computed(() => extrasEvent.value?.eligibleExtras ?? [])

const extrasPayOpen = ref(false)
const extrasCreating = ref(false)
const extrasPaying = ref(false)
const extrasStripeReady = ref(false)
const extrasPaymentError = ref<string | null>(null)
const extrasPaymentElementId = `extras-pay-${Math.random().toString(36).slice(2, 10)}`
const extrasPayInFlight = ref<{ amountCents: number; label: string; clientSecret: string } | null>(null)
let extrasStripe: any = null
let extrasElements: any = null

// Per-selection effective price = variant override OR product price.
function selectionPrice(s: ExtraSelection): number {
    const product = (extrasEvent.value?.eligibleExtras ?? []).find(e => e.productId === s.productId)
    if (!product) return 0
    if (s.variantId) {
        const v = product.variants.find(x => x.id === s.variantId)
        if (v) return v.priceCents
    }
    return product.priceCents
}

const extrasUnits = computed(() =>
    extraSelections.value.reduce((sum, s) => sum + s.quantity, 0))
const extrasTotalCents = computed(() =>
    extraSelections.value.reduce((sum, s) => sum + s.quantity * selectionPrice(s), 0))

function openExtras(ev: EventDto) {
    extrasEvent.value = ev
    extraSelections.value = []
    detailOpen.value = false
    // Vuetify chokes on opening one dialog the same tick another closes —
    // overlay/scroll-lock handoff cancels the new one. Defer past the close
    // transition so the new dialog actually paints.
    setTimeout(() => { extrasDialog.value = true }, 200)
}

async function buyExtras() {
    if (!extrasEvent.value || extrasUnits.value === 0) return
    extrasCreating.value = true
    try {
        const items = extraSelections.value
            .filter(s => s.quantity > 0)
            .map(s => ({ productId: s.productId, quantity: s.quantity, variantId: s.variantId ?? null }))
        const r = await extraService.buy({ eventId: extrasEvent.value.id, items })
        const data = (r.data as any).data
        const labelParts = extraSelections.value
            .filter(s => s.quantity > 0)
            .map(s => {
                const product = (extrasEvent.value!.eligibleExtras ?? []).find(e => e.productId === s.productId)
                if (!product) return ''
                let label = product.name
                if (s.variantId) {
                    const v = product.variants.find(x => x.id === s.variantId)
                    if (v) {
                        const attrs = [v.size, v.color, v.gender].filter(x => !!x).join(' / ')
                        if (attrs) label = `${product.name} (${attrs})`
                    }
                }
                return `${s.quantity}× ${label}`
            })
            .filter(s => !!s)
        extrasPayInFlight.value = {
            amountCents: data.amountCents,
            label: labelParts.join(', '),
            clientSecret: data.clientSecret,
        }
        extrasDialog.value = false
        extrasPayOpen.value = true
        await nextTick()
        await mountExtrasStripe()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not start payment.', 'error')
    } finally {
        extrasCreating.value = false
    }
}

async function mountExtrasStripe() {
    if (!extrasPayInFlight.value) return
    extrasStripe = await getStripe(branding.stripePublishableKey)
    if (!extrasStripe) { extrasPaymentError.value = 'Stripe not available.'; return }
    extrasElements = extrasStripe.elements({ clientSecret: extrasPayInFlight.value.clientSecret })
    const pe = extrasElements.create('payment')
    pe.mount(`#${extrasPaymentElementId}`)
    extrasStripeReady.value = true
}

async function payExtras() {
    if (!extrasStripe || !extrasElements) return
    extrasPaying.value = true
    extrasPaymentError.value = null
    try {
        const { error } = await extrasStripe.confirmPayment({
            elements: extrasElements,
            confirmParams: { return_url: window.location.origin + '/User/MyPasses' },
            redirect: 'if_required',
        })
        if (error) extrasPaymentError.value = error.message || 'Payment failed.'
        else {
            extrasPayOpen.value = false
            flash('Add-ons purchased!', 'success')
        }
    } catch (err: any) {
        extrasPaymentError.value = err?.message || 'Payment failed.'
    } finally {
        extrasPaying.value = false
    }
}

const subscribeOpen = ref(false)
const subSaving = ref(false)
const subscribed = ref(false)
const subForm = ref({ email: '', phone: '', notifyEmail: true, notifySms: false })
const canSubmitSubscribe = computed(() => {
    if (!/\S+@\S+\.\S+/.test(subForm.value.email)) return false
    if (!subForm.value.notifyEmail && !subForm.value.notifySms) return false
    if (subForm.value.notifySms && subForm.value.phone.replace(/\D/g, '').length < 10) return false
    return true
})

async function openSubscribe() {
    if (authHelper.isAuthenticated()) {
        try {
            const r = await subscriptionService.mine()
            const data = (r.data as any).data
            subForm.value.email = data.email ?? ''
            subForm.value.phone = data.phone ?? ''
            subForm.value.notifyEmail = data.notifyEmail
            subForm.value.notifySms = data.notifySms
            subscribed.value = data.subscribed
        } catch { /* not subscribed yet */ }
    }
    subscribeOpen.value = true
}

async function submitSubscribe() {
    subSaving.value = true
    try {
        await subscriptionService.subscribe({
            email: subForm.value.email.trim(),
            phone: subForm.value.notifySms ? subForm.value.phone.trim() : null,
            notifyEmail: subForm.value.notifyEmail,
            notifySms: subForm.value.notifySms,
        })
        subscribed.value = true
        subscribeOpen.value = false
        snackbarText.value = 'Subscribed! You\'ll hear from us when new events drop.'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Could not subscribe — please try again.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        subSaving.value = false
    }
}

const monthLabel = computed(() => cursor.value.format('MMMM YYYY'))

// Hours-of-operation map indexed by weekday key. Returns null when the tenant
// hasn't configured hours, in which case calendar cells skip the hours line
// entirely (better than showing default 9–5 for everyone).
const weeklyHours = computed<Record<string, WeekdayHours> | null>(() => {
    if (!branding.hoursJson) return null
    try {
        const parsed = JSON.parse(branding.hoursJson) as Record<string, WeekdayHours>
        return Object.keys(parsed).length > 0 ? parsed : null
    } catch {
        return null
    }
})

function hoursLabelFor(date: dayjs.Dayjs): string {
    const map = weeklyHours.value
    if (!map) return ''
    const key = weekdayKeys[date.day()]
    const h = map[key]
    if (!h) return ''
    if (h.closed) return 'Closed'
    if (!h.open || !h.close) return ''
    return `${formatTime12(h.open)} – ${formatTime12(h.close)}`
}

// Show-hours toggle. Defaults on when the tenant has configured hours; the
// toggle itself is hidden entirely when there's nothing to show. Preference
// persists per-browser so users don't have to flip it every visit.
const SHOW_HOURS_KEY = 'ridepass.calendar.showHours'
const hasHoursConfigured = computed(() => weeklyHours.value !== null)
const showHours = ref<boolean>((() => {
    const stored = localStorage.getItem(SHOW_HOURS_KEY)
    return stored === null ? true : stored === '1'
})())
watch(showHours, v => { localStorage.setItem(SHOW_HOURS_KEY, v ? '1' : '0') })

const days = computed<DayCell[]>(() => {
    const monthStart = cursor.value.startOf('month')
    const monthEnd = cursor.value.endOf('month')
    const gridStart = monthStart.startOf('week')   // Sunday
    const gridEnd = monthEnd.endOf('week')         // Saturday
    const today = dayjs().tz(tz.value).startOf('day')
    const cells: DayCell[] = []
    let cursorDay = gridStart
    while (cursorDay.isBefore(gridEnd) || cursorDay.isSame(gridEnd, 'day')) {
        const dayStartUtc = cursorDay.utc()
        const dayEndUtc = cursorDay.add(1, 'day').utc()
        cells.push({
            date: cursorDay,
            dayNumber: cursorDay.date(),
            inMonth: cursorDay.month() === monthStart.month(),
            isToday: cursorDay.isSame(today, 'day'),
            hoursLabel: hoursLabelFor(cursorDay),
            events: events.value.filter(e =>
                dayjs.utc(e.startsAtUtc).isBefore(dayEndUtc)
                && dayjs.utc(e.endsAtUtc).isAfter(dayStartUtc)
                && e.status !== 'cancelled'),
            blackout: blackouts.value.find(b => {
                const startUtc = dayjs.utc(b.startsAtUtc)
                const endUtc = dayjs.utc(b.endsAtUtc)
                // Legacy zero-duration "all day" rows (start == end) fall between cell
                // boundaries with strict overlap. Treat them as covering the day they
                // land on in tenant tz.
                if (startUtc.valueOf() === endUtc.valueOf()) {
                    return startUtc.tz(tz.value).isSame(cursorDay, 'day')
                }
                return startUtc.isBefore(dayEndUtc) && endUtc.isAfter(dayStartUtc)
            }) ?? null,
        })
        cursorDay = cursorDay.add(1, 'day')
    }
    return cells
})

async function load() {
    loading.value = true
    try {
        const gridStart = cursor.value.startOf('month').startOf('week')
        const gridEnd = cursor.value.endOf('month').endOf('week').add(1, 'day')
        const fromUtc = gridStart.utc().toISOString()
        const toUtc = gridEnd.utc().toISOString()
        const [e, b] = await Promise.all([
            eventService.list(fromUtc, toUtc),
            blackoutService.list(fromUtc, toUtc).catch(() => ({ data: { data: [] } })),
        ])
        events.value = ((e.data as any).data as EventDto[])
        blackouts.value = ((b.data as any).data as BlackoutDto[])
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Failed to load calendar.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        loading.value = false
    }
}

function prevMonth() { cursor.value = cursor.value.subtract(1, 'month') }
function nextMonth() { cursor.value = cursor.value.add(1, 'month') }
function goToday() { cursor.value = dayjs().tz(tz.value).startOf('month') }

function selectDay(day: DayCell) {
    selectedDay.value = day
    detailOpen.value = true
}

function formatLong(d: dayjs.Dayjs): string {
    return d.format('dddd, MMMM D, YYYY')
}
// Event-image fallback chain: per-event image → event-type default → none.
function eventCoverImage(ev: EventDto): string | null {
    return ev.imageUrl ?? ev.eventTypeImageUrl ?? null
}

function hasEnded(ev: EventDto): boolean {
    return dayjs.utc(ev.endsAtUtc).isBefore(dayjs.utc())
}

function findUsablePass(ev: EventDto): MySeasonPass | null {
    const startLocal = dayjs.utc(ev.startsAtUtc).tz(tz.value)
    const dow = startLocal.day()
    for (const p of myPasses.value) {
        if (p.status !== 'paid') continue
        if (startLocal.isBefore(dayjs(p.validFromDate)) || startLocal.isAfter(dayjs(p.validToDate).endOf('day'))) continue
        if (p.productKind === 'days_of_week' && p.validDaysOfWeek && !p.validDaysOfWeek.includes(dow)) continue
        if (p.productKind === 'credits' && (p.creditsRemaining ?? 0) <= 0) continue
        return p
    }
    return null
}
function passUsableFor(ev: EventDto): boolean { return !!findUsablePass(ev) }

async function reserveWithPass(ev: EventDto) {
    const pass = findUsablePass(ev)
    if (!pass) return
    reservingId.value = ev.id
    try {
        await seasonPassService.reserve(pass.id, ev.id)
        snackbarText.value = 'Reserved! Show your pass at the gate on event day.'
        snackbarColor.value = 'success'
        snackbar.value = true
        // Refresh credits
        const r = await seasonPassService.listMine()
        myPasses.value = (r.data as any).data
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Could not reserve.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        reservingId.value = null
    }
}
function formatTime(utc: string): string {
    return dayjs.utc(utc).tz(tz.value).format('h:mm A')
}

watch(cursor, load)

// `?eventId=<id>` — Home's "View on calendar" link drops the rider here with this
// query param. We pull a wide event window once to find the target, jump the
// cursor to that event's month (which triggers `load`), then open its day cell.
const route = useRoute()

onMounted(async () => {
    const focusEventId = (route.query.eventId as string | undefined) || null
    let targetEvent: EventDto | null = null

    if (focusEventId) {
        try {
            const tzn = tz.value
            const wide = await eventService.list(
                dayjs().tz(tzn).startOf('day').subtract(30, 'day').utc().toISOString(),
                dayjs().tz(tzn).startOf('day').add(365, 'day').utc().toISOString(),
            )
            const all = (wide.data as any).data as EventDto[]
            targetEvent = all.find(e => e.id === focusEventId) ?? null
            if (targetEvent) {
                cursor.value = dayjs.utc(targetEvent.startsAtUtc).tz(tzn).startOf('month')
            }
        } catch { /* fall through to default month */ }
    }

    await load()

    if (targetEvent) {
        await nextTick()
        const eventDay = days.value.find(d => d.events.some(e => e.id === targetEvent!.id))
        if (eventDay) {
            selectDay(eventDay)
            // The detailOpen watcher resets selectedEventId to the day's first event;
            // override on the next tick so multi-event days highlight the right one.
            await nextTick()
            selectedEventId.value = targetEvent.id
        }
    }

    if (authHelper.isAuthenticated()) {
        try {
            const r = await seasonPassService.listMine()
            myPasses.value = (r.data as any).data
        } catch { /* not critical */ }
    }
})
</script>

<style scoped>
.calendar-grid {
    display: grid;
    grid-template-columns: repeat(7, 1fr);
    gap: 1px;
    background: rgba(0, 0, 0, 0.08);
}
.weekday-header {
    background: rgb(var(--v-theme-surface));
    padding: 8px;
    text-align: center;
    font-weight: 600;
    font-size: 0.85rem;
    color: rgba(var(--v-theme-on-surface), 0.7);
}
.day-cell {
    background: rgb(var(--v-theme-surface));
    min-height: 110px;
    padding: 6px;
    cursor: pointer;
    transition: background-color 0.1s;
    overflow: hidden;
}
.day-cell:hover {
    background: rgba(var(--v-theme-primary), 0.05);
}
.day-cell.other-month {
    background: rgba(var(--v-theme-on-surface), 0.03);
}
.day-cell.other-month .day-num {
    color: rgba(var(--v-theme-on-surface), 0.35);
}
.day-cell.is-today .day-num {
    background: rgb(var(--v-theme-primary));
    color: rgb(var(--v-theme-on-primary));
    border-radius: 50%;
    width: 24px;
    height: 24px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
}
.day-cell.has-blackout {
    background: rgba(244, 67, 54, 0.05);
}
.day-num {
    font-size: 0.85rem;
    font-weight: 500;
}
.day-top-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 4px;
    margin-bottom: 4px;
}
.day-hours {
    font-size: 0.65rem;
    color: rgba(var(--v-theme-on-surface), 0.55);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    max-width: 100%;
}
.day-hours.is-closed {
    color: #c62828;
    font-weight: 600;
}
.day-cell.other-month .day-hours {
    color: rgba(var(--v-theme-on-surface), 0.25);
}
.blackout-chip {
    background: rgba(244, 67, 54, 0.15);
    color: #c62828;
    font-size: 0.7rem;
    padding: 2px 6px;
    border-radius: 3px;
    margin-bottom: 2px;
    font-weight: 600;
}
.event-chip {
    color: white;
    font-size: 0.7rem;
    padding: 2px 6px;
    border-radius: 3px;
    margin-bottom: 2px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}
.event-dot {
    display: inline-block;
    width: 10px;
    height: 10px;
    border-radius: 50%;
}
.event-block {
    border-radius: 6px;
    padding: 6px 8px;
    margin-left: -8px;
    margin-right: -8px;
}
.event-block.is-selected {
    background: rgba(var(--v-theme-primary), 0.08);
    box-shadow: inset 3px 0 0 0 rgb(var(--v-theme-primary));
}
@media (max-width: 600px) {
    .day-cell { min-height: 70px; padding: 3px; font-size: 0.7rem; }
    .event-chip, .blackout-chip { font-size: 0.6rem; padding: 1px 3px; }
}
</style>
