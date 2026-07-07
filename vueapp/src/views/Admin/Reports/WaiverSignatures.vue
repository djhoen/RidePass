<template>
    <div>
        <div class="d-flex flex-wrap align-center ga-2 mb-4">
            <v-autocomplete v-model="selectedEventId" :items="eventOptions" item-title="title" item-value="value"
                :loading="loadingEvents" label="Event" density="compact" hide-details clearable
                style="min-width: 320px; max-width: 480px" @update:model-value="onEventChange"></v-autocomplete>
            <v-btn variant="text" :disabled="!selectedEventId" :loading="loading" @click="loadReport">Refresh</v-btn>
            <v-spacer></v-spacer>
            <template v-if="report">
                <v-chip size="small" variant="tonal">{{ report.totalAttendees }} attendees</v-chip>
                <v-chip size="small" :color="allSigned ? 'success' : 'warning'" variant="tonal">
                    {{ report.totalSigned }} / {{ report.totalAttendees }} cleared
                </v-chip>
            </template>
        </div>

        <v-card v-if="!selectedEventId" variant="outlined" class="pa-6 text-center text-medium-emphasis">
            Pick an event to see who has signed their waiver.
        </v-card>

        <v-card v-else variant="outlined">
            <v-text-field v-model="search" prepend-inner-icon="mdi-magnify" label="Search rider"
                density="compact" hide-details class="pa-3"></v-text-field>
            <v-data-table :headers="headers" :items="filteredRows" :loading="loading" density="compact"
                :items-per-page="50" item-value="purchaseId">
                <template #[`item.audience`]="{ item }">
                    <v-chip size="x-small" variant="tonal" :color="item.audience === 'rider' ? 'primary' : 'secondary'">
                        {{ item.audience }}
                    </v-chip>
                </template>
                <template #[`item.waiver`]="{ item }">
                    <v-chip size="x-small" :color="waiverColor(item)" variant="flat">{{ waiverLabel(item) }}</v-chip>
                </template>
                <template #[`item.registrationComplete`]="{ item }">
                    <v-icon :color="item.registrationComplete ? 'success' : 'warning'" size="small">
                        {{ item.registrationComplete ? 'mdi-check-circle' : 'mdi-alert-circle-outline' }}
                    </v-icon>
                </template>
                <template #[`item.signedBy`]="{ item }">
                    <span v-if="item.signedByParent">
                        Parent{{ item.parentGuardianName ? ` — ${item.parentGuardianName}` : '' }}
                    </span>
                    <span v-else-if="item.signerName">{{ item.signerName }}</span>
                    <span v-else class="text-medium-emphasis">—</span>
                </template>
                <template #[`item.signedAtUtc`]="{ item }">
                    {{ item.signedAtUtc ? formatWhen(item.signedAtUtc) : '—' }}
                </template>
            </v-data-table>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { ReportsService, type EventWaiverSignatureReport, type EventWaiverSignatureRow } from '@/services/ReportsService'
import { EventService, type EventDto } from '@/services/EventService'
import { branding } from '@/stores/branding'

const props = defineProps<{ initialEventId?: string | null }>()
const emit = defineEmits<{ (e: 'select-event', id: string | null): void }>()

const reportsService = new ReportsService()
const eventService = new EventService()

const events = ref<EventDto[]>([])
const loadingEvents = ref(false)
const selectedEventId = ref<string | null>(props.initialEventId ?? null)
const report = ref<EventWaiverSignatureReport | null>(null)
const loading = ref(false)
const search = ref('')

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('error')

function tz() { return branding.timezone || 'UTC' }
function formatShort(iso: string) { return dayjs.utc(iso).tz(tz()).format('YYYY-MM-DD ddd') }
function formatWhen(iso: string) { return dayjs.utc(iso).tz(tz()).format('MMM D, h:mm A') }

const headers = [
    { title: 'Attendee', key: 'attendeeName', sortable: true },
    { title: 'Type', key: 'audience', sortable: true, width: 110 },
    { title: 'Class / Gate', key: 'tierName', sortable: true },
    { title: 'Race #', key: 'raceNumber', sortable: true, width: 90 },
    { title: 'Registered', key: 'registrationComplete', sortable: true, width: 110 },
    { title: 'Waiver', key: 'waiver', sortable: false, width: 130 },
    { title: 'Signed by', key: 'signedBy', sortable: false },
    { title: 'Signed', key: 'signedAtUtc', sortable: true, width: 150 },
]

const eventOptions = computed(() => events.value.slice()
    .sort((a, b) => a.startsAtUtc.localeCompare(b.startsAtUtc))
    .map(e => ({ title: `${formatShort(e.startsAtUtc)} — ${e.title}`, value: e.id })))

const allSigned = computed(() => !!report.value && report.value.totalSigned >= report.value.totalAttendees)

const filteredRows = computed(() => {
    if (!report.value) return []
    const q = search.value.trim().toLowerCase()
    if (!q) return report.value.rows
    return report.value.rows.filter(r => r.attendeeName.toLowerCase().includes(q)
        || (r.raceNumber ?? '').toLowerCase().includes(q))
})

function waiverLabel(r: EventWaiverSignatureRow): string {
    if (!r.waiverRequired) return 'Not required'
    return r.waiverSigned ? 'Signed' : 'Unsigned'
}
function waiverColor(r: EventWaiverSignatureRow): string {
    if (!r.waiverRequired) return 'grey'
    return r.waiverSigned ? 'success' : 'error'
}

function flash(text: string, color: 'success' | 'error' = 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

function onEventChange(id: string | null) {
    emit('select-event', id)
    if (id) loadReport()
    else report.value = null
}

async function loadEvents() {
    loadingEvents.value = true
    try {
        const from = dayjs().tz(tz()).startOf('day').subtract(180, 'day').utc().toISOString()
        const to = dayjs().tz(tz()).startOf('day').add(365, 'day').utc().toISOString()
        const r = await eventService.list(from, to)
        events.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load the event list. Refresh to try again.')
    } finally {
        loadingEvents.value = false
    }
}

async function loadReport() {
    if (!selectedEventId.value) return
    loading.value = true
    try {
        const r = await reportsService.getEventWaiverSignatures(selectedEventId.value)
        report.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load the waiver report for this event. Refresh to try again.')
    } finally {
        loading.value = false
    }
}

onMounted(async () => {
    await loadEvents()
    if (selectedEventId.value) loadReport()
})
</script>
