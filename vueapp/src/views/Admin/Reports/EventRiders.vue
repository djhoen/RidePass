<template>
    <div>
        <div class="rp-pagehead">
            <div>
                <div class="rp-pagehead-title">Event Riders</div>
            </div>
            <div class="d-flex align-center flex-wrap ga-2">
                <v-autocomplete v-model="selectedEventId" :items="eventOptions" item-title="title" item-value="value"
                    label="Pick an event" density="compact" hide-details
                    style="min-width: 320px" :loading="loadingEvents"
                    @update:model-value="loadReport"></v-autocomplete>
                <v-btn variant="text" :disabled="!selectedEventId" :loading="loading" @click="loadReport">Refresh</v-btn>
            </div>
        </div>

        <v-card v-if="report" class="mb-4 pa-3" variant="outlined">
            <div class="d-flex align-center flex-wrap ga-3">
                <div class="flex-grow-1">
                    <div class="text-h6">{{ report.eventTitle }}</div>
                    <div class="text-caption text-medium-emphasis">{{ formatLong(report.eventStartsAtUtc) }}</div>
                </div>
                <div class="text-right rp-kpi">
                    <div class="rp-kpi-label mt-0">Checked in</div>
                    <div class="rp-kpi-value">{{ report.totalCheckedIn }} / {{ report.totalRegistrants }}</div>
                </div>
            </div>
        </v-card>

        <v-card v-if="report" class="mb-3 pa-3">
            <div class="d-flex flex-wrap align-center ga-3">
                <v-text-field v-model="search" prepend-inner-icon="mdi-magnify" placeholder="Search name, email, race #, class..."
                    density="compact" hide-details clearable style="max-width: 360px"></v-text-field>
                <v-select v-model="classFilter" :items="classOptions" label="Class" density="compact" hide-details clearable
                    style="max-width: 240px"></v-select>
                <v-spacer></v-spacer>
                <v-btn variant="tonal" :disabled="selectedRows.length === 0" prepend-icon="mdi-message-text"
                    @click="openMessageDialog">
                    Send message ({{ selectedRows.length }})
                </v-btn>
                <v-btn v-if="branding.tracksideExportEnabled" variant="tonal" prepend-icon="mdi-export"
                    :loading="exportingCsv" :disabled="!selectedEventId" @click="exportTracksideCsv">
                    Export Trackside CSV
                </v-btn>
            </div>
        </v-card>

        <!-- Scheduled-messages panel: only shows when there's something pending for this
             event. Each row can be cancelled while still pending; once the dispatcher
             picks it up it disappears from the list (succeeded/failed/cancelled all hide). -->
        <v-card v-if="report && scheduledMessages.length > 0" class="mb-3 pa-3">
            <div class="text-subtitle-2 mb-2">
                <v-icon size="small" class="mr-1">mdi-clock-outline</v-icon>
                Scheduled messages ({{ scheduledMessages.length }})
            </div>
            <v-table density="compact" class="bg-transparent">
                <tbody>
                    <tr v-for="m in scheduledMessages" :key="m.id">
                        <td>{{ formatLong(m.runAtUtc) }}</td>
                        <td class="text-medium-emphasis">{{ m.summary || '—' }}</td>
                        <td class="text-right" style="width: 110px">
                            <v-btn size="small" variant="text" color="error"
                                :disabled="cancellingId === m.id"
                                @click="cancelScheduled(m)">
                                Cancel
                            </v-btn>
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-card v-if="report">
            <v-data-table v-model="selectedRows" :items="filteredRows" :headers="headers" item-value="purchaseId"
                show-select density="comfortable" :items-per-page="50"
                :items-per-page-options="[25, 50, 100, -1]">
                <template #item.checkedIn="{ item }">
                    <v-btn size="small" :color="item.checkedIn ? 'success' : 'default'"
                        :variant="item.checkedIn ? 'tonal' : 'outlined'" @click="toggleCheckIn(item)">
                        <v-icon :icon="item.checkedIn ? 'mdi-check' : 'mdi-circle-outline'" size="small" class="mr-1"></v-icon>
                        {{ item.checkedIn ? 'Checked in' : 'Check in' }}
                    </v-btn>
                </template>
                <template #item.raceNumber="{ item }">
                    <span v-if="!isRaceEntry(item)" class="text-medium-emphasis">—</span>
                    <v-text-field v-else density="compact" hide-details variant="outlined" :model-value="item.raceNumber ?? ''"
                        :placeholder="item.userRaceNumber ?? '—'"
                        style="max-width: 110px"
                        @blur="commitRaceNumber(item, ($event.target as HTMLInputElement).value)"
                        @keydown.enter="(ev: any) => ev.target.blur()"></v-text-field>
                </template>
                <template #item.purchaserName="{ item }">
                    <div>{{ item.purchaserName }}</div>
                    <div v-if="item.hometown" class="text-caption text-medium-emphasis">{{ item.hometown }}</div>
                </template>
                <template #item.purchaserEmail="{ item }">
                    <a :href="`mailto:${item.purchaserEmail}`">{{ item.purchaserEmail }}</a>
                </template>
                <template #item.purchaserPhone="{ item }">
                    <a v-if="item.purchaserPhone" :href="`tel:${item.purchaserPhone}`">{{ item.purchaserPhone }}</a>
                    <span v-else class="text-medium-emphasis">—</span>
                </template>
                <template #item.itemName="{ item }">
                    <div>{{ item.itemName }}</div>
                    <div v-if="item.tierKind === 'gate_fee'" class="text-caption text-medium-emphasis">
                        {{ item.tierAudience === 'spectator' ? 'spectator gate' : 'rider gate' }}
                    </div>
                </template>
                <template #item.status="{ item }">
                    <v-chip size="x-small" :color="statusColor(item.status)">{{ item.status }}</v-chip>
                </template>
            </v-data-table>
        </v-card>

        <v-card v-else-if="!loading && !selectedEventId" variant="outlined" class="pa-6 text-center">
            <v-icon size="48" class="text-muted2 mb-2">mdi-account-group-outline</v-icon>
            <p class="text-body-2 text-medium-emphasis">
                Pick an event above to see who's registered and who has checked in.
            </p>
        </v-card>

        <v-progress-circular v-else-if="loading" indeterminate color="primary"></v-progress-circular>

        <v-dialog v-model="messageDialog" max-width="600" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Send message to {{ selectedRows.length }} {{ selectedRows.length === 1 ? 'rider' : 'riders' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="messageDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-btn-toggle v-model="msgChannel" mandatory color="primary" density="compact" class="mb-3">
                        <v-btn value="sms" prepend-icon="mdi-message-text">Text</v-btn>
                        <v-btn value="email" prepend-icon="mdi-email">Email</v-btn>
                    </v-btn-toggle>
                    <p class="text-caption text-medium-emphasis mb-2">
                        <span v-if="msgChannel === 'sms'">
                            Riders without a phone number will be skipped. SMS uses your tenant's Twilio config.
                        </span>
                        <span v-else>
                            Riders without an email will be skipped. Body is wrapped in your tenant's branded shell.
                        </span>
                    </p>

                    <v-text-field v-if="msgChannel === 'email'" v-model="msgSubject" label="Subject" density="compact"
                        maxlength="200" class="mb-2"></v-text-field>

                    <v-textarea v-model="msgBody" label="Message" rows="4" auto-grow counter
                        :maxlength="msgChannel === 'sms' ? 800 : 2000" density="compact" class="mt-4"></v-textarea>

                    <!-- Schedule-for picker. Blank = send now. -->
                    <div class="d-flex align-center ga-2 mt-2">
                        <v-text-field v-model="msgRunAtLocal" type="datetime-local" label="Schedule for (optional)"
                            density="compact" hide-details clearable
                            :hint="msgRunAtLocal ? `Will fire in tenant time (${tz()})` : 'Leave blank to send immediately'"
                            persistent-hint></v-text-field>
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="sendingMsg" @click="messageDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="sendingMsg" :disabled="!canSubmitMessage" @click="submitMessage">
                        {{ msgRunAtLocal ? 'Schedule' : 'Send now' }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Check-in refused because the rider turned 18 since their waiver: send them the waiver
             signing link (the gate's own sign-request flow) so they sign it as an adult. -->
        <v-dialog v-model="adultOpen" max-width="460">
            <v-card v-if="adultBlock">
                <v-card-title>Send an adult waiver link</v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-3">{{ adultBlock.message }}</p>
                    <v-text-field v-model="adultEmail" type="email" density="compact" label="Rider's email"
                        :hint="adultBlock.riderEmail ? 'The rider\'s own email on file.' : ''" persistent-hint
                        :error-messages="adultError ? [adultError] : []"></v-text-field>
                    <div v-if="adultResult" class="mt-3 text-body-2">
                        {{ adultResult.sent ? `Sent to ${adultResult.recipientEmail}.`
                            : 'Email isn’t available here, so open this link on the rider’s phone:' }}
                        <div class="text-caption mt-1" style="word-break: break-all">{{ adultResult.link }}</div>
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="adultSending" @click="adultOpen = false">{{ adultResult ? 'Done' : 'Cancel' }}</v-btn>
                    <v-btn v-if="!adultResult" color="primary" :loading="adultSending" :disabled="!adultEmail.includes('@')"
                        @click="sendAdultLink">Send link</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Check-in refused because the tenant requires a current birthdate check: verify the
             rider's ID here (the gate's own verify endpoints), then the check-in is retried. -->
        <v-dialog v-model="verifyOpen" max-width="440">
            <v-card v-if="verifyBlock">
                <v-card-title>Verify ID and age</v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-2">{{ verifyBlock.message }}</p>
                    <div class="text-h6">{{ verifyBlock.riderName || 'This rider' }}</div>
                    <v-text-field v-model="verifyDob" type="date" label="Date of birth on the ID"
                        density="compact" class="mt-4"
                        :error-messages="verifyError ? [verifyError] : []"></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="verifying" @click="verifyOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="verifying" :disabled="!verifyDob" @click="verifyAndCheckIn">
                        Verify and check in
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Check-in refused because the tenant requires a rider photo and none is on file: take it
             here (the gate's own photo endpoint, saved for the rider), then the check-in is retried. -->
        <v-dialog v-model="photoOpen" max-width="420">
            <v-card v-if="photoBlock">
                <v-card-title>Take rider photo</v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-2">{{ photoBlock.message }}</p>
                    <div class="text-h6 mb-3">{{ photoBlock.riderName || 'This rider' }}</div>
                    <PhotoCapture v-model="photoData" button-label="Take photo" />
                    <div v-if="photoError" class="text-error text-body-2 mt-3">{{ photoError }}</div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="photoSaving" @click="photoOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="photoSaving" :disabled="!photoData" @click="savePhotoAndCheckIn">
                        Save and check in
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import dayjs from 'dayjs'
import { ReportsService, type EventRiderReport, type EventRiderRow, type ScheduledRiderMessage } from '@/services/ReportsService'
import { EventService, type EventDto } from '@/services/EventService'
import { TicketService, type GateWaiverSignRequestResult } from '@/services/TicketService'
import { SeasonPassService } from '@/services/SeasonPassService'
import { branding } from '@/stores/branding'
import PhotoCapture from '@/components/PhotoCapture.vue'

const props = defineProps<{ initialEventId?: string | null }>()

const reportsService = new ReportsService()
const eventService = new EventService()
const ticketService = new TicketService()
const seasonPassService = new SeasonPassService()

const events = ref<EventDto[]>([])
const loadingEvents = ref(false)
const selectedEventId = ref<string | null>(props.initialEventId ?? null)
const report = ref<EventRiderReport | null>(null)
const loading = ref(false)

const search = ref('')
const classFilter = ref<string | null>(null)
const selectedRows = ref<string[]>([])

const messageDialog = ref(false)
const msgChannel = ref<'sms' | 'email'>('sms')
const msgSubject = ref('')
const msgBody = ref('')
// datetime-local string in TENANT timezone — we convert to UTC when sending.
// Blank = send immediately.
const msgRunAtLocal = ref<string | null>(null)
const sendingMsg = ref(false)

const scheduledMessages = ref<ScheduledRiderMessage[]>([])
const cancellingId = ref<string | null>(null)

const canSubmitMessage = computed(() => {
    if (!msgBody.value.trim()) return false
    if (msgChannel.value === 'email' && !msgSubject.value.trim()) return false
    return true
})

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('error')

const eventOptions = computed(() => events.value.slice()
    .sort((a, b) => a.startsAtUtc.localeCompare(b.startsAtUtc))
    .map(e => ({ title: `${formatShort(e.startsAtUtc)} — ${e.title}`, value: e.id })))

const classOptions = computed(() => {
    if (!report.value) return []
    const set = new Set<string>()
    for (const r of report.value.rows) set.add(r.itemName)
    return Array.from(set).sort()
})

const headers = [
    { title: 'In', key: 'checkedIn', sortable: true, width: 130 },
    { title: 'Race #', key: 'raceNumber', sortable: true, width: 130 },
    { title: 'Name', key: 'purchaserName', sortable: true },
    { title: 'Email', key: 'purchaserEmail', sortable: true },
    { title: 'Phone', key: 'purchaserPhone', sortable: false, width: 130 },
    { title: 'Class', key: 'itemName', sortable: true },
    { title: 'Status', key: 'status', sortable: true, width: 110 },
]

// Substring match across the visible-ish fields. "Fuzzy" in the practical
// sense — admins type a fragment of a name / email / number / class and we
// keep matching rows. Real Levenshtein isn't worth the dependency.
const filteredRows = computed<EventRiderRow[]>(() => {
    if (!report.value) return []
    const q = search.value.trim().toLowerCase()
    return report.value.rows.filter(r => {
        if (classFilter.value && r.itemName !== classFilter.value) return false
        if (!q) return true
        const hay = [
            r.purchaserName, r.purchaserEmail, r.purchaserPhone ?? '',
            r.itemName, r.raceNumber ?? '', r.userRaceNumber ?? '',
            r.hometown ?? '',
        ].join(' ').toLowerCase()
        return hay.includes(q)
    })
})

const exportingCsv = ref(false)
async function exportTracksideCsv() {
    if (!selectedEventId.value) return
    exportingCsv.value = true
    try {
        const { blob, filename } = await reportsService.downloadTracksideCsv(selectedEventId.value)
        const url = URL.createObjectURL(blob)
        const a = document.createElement('a')
        a.href = url; a.download = filename
        document.body.appendChild(a); a.click()
        a.remove(); URL.revokeObjectURL(url)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not export the CSV. Try again.', 'error')
    } finally {
        exportingCsv.value = false
    }
}

function tz() { return branding.timezone || 'UTC' }
function formatShort(iso: string) { return dayjs.utc(iso).tz(tz()).format('YYYY-MM-DD ddd') }
function formatLong(iso: string) { return dayjs.utc(iso).tz(tz()).format('dddd, MMM D, YYYY · h:mm A') }

function statusColor(s: string): string {
    switch (s) {
        case 'paid': return 'success'
        case 'redeemed': return 'primary'
        case 'reserved': return 'success'
        case 'checked_in': return 'primary'
        case 'pending': return 'warning'
        case 'refunded': return 'grey'
        case 'failed': return 'error'
        default: return undefined as unknown as string
    }
}

function isRaceEntry(row: EventRiderRow): boolean {
    return row.source === 'event_ticket' && row.tierKind === 'race_entry'
}

async function loadEvents() {
    loadingEvents.value = true
    try {
        const tzn = tz()
        const fromUtc = dayjs().tz(tzn).startOf('day').subtract(180, 'day').utc().toISOString()
        const toUtc = dayjs().tz(tzn).startOf('day').add(365, 'day').utc().toISOString()
        const r = await eventService.list(fromUtc, toUtc)
        events.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load events.', 'error')
    } finally {
        loadingEvents.value = false
    }
}

async function loadReport() {
    if (!selectedEventId.value) return
    loading.value = true
    try {
        const r = await reportsService.getEventRiders(selectedEventId.value)
        report.value = (r.data as any).data
        selectedRows.value = []
        // Fire-and-forget — the scheduled panel is optional context, not
        // load-bearing for the report itself.
        loadScheduledMessages()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load report.', 'error')
    } finally {
        loading.value = false
    }
}

async function toggleCheckIn(row: EventRiderRow) {
    try {
        await reportsService.setCheckIn(row.purchaseId, row.source, !row.checkedIn)
        await loadReport()
    } catch (err: any) {
        // The tenant requires a current birthdate check and this rider has none: offer to verify
        // the ID right here instead of a dead end.
        const data = err.response?.data?.data
        // Turned 18 since their waiver: offer to send them the link to sign as an adult.
        if (data?.code === 'adult_waiver_needed' && data.purchaseId) {
            adultBlock.value = {
                message: err.response.data.error, riderEmail: data.riderEmail ?? null,
                kind: data.signKind, purchaseId: data.purchaseId, eventId: data.eventId ?? null,
            }
            adultEmail.value = data.riderEmail ?? ''
            adultError.value = ''
            adultResult.value = null
            adultOpen.value = true
            return
        }
        // Required rider photo, none on file: take it right here.
        if (data?.code === 'photo_needed' && data.purchaseId) {
            photoBlock.value = { row, message: err.response.data.error, riderName: data.riderName ?? null,
                kind: data.photoKind, purchaseId: data.purchaseId }
            photoData.value = null
            photoError.value = ''
            photoOpen.value = true
            return
        }
        if (data?.code === 'birthdate_unverified' && data.verifyToken) {
            verifyBlock.value = {
                row, message: err.response.data.error, riderName: data.riderName ?? null,
                verifyKind: data.verifyKind, verifyToken: data.verifyToken, purchaseId: data.purchaseId,
            }
            verifyDob.value = ''
            verifyError.value = ''
            verifyOpen.value = true
            return
        }
        flash(err.response?.data?.error || 'Check-in failed.', 'error')
    }
}

// ── Adult waiver link from a "turned 18" refusal (reuses the gate's sign-request flow) ──
const adultOpen = ref(false)
const adultSending = ref(false)
const adultEmail = ref('')
const adultError = ref('')
const adultResult = ref<GateWaiverSignRequestResult | null>(null)
const adultBlock = ref<{ message: string; riderEmail: string | null; kind: 'ticket' | 'season_pass';
    purchaseId: string; eventId: string | null } | null>(null)

async function sendAdultLink() {
    const b = adultBlock.value
    if (!b || !adultEmail.value.includes('@')) return
    adultSending.value = true
    adultError.value = ''
    try {
        const r = await ticketService.gateWaiverSignRequest({ kind: b.kind, purchaseId: b.purchaseId,
            email: adultEmail.value.trim(), eventId: b.eventId })
        adultResult.value = r.data.data
    } catch (err: any) {
        adultError.value = err.response?.data?.error || 'Couldn\'t send the link.'
    } finally {
        adultSending.value = false
    }
}

// ── Verify ID from a birthdate-check refusal ──
const verifyOpen = ref(false)
const verifying = ref(false)
const verifyDob = ref('')
const verifyError = ref('')
const verifyBlock = ref<{ row: EventRiderRow; message: string; riderName: string | null;
    verifyKind: 'ticket' | 'season_pass'; verifyToken: string; purchaseId: string } | null>(null)

async function verifyAndCheckIn() {
    const b = verifyBlock.value
    if (!b || !verifyDob.value) return
    verifying.value = true
    verifyError.value = ''
    try {
        if (b.verifyKind === 'season_pass') await seasonPassService.verifyPassHolderId(b.verifyToken, verifyDob.value)
        else await ticketService.verifyOrderRiderId(b.verifyToken, b.purchaseId, verifyDob.value)
    } catch (err: any) {
        verifyError.value = err.response?.data?.error || 'Couldn\'t record the ID check.'
        verifying.value = false
        return
    }
    verifying.value = false
    verifyOpen.value = false
    flash('ID and age verified.', 'success')
    await toggleCheckIn(b.row)
}

// Take a rider photo from a "no photo on file" refusal.
const photoOpen = ref(false)
const photoSaving = ref(false)
const photoData = ref<string | null>(null)
const photoError = ref('')
const photoBlock = ref<{ row: EventRiderRow; message: string; riderName: string | null;
    kind: 'ticket' | 'season_pass'; purchaseId: string } | null>(null)

async function savePhotoAndCheckIn() {
    const b = photoBlock.value
    if (!b || !photoData.value) return
    photoSaving.value = true
    photoError.value = ''
    try {
        await ticketService.takeRiderPhoto(b.kind, b.purchaseId, photoData.value)
    } catch (err: any) {
        photoError.value = err.response?.data?.error || 'Couldn\'t save the photo.'
        photoSaving.value = false
        return
    }
    photoSaving.value = false
    photoOpen.value = false
    flash('Photo saved.', 'success')
    await toggleCheckIn(b.row)
}

async function commitRaceNumber(row: EventRiderRow, value: string) {
    const trimmed = value.trim()
    const next = trimmed.length === 0 ? null : trimmed
    if (next === row.raceNumber) return
    try {
        await reportsService.setRaceNumber(row.purchaseId, next)
        row.raceNumber = next   // optimistic update; full reload not needed
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    }
}

function openMessageDialog() {
    msgChannel.value = 'sms'
    msgSubject.value = ''
    msgBody.value = ''
    msgRunAtLocal.value = null
    messageDialog.value = true
}

async function submitMessage() {
    if (!selectedEventId.value || !canSubmitMessage.value) return
    sendingMsg.value = true
    try {
        // Convert the tenant-local datetime to UTC if scheduled. Blank → null
        // (immediate). Server treats <= now as immediate too (60s grace).
        const runAtUtc = msgRunAtLocal.value
            ? dayjs.tz(msgRunAtLocal.value, tz()).utc().toISOString()
            : null
        const r = await reportsService.sendRiderMessage(selectedEventId.value, {
            purchaseIds: selectedRows.value,
            channel: msgChannel.value,
            subject: msgChannel.value === 'email' ? msgSubject.value.trim() : null,
            body: msgBody.value.trim(),
            runAtUtc,
        })
        const result = (r.data as any).data
        if (result.scheduledTaskId) {
            flash(`Scheduled for ${formatLong(result.scheduledRunAtUtc)}.`, 'success')
            await loadScheduledMessages()
        } else {
            const sent = result.sent ?? 0
            const skipped = result.skipped ?? 0
            const names: string[] = result.skippedNames ?? []
            const summary = `Sent ${sent}, skipped ${skipped}` +
                (skipped > 0 ? ` (${names.slice(0, 3).join(', ')}${names.length > 3 ? '…' : ''})` : '')
            flash(summary, skipped > 0 ? 'error' : 'success')
        }
        messageDialog.value = false
    } catch (err: any) {
        flash(err.response?.data?.error || 'Send failed.', 'error')
    } finally {
        sendingMsg.value = false
    }
}

async function loadScheduledMessages() {
    if (!selectedEventId.value) { scheduledMessages.value = []; return }
    try {
        const r = await reportsService.listScheduledRiderMessages(selectedEventId.value)
        scheduledMessages.value = (r.data as any).data
    } catch (err: any) {
        scheduledMessages.value = []
        flash(err.response?.data?.error || 'Couldn’t load scheduled messages for this event. Refresh to try again.', 'error')
    }
}

async function cancelScheduled(m: ScheduledRiderMessage) {
    cancellingId.value = m.id
    try {
        await reportsService.cancelScheduledRiderMessage(m.id)
        await loadScheduledMessages()
        flash('Cancelled.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Cancel failed.', 'error')
    } finally {
        cancellingId.value = null
    }
}

function flash(text: string, color: 'success' | 'error' = 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onMounted(async () => {
    await loadEvents()
    if (selectedEventId.value) await loadReport()
})

watch(() => props.initialEventId, (id) => {
    if (id && id !== selectedEventId.value) {
        selectedEventId.value = id
        loadReport()
    }
})
</script>
