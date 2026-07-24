<template>
    <v-container fluid>
        <div class="d-flex align-center mb-4 flex-wrap ga-2">
            <h1 class="text-h5">{{ isSpectator ? 'Spectator Report' : 'Rider Report' }}</h1>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-refresh" @click="load">Refresh</v-btn>
        </div>

        <!-- Tiles follow the visible rows, so the event filter narrows them too. -->
        <div class="d-flex ga-3 mb-4 flex-wrap">
            <v-card variant="tonal" class="stat-tile">
                <v-card-text>
                    <div class="text-h5">{{ filtered.length }}</div>
                    <div class="text-caption text-medium-emphasis">Registrations</div>
                </v-card-text>
            </v-card>
            <v-card variant="tonal" class="stat-tile">
                <v-card-text>
                    <div class="text-h5">{{ checkedInCount }}</div>
                    <div class="text-caption text-medium-emphasis">Checked in</div>
                </v-card-text>
            </v-card>
            <v-card variant="tonal" :color="missingWaiverCount > 0 ? 'error' : 'success'" class="stat-tile">
                <v-card-text>
                    <div class="text-h5">{{ missingWaiverCount }}</div>
                    <div class="text-caption">Missing waiver</div>
                </v-card-text>
            </v-card>
        </div>

        <div class="d-flex mb-3 ga-2 flex-wrap align-center">
            <v-text-field v-model="fromDate" type="date" label="From" density="compact" hide-details
                style="max-width: 170px" @update:model-value="load" />
            <v-text-field v-model="toDate" type="date" label="To" density="compact" hide-details
                style="max-width: 170px" @update:model-value="load" />
            <v-select v-if="eventOptions.length > 1" v-model="eventFilter" :items="eventOptions"
                density="compact" hide-details clearable label="Event" style="max-width: 260px" />
            <v-text-field v-model="search" density="compact" hide-details clearable
                :label="branding.wristbandsEnabled ? 'Search name, email, or wristband' : 'Search name or email'"
                prepend-inner-icon="mdi-magnify" style="max-width: 300px"
                @update:model-value="debouncedLoad" />
        </div>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <v-snackbar v-model="cappedToast" :timeout="6000" color="warning" location="top">
            Showing the first {{ rowCap.toLocaleString() }} rows. Narrow the date range or search to see the rest.
            <template #actions>
                <v-btn variant="text" @click="cappedToast = false">Dismiss</v-btn>
            </template>
        </v-snackbar>

        <v-card variant="outlined">
            <v-table density="compact" hover>
                <thead>
                    <tr>
                        <th>{{ isSpectator ? 'Spectator' : 'Rider' }}</th>
                        <th>Event</th>
                        <th>Item</th>
                        <th>Checked in</th>
                        <th v-if="branding.wristbandsEnabled">Wristband</th>
                        <th>Waiver</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="r in filtered" :key="r.purchaseId" class="row-click" @click="openDetail(r)">
                        <td>
                            {{ r.riderName }}
                            <div v-if="r.email" class="text-caption text-medium-emphasis">{{ r.email }}</div>
                        </td>
                        <td class="text-no-wrap">
                            {{ r.eventTitle }}
                            <span class="text-caption text-medium-emphasis">{{ formatDay(r.eventStartsAtUtc) }}</span>
                        </td>
                        <td>{{ r.itemName }}</td>
                        <td class="text-no-wrap">
                            <span v-if="r.checkedIn">{{ r.checkedInAtUtc ? formatTime(r.checkedInAtUtc) : 'Yes' }}</span>
                            <span v-else class="text-caption text-medium-emphasis">Not yet</span>
                        </td>
                        <td v-if="branding.wristbandsEnabled" class="text-no-wrap">
                            <v-chip v-if="r.wristbandCode" size="x-small" color="teal" prepend-icon="mdi-watch">
                                {{ r.wristbandCode }}
                            </v-chip>
                            <span v-else class="text-caption text-medium-emphasis"></span>
                        </td>
                        <td>
                            <v-chip size="x-small" :color="r.waiverSigned ? 'success' : 'error'">
                                {{ r.waiverSigned ? 'Signed' : 'Missing' }}
                            </v-chip>
                        </td>
                    </tr>
                    <tr v-if="!loading && !loadError && filtered.length === 0">
                        <td :colspan="branding.wristbandsEnabled ? 6 : 5"
                            class="text-center text-medium-emphasis py-6">
                            No riders in this range{{ eventFilter || search ? ' matching these filters' : '' }}.
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- ── Rider detail drill-in ─────────────────────────────────────── -->
        <v-dialog v-model="detailOpen" max-width="680">
            <v-card>
                <v-card-title class="d-flex align-center">
                    {{ detail?.riderName || detailName || 'Rider' }}
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="detailOpen = false" />
                </v-card-title>
                <v-card-text>
                    <v-alert v-if="detailError" type="error" variant="tonal" class="mb-4">{{ detailError }}</v-alert>
                    <v-skeleton-loader v-if="detailLoading" type="article" />
                    <template v-else-if="detail">
                        <div v-if="detail.email" class="text-caption text-medium-emphasis mb-3">{{ detail.email }}</div>

                        <h3 class="text-subtitle-2 mb-2">Registered for</h3>
                        <v-table density="compact" class="mb-4">
                            <tbody>
                                <tr v-for="g in detail.registrations" :key="g.purchaseId">
                                    <td class="text-no-wrap">{{ formatDay(g.eventStartsAtUtc) }}</td>
                                    <td>{{ g.eventTitle }}</td>
                                    <td>{{ g.itemName }}</td>
                                    <td class="text-no-wrap">
                                        <v-chip v-if="g.checkedIn" size="x-small" color="success">Checked in</v-chip>
                                        <v-chip v-if="branding.wristbandsEnabled && g.wristbandCode" size="x-small"
                                            color="teal" class="ml-1" prepend-icon="mdi-watch">{{ g.wristbandCode }}</v-chip>
                                    </td>
                                </tr>
                                <tr v-if="detail.registrations.length === 0">
                                    <td class="text-caption text-medium-emphasis">No registrations in the last year.</td>
                                </tr>
                            </tbody>
                        </v-table>

                        <h3 class="text-subtitle-2 mb-2">Waivers signed</h3>
                        <v-table density="compact">
                            <tbody>
                                <tr v-for="w in detail.waivers" :key="w.id">
                                    <td class="text-no-wrap">{{ formatDay(w.signedAtUtc) }}</td>
                                    <td>
                                        {{ w.waiverName }} v{{ w.waiverVersion }}
                                        <v-chip v-if="!w.waiverIsCurrent" size="x-small" color="warning" class="ml-1">Outdated</v-chip>
                                    </td>
                                    <td class="text-caption text-medium-emphasis">
                                        <span v-if="w.signedByParent">signed by {{ w.parentName || 'guardian' }}</span>
                                    </td>
                                </tr>
                                <tr v-if="detail.waivers.length === 0">
                                    <td class="text-caption text-medium-emphasis">No waivers on file.</td>
                                </tr>
                            </tbody>
                        </v-table>
                    </template>
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="detailOpen = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import { ReportsService, type RiderDetailResponse, type RiderReportItem, type RiderReportResponse } from '@/services/ReportsService'
import { branding } from '@/stores/branding'

const props = defineProps<{
    // 'rider' (default) or 'spectator': the same page serves both Admission reports.
    audience?: 'rider' | 'spectator'
}>()
const audience = props.audience ?? 'rider'
const isSpectator = audience === 'spectator'

const route = useRoute()
const service = new ReportsService()

const tz = () => branding.timezone || 'UTC'
// Default to today (tenant timezone); ?date=YYYY-MM-DD (from the Daily Events jump) overrides.
const initialDate = typeof route.query.date === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(route.query.date)
    ? route.query.date
    : dayjs().tz(tz()).format('YYYY-MM-DD')
const fromDate = ref(initialDate)
const toDate = ref(initialDate)
const search = ref('')
const eventFilter = ref<string | null>(
    typeof route.query.eventId === 'string' ? route.query.eventId : null)

const data = ref<RiderReportResponse | null>(null)
const loading = ref(false)
const loadError = ref<string | null>(null)
// Kept in sync with RiderReportCap on the server; only used for the "results capped" toast copy.
const rowCap = 10000
const cappedToast = ref(false)

const eventOptions = computed(() => {
    const seen = new Map<string, string>()
    for (const r of data.value?.rows ?? []) {
        if (!seen.has(r.eventId)) seen.set(r.eventId, `${r.eventTitle} (${formatDay(r.eventStartsAtUtc)})`)
    }
    return [...seen.entries()].map(([value, title]) => ({ value, title }))
})

const filtered = computed(() => (data.value?.rows ?? [])
    .filter(r => !eventFilter.value || r.eventId === eventFilter.value))
const checkedInCount = computed(() => filtered.value.filter(r => r.checkedIn).length)
const missingWaiverCount = computed(() => filtered.value.filter(r => !r.waiverSigned).length)

let seq = 0
async function load() {
    if (!fromDate.value || !toDate.value) return
    const s = ++seq
    loading.value = true
    loadError.value = null
    try {
        const fromUtc = dayjs.tz(fromDate.value, tz()).startOf('day').utc().toISOString()
        const toUtc = dayjs.tz(toDate.value, tz()).add(1, 'day').startOf('day').utc().toISOString()
        const res = await service.getRiders(fromUtc, toUtc, search.value || undefined, audience)
        if (s !== seq) return
        data.value = res.data.data
        if (data.value.truncated) cappedToast.value = true
        // Drop a stale event filter when the new range no longer contains that event.
        if (eventFilter.value && !data.value.rows.some(r => r.eventId === eventFilter.value)) {
            eventFilter.value = null
        }
    } catch (err: any) {
        if (s !== seq) return
        loadError.value = err.response?.data?.error
            ?? 'Could not load the rider report. Check the date range and try Refresh.'
    } finally {
        if (s === seq) loading.value = false
    }
}

let timer: ReturnType<typeof setTimeout> | null = null
function debouncedLoad() {
    if (timer) clearTimeout(timer)
    timer = setTimeout(load, 300)
}

// ── Rider drill-in ──────────────────────────────────────────────────────────
const detailOpen = ref(false)
const detail = ref<RiderDetailResponse | null>(null)
const detailName = ref('')
const detailLoading = ref(false)
const detailError = ref<string | null>(null)

async function openDetail(r: RiderReportItem) {
    if (!r.userId && !r.email) {
        // Walk-up rows with no identity have nothing more to show than the row itself.
        return
    }
    detailOpen.value = true
    detailLoading.value = true
    detailError.value = null
    detail.value = null
    detailName.value = r.riderName
    try {
        const res = await service.getRiderDetail({ userId: r.userId, email: r.email, name: r.riderName })
        detail.value = res.data.data
    } catch (err: any) {
        detailError.value = err.response?.data?.error
            ?? 'Could not load this rider\'s details. Close and try again.'
    } finally {
        detailLoading.value = false
    }
}

function formatDay(utc: string): string {
    return dayjs.utc(utc).tz(tz()).format('MMM D')
}
function formatTime(utc: string): string {
    return dayjs.utc(utc).tz(tz()).format('HH:mm')
}

onMounted(load)
</script>

<style scoped>
.stat-tile { min-width: 150px; }
.row-click { cursor: pointer; }
</style>
