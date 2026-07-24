<template>
    <v-container fluid>
        <div class="d-flex align-center mb-4 flex-wrap ga-2">
            <h1 class="text-h5">Compliance Today</h1>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-refresh" @click="load">Refresh</v-btn>
        </div>

        <div class="d-flex ga-3 mb-4 flex-wrap">
            <v-card variant="tonal" class="stat-tile">
                <v-card-text>
                    <div class="text-h5">{{ data?.totalOnSite ?? 0 }}</div>
                    <div class="text-caption text-medium-emphasis">Expected today</div>
                </v-card-text>
            </v-card>
            <v-card variant="tonal" class="stat-tile">
                <v-card-text>
                    <div class="text-h5">{{ checkedInCount }}</div>
                    <div class="text-caption text-medium-emphasis">Checked in</div>
                </v-card-text>
            </v-card>
            <v-card variant="tonal" :color="(data?.missingCount ?? 0) > 0 ? 'error' : 'success'" class="stat-tile">
                <v-card-text>
                    <div class="text-h5">{{ data?.missingCount ?? 0 }}</div>
                    <div class="text-caption">Missing a current waiver</div>
                </v-card-text>
            </v-card>
        </div>

        <div class="d-flex mb-3 ga-2 flex-wrap align-center">
            <v-select v-model="sourceFilter" :items="sourceOptions" density="compact" hide-details
                clearable label="Source" style="max-width: 190px" />
            <v-checkbox v-model="missingOnly" density="compact" hide-details label="Missing only" />
        </div>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <v-card variant="outlined">
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Email</th>
                        <th>Source</th>
                        <th>Waiver signed</th>
                        <th>Checked in</th>
                        <th class="text-right">Action</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="(r, i) in filtered" :key="i">
                        <td>{{ r.personName }}</td>
                        <td>{{ r.email || '' }}</td>
                        <td>
                            <v-chip size="x-small" :color="sourceColor(r.source)">{{ sourceLabel(r.source) }}</v-chip>
                            <span class="text-caption text-medium-emphasis ml-1">{{ r.label }}</span>
                        </td>
                        <td class="text-no-wrap">
                            <template v-if="r.waiverStatus === 'signed'">
                                <v-chip size="x-small" color="success">Signed</v-chip>
                                <span v-if="r.signedAtUtc" class="text-caption text-medium-emphasis ml-1">
                                    {{ formatSigned(r.signedAtUtc) }}
                                </span>
                            </template>
                            <v-chip v-else size="x-small" color="error">Missing</v-chip>
                        </td>
                        <td class="text-no-wrap">
                            <span v-if="r.checkedInAtUtc">{{ formatWhen(r.checkedInAtUtc) }}</span>
                            <span v-else class="text-caption text-medium-emphasis">Not yet</span>
                        </td>
                        <td class="text-right">
                            <v-tooltip v-if="r.waiverStatus === 'missing' && r.email" location="top">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" size="small" variant="text" color="primary"
                                        :loading="sendingEmail === r.email"
                                        prepend-icon="mdi-email-arrow-right-outline"
                                        @click="sendLink(r)">Send link</v-btn>
                                </template>
                                Email {{ r.email }} a link to sign the waiver right now
                            </v-tooltip>
                            <span v-else-if="r.waiverStatus === 'missing'" class="text-caption text-medium-emphasis">
                                No email on file
                            </span>
                        </td>
                    </tr>
                    <tr v-if="!loading && !loadError && filtered.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-6">
                            {{ (data?.items?.length ?? 0) === 0 ? 'Nobody is expected today: no tickets, reservations, rentals, or lessons for events running today.' : 'Nothing matches these filters.' }}
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-snackbar v-model="snack" :timeout="4000" color="success">{{ snackText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import { WaiverService, type WaiverComplianceItem, type WaiverComplianceResponse } from '@/services/WaiverService'
import { branding } from '@/stores/branding'

const service = new WaiverService()
const data = ref<WaiverComplianceResponse | null>(null)
const loading = ref(false)
const loadError = ref<string | null>(null)
const sourceFilter = ref<string | null>(null)
const missingOnly = ref(false)
const sendingEmail = ref<string | null>(null)
const snack = ref(false)
const snackText = ref('')

const sourceOptions = [
    { title: 'Event tickets', value: 'ticket' },
    { title: 'Season passes', value: 'pass' },
    { title: 'Rentals', value: 'rental' },
    { title: 'Lessons', value: 'lesson' },
]

const checkedInCount = computed(() =>
    (data.value?.items ?? []).filter(r => r.checkedInAtUtc).length)

const filtered = computed(() => (data.value?.items ?? []).filter(r =>
    (!sourceFilter.value || r.source === sourceFilter.value)
    && (!missingOnly.value || r.waiverStatus === 'missing')))

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const res = await service.complianceToday()
        data.value = res.data.data
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            ?? 'Could not load today\'s compliance list. Check your connection and try Refresh.'
    } finally {
        loading.value = false
    }
}

async function sendLink(r: WaiverComplianceItem) {
    if (!r.email) return
    sendingEmail.value = r.email
    try {
        await service.createSignRequest({ email: r.email, name: r.personName })
        snackText.value = `Signing link sent to ${r.email}`
        snack.value = true
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            ?? `Could not send the signing link to ${r.email}. Check the address and the email settings, then try again.`
    } finally {
        sendingEmail.value = null
    }
}

function sourceLabel(s: string): string {
    return s === 'ticket' ? 'Ticket' : s === 'pass' ? 'Pass' : s === 'rental' ? 'Rental' : 'Lesson'
}
function sourceColor(s: string): string {
    return s === 'ticket' ? 'primary' : s === 'pass' ? 'purple' : s === 'rental' ? 'teal' : 'indigo'
}
// Same-day check-ins show time only; earlier ones (a mid-week camp check-in still
// on site today) carry the date. Signatures can be months old, so they always
// carry the date. Everything in the tenant's timezone.
function formatWhen(utc: string): string {
    const t = dayjs.utc(utc).tz(branding.timezone || 'UTC')
    const today = dayjs().tz(branding.timezone || 'UTC')
    return t.isSame(today, 'day') ? t.format('HH:mm') : t.format('MMM D HH:mm')
}
function formatSigned(utc: string): string {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('YYYY-MM-DD HH:mm')
}

onMounted(load)
</script>

<style scoped>
.stat-tile { min-width: 160px; }
</style>
