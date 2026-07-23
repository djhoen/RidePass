<template>
    <v-container fluid>
        <div class="d-flex align-center mb-4 flex-wrap ga-2">
            <h1 class="text-h5">Signed Waivers</h1>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-refresh" @click="reload">Refresh</v-btn>
        </div>

        <v-tabs v-model="tab" :height="40" class="mb-4 sub-tabs"
            hide-slider selected-class="sub-tab-active">
            <v-tab value="signatures" class="sub-tab">Signatures</v-tab>
            <v-tab value="people" class="sub-tab">People</v-tab>
        </v-tabs>

        <!-- ── Signatures tab: the flat log, newest first ─────────────────── -->
        <div v-if="tab === 'signatures'">
            <div class="d-flex mb-3 ga-2 flex-wrap align-center">
                <v-text-field v-model="sigSearch" density="compact" hide-details clearable
                    label="Search name, email, or guardian" prepend-inner-icon="mdi-magnify"
                    style="max-width: 320px" @update:model-value="debouncedSigLoad" />
                <v-select v-if="!waiverFilterUnavailable" v-model="sigWaiverId" :items="waiverOptions"
                    density="compact" hide-details clearable label="Waiver document"
                    style="max-width: 220px" @update:model-value="resetSigPage" />
                <v-select v-model="sigContext" :items="contextOptions" density="compact" hide-details
                    clearable label="Context" style="max-width: 170px" @update:model-value="resetSigPage" />
                <v-checkbox v-model="sigMinorsOnly" density="compact" hide-details label="Minors only"
                    @update:model-value="resetSigPage" />
            </div>

            <v-alert v-if="sigError" type="error" variant="tonal" class="mb-4">{{ sigError }}</v-alert>

            <v-card variant="outlined">
                <v-table density="compact" hover>
                    <thead>
                        <tr>
                            <th>Signed</th>
                            <th>Name</th>
                            <th>Email</th>
                            <th>Waiver</th>
                            <th>Guardian</th>
                            <th>Context</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="s in signatures" :key="s.id" class="row-click" @click="openDetail(s.id)">
                            <td class="text-no-wrap">{{ formatWhen(s.signedAtUtc) }}</td>
                            <td>
                                {{ s.signerName || '(unnamed)' }}
                                <v-chip v-if="s.signedByParent" size="x-small" color="indigo" class="ml-1">Minor</v-chip>
                            </td>
                            <td>{{ s.signerEmail || '' }}</td>
                            <td class="text-no-wrap">
                                {{ s.waiverName }} v{{ s.waiverVersion }}
                                <v-chip v-if="!s.waiverIsCurrent" size="x-small" color="warning" class="ml-1">Outdated</v-chip>
                            </td>
                            <td>{{ s.parentName || '' }}</td>
                            <td><v-chip size="x-small" :color="contextColor(s.context)">{{ contextLabel(s.context) }}</v-chip></td>
                        </tr>
                        <tr v-if="!sigLoading && !sigError && signatures.length === 0">
                            <td colspan="6" class="text-center text-medium-emphasis py-6">No signatures match these filters.</td>
                        </tr>
                    </tbody>
                </v-table>
                <div class="d-flex align-center pa-2 flex-wrap ga-2">
                    <span class="text-caption text-medium-emphasis">{{ sigTotal }} signatures</span>
                    <v-spacer />
                    <v-pagination v-model="sigPage" :length="sigPages" :total-visible="7" density="compact"
                        @update:model-value="loadSignatures" />
                </div>
            </v-card>
        </div>

        <!-- ── People tab: one row per person, minors + guardians surfaced ── -->
        <div v-if="tab === 'people'">
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
                        <tr v-for="p in people" :key="p.personKey">
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
                                    @click.prevent="filterByGuardian(p.guardianName)">{{ p.guardianName }}</a>
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
                        <tr v-if="!pplLoading && !pplError && people.length === 0">
                            <td colspan="7" class="text-center text-medium-emphasis py-6">No people match these filters.</td>
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
        </div>

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
const tab = ref<'signatures' | 'people'>('signatures')
const pageSize = 50

// ── Signatures tab state ─────────────────────────────────────────────────────
const signatures = ref<AdminWaiverSignatureItem[]>([])
const sigSearch = ref('')
const sigWaiverId = ref<string | null>(null)
const sigContext = ref<string | null>(null)
const sigMinorsOnly = ref(false)
const sigPage = ref(1)
const sigTotal = ref(0)
const sigLoading = ref(false)
const sigError = ref<string | null>(null)
const sigPages = computed(() => Math.max(1, Math.ceil(sigTotal.value / pageSize)))

const contextOptions = [
    { title: 'Event ticket', value: 'ticket' },
    { title: 'Rental', value: 'rental' },
    { title: 'Account / kiosk', value: 'account' },
]

// Waiver-document filter needs the CatalogManage-gated admin list; a viewer with
// only customers.view gets a 403, so the filter quietly hides for them.
const waiverOptions = ref<{ title: string; value: string }[]>([])
const waiverFilterUnavailable = ref(false)

let sigSeq = 0
async function loadSignatures() {
    const seq = ++sigSeq
    sigLoading.value = true
    sigError.value = null
    try {
        const res = await service.listSignatures({
            search: sigSearch.value || undefined,
            waiverId: sigWaiverId.value || undefined,
            context: sigContext.value || undefined,
            minorsOnly: sigMinorsOnly.value || undefined,
            page: sigPage.value,
            pageSize,
        })
        if (seq !== sigSeq) return
        signatures.value = res.data.data.items
        sigTotal.value = res.data.data.total
    } catch (err: any) {
        if (seq !== sigSeq) return
        sigError.value = err.response?.data?.error
            ?? 'Could not load the signature log. Check your connection and try Refresh.'
    } finally {
        if (seq === sigSeq) sigLoading.value = false
    }
}

let sigTimer: ReturnType<typeof setTimeout> | null = null
function debouncedSigLoad() {
    if (sigTimer) clearTimeout(sigTimer)
    sigTimer = setTimeout(() => { sigPage.value = 1; loadSignatures() }, 300)
}
function resetSigPage() { sigPage.value = 1; loadSignatures() }

// ── People tab state ─────────────────────────────────────────────────────────
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
${d.signatureDataUrl?.startsWith('data:image/png;base64,') ? `<div class="sig"><img src="${d.signatureDataUrl}" alt="Signature" /></div>` : ''}
</body></html>`)
    w.document.close()
    w.focus()
    setTimeout(() => w.print(), 250)
}

// ── Shared helpers ───────────────────────────────────────────────────────────
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

function reload() {
    if (tab.value === 'signatures') loadSignatures()
    else loadPeople()
}

onMounted(async () => {
    loadSignatures()
    loadPeople()
    try {
        const res = await service.listAdmin()
        waiverOptions.value = res.data.data.map(w => ({ title: `${w.name} v${w.version}`, value: w.id }))
    } catch {
        // Optional enrichment: viewer lacks catalog.manage (or the list failed), so
        // the document filter hides rather than blocking the page.
        waiverFilterUnavailable.value = true
    }
})
</script>

<style scoped>
/* Sub-tabs: pills on a tinted rail (house style, matches BikeShop/Inventory). */
.sub-tabs {
    background: rgba(var(--v-theme-on-surface), 0.04);
    border-radius: 4px;
    padding: 4px;
    display: inline-flex;
    flex: 0 0 auto;
}
.sub-tabs :deep(.v-slide-group__content) {
    gap: 4px;
    align-items: center;
}
.sub-tabs :deep(.v-tab) {
    border-radius: 4px;
    height: 32px;
    min-height: 32px;
    min-width: 0;
    padding: 0 18px;
    font-size: 13px;
    letter-spacing: 0.01em;
    text-transform: none;
    opacity: 0.75;
}
.sub-tabs :deep(.sub-tab-active) {
    background: rgb(var(--v-theme-surface));
    opacity: 1;
    font-weight: 600;
    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.12);
}
.row-click { cursor: pointer; }
.guardian-link { color: rgb(var(--v-theme-primary)); text-decoration: none; }
.guardian-link:hover { text-decoration: underline; }
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
