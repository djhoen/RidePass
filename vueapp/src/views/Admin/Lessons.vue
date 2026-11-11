<template>
    <v-container style="max-width: 960px">
        <div class="rp-pagehead">
            <div>
                <div class="rp-pagehead-title">Lessons</div>
                <div class="rp-pagehead-sub">The next three weeks of lesson sessions. Move a rider to another date, or cancel a
                    session for weather: every rider on it is emailed a link to rebook or take a refund.
                    <template v-if="branding.lessonRebookOnly">Riders can move their own lesson up to
                        {{ branding.lessonChangeCutoffHours }} hours before it starts, and can't cancel it for a refund.</template>
                </div>
            </div>
        </div>

        <div v-if="loading" class="text-center pa-6"><v-progress-circular indeterminate></v-progress-circular></div>
        <v-card v-else-if="sessions.length === 0" class="pa-4 text-medium-emphasis">No lesson sessions in the next three weeks.</v-card>
        <v-expansion-panels v-else v-model="openPanel" variant="accordion">
            <v-expansion-panel v-for="s in sessions" :key="s.eventId" :value="s.eventId">
                <v-expansion-panel-title @click="loadBookings(s.eventId)">
                    <div class="d-flex align-center flex-wrap ga-2" style="width: 100%">
                        <div style="min-width: 190px">{{ formatTenantDateTime(s.startsAt, 'ddd, MMM D, h:mm A') }}</div>
                        <div class="font-weight-medium">{{ s.title }}</div>
                        <v-chip v-if="s.weatherCancelledAt" size="x-small" color="warning" variant="tonal">Cancelled for weather</v-chip>
                        <v-chip v-else-if="s.status === 'cancelled'" size="x-small" variant="tonal">Cancelled</v-chip>
                        <v-spacer></v-spacer>
                        <span class="text-caption text-medium-emphasis mr-2">{{ s.booked }} booked</span>
                    </div>
                </v-expansion-panel-title>
                <v-expansion-panel-text>
                    <div v-if="bookingsLoading[s.eventId]" class="text-center pa-3"><v-progress-circular indeterminate size="24"></v-progress-circular></div>
                    <template v-else>
                        <div v-if="!(bookings[s.eventId]?.length)" class="text-medium-emphasis pa-2">Nobody is booked.</div>
                        <v-table v-else density="compact">
                            <thead><tr><th>Rider</th><th>Group</th><th>Booked by</th><th>Status</th><th></th></tr></thead>
                            <tbody>
                                <tr v-for="b in bookings[s.eventId]" :key="b.ticketId">
                                    <td>{{ b.riderName }}</td>
                                    <td>{{ b.tierName }}</td>
                                    <td>{{ b.purchaserName }}<div class="text-caption text-medium-emphasis">{{ b.purchaserEmail }}</div></td>
                                    <td>{{ b.status === 'redeemed' ? 'Checked in' : 'Booked' }}</td>
                                    <td class="text-right">
                                        <v-btn v-if="b.status === 'paid' && canMove" size="small" variant="text" color="primary"
                                            @click="openMove(b, s)">Change date</v-btn>
                                    </td>
                                </tr>
                            </tbody>
                        </v-table>
                        <template v-if="bikes[s.eventId]?.length">
                            <div class="text-subtitle-2 mt-4 mb-1">Bikes for this session</div>
                            <v-table density="compact">
                                <thead><tr><th>Rented by</th><th>Bike</th><th>Rider size</th></tr></thead>
                                <tbody>
                                    <tr v-for="k in bikes[s.eventId]" :key="k.rentalId">
                                        <td>{{ k.renterName }}</td>
                                        <td>{{ k.items }}</td>
                                        <td>{{ sizeText(k) }}</td>
                                    </tr>
                                </tbody>
                            </v-table>
                        </template>
                        <div v-if="canWeather && !s.weatherCancelledAt && s.status === 'scheduled'" class="d-flex mt-3">
                            <v-spacer></v-spacer>
                            <v-btn variant="outlined" color="warning" prepend-icon="mdi-weather-lightning-rainy"
                                :loading="cancelling === s.eventId" @click="weatherCancel(s)">Cancel for weather</v-btn>
                        </div>
                    </template>
                </v-expansion-panel-text>
            </v-expansion-panel>
        </v-expansion-panels>

        <v-dialog v-model="moveDialog" max-width="560">
            <v-card v-if="moveTarget">
                <v-card-title>Change date</v-card-title>
                <v-card-text class="pt-2">
                    <LessonChangePanel :ticket-id="moveTarget.ticketId" staff @changed="onMoved" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="moveDialog = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" timeout="5000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { LessonChangeService, type LessonSession, type LessonBooking, type LessonBike } from '@/services/LessonChangeService'
import LessonChangePanel from '@/components/LessonChangePanel.vue'
import { formatTenantDateTime } from '@/helpers/TenantTime'
import { useConfirm } from '@/composables/useConfirm'
import { branding } from '@/stores/branding'
import authHelper from '@/helpers/AuthHelper'
import { Perm } from '@/helpers/TenantPermissions'

// Staff view of lesson sessions (Script0319): bookings per session, Change date, Cancel for weather.
const svc = new LessonChangeService()
const confirm = useConfirm()
const canMove = authHelper.hasPermission(Perm.SalesCancel)
const canWeather = authHelper.hasPermission(Perm.CatalogManage)

const loading = ref(true)
const sessions = ref<LessonSession[]>([])
const openPanel = ref<string | null>(null)
const bookings = ref<Record<string, LessonBooking[]>>({})
const bookingsLoading = ref<Record<string, boolean>>({})
const bikes = ref<Record<string, LessonBike[]>>({})
// Height in inches -> 5' 4", weight in lb (the shop sizes the bike from these).
function sizeText(k: LessonBike): string {
    const h = k.riderHeightIn != null ? `${Math.floor(k.riderHeightIn / 12)}' ${k.riderHeightIn % 12}"` : null
    const w = k.riderWeightLb != null ? `${k.riderWeightLb} lb` : null
    return [h, w].filter(Boolean).join(' · ') || 'Not given'
}
const cancelling = ref<string | null>(null)
const moveDialog = ref(false)
const moveTarget = ref<LessonBooking | null>(null)
const moveSession = ref<LessonSession | null>(null)

async function loadSessions() {
    try {
        sessions.value = (await svc.sessions(21)).data.data
    } catch (e: any) {
        flash(e?.response?.data?.error || 'Could not load lessons.', 'error')
    } finally {
        loading.value = false
    }
}

onMounted(loadSessions)

async function loadBookings(eventId: string, force = false) {
    if (bookings.value[eventId] && !force) return
    bookingsLoading.value[eventId] = true
    try {
        const [b, k] = await Promise.all([svc.bookings(eventId), svc.bikes(eventId)])
        bookings.value[eventId] = b.data.data
        bikes.value[eventId] = k.data.data
    } catch (e: any) {
        flash(e?.response?.data?.error || 'Could not load bookings.', 'error')
    } finally {
        bookingsLoading.value[eventId] = false
    }
}

function openMove(b: LessonBooking, s: LessonSession) {
    moveTarget.value = b
    moveSession.value = s
    moveDialog.value = true
}

async function onMoved() {
    if (moveSession.value) await loadBookings(moveSession.value.eventId, true)
    const fresh = (await svc.sessions(21)).data.data
    sessions.value = fresh
    // Refresh any other open session's list lazily on its next open.
    for (const id of Object.keys(bookings.value)) if (id !== moveSession.value?.eventId) delete bookings.value[id]
}

async function weatherCancel(s: LessonSession) {
    const ok = await confirm({
        title: `Cancel "${s.title}" for weather?`,
        message: `The session is cancelled and the ${s.booked} rider${s.booked === 1 ? '' : 's'} booked on it are emailed a link to move to `
            + 'another date or take a full refund. Nobody is refunded until they choose.',
        confirmText: 'Cancel for weather',
        confirmColor: 'warning',
    })
    if (!ok) return
    cancelling.value = s.eventId
    try {
        const r = await svc.weatherCancel(s.eventId)
        flash(`Cancelled. ${r.data.data.emailed} of ${r.data.data.bookings} booking${r.data.data.bookings === 1 ? '' : 's'} emailed.`, 'success')
        await loadSessions()
    } catch (e: any) {
        flash(e?.response?.data?.error || 'Could not cancel the session.', 'error')
    } finally {
        cancelling.value = null
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
</script>
