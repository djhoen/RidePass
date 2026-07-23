<template>
    <v-container fluid>
        <div class="d-flex align-center mb-4 flex-wrap ga-2">
            <h1 class="text-h5">Signature Requests</h1>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-refresh" @click="load">Refresh</v-btn>
            <v-btn color="primary" variant="tonal" prepend-icon="mdi-account-multiple-plus"
                @click="openBulk">Event roster</v-btn>
            <v-btn color="primary" prepend-icon="mdi-email-arrow-right-outline"
                @click="openNew">New request</v-btn>
        </div>

        <div class="d-flex mb-3 ga-2 flex-wrap align-center">
            <v-text-field v-model="search" density="compact" hide-details clearable
                label="Search recipient" prepend-inner-icon="mdi-magnify"
                style="max-width: 300px" @update:model-value="debouncedLoad" />
            <v-select v-model="status" :items="statusOptions" density="compact" hide-details
                clearable label="Status" style="max-width: 170px" @update:model-value="resetPage" />
        </div>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <v-card variant="outlined">
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Recipient</th>
                        <th>Waiver</th>
                        <th>Origin</th>
                        <th>Status</th>
                        <th>Created</th>
                        <th class="text-right">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="r in items" :key="r.id">
                        <td>
                            {{ r.recipientName || '' }}
                            <div class="text-caption text-medium-emphasis">{{ r.recipientEmail }}</div>
                        </td>
                        <td>{{ r.waiverName ? `${r.waiverName} v${r.waiverVersion}` : 'Active default' }}</td>
                        <td>{{ r.eventTitle || 'Manual' }}</td>
                        <td>
                            <v-tooltip location="top">
                                <template #activator="{ props }">
                                    <v-chip v-bind="props" size="x-small" :color="statusColor(r.status)">
                                        {{ statusLabel(r.status) }}
                                    </v-chip>
                                </template>
                                <div v-if="r.sentAtUtc">Sent {{ formatWhen(r.sentAtUtc) }}</div>
                                <div v-if="r.openedAtUtc">Opened {{ formatWhen(r.openedAtUtc) }}</div>
                                <div v-if="r.signedAtUtc">Signed {{ formatWhen(r.signedAtUtc) }}</div>
                                <div v-if="!r.sentAtUtc && !r.openedAtUtc && !r.signedAtUtc">Not sent yet</div>
                            </v-tooltip>
                        </td>
                        <td class="text-no-wrap">{{ formatWhen(r.createdAtUtc) }}</td>
                        <td class="text-right text-no-wrap">
                            <v-tooltip location="top">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" icon="mdi-link-variant" size="x-small" variant="text"
                                        @click="copyLink(r)" />
                                </template>
                                Copy signing link
                            </v-tooltip>
                            <v-tooltip v-if="r.status !== 'signed' && r.status !== 'cancelled'" location="top">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" icon="mdi-email-sync-outline" size="x-small" variant="text"
                                        :loading="busyId === r.id" @click="resend(r)" />
                                </template>
                                Resend the email
                            </v-tooltip>
                            <v-tooltip v-if="r.status !== 'signed' && r.status !== 'cancelled'" location="top">
                                <template #activator="{ props }">
                                    <v-btn v-bind="props" icon="mdi-close-circle-outline" size="x-small" variant="text"
                                        color="error" :loading="busyId === r.id" @click="cancel(r)" />
                                </template>
                                Cancel this request
                            </v-tooltip>
                        </td>
                    </tr>
                    <tr v-if="!loading && !loadError && items.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-6">
                            No signature requests yet. Send one with New request, or blanket an event with Event roster.
                        </td>
                    </tr>
                </tbody>
            </v-table>
            <div class="d-flex align-center pa-2 flex-wrap ga-2">
                <span class="text-caption text-medium-emphasis">{{ total }} requests</span>
                <v-spacer />
                <v-pagination v-model="page" :length="pages" :total-visible="7" density="compact"
                    @update:model-value="load" />
            </div>
        </v-card>

        <!-- New single request -->
        <v-dialog v-model="newOpen" max-width="480">
            <v-card>
                <v-card-title class="d-flex align-center">
                    New Signature Request
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="newOpen = false" />
                </v-card-title>
                <v-card-text>
                    <v-alert v-if="newError" type="error" variant="tonal" class="mb-4">{{ newError }}</v-alert>
                    <v-text-field v-model="newEmail" label="Recipient email" density="compact" type="email" />
                    <v-text-field v-model="newName" label="Recipient name (optional)" density="compact" class="mt-4" />
                    <p class="text-caption text-medium-emphasis mt-2">
                        They'll get an email with a link to sign the active waiver. If email isn't
                        configured on this server, the request is still created and you can copy
                        the link from the list.
                    </p>
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="newOpen = false">Close</v-btn>
                    <v-btn color="primary" :loading="newSaving" :disabled="!newEmail.includes('@')"
                        @click="createRequest">Send</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Bulk roster request -->
        <v-dialog v-model="bulkOpen" max-width="520">
            <v-card>
                <v-card-title class="d-flex align-center">
                    Request From an Event Roster
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="bulkOpen = false" />
                </v-card-title>
                <v-card-text>
                    <v-alert v-if="bulkError" type="error" variant="tonal" class="mb-4">{{ bulkError }}</v-alert>
                    <p class="text-body-2 mb-3">
                        Everyone with a paid ticket on the event who doesn't already have a current
                        waiver (or an outstanding request) gets a signing link by email.
                    </p>
                    <v-select v-model="bulkEventId" :items="eventOptions" density="compact"
                        label="Upcoming event" :loading="eventsLoading" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="bulkOpen = false">Close</v-btn>
                    <v-btn color="primary" :loading="bulkSaving" :disabled="!bulkEventId"
                        @click="createBulk">Send to roster</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snack" :timeout="5000" color="success">{{ snackText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import { WaiverService, type WaiverSignRequestItem } from '@/services/WaiverService'
import { EventService } from '@/services/EventService'
import { branding } from '@/stores/branding'

const service = new WaiverService()
const eventService = new EventService()
const pageSize = 50

const items = ref<WaiverSignRequestItem[]>([])
const search = ref('')
const status = ref<string | null>(null)
const page = ref(1)
const total = ref(0)
const loading = ref(false)
const loadError = ref<string | null>(null)
const busyId = ref<string | null>(null)
const snack = ref(false)
const snackText = ref('')
const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)))

const statusOptions = [
    { title: 'Pending', value: 'pending' },
    { title: 'Sent', value: 'sent' },
    { title: 'Opened', value: 'opened' },
    { title: 'Signed', value: 'signed' },
    { title: 'Cancelled', value: 'cancelled' },
]

let seq = 0
async function load() {
    const s = ++seq
    loading.value = true
    loadError.value = null
    try {
        const res = await service.listSignRequests({
            search: search.value || undefined,
            status: status.value || undefined,
            page: page.value,
            pageSize,
        })
        if (s !== seq) return
        items.value = res.data.data.items
        total.value = res.data.data.total
    } catch (err: any) {
        if (s !== seq) return
        loadError.value = err.response?.data?.error
            ?? 'Could not load signature requests. Check your connection and try Refresh.'
    } finally {
        if (s === seq) loading.value = false
    }
}

let timer: ReturnType<typeof setTimeout> | null = null
function debouncedLoad() {
    if (timer) clearTimeout(timer)
    timer = setTimeout(() => { page.value = 1; load() }, 300)
}
function resetPage() { page.value = 1; load() }

// ── New single request ──────────────────────────────────────────────────────
const newOpen = ref(false)
const newEmail = ref('')
const newName = ref('')
const newSaving = ref(false)
const newError = ref<string | null>(null)

function openNew() {
    newEmail.value = ''
    newName.value = ''
    newError.value = null
    newOpen.value = true
}

async function createRequest() {
    newSaving.value = true
    newError.value = null
    try {
        const res = await service.createSignRequest({
            email: newEmail.value.trim(),
            name: newName.value.trim() || null,
        })
        newOpen.value = false
        snackText.value = res.data.data.status === 'sent'
            ? `Signing link sent to ${res.data.data.recipientEmail}`
            : 'Request created. Email is not configured, so copy the link from the list and send it yourself.'
        snack.value = true
        load()
    } catch (err: any) {
        newError.value = err.response?.data?.error
            ?? 'Could not create the request. Check the email address and try again.'
    } finally {
        newSaving.value = false
    }
}

// ── Bulk roster request ─────────────────────────────────────────────────────
const bulkOpen = ref(false)
const bulkEventId = ref<string | null>(null)
const bulkSaving = ref(false)
const bulkError = ref<string | null>(null)
const eventOptions = ref<{ title: string; value: string }[]>([])
const eventsLoading = ref(false)

async function openBulk() {
    bulkEventId.value = null
    bulkError.value = null
    bulkOpen.value = true
    eventsLoading.value = true
    try {
        const from = new Date().toISOString()
        const to = new Date(Date.now() + 60 * 86400_000).toISOString()
        const res = await eventService.list(from, to)
        eventOptions.value = (res.data.data ?? []).map(e => ({
            title: `${e.title} (${dayjs.utc(e.startsAtUtc).tz(branding.timezone || 'UTC').format('MMM D')})`,
            value: e.id,
        }))
    } catch (err: any) {
        bulkError.value = err.response?.data?.error
            ?? 'Could not load upcoming events. Close this dialog and try again.'
    } finally {
        eventsLoading.value = false
    }
}

async function createBulk() {
    if (!bulkEventId.value) return
    bulkSaving.value = true
    bulkError.value = null
    try {
        const res = await service.createBulkSignRequests(bulkEventId.value)
        const r = res.data.data
        bulkOpen.value = false
        snackText.value = `Sent ${r.created} signing link${r.created === 1 ? '' : 's'}`
            + (r.alreadyCovered > 0 ? `; ${r.alreadyCovered} already covered` : '')
            + (r.emailFailures > 0 ? `; ${r.emailFailures} email${r.emailFailures === 1 ? '' : 's'} failed (copy those links manually)` : '')
        snack.value = true
        load()
    } catch (err: any) {
        bulkError.value = err.response?.data?.error
            ?? 'Could not send to the roster. Check the event and email settings, then try again.'
    } finally {
        bulkSaving.value = false
    }
}

// ── Row actions ─────────────────────────────────────────────────────────────
async function copyLink(r: WaiverSignRequestItem) {
    try {
        await navigator.clipboard.writeText(r.link)
        snackText.value = 'Signing link copied'
        snack.value = true
    } catch {
        loadError.value = `Could not access the clipboard. The link is: ${r.link}`
    }
}

async function resend(r: WaiverSignRequestItem) {
    busyId.value = r.id
    try {
        await service.resendSignRequest(r.id)
        snackText.value = `Resent to ${r.recipientEmail}`
        snack.value = true
        load()
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            ?? `Could not resend to ${r.recipientEmail}. Check the email settings and try again.`
    } finally {
        busyId.value = null
    }
}

async function cancel(r: WaiverSignRequestItem) {
    busyId.value = r.id
    try {
        await service.cancelSignRequest(r.id)
        snackText.value = 'Request cancelled; the link no longer works'
        snack.value = true
        load()
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            ?? 'Could not cancel the request. Refresh and try again.'
    } finally {
        busyId.value = null
    }
}

function statusLabel(s: string): string {
    return s.charAt(0).toUpperCase() + s.slice(1)
}
function statusColor(s: string): string {
    return s === 'signed' ? 'success' : s === 'opened' ? 'primary'
        : s === 'sent' ? 'teal' : s === 'cancelled' ? 'grey' : 'warning'
}
function formatWhen(utc: string): string {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('YYYY-MM-DD HH:mm')
}

onMounted(load)
</script>
