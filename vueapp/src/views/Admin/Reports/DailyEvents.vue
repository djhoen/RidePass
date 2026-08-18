<template>
    <div>
        <div class="d-flex align-center mb-4 flex-wrap ga-3">
            <h2 class="text-h5">Daily Events</h2>
            <v-spacer></v-spacer>
            <v-text-field v-model="localDate" type="date" label="Date" density="compact" hide-details
                style="max-width: 180px" @change="loadReport"></v-text-field>
            <v-btn variant="text" @click="prevDay">
                <v-icon>mdi-chevron-left</v-icon>
            </v-btn>
            <v-btn variant="text" @click="nextDay">
                <v-icon>mdi-chevron-right</v-icon>
            </v-btn>
            <v-btn color="primary" :loading="loading" @click="loadReport">Refresh</v-btn>
            <v-btn variant="outlined" prepend-icon="mdi-file-delimited-outline"
                :disabled="loading || !report" @click="exportCsv">Export CSV</v-btn>
        </div>

        <p class="text-caption text-medium-emphasis mb-3">
            All events on <strong>{{ formatLong(localDate) }}</strong>
            in your tenant timezone (<strong>{{ tz }}</strong>).
        </p>

        <v-card>
            <v-table density="comfortable">
                <thead>
                    <tr>
                        <th style="width: 110px">Time</th>
                        <th>Event</th>
                        <th style="width: 130px">Type</th>
                        <th style="width: 100px" class="text-right">Registered</th>
                        <th style="width: 100px" class="text-right">Checked in</th>
                        <th style="width: 110px" class="text-right">Revenue</th>
                        <th style="width: 100px">Status</th>
                        <th style="width: 110px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="r in report?.rows ?? []" :key="r.eventId">
                        <td>
                            <span v-if="r.allDay">All day</span>
                            <span v-else>{{ formatTime(r.startsAtUtc) }}</span>
                        </td>
                        <td><strong>{{ r.title }}</strong></td>
                        <td>
                            <v-chip v-if="r.eventTypeName" size="x-small">{{ r.eventTypeName }}</v-chip>
                        </td>
                        <td class="text-right">
                            {{ r.registered }}<span v-if="r.capacity" class="text-medium-emphasis"> / {{ r.capacity }}</span>
                        </td>
                        <td class="text-right">{{ r.checkedIn }}</td>
                        <td class="text-right">${{ (r.revenueCents / 100).toFixed(2) }}</td>
                        <td>
                            <v-chip size="x-small" :color="r.status === 'scheduled' ? 'success' : 'grey'">
                                {{ r.status }}
                            </v-chip>
                        </td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" prepend-icon="mdi-account-group"
                                @click="emit('selectEvent', r.eventId, localDate)">
                                Riders
                            </v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && (report?.rows.length ?? 0) === 0">
                        <td colspan="8" class="text-center text-medium-emphasis py-8">
                            No events scheduled on this date.
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-snackbar v-model="snackbar" color="error" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { ReportsService, type DailyEventReport } from '@/services/ReportsService'
import { branding } from '@/stores/branding'
import { downloadCsv, csvMoney } from '@/helpers/csv'
import { formatTenantDateTime } from '@/helpers/TenantTime'

const emit = defineEmits<{ (e: 'selectEvent', eventId: string, date: string): void }>()

const reportsService = new ReportsService()

const tz = computed(() => branding.timezone || 'UTC')
const localDate = ref(dayjs().tz(tz.value).format('YYYY-MM-DD'))
const report = ref<DailyEventReport | null>(null)
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')

function prevDay() {
    localDate.value = dayjs(localDate.value).subtract(1, 'day').format('YYYY-MM-DD')
    loadReport()
}
function nextDay() {
    localDate.value = dayjs(localDate.value).add(1, 'day').format('YYYY-MM-DD')
    loadReport()
}

function formatLong(d: string): string {
    return dayjs(d).format('dddd, MMM D, YYYY')
}
function formatTime(iso: string): string {
    return dayjs.utc(iso).tz(tz.value).format('h:mm A')
}

async function loadReport() {
    if (!localDate.value) return
    // Tapping prev/next day quickly can leave two loads in flight; only the latest applies its
    // result so an older day's slower response can't display under the current date.
    const seq = ++reportSeq
    loading.value = true
    try {
        // Compute the local-day window in tenant tz, convert to UTC for the API.
        const start = dayjs.tz(localDate.value + 'T00:00', tz.value)
        const fromUtc = start.utc().toISOString()
        const toUtc = start.add(1, 'day').utc().toISOString()
        const r = await reportsService.getDailyEvents(fromUtc, toUtc, localDate.value)
        if (seq !== reportSeq) return
        report.value = (r.data as any).data
    } catch (err: any) {
        if (seq !== reportSeq) return
        flash(err.response?.data?.error || 'Failed to load report.')
    } finally {
        if (seq === reportSeq) loading.value = false
    }
}
let reportSeq = 0

function flash(text: string) {
    snackbarText.value = text
    snackbar.value = true
}

onMounted(loadReport)

function exportCsv() {
    const r = report.value
    if (!r) return
    downloadCsv(
        `daily-events-${localDate.value}.csv`,
        ['Event', 'Type', 'Starts', 'Ends', 'Status', 'Capacity', 'Registered', 'Checked in', 'Revenue'],
        r.rows.map(x => [
            x.title, x.eventTypeName,
            x.allDay ? 'All day' : formatTenantDateTime(x.startsAtUtc),
            x.allDay ? '' : formatTenantDateTime(x.endsAtUtc),
            x.status, x.capacity, x.registered, x.checkedIn, csvMoney(x.revenueCents),
        ]),
    )
}
</script>
