<template>
    <v-container fluid>
        <div class="d-flex align-center mb-4 flex-wrap ga-2">
            <h1 class="text-h5">{{ isSpectator ? 'Spectator Report' : 'Rider Report' }}</h1>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-refresh" :loading="loading" @click="load">Refresh</v-btn>
        </div>

        <!-- Tiles follow the visible rows, so the event filter narrows them too. While the first
             load is in flight they show a dash: a hard 0 reads as "nobody registered". -->
        <div class="d-flex ga-3 mb-4 flex-wrap">
            <v-card variant="tonal" class="stat-tile">
                <v-card-text>
                    <div class="text-h5">{{ initialLoading ? '-' : filtered.length }}</div>
                    <div class="text-caption text-medium-emphasis">Registrations</div>
                </v-card-text>
            </v-card>
            <v-card variant="tonal" class="stat-tile">
                <v-card-text>
                    <div class="text-h5">{{ initialLoading ? '-' : checkedInCount }}</div>
                    <div class="text-caption text-medium-emphasis">Checked in</div>
                </v-card-text>
            </v-card>
            <v-card variant="tonal" :color="initialLoading ? undefined : (missingWaiverCount > 0 ? 'error' : 'success')"
                class="stat-tile">
                <v-card-text>
                    <div class="text-h5">{{ initialLoading ? '-' : missingWaiverCount }}</div>
                    <div class="text-caption">Missing waiver</div>
                </v-card-text>
            </v-card>
        </div>

        <!-- Server-side filters (they narrow the query, so they reach rows a capped report hid). -->
        <div class="d-flex mb-2 ga-2 flex-wrap align-center">
            <v-text-field v-model="fromDate" type="date" label="From" density="compact" hide-details
                style="max-width: 170px" @update:model-value="load" />
            <v-text-field v-model="toDate" type="date" label="To" density="compact" hide-details
                style="max-width: 170px" @update:model-value="load" />
            <v-select v-model="purchaseTypeFilter" :items="purchaseTypeOptions" label="Purchase type"
                density="compact" hide-details multiple chips closable-chips clearable
                style="min-width: 240px; max-width: 340px" @update:model-value="load" />
            <v-select v-model="eventTypeFilter" :items="eventTypeOptions" label="Event type"
                density="compact" hide-details multiple chips closable-chips clearable
                style="min-width: 200px; max-width: 300px" @update:model-value="load" />
            <v-text-field v-model="search" density="compact" hide-details clearable
                :label="branding.wristbandsEnabled ? 'Search name, email, or wristband' : 'Search name or email'"
                prepend-inner-icon="mdi-magnify" style="max-width: 300px"
                @update:model-value="debouncedLoad" />
        </div>

        <!-- Quick filters over the rows already loaded: no round trip, instant. -->
        <div class="d-flex mb-3 ga-2 flex-wrap align-center">
            <v-select v-if="eventOptions.length > 1" v-model="eventFilter" :items="eventOptions"
                density="compact" hide-details clearable label="Event" style="max-width: 260px" />
            <v-select v-model="checkInFilter" :items="checkInOptions" label="Check-in"
                density="compact" hide-details style="max-width: 170px" />
            <v-select v-model="waiverFilter" :items="waiverOptions" label="Waiver"
                density="compact" hide-details style="max-width: 170px" />
            <v-chip :color="incompleteOnly ? 'warning' : undefined" :variant="incompleteOnly ? 'flat' : 'outlined'"
                size="small" @click="incompleteOnly = !incompleteOnly">
                <v-tooltip activator="parent" location="top">
                    Paid, but rider details or waiver were never completed
                </v-tooltip>
                Incomplete registration
            </v-chip>
            <v-chip :color="minorsOnly ? 'warning' : undefined" :variant="minorsOnly ? 'flat' : 'outlined'"
                size="small" @click="minorsOnly = !minorsOnly">
                <v-tooltip activator="parent" location="top">
                    Under 18 on the day of the event, by the birthdate on the entry
                </v-tooltip>
                Minors
            </v-chip>
            <v-btn v-if="anyQuickFilter" variant="text" size="small" @click="clearQuickFilters">Clear</v-btn>
        </div>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <v-snackbar :model-value="!!filterError" :timeout="8000" color="warning" location="top"
            @update:model-value="filterError = null">
            {{ filterError }}
            <template #actions>
                <v-btn variant="text" @click="filterError = null">Dismiss</v-btn>
            </template>
        </v-snackbar>

        <v-snackbar v-model="cappedToast" :timeout="6000" color="warning" location="top">
            Showing the first {{ rowCap.toLocaleString() }} rows. Narrow the date range or search to see the rest.
            <template #actions>
                <v-btn variant="text" @click="cappedToast = false">Dismiss</v-btn>
            </template>
        </v-snackbar>

        <!-- :loading paints the indeterminate bar across the top of the card, so a refresh or a
             filter change is visible without blanking the rows already on screen. -->
        <v-card variant="outlined" :loading="loading">
            <v-table density="compact" hover>
                <thead>
                    <tr>
                        <th>{{ isSpectator ? 'Spectator' : 'Rider' }}</th>
                        <th>Event</th>
                        <th>Type</th>
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
                            <v-chip v-if="isMinor(r)" size="x-small" color="warning" variant="tonal" class="ml-1">
                                {{ r.ageAtEvent }}
                            </v-chip>
                            <div v-if="r.email" class="text-caption text-medium-emphasis">{{ r.email }}</div>
                        </td>
                        <td class="text-no-wrap">
                            <span v-if="r.eventTitle">{{ r.eventTitle }}</span>
                            <span v-else class="text-medium-emphasis font-italic">Walk-up</span>
                            <span class="text-caption text-medium-emphasis">{{ formatDay(r.eventStartsAtUtc) }}</span>
                            <div v-if="r.eventTypeName" class="text-caption text-medium-emphasis">{{ r.eventTypeName }}</div>
                        </td>
                        <td class="text-no-wrap">
                            <v-chip size="x-small" variant="tonal" :color="purchaseTypeColor(r.purchaseType)">
                                {{ purchaseTypeLabel(r.purchaseType) }}
                            </v-chip>
                        </td>
                        <td>
                            {{ r.itemName }}
                            <v-chip v-if="!r.registrationComplete" size="x-small" color="warning" variant="tonal"
                                class="ml-1">
                                <v-tooltip activator="parent" location="top">
                                    Paid, but rider details or waiver were never completed
                                </v-tooltip>
                                Incomplete
                            </v-chip>
                        </td>
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
                    <!-- First load: placeholder rows, so the table reads as "loading" rather than
                         as an empty result until the response lands. -->
                    <template v-if="initialLoading">
                        <tr v-for="n in 6" :key="'sk' + n">
                            <td v-for="c in (branding.wristbandsEnabled ? 7 : 6)" :key="'skc' + c">
                                <v-skeleton-loader type="text" />
                            </td>
                        </tr>
                    </template>
                    <tr v-if="!loading && !loadError && filtered.length === 0">
                        <td :colspan="branding.wristbandsEnabled ? 7 : 6"
                            class="text-center text-medium-emphasis py-6">
                            No riders in this range{{ eventFilter || search ? ' matching these filters' : '' }}.
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- ── Rider detail drill-in ─────────────────────────────────────── -->
        <v-dialog v-model="detailOpen" max-width="760">
            <v-card class="d-flex flex-column" style="max-height: 90vh;">
                <v-card-title class="d-flex align-center">
                    {{ detail?.riderName || detailName || 'Rider' }}
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="detailOpen = false" />
                </v-card-title>
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0;">
                    <v-alert v-if="detailError" type="error" variant="tonal" class="mb-4">{{ detailError }}</v-alert>
                    <v-skeleton-loader v-if="detailLoading" type="article" />
                    <template v-else-if="detail">
                        <div class="d-flex flex-wrap ga-2 align-center mb-3">
                            <v-chip v-if="detail.profile?.isGuest" size="small" variant="tonal">
                                <v-tooltip activator="parent" location="top">
                                    Bought as a guest, so there is no account profile. Details come from their entries.
                                </v-tooltip>
                                Guest (no account)
                            </v-chip>
                            <v-chip v-if="detailIsMinor" size="small" color="warning">
                                Minor, age {{ detail.profile?.age }}
                            </v-chip>
                            <v-spacer />
                            <v-btn v-if="detail.profile?.userId" variant="tonal" size="small"
                                prepend-icon="mdi-account-details"
                                :to="`/Admin/Customers/${detail.profile.userId}`">
                                Full customer profile
                            </v-btn>
                        </div>

                        <v-row dense class="mb-2">
                            <v-col cols="12" sm="6">
                                <div v-for="f in contactFields" :key="f.label" class="detail-line">
                                    <span class="detail-label">{{ f.label }}</span>
                                    <span>{{ f.value }}</span>
                                </div>
                            </v-col>
                            <v-col cols="12" sm="6">
                                <div v-for="f in activityFields" :key="f.label" class="detail-line">
                                    <span class="detail-label">{{ f.label }}</span>
                                    <span>{{ f.value }}</span>
                                </div>
                            </v-col>
                        </v-row>

                        <v-alert v-if="detail.profile?.emergencyContactName || detail.profile?.emergencyContactPhone"
                            type="info" variant="tonal" density="compact" class="mb-4">
                            <strong>Emergency contact:</strong>
                            {{ detail.profile?.emergencyContactName || 'Not named' }}
                            <span v-if="detail.profile?.emergencyContactPhone">
                                ({{ detail.profile.emergencyContactPhone }})
                            </span>
                        </v-alert>

                        <h3 class="text-subtitle-2 mb-2">Registered for</h3>
                        <v-table density="compact" class="mb-4">
                            <tbody>
                                <tr v-for="g in detail.registrations" :key="g.purchaseId">
                                    <td class="text-no-wrap">{{ formatDay(g.eventStartsAtUtc) }}</td>
                                    <td>
                                        <span v-if="g.eventTitle">{{ g.eventTitle }}</span>
                                        <span v-else class="text-medium-emphasis font-italic">Walk-up</span>
                                        <div v-if="g.eventTypeName" class="text-caption text-medium-emphasis">
                                            {{ g.eventTypeName }}
                                        </div>
                                    </td>
                                    <td>
                                        {{ g.itemName }}
                                        <div class="text-caption text-medium-emphasis">
                                            {{ purchaseTypeLabel(g.purchaseType) }}
                                        </div>
                                    </td>
                                    <td class="text-no-wrap">
                                        <v-chip v-if="g.checkedIn" size="x-small" color="success">Checked in</v-chip>
                                        <v-chip v-if="!g.registrationComplete" size="x-small" color="warning"
                                            variant="tonal" class="ml-1">Incomplete</v-chip>
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
                                <template v-for="w in detail.waivers" :key="w.id">
                                    <tr>
                                        <td class="text-no-wrap">{{ formatDay(w.signedAtUtc) }}</td>
                                        <td>
                                            {{ w.waiverName }} v{{ w.waiverVersion }}
                                            <v-chip v-if="!w.waiverIsCurrent" size="x-small" color="warning" class="ml-1">Outdated</v-chip>
                                            <div v-if="w.signerName" class="text-caption text-medium-emphasis">
                                                signed by {{ w.signerName }}
                                            </div>
                                        </td>
                                        <td class="text-caption text-medium-emphasis">
                                            <span v-if="w.signedByParent">guardian: {{ w.parentName || 'unnamed' }}</span>
                                        </td>
                                        <td class="text-no-wrap">
                                            <v-btn v-if="w.hasSignatureImage" variant="text" size="small"
                                                :loading="signatureLoadingId === w.id"
                                                :prepend-icon="openSignatureId === w.id ? 'mdi-chevron-up' : 'mdi-draw'"
                                                @click="toggleSignature(w.id)">
                                                {{ openSignatureId === w.id ? 'Hide' : 'Signature' }}
                                            </v-btn>
                                            <span v-else class="text-caption text-medium-emphasis">No image</span>
                                        </td>
                                    </tr>
                                    <tr v-if="openSignatureId === w.id">
                                        <td colspan="4">
                                            <v-alert v-if="signatureError" type="error" variant="tonal" density="compact">
                                                {{ signatureError }}
                                            </v-alert>
                                            <div v-else-if="signatureImages[w.id]" class="sig-frame">
                                                <img :src="signatureImages[w.id]" alt="Waiver signature" class="sig-img" />
                                            </div>
                                        </td>
                                    </tr>
                                </template>
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
import {
    ReportsService, RIDER_PURCHASE_TYPE_LABELS,
    type RiderDetailResponse, type RiderPurchaseType, type RiderReportItem, type RiderReportResponse,
} from '@/services/ReportsService'
import { EventTypeService, type EventType } from '@/services/EventTypeService'
import { branding } from '@/stores/branding'

const props = defineProps<{
    // 'rider' (default) or 'spectator': the same page serves both Admission reports.
    audience?: 'rider' | 'spectator'
}>()
const audience = props.audience ?? 'rider'
const isSpectator = audience === 'spectator'

const route = useRoute()
const service = new ReportsService()
const eventTypeService = new EventTypeService()

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

// Server-side filters: they change the query, so a change reloads.
const purchaseTypeFilter = ref<RiderPurchaseType[]>([])
const eventTypeFilter = ref<string[]>([])
// Client-side quick filters over the rows already on screen.
const checkInFilter = ref<'all' | 'in' | 'out'>('all')
const waiverFilter = ref<'all' | 'signed' | 'missing'>('all')
const incompleteOnly = ref(false)
const minorsOnly = ref(false)

const checkInOptions = [
    { value: 'all', title: 'All' },
    { value: 'in', title: 'Checked in' },
    { value: 'out', title: 'Not yet' },
]
const waiverOptions = [
    { value: 'all', title: 'All' },
    { value: 'signed', title: 'Signed' },
    { value: 'missing', title: 'Missing' },
]
// The spectator report only ever contains spectator passes, so offering rider buckets there
// would be a list of guaranteed-empty filters.
const purchaseTypeOptions = computed(() =>
    (isSpectator
        ? (['spectator_pass'] as RiderPurchaseType[])
        : (['day_ticket', 'race_entry', 'season_pass_unlimited', 'season_pass_credits', 'season_pass_days'] as RiderPurchaseType[])
    ).map(v => ({ value: v, title: RIDER_PURCHASE_TYPE_LABELS[v] })))

// Event types come from the tenant's own list (Highland renamed lesson -> "Clinic"), not from
// whatever happens to be in the current result set, so you can filter TO a type that is absent.
const eventTypes = ref<EventType[]>([])
const eventTypeOptions = computed(() =>
    eventTypes.value.map(t => ({ value: t.code, title: t.name })))

const anyQuickFilter = computed(() =>
    checkInFilter.value !== 'all' || waiverFilter.value !== 'all'
    || incompleteOnly.value || minorsOnly.value || !!eventFilter.value)

function clearQuickFilters() {
    checkInFilter.value = 'all'
    waiverFilter.value = 'all'
    incompleteOnly.value = false
    minorsOnly.value = false
    eventFilter.value = null
}

const isMinor = (r: RiderReportItem) => r.ageAtEvent !== null && r.ageAtEvent < 18
const purchaseTypeLabel = (t: RiderPurchaseType) => RIDER_PURCHASE_TYPE_LABELS[t] ?? t
function purchaseTypeColor(t: RiderPurchaseType) {
    if (t === 'race_entry') return 'deep-purple'
    if (t === 'spectator_pass') return 'blue-grey'
    return t.startsWith('season_pass') ? 'teal' : undefined
}

const data = ref<RiderReportResponse | null>(null)
const loading = ref(false)
const loadError = ref<string | null>(null)
// Failure of the secondary event-type load. Kept separate from loadError so it can't mask (or be
// masked by) the report's own error.
const filterError = ref<string | null>(null)
// First load of the page (nothing to show yet) vs a refresh over rows already on screen: the
// former gets skeleton rows, the latter just the progress bar so the table doesn't flash empty.
const initialLoading = computed(() => loading.value && data.value === null)
// Kept in sync with RiderReportCap on the server; only used for the "results capped" toast copy.
const rowCap = 10000
const cappedToast = ref(false)

const eventOptions = computed(() => {
    const seen = new Map<string, string>()
    for (const r of data.value?.rows ?? []) {
        // Walk-up admissions carry no event, so they contribute no option.
        if (!r.eventId) continue
        if (!seen.has(r.eventId)) {
            seen.set(r.eventId, `${r.eventTitle ?? 'Untitled'} (${formatDay(r.eventStartsAtUtc)})`)
        }
    }
    return [...seen.entries()].map(([value, title]) => ({ value, title }))
})

const filtered = computed(() => (data.value?.rows ?? []).filter(r =>
    (!eventFilter.value || r.eventId === eventFilter.value)
    && (checkInFilter.value === 'all' || (checkInFilter.value === 'in' ? r.checkedIn : !r.checkedIn))
    && (waiverFilter.value === 'all' || (waiverFilter.value === 'signed' ? r.waiverSigned : !r.waiverSigned))
    && (!incompleteOnly.value || !r.registrationComplete)
    && (!minorsOnly.value || isMinor(r))))
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
        const res = await service.getRiders(fromUtc, toUtc, search.value || undefined, audience, {
            purchaseTypes: purchaseTypeFilter.value,
            eventTypeCodes: eventTypeFilter.value,
        })
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

// Signature images are fetched one at a time, only when opened, and cached for the dialog's life.
const openSignatureId = ref<string | null>(null)
const signatureLoadingId = ref<string | null>(null)
const signatureImages = ref<Record<string, string>>({})
const signatureError = ref<string | null>(null)

const detailIsMinor = computed(() => {
    const age = detail.value?.profile?.age
    return age !== null && age !== undefined && age < 18
})

// Two balanced columns of "label: value", each dropping the fields this rider has no data for
// rather than rendering a column of blanks.
type DetailField = { label: string; value: string }
const contactFields = computed<DetailField[]>(() => {
    const p = detail.value?.profile
    const out: DetailField[] = []
    const email = p?.email ?? detail.value?.email
    if (email) out.push({ label: 'Email', value: email })
    if (p?.phone) out.push({ label: 'Phone', value: p.phone })
    if (p?.hometown) out.push({ label: 'Hometown', value: p.hometown })
    if (p?.age !== null && p?.age !== undefined) {
        out.push({ label: 'Age', value: p.birthdateUtc ? `${p.age} (${formatDate(p.birthdateUtc)})` : String(p.age) })
    }
    if (p?.raceNumber) out.push({ label: 'Race number', value: p.raceNumber })
    if (p?.bike) out.push({ label: 'Bike', value: p.bike })
    if (p?.parentGuardianName) out.push({ label: 'Parent / guardian', value: p.parentGuardianName })
    return out
})
const activityFields = computed<DetailField[]>(() => {
    const p = detail.value?.profile
    if (!p) return []
    const out: DetailField[] = [
        { label: 'Entries', value: String(p.totalRegistrations) },
        { label: 'Attended', value: String(p.totalCheckedIn) },
        { label: 'Spent', value: `$${(p.totalSpentCents / 100).toFixed(2)}` },
    ]
    if (p.firstVisitUtc) out.push({ label: 'First seen', value: formatDate(p.firstVisitUtc) })
    if (p.lastVisitUtc) out.push({ label: 'Last seen', value: formatDate(p.lastVisitUtc) })
    if (p.memberSinceUtc) out.push({ label: 'Account since', value: formatDate(p.memberSinceUtc) })
    return out
})

async function toggleSignature(waiverId: string) {
    signatureError.value = null
    if (openSignatureId.value === waiverId) {
        openSignatureId.value = null
        return
    }
    // Already fetched once in this dialog: just reopen it.
    if (signatureImages.value[waiverId]) {
        openSignatureId.value = waiverId
        return
    }
    signatureLoadingId.value = waiverId
    try {
        const res = await service.getRiderWaiverSignature(waiverId)
        signatureImages.value[waiverId] = res.data.data.signatureDataUrl
        openSignatureId.value = waiverId
    } catch (err: any) {
        openSignatureId.value = waiverId
        signatureError.value = err.response?.data?.error
            ?? 'Could not load the signature image for this waiver. Try again, or open the full customer profile.'
    } finally {
        signatureLoadingId.value = null
    }
}

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
    openSignatureId.value = null
    signatureError.value = null
    signatureImages.value = {}
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
// Profile dates span seasons, so unlike the in-range row dates these carry the year.
function formatDate(utc: string): string {
    return dayjs.utc(utc).tz(tz()).format('MMM D, YYYY')
}

onMounted(async () => {
    // Kick the report off first; the event-type list only feeds a filter dropdown, so it must
    // never delay (or block) the rows.
    load()
    try {
        eventTypes.value = (await eventTypeService.list()).data.data
    } catch (err: any) {
        // Non-fatal: the report still works, the Event type filter just has nothing to offer.
        filterError.value = err.response?.data?.error
            ?? 'Could not load the event-type list, so that filter is empty. The report itself is unaffected; use Refresh to retry.'
    }
})
</script>

<style scoped>
.stat-tile { min-width: 150px; }
.row-click { cursor: pointer; }

/* Drill-in profile: label/value pairs on one line, label fixed so the values line up. */
.detail-line {
    display: flex;
    gap: 8px;
    font-size: 0.875rem;
    padding: 2px 0;
}
.detail-label {
    flex: 0 0 108px;
    color: rgba(var(--v-theme-on-surface), 0.6);
}

/* Signatures are drawn dark-on-transparent, so they need a light plate to stay legible in the
   dark theme. Height-capped: some are drawn on a very wide canvas. */
.sig-frame {
    background: #fff;
    border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
    border-radius: 4px;
    padding: 8px;
    display: inline-block;
    max-width: 100%;
    overflow-x: auto;
}
.sig-img {
    display: block;
    max-height: 160px;
    max-width: 100%;
}
</style>
