<template>
    <v-container fluid>
        <div class="d-flex align-center mb-4 flex-wrap ga-2">
            <h1 class="text-h5">Signed Waivers</h1>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-refresh" @click="loadPeople">Refresh</v-btn>
        </div>

        <!-- One person per row (rider account, or name+birthdate for walk-ups); expanding a
             row lists that person's individual signatures. Replaces the old two-tab
             (flat log / people) layout with a single people-first master-detail. -->
        <div class="d-flex mb-3 ga-2 flex-wrap align-center">
            <v-text-field v-model="pplSearch" density="compact" hide-details clearable
                label="Search name, email, or guardian" prepend-inner-icon="mdi-magnify"
                style="max-width: 320px" @update:model-value="debouncedPplLoad" />
            <v-select v-model="pplStatus" :items="statusOptions" density="compact" hide-details
                clearable label="Waiver status" style="max-width: 180px" @update:model-value="resetPplPage" />
            <v-select v-model="pplQuick" :items="quickOptions" density="compact" hide-details
                clearable label="Show" style="max-width: 200px" @update:model-value="resetPplPage" />
        </div>

        <v-alert v-if="pplError" type="error" variant="tonal" class="mb-4">{{ pplError }}</v-alert>

        <v-card variant="outlined">
            <v-table density="compact" hover>
                <thead>
                    <tr>
                        <th style="width: 36px"></th>
                        <th>Name</th>
                        <th>Email</th>
                        <th>Age</th>
                        <th>Guardian</th>
                        <th>Last signed</th>
                        <th class="text-right">Signatures</th>
                        <th>Status</th>
                    </tr>
                </thead>
                <tbody>
                    <template v-for="p in people" :key="p.personKey">
                        <tr class="row-click" @click="toggleExpand(p)">
                            <td>
                                <v-icon size="18" class="text-medium-emphasis">
                                    {{ expandedKey === p.personKey ? 'mdi-chevron-down' : 'mdi-chevron-right' }}
                                </v-icon>
                            </td>
                            <td>
                                {{ p.personName }}
                                <v-chip v-if="p.isMinor" size="x-small" color="indigo" class="ml-1">Minor</v-chip>
                                <v-tooltip v-if="p.agingOutSoon" location="top">
                                    <template #activator="{ props }">
                                        <v-chip v-bind="props" size="x-small" color="warning" class="ml-1">Turns 18 soon</v-chip>
                                    </template>
                                    Turns 18 within 90 days. A guardian-signed waiver stops being valid at 18,
                                    so they will need to sign their own on their next visit.
                                </v-tooltip>
                            </td>
                            <td>{{ p.personEmail || '' }}</td>
                            <td class="text-no-wrap">{{ ageLabel(p.birthdate) }}</td>
                            <td>
                                <a v-if="p.guardianName" href="#" class="guardian-link"
                                    @click.prevent.stop="filterByGuardian(p.guardianName)">{{ p.guardianName }}</a>
                                <span v-if="p.guardianPhone" class="text-caption text-medium-emphasis ml-1">{{ p.guardianPhone }}</span>
                            </td>
                            <td class="text-no-wrap">{{ formatWhen(p.lastSignedAtUtc) }}</td>
                            <td class="text-right">{{ p.signatureCount }}</td>
                            <td>
                                <v-chip size="x-small" :color="p.hasCurrentWaiver ? 'success' : 'warning'">
                                    {{ p.hasCurrentWaiver ? 'Current' : 'Outdated' }}
                                </v-chip>
                            </td>
                        </tr>

                        <!-- Expanded: this person's individual signatures, newest first. -->
                        <tr v-if="expandedKey === p.personKey">
                            <td colspan="8" class="expand-cell">
                                <v-alert v-if="expError" type="error" variant="tonal" density="compact" class="my-2">
                                    {{ expError }}
                                </v-alert>
                                <v-skeleton-loader v-else-if="expLoading" type="list-item-two-line" />
                                <template v-else>
                                    <div v-for="s in expSignatures" :key="s.id"
                                        class="sig-line d-flex align-center ga-3 py-1 px-2"
                                        role="button" tabindex="0"
                                        @click="openDetail(s.id)" @keyup.enter="openDetail(s.id)">
                                        <v-icon size="16" class="text-medium-emphasis">mdi-draw-pen</v-icon>
                                        <span class="text-no-wrap">{{ formatWhen(s.signedAtUtc) }}</span>
                                        <span class="text-no-wrap">
                                            {{ s.waiverName }} v{{ s.waiverVersion }}
                                            <v-chip v-if="!s.waiverIsCurrent" size="x-small" color="warning" class="ml-1">Outdated</v-chip>
                                        </span>
                                        <v-chip size="x-small" :color="contextColor(s.context)">{{ contextLabel(s.context) }}</v-chip>
                                        <span v-if="s.signedByParent" class="text-caption text-medium-emphasis">
                                            signed by {{ s.parentName || 'guardian' }}
                                        </span>
                                        <v-spacer />
                                        <span class="text-caption text-medium-emphasis">View</span>
                                    </div>
                                    <div v-if="expSignatures.length === 0" class="text-caption text-medium-emphasis py-2 px-2">
                                        No signatures found for this person.
                                    </div>
                                </template>
                            </td>
                        </tr>
                    </template>
                    <tr v-if="!pplLoading && !pplError && people.length === 0">
                        <td colspan="8" class="text-center text-medium-emphasis py-6">No people match these filters.</td>
                    </tr>
                </tbody>
            </v-table>
            <div class="d-flex align-center pa-2 flex-wrap ga-2">
                <span class="text-caption text-medium-emphasis">{{ pplTotal }} people</span>
                <v-spacer />
                <v-pagination v-model="pplPage" :length="pplPages" :total-visible="7" density="compact"
                    @update:model-value="loadPeople" />
            </div>
        </v-card>

        <!-- ── Signature detail dialog ───────────────────────────────────── -->
        <v-dialog v-model="detailOpen" max-width="640">
            <v-card>
                <v-card-title class="d-flex align-center">
                    Waiver Signature
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="detailOpen = false" />
                </v-card-title>
                <v-card-text>
                    <v-alert v-if="detailError" type="error" variant="tonal" class="mb-4">{{ detailError }}</v-alert>
                    <v-skeleton-loader v-if="detailLoading" type="article" />
                    <template v-else-if="detail">
                        <div class="detail-grid">
                            <div class="label">Signer</div>
                            <div>{{ detail.signerName || '(unnamed)' }}
                                <v-chip v-if="detail.signedByParent" size="x-small" color="indigo" class="ml-1">Minor</v-chip>
                            </div>
                            <div class="label">Email</div><div>{{ detail.signerEmail || '' }}</div>
                            <div class="label">Birthdate</div><div>{{ detail.birthdate ? `${formatDay(detail.birthdate)} (${ageLabel(detail.birthdate)})` : '' }}</div>
                            <template v-if="detail.signedByParent">
                                <div class="label">Guardian</div>
                                <div>{{ detail.parentName || '' }} <span v-if="detail.parentPhone" class="text-medium-emphasis">{{ detail.parentPhone }}</span></div>
                            </template>
                            <div class="label">Waiver</div><div>{{ detail.waiverName }} v{{ detail.waiverVersion }} ({{ detail.waiverTitle }})</div>
                            <div class="label">Signed</div><div>{{ formatWhen(detail.signedAtUtc) }}</div>
                            <div class="label">IP address</div><div>{{ detail.ipAddress || '' }}</div>
                            <div class="label">Context</div>
                            <div>
                                <template v-if="detail.ticketEventTitle">Event ticket: {{ detail.ticketEventTitle }}</template>
                                <template v-else-if="detail.rentalLabel">Rental: {{ detail.rentalLabel }}</template>
                                <template v-else>Account / kiosk signature</template>
                            </div>
                            <template v-if="detail.emergencyContactName || detail.emergencyContactPhone">
                                <div class="label">Emergency contact</div>
                                <div>{{ detail.emergencyContactName || '' }} {{ detail.emergencyContactPhone || '' }}</div>
                            </template>
                        </div>
                        <div v-if="detail.signatureDataUrl" class="signature-box mt-4">
                            <img :src="detail.signatureDataUrl" alt="Signature" />
                        </div>
                        <div v-else class="text-medium-emphasis mt-4">No signature image was captured for this record.</div>
                    </template>
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn v-if="detail" prepend-icon="mdi-printer" @click="printDetail">Print</v-btn>
                    <v-btn variant="text" @click="detailOpen = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import {
    WaiverService,
    type AdminWaiverPersonItem,
    type AdminWaiverSignatureDetail,
    type AdminWaiverSignatureItem,
} from '@/services/WaiverService'
import { branding } from '@/stores/branding'

const service = new WaiverService()
const pageSize = 50

// ── People table state ───────────────────────────────────────────────────────
const people = ref<AdminWaiverPersonItem[]>([])
const pplSearch = ref('')
const pplStatus = ref<string | null>(null)
const pplQuick = ref<string | null>(null)
const pplPage = ref(1)
const pplTotal = ref(0)
const pplLoading = ref(false)
const pplError = ref<string | null>(null)
const pplPages = computed(() => Math.max(1, Math.ceil(pplTotal.value / pageSize)))

const statusOptions = [
    { title: 'Current waiver on file', value: 'current' },
    { title: 'Outdated only', value: 'outdated' },
]
const quickOptions = [
    { title: 'Minors', value: 'minors' },
    { title: 'Turning 18 soon', value: 'agingOut' },
]

let pplSeq = 0
async function loadPeople() {
    const seq = ++pplSeq
    pplLoading.value = true
    pplError.value = null
    expandedKey.value = null
    try {
        const res = await service.listPeople({
            search: pplSearch.value || undefined,
            status: pplStatus.value || undefined,
            minorsOnly: pplQuick.value === 'minors' || undefined,
            agingOut: pplQuick.value === 'agingOut' || undefined,
            page: pplPage.value,
            pageSize,
        })
        if (seq !== pplSeq) return
        people.value = res.data.data.items
        pplTotal.value = res.data.data.total
    } catch (err: any) {
        if (seq !== pplSeq) return
        pplError.value = err.response?.data?.error
            ?? 'Could not load people. Check your connection and try Refresh.'
    } finally {
        if (seq === pplSeq) pplLoading.value = false
    }
}

let pplTimer: ReturnType<typeof setTimeout> | null = null
function debouncedPplLoad() {
    if (pplTimer) clearTimeout(pplTimer)
    pplTimer = setTimeout(() => { pplPage.value = 1; loadPeople() }, 300)
}
function resetPplPage() { pplPage.value = 1; loadPeople() }

function filterByGuardian(name: string) {
    pplSearch.value = name
    pplPage.value = 1
    loadPeople()
}

// ── Row expansion: the person's individual signatures ────────────────────────
const expandedKey = ref<string | null>(null)
const expSignatures = ref<AdminWaiverSignatureItem[]>([])
const expLoading = ref(false)
const expError = ref<string | null>(null)

async function toggleExpand(p: AdminWaiverPersonItem) {
    if (expandedKey.value === p.personKey) {
        expandedKey.value = null
        return
    }
    expandedKey.value = p.personKey
    expSignatures.value = []
    expLoading.value = true
    expError.value = null
    try {
        const res = await service.listSignatures({ personKey: p.personKey, pageSize: 100 })
        if (expandedKey.value !== p.personKey) return
        expSignatures.value = res.data.data.items
    } catch (err: any) {
        if (expandedKey.value !== p.personKey) return
        expError.value = err.response?.data?.error
            ?? `Could not load ${p.personName}'s signatures. Check your connection and try again.`
    } finally {
        if (expandedKey.value === p.personKey) expLoading.value = false
    }
}

// ── Detail dialog ────────────────────────────────────────────────────────────
const detailOpen = ref(false)
const detail = ref<AdminWaiverSignatureDetail | null>(null)
const detailLoading = ref(false)
const detailError = ref<string | null>(null)

async function openDetail(id: string) {
    detailOpen.value = true
    detailLoading.value = true
    detailError.value = null
    detail.value = null
    try {
        const res = await service.getSignatureDetail(id)
        detail.value = res.data.data
    } catch (err: any) {
        detailError.value = err.response?.data?.error
            ?? 'Could not load this signature. Close the dialog and try again.'
    } finally {
        detailLoading.value = false
    }
}

function printDetail() {
    const d = detail.value
    if (!d) return
    const esc = (s: string | null) => (s ?? '')
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    const row = (label: string, value: string) =>
        value ? `<tr><td class="l">${label}</td><td>${value}</td></tr>` : ''
    const context = d.ticketEventTitle ? `Event ticket: ${esc(d.ticketEventTitle)}`
        : d.rentalLabel ? `Rental: ${esc(d.rentalLabel)}`
        : 'Account / kiosk signature'
    const w = window.open('', '_blank', 'width=720,height=900')
    if (!w) {
        detailError.value = 'The print window was blocked. Allow pop-ups for this site and try again.'
        return
    }
    w.document.write(`<!doctype html><html><head><title>Waiver Signature</title>
<style>
 body { font-family: Arial, sans-serif; margin: 32px; color: #111; }
 h1 { font-size: 18px; margin: 0 0 2px; } h2 { font-size: 14px; font-weight: normal; margin: 0 0 20px; color: #444; }
 table { border-collapse: collapse; width: 100%; font-size: 13px; }
 td { padding: 5px 8px; border-bottom: 1px solid #ddd; vertical-align: top; }
 td.l { width: 170px; color: #555; }
 .sig { margin-top: 24px; border: 1px solid #ccc; padding: 12px; }
 .sig img { max-width: 100%; max-height: 160px; }
</style></head><body>
<h1>${esc(branding.displayName || '')} Waiver Signature</h1>
<h2>${esc(d.waiverName)} v${d.waiverVersion}: ${esc(d.waiverTitle)}</h2>
<table>
${row('Signer', esc(d.signerName) || '(unnamed)')}
${row('Email', esc(d.signerEmail))}
${row('Birthdate', d.birthdate ? formatDay(d.birthdate) : '')}
${row('Signed by guardian', d.signedByParent ? 'Yes' : '')}
${row('Guardian', esc(d.parentName))}
${row('Guardian phone', esc(d.parentPhone))}
${row('Signed at', formatWhen(d.signedAtUtc))}
${row('IP address', esc(d.ipAddress))}
${row('Context', context)}
${row('Emergency contact', [esc(d.emergencyContactName), esc(d.emergencyContactPhone)].filter(Boolean).join(' '))}
</table>
${d.signatureDataUrl?.startsWith('data:image/') ? `<div class="sig"><img src="${d.signatureDataUrl}" alt="Signature" /></div>` : ''}
</body></html>`)
    w.document.close()
    w.focus()
    setTimeout(() => w.print(), 250)
}

// ── Shared helpers (tenant timezone) ─────────────────────────────────────────
function tz() { return branding.timezone || 'UTC' }
function formatWhen(utc: string): string {
    return dayjs.utc(utc).tz(tz()).format('YYYY-MM-DD HH:mm')
}
function formatDay(d: string): string {
    return dayjs(d).format('YYYY-MM-DD')
}
function ageLabel(birthdate: string | null): string {
    if (!birthdate) return ''
    const age = dayjs().diff(dayjs(birthdate), 'year')
    return `${age}`
}
function contextLabel(c: string): string {
    return c === 'ticket' ? 'Ticket' : c === 'rental' ? 'Rental' : 'Account'
}
function contextColor(c: string): string {
    return c === 'ticket' ? 'primary' : c === 'rental' ? 'teal' : 'grey'
}

onMounted(loadPeople)
</script>

<style scoped>
.row-click { cursor: pointer; }
.guardian-link { color: rgb(var(--v-theme-primary)); text-decoration: none; }
.guardian-link:hover { text-decoration: underline; }
.expand-cell {
    background: rgba(var(--v-theme-on-surface), 0.03);
    padding-top: 4px !important;
    padding-bottom: 4px !important;
}
.sig-line { cursor: pointer; border-radius: 4px; }
.sig-line:hover { background: rgba(var(--v-theme-on-surface), 0.05); }
.detail-grid {
    display: grid;
    grid-template-columns: 170px 1fr;
    row-gap: 6px;
    font-size: 14px;
}
.detail-grid .label { color: rgba(var(--v-theme-on-surface), 0.6); }
.signature-box {
    border: 1px solid rgba(var(--v-theme-on-surface), 0.2);
    border-radius: 4px;
    padding: 12px;
    background: #fff;
}
.signature-box img { max-width: 100%; max-height: 180px; display: block; }
</style>
