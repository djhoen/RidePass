<template>
    <v-container style="max-width: 960px">
        <h1 class="text-h4 mb-4">Check-In</h1>

        <!-- Event roster: pick an event, see who's been checked in. The table reuses
             the Event Riders report endpoint so the data is identical to the
             Reporting → Event Riders pane. -->
        <v-card class="mb-4 pa-4">
            <div class="d-flex align-center flex-wrap ga-3">
                <v-autocomplete v-model="selectedEventId" :items="eventOptions"
                    item-title="title" item-value="value"
                    label="Event" density="compact" hide-details
                    style="min-width: 320px; flex: 1 1 320px"
                    :loading="loadingEvents"
                    @update:model-value="loadRoster"></v-autocomplete>
                <v-btn variant="text" :loading="loadingRoster" :disabled="!selectedEventId" @click="loadRoster">
                    Refresh
                </v-btn>
            </div>

            <div v-if="roster" class="d-flex align-center mt-4 flex-wrap ga-4">
                <div>
                    <div class="text-overline text-medium-emphasis">Checked in</div>
                    <div class="text-h4">{{ roster.totalCheckedIn }} / {{ roster.totalRegistrants }}</div>
                </div>
                <div class="text-medium-emphasis">{{ roster.eventTitle }}</div>
            </div>

            <v-table v-if="roster && roster.rows.length > 0" density="compact" class="mt-3">
                <thead>
                    <tr>
                        <th style="width: 50px">In</th>
                        <th>Name</th>
                        <th>Item</th>
                        <th style="width: 90px">Source</th>
                        <th style="width: 110px">Status</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="r in roster.rows" :key="r.purchaseId">
                        <td>
                            <v-icon v-if="r.checkedIn" color="success" size="small">mdi-check-circle</v-icon>
                            <v-icon v-else color="grey" size="small">mdi-circle-outline</v-icon>
                        </td>
                        <td>{{ r.purchaserName }}</td>
                        <td>{{ r.itemName }}</td>
                        <td><v-chip size="x-small" :color="sourceColor(r.source)">{{ sourceLabel(r.source) }}</v-chip></td>
                        <td><v-chip size="x-small" :color="statusColor(r.status)">{{ r.status }}</v-chip></td>
                    </tr>
                </tbody>
            </v-table>
            <div v-else-if="roster && roster.rows.length === 0" class="text-medium-emphasis mt-3">
                Nobody registered for this event yet.
            </div>
        </v-card>

        <!-- Scan card. Camera surface lives here; a manual paste field handles the
             keyboard-wedge / typed-token flow when the camera isn't usable. -->
        <v-card class="mb-4 pa-4">
            <v-card-title>Scan a pass</v-card-title>
            <v-card-text>
                <div id="qr-reader" class="reader-surface mb-3"></div>
                <div class="d-flex ga-2 flex-wrap">
                    <v-btn v-if="!scanning" color="primary" prepend-icon="mdi-camera" @click="startScan">
                        Start Camera
                    </v-btn>
                    <v-btn v-else color="error" prepend-icon="mdi-stop" @click="stopScan">
                        Stop Camera
                    </v-btn>
                </div>
                <v-divider class="my-4"></v-divider>
                <p class="text-caption text-medium-emphasis mb-2">Or paste a token / redeem URL:</p>
                <div class="d-flex ga-2">
                    <v-text-field v-model="manualInput" label="Token or URL" density="compact" hide-details
                        @keyup.enter="lookupManual"></v-text-field>
                    <v-btn color="primary" :loading="lookingUp" @click="lookupManual">Look Up</v-btn>
                </div>
            </v-card-text>
        </v-card>

        <!-- Rider card. Visible once a token resolves. Photo + waiver/membership
             warnings up top, today's events as the action surface, future events
             listed below for context (not actionable here). -->
        <v-card v-if="lookup" class="mb-4 pa-4">
            <v-row>
                <v-col v-if="lookup.photoDataUrl" cols="12" sm="auto">
                    <img :src="lookup.photoDataUrl" alt="Pass holder"
                        style="max-width: 200px; border-radius: 8px; border: 1px solid rgba(0,0,0,0.12)" />
                </v-col>
                <v-col>
                    <h2 class="text-h5 mb-1">{{ lookup.purchaserName }}</h2>
                    <div class="text-body-2 text-medium-emphasis">{{ lookup.purchaserEmail }}</div>
                    <div v-if="lookup.purchaserPhone" class="text-body-2 text-medium-emphasis">
                        {{ lookup.purchaserPhone }}
                    </div>
                    <div v-if="!lookup.photoDataUrl" class="mt-2">
                        <v-alert type="warning" variant="tonal" density="compact">
                            No photo on file — verify the rider's identity another way.
                        </v-alert>
                    </div>

                    <v-alert v-if="lookup.requiresWaiver && !lookup.waiverSigned"
                        type="warning" variant="tonal" density="compact" class="mt-3"
                        prepend-icon="mdi-file-sign">
                        Waiver required for one of today's events but not signed yet.
                    </v-alert>

                    <v-alert v-if="lookup.requiresMembership && !lookup.membershipActive"
                        type="warning" variant="tonal" density="compact" class="mt-3"
                        prepend-icon="mdi-card-account-details-outline">
                        No active {{ lookup.membershipName }}. Required at this track.
                    </v-alert>
                </v-col>
            </v-row>

            <v-divider class="my-4"></v-divider>

            <div class="text-subtitle-1 mb-2">Today's events</div>
            <div v-if="lookup.todayRegistrations.length === 0" class="text-medium-emphasis mb-2">
                No registrations for today on this rider's account.
            </div>
            <div v-for="r in lookup.todayRegistrations" :key="r.id"
                class="d-flex align-center py-2 ga-3 flex-wrap"
                style="border-bottom: 1px solid rgba(0,0,0,0.06)">
                <div class="flex-grow-1">
                    <div class="text-body-1"><strong>{{ r.eventTitle }}</strong></div>
                    <div class="text-caption text-medium-emphasis">
                        {{ r.itemName }} ·
                        <v-chip size="x-small" :color="sourceColor(r.source)">{{ sourceLabel(r.source) }}</v-chip>
                        <span class="ml-2">{{ formatTime(r.eventStartsAtUtc) }}</span>
                    </div>
                </div>
                <div>
                    <v-btn v-if="r.checkedIn" variant="text" color="success" prepend-icon="mdi-check-circle">
                        Checked in
                        <span v-if="r.checkedInAtUtc" class="ml-1 text-caption">
                            ({{ formatTime(r.checkedInAtUtc) }})
                        </span>
                    </v-btn>
                    <v-btn v-else color="primary" :loading="checkingInId === r.id"
                        @click="performCheckIn(r)">
                        Check in
                    </v-btn>
                </div>
            </div>

            <template v-if="lookup.futureRegistrations.length > 0">
                <v-divider class="my-4"></v-divider>
                <div class="text-subtitle-2 mb-2">Future events (not checked in here)</div>
                <div v-for="r in lookup.futureRegistrations" :key="r.id"
                    class="d-flex align-center py-1 ga-2 flex-wrap text-caption text-medium-emphasis">
                    <span>{{ formatDateLong(r.eventStartsAtUtc) }}</span>
                    <span>·</span>
                    <span>{{ r.eventTitle }}</span>
                    <span>·</span>
                    <span>{{ r.itemName }}</span>
                    <v-chip size="x-small" :color="sourceColor(r.source)">{{ sourceLabel(r.source) }}</v-chip>
                </div>
            </template>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import dayjs from 'dayjs'
import { Html5Qrcode } from 'html5-qrcode'
import { ReportsService, type CheckInLookup, type CheckInRegistration, type EventRiderReport } from '@/services/ReportsService'
import { EventService, type EventDto } from '@/services/EventService'
import { TicketService } from '@/services/TicketService'
import { SeasonPassService } from '@/services/SeasonPassService'
import { branding } from '@/stores/branding'

const reportsService = new ReportsService()
const eventService = new EventService()
const ticketService = new TicketService()
const seasonPassService = new SeasonPassService()

// ── Event roster (top section) ──────────────────────────────────────────────
const events = ref<EventDto[]>([])
const loadingEvents = ref(false)
const selectedEventId = ref<string | null>(null)
const roster = ref<EventRiderReport | null>(null)
const loadingRoster = ref(false)

const eventOptions = computed(() => events.value
    .slice()
    .sort((a, b) => a.startsAtUtc.localeCompare(b.startsAtUtc))
    .map(e => ({
        title: `${formatShort(e.startsAtUtc)} — ${e.title}`,
        value: e.id,
    })))

function tz() { return branding.timezone || 'UTC' }
function formatShort(iso: string): string { return dayjs.utc(iso).tz(tz()).format('YYYY-MM-DD ddd') }
function formatTime(iso: string): string { return dayjs.utc(iso).tz(tz()).format('h:mm A') }
function formatDateLong(iso: string): string { return dayjs.utc(iso).tz(tz()).format('ddd, MMM D · h:mm A') }

async function loadEvents() {
    loadingEvents.value = true
    try {
        // Wide window so a gate worker on a slow day can pick recent / upcoming events.
        const tzn = tz()
        const fromUtc = dayjs().tz(tzn).startOf('day').subtract(7, 'day').utc().toISOString()
        const toUtc = dayjs().tz(tzn).startOf('day').add(180, 'day').utc().toISOString()
        const r = await eventService.list(fromUtc, toUtc)
        events.value = (r.data as any).data
        // Default to the first event happening today, if any.
        const todayKey = dayjs().tz(tzn).format('YYYY-MM-DD')
        const todayEvent = events.value.find(e => dayjs.utc(e.startsAtUtc).tz(tzn).format('YYYY-MM-DD') === todayKey)
        if (todayEvent && !selectedEventId.value) {
            selectedEventId.value = todayEvent.id
            await loadRoster()
        }
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load events.', 'error')
    } finally {
        loadingEvents.value = false
    }
}

async function loadRoster() {
    if (!selectedEventId.value) { roster.value = null; return }
    loadingRoster.value = true
    try {
        const r = await reportsService.getEventRiders(selectedEventId.value)
        roster.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load roster.', 'error')
    } finally {
        loadingRoster.value = false
    }
}

// ── Scanner ─────────────────────────────────────────────────────────────────
const scanning = ref(false)
let scanner: Html5Qrcode | null = null

async function startScan() {
    try {
        scanner = new Html5Qrcode('qr-reader')
        await scanner.start(
            { facingMode: 'environment' },
            { fps: 10, qrbox: { width: 260, height: 260 } },
            onDecoded,
            () => {},
        )
        scanning.value = true
    } catch (err: any) {
        flash(err?.message || 'Failed to start camera.', 'error')
    }
}

async function stopScan() {
    if (!scanner) return
    try { await scanner.stop(); await scanner.clear() } catch {}
    scanner = null
    scanning.value = false
}

async function onDecoded(decodedText: string) {
    const token = extractToken(decodedText)
    if (!token) return
    await stopScan()
    await doLookup(token)
}

function extractToken(raw: string): string | null {
    const m = raw.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i)
    return m ? m[0] : null
}

// ── Manual lookup ───────────────────────────────────────────────────────────
const manualInput = ref('')
const lookingUp = ref(false)

async function lookupManual() {
    const token = extractToken(manualInput.value)
    if (!token) { flash('No token found in input.', 'error'); return }
    await doLookup(token)
}

// ── Rider lookup ────────────────────────────────────────────────────────────
const lookup = ref<CheckInLookup | null>(null)
const checkingInId = ref<string | null>(null)

async function doLookup(token: string) {
    lookingUp.value = true
    try {
        // Window: today's local-day start through +90 days. The backend splits
        // today vs future by comparing event start to fromUtc + 1 day.
        const tzn = tz()
        const start = dayjs().tz(tzn).startOf('day')
        const fromUtc = start.utc().toISOString()
        const toUtc = start.add(90, 'day').utc().toISOString()
        const r = await reportsService.checkInLookup(token, fromUtc, toUtc)
        lookup.value = (r.data as any).data
        manualInput.value = ''
    } catch (err: any) {
        lookup.value = null
        flash(err.response?.data?.error || 'No registration found for that token.', 'error')
    } finally {
        lookingUp.value = false
    }
}

async function performCheckIn(r: CheckInRegistration) {
    if (lookup.value && lookup.value.requiresWaiver && !lookup.value.waiverSigned) {
        if (!confirm('This event requires a signed waiver and the rider hasn\'t signed it. Check in anyway?')) return
    }
    if (lookup.value && lookup.value.requiresMembership && !lookup.value.membershipActive) {
        if (!confirm(`This rider doesn't have an active ${lookup.value.membershipName}. Check in anyway?`)) return
    }
    checkingInId.value = r.id
    try {
        if (r.source === 'season_pass') {
            await seasonPassService.checkIn(r.id)
        } else if (r.redemptionToken) {
            // Pass + ticket share the token-based redemption endpoint.
            await ticketService.redeem(r.redemptionToken)
        } else {
            throw new Error('Missing redemption token for this registration.')
        }
        flash('Checked in!', 'success')
        // Re-pull lookup so check-in state and timestamps refresh, and reload
        // the event roster if it covers this event.
        if (lookup.value) {
            const lastTokenSrc = lookup.value.todayRegistrations.find(x => x.id === r.id)
            // Lookup needs a token — easiest: keep manualInput in sync above. If we
            // came from a scan, the redemption token on the matched registration is
            // the most reliable thing we have right now. For season pass the matched
            // token is the season pass purchase token, which we don't have here.
            // Simpler: just set the registration to checked in locally.
            if (lastTokenSrc) {
                lastTokenSrc.checkedIn = true
                lastTokenSrc.checkedInAtUtc = new Date().toISOString()
            }
        }
        if (selectedEventId.value === r.eventId) await loadRoster()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Check-in failed.', 'error')
    } finally {
        checkingInId.value = null
    }
}

// ── Helpers ────────────────────────────────────────────────────────────────
function sourceColor(s: string): string {
    switch (s) {
        case 'pass': return 'primary'
        case 'event_ticket': return 'deep-orange'
        case 'season_pass': return 'success'
        default: return 'grey'
    }
}
function sourceLabel(s: string): string {
    switch (s) {
        case 'pass': return 'Pass'
        case 'event_ticket': return 'Ticket'
        case 'season_pass': return 'Season'
        default: return s
    }
}
function statusColor(s: string): string {
    switch (s) {
        case 'paid': return 'success'
        case 'redeemed': return 'primary'
        case 'reserved': return 'success'
        case 'checked_in': return 'primary'
        case 'pending': return 'warning'
        case 'refunded': return 'grey'
        default: return undefined as unknown as string
    }
}

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')
function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onMounted(loadEvents)
onBeforeUnmount(() => { if (scanner) stopScan() })
</script>

<style scoped>
.reader-surface {
    width: 100%;
    max-width: 420px;
    min-height: 260px;
    border: 1px dashed rgba(0, 0, 0, 0.2);
    border-radius: 6px;
    margin: 0 auto;
    background: #f5f5f5;
}
</style>
