<template>
    <v-container fluid>
        <div class="d-flex align-center mb-4 flex-wrap ga-2">
            <h1 class="text-h5">Employee Passes</h1>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-refresh" :loading="loading" @click="load">Refresh</v-btn>
        </div>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <v-tabs v-model="tab" density="compact" class="mb-4">
            <v-tab value="passes">Passes</v-tab>
            <v-tab value="discounts">Discounts</v-tab>
        </v-tabs>

        <v-window v-model="tab">
        <v-window-item value="passes">

        <!-- Nothing can be issued until a staff-only product exists, so say that plainly rather
             than showing a roster whose Issue buttons all fail. -->
        <v-alert v-if="!loading && !loadError && products.length === 0" type="info" variant="tonal" class="mb-4">
            No employee pass product yet. Create a season pass product and mark it
            <strong>Employee pass</strong> to start issuing. Employee products are never shown to
            riders and can't be bought.
        </v-alert>

        <div class="d-flex ga-3 mb-4 flex-wrap">
            <v-card variant="tonal" class="stat-tile">
                <v-card-text>
                    <div class="text-h5">{{ initialLoading ? '-' : activeCount }}</div>
                    <div class="text-caption text-medium-emphasis">Active passes</div>
                </v-card-text>
            </v-card>
            <v-card variant="tonal" class="stat-tile">
                <v-card-text>
                    <div class="text-h5">{{ initialLoading ? '-' : needsAttentionCount }}</div>
                    <div class="text-caption text-medium-emphasis">Need attention</div>
                </v-card-text>
            </v-card>
            <v-card variant="tonal" class="stat-tile">
                <v-card-text>
                    <div class="text-h5">{{ initialLoading ? '-' : noPassCount }}</div>
                    <div class="text-caption text-medium-emphasis">No pass</div>
                </v-card-text>
            </v-card>
        </div>

        <div class="d-flex mb-3 ga-2 flex-wrap align-center">
            <v-text-field v-model="search" density="compact" hide-details clearable
                label="Search name or email" prepend-inner-icon="mdi-magnify" style="max-width: 320px" />
            <v-select v-model="stateFilter" :items="stateOptions" label="Pass state"
                density="compact" hide-details style="max-width: 220px" />
            <v-checkbox v-model="hideInactiveStaff" label="Hide inactive staff"
                density="compact" hide-details />
        </div>

        <v-card variant="outlined" :loading="loading">
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Employee</th>
                        <th>Role</th>
                        <th>Pass</th>
                        <th>State</th>
                        <th>Approved</th>
                        <th class="text-right" style="width: 190px"></th>
                    </tr>
                </thead>
                <tbody>
                    <template v-if="initialLoading">
                        <tr v-for="n in 5" :key="'sk' + n">
                            <td v-for="c in 6" :key="'skc' + c"><v-skeleton-loader type="text" /></td>
                        </tr>
                    </template>
                    <tr v-for="r in filtered" :key="r.userId">
                        <td>
                            {{ r.name || r.email }}
                            <div class="text-caption text-medium-emphasis">{{ r.email }}</div>
                        </td>
                        <td class="text-no-wrap">
                            <span class="text-caption">{{ prettyRole(r.role) }}</span>
                            <v-chip v-if="!r.isActiveEmployee" size="x-small" color="error" variant="tonal" class="ml-1">
                                Inactive
                            </v-chip>
                        </td>
                        <td class="text-no-wrap">
                            <span v-if="r.productName">{{ r.productName }}</span>
                            <span v-else class="text-medium-emphasis">-</span>
                            <div v-if="r.amountCents" class="text-caption text-medium-emphasis">
                                ${{ (r.amountCents / 100).toFixed(2) }}
                            </div>
                        </td>
                        <td class="text-no-wrap">
                            <v-chip size="x-small" :color="stateColor(r.passState)" variant="tonal">
                                <v-tooltip activator="parent" location="top">{{ stateHelp(r.passState) }}</v-tooltip>
                                {{ stateLabel(r.passState) }}
                            </v-chip>
                        </td>
                        <td class="text-no-wrap">
                            <template v-if="r.issuedAtUtc">
                                <div class="text-caption">{{ formatDate(r.issuedAtUtc) }}</div>
                                <div v-if="r.issuedByName" class="text-caption text-medium-emphasis">
                                    by {{ r.issuedByName }}
                                </div>
                            </template>
                            <span v-else class="text-medium-emphasis">-</span>
                        </td>
                        <td class="text-right text-no-wrap">
                            <v-btn v-if="!r.passPurchaseId" size="small" variant="tonal"
                                :disabled="!r.isActiveEmployee || products.length === 0"
                                @click="openIssue(r)">
                                <v-tooltip v-if="!r.isActiveEmployee" activator="parent" location="top">
                                    This account is inactive, so a pass issued to it wouldn't work.
                                </v-tooltip>
                                Issue
                            </v-btn>
                            <v-btn v-else size="small" variant="text" color="error" @click="openRevoke(r)">
                                Revoke
                            </v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && !loadError && filtered.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-6">
                            No staff match these filters.
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- ── Issue ───────────────────────────────────────────────────────── -->
        <v-dialog v-model="issueOpen" max-width="520">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Issue an employee pass</span>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="issueOpen = false" />
                </v-card-title>
                <v-divider />
                <v-card-text>
                    <v-alert v-if="actionError" type="error" variant="tonal" density="compact" class="mb-4">
                        {{ actionError }}
                    </v-alert>
                    <p class="text-body-2 mb-1">
                        Granting a pass to <strong>{{ issueTarget?.name || issueTarget?.email }}</strong>.
                    </p>
                    <p class="text-caption text-medium-emphasis mb-2">
                        They still have to add a photo and sign the waiver before it will scan, and
                        the pass stops working the moment the account is deactivated.
                    </p>
                    <v-select v-model="issueProductId" :items="productItems" label="Employee pass product"
                        density="compact" class="mt-4" />
                    <div v-if="selectedProductPrice > 0" class="text-caption text-medium-emphasis mt-2">
                        This product costs ${{ (selectedProductPrice / 100).toFixed(2) }}, so the pass is
                        issued awaiting payment and won't admit anyone until it's settled.
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="issueOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" :disabled="!issueProductId" @click="confirmIssue">
                        Issue pass
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- ── Revoke ──────────────────────────────────────────────────────── -->
        <v-dialog v-model="revokeOpen" max-width="520">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Revoke this employee pass</span>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="revokeOpen = false" />
                </v-card-title>
                <v-divider />
                <v-card-text>
                    <v-alert v-if="actionError" type="error" variant="tonal" density="compact" class="mb-4">
                        {{ actionError }}
                    </v-alert>
                    <p class="text-body-2 mb-2">
                        Withdrawing the pass from <strong>{{ revokeTarget?.name || revokeTarget?.email }}</strong>.
                        Admissions they've already used are unaffected.
                    </p>
                    <p class="text-caption text-medium-emphasis">
                        If they've only left temporarily, deactivating the account is enough: the pass
                        stops working on its own and comes back if they return.
                    </p>
                    <v-text-field v-model="revokeReason" label="Reason" density="compact" class="mt-4"
                        :error-messages="revokeReason.trim() ? [] : ['A reason is required.']" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="revokeOpen = false">Cancel</v-btn>
                    <v-btn color="error" :loading="saving" :disabled="!revokeReason.trim()" @click="confirmRevoke">
                        Revoke
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        </v-window-item>

        <!-- ── Discounts ───────────────────────────────────────────────────── -->
        <v-window-item value="discounts">
            <v-alert v-if="!loading && products.length === 0" type="info" variant="tonal">
                Create an employee pass product first. Perks hang off the pass, so there is
                nothing to configure until one exists.
            </v-alert>

            <template v-else-if="perkProduct">
                <v-select v-if="products.length > 1" v-model="perkProductId" :items="productItems"
                    label="Employee pass product" density="compact" hide-details
                    style="max-width: 340px" class="mb-4" />

                <p class="text-body-2 text-medium-emphasis mb-4">
                    What <strong>{{ perkProduct.name }}</strong> is worth beyond admission. These
                    apply to any active employee holding this pass, at the counter and online.
                    100% means free.
                </p>

                <v-card variant="outlined" class="pa-4 mb-4">
                    <div class="text-subtitle-1 font-weight-bold mb-1">Bike shop</div>
                    <div class="text-caption text-medium-emphasis mb-3">
                        Applies at the shop register and in the online store.
                    </div>
                    <div class="d-flex ga-4 flex-wrap">
                        <v-text-field v-model.number="perk.retail" type="number" min="0" max="100"
                            label="Retail % off" density="compact" hide-details suffix="%"
                            style="max-width: 170px" :disabled="!surfaceLive.retail" />
                        <v-text-field v-model.number="perk.rental" type="number" min="0" max="100"
                            label="Rentals % off" density="compact" hide-details suffix="%"
                            style="max-width: 170px" :disabled="!surfaceLive.rental" />
                    </div>
                </v-card>

                <v-card variant="outlined" class="pa-4 mb-4">
                    <div class="text-subtitle-1 font-weight-bold mb-1">Food &amp; beverage</div>
                    <div class="text-caption text-medium-emphasis mb-3">
                        At the F&amp;B window the cashier looks the employee up by email or phone
                        under Member discount, and this comes up instead of the general season pass
                        discount. No manager PIN needed. Counter only: online food orders don't
                        apply member discounts yet.
                    </div>
                    <v-alert v-if="!surfaceLive.concession" type="warning" variant="tonal"
                        density="compact" class="mb-3">
                        The F&amp;B till isn't reading per-pass perks right now, so a percentage set
                        here wouldn't apply at the window.
                    </v-alert>
                    <v-text-field v-model.number="perk.concession" type="number" min="0" max="100"
                        label="F&B % off" density="compact" hide-details suffix="%"
                        style="max-width: 170px" :disabled="!surfaceLive.concession" />
                </v-card>

                <v-card variant="outlined" class="pa-4 mb-4">
                    <div class="text-subtitle-1 font-weight-bold mb-1">Event entry</div>
                    <div class="text-caption text-medium-emphasis mb-3">
                        Per event type, so staff can ride free on open days but still pay to race.
                        Leave blank for no discount.
                    </div>
                    <div v-if="eventTypes.length === 0" class="text-caption text-medium-emphasis">
                        No event types defined yet.
                    </div>
                    <div v-for="t in eventTypes" :key="t.id" class="d-flex align-center ga-3 mb-2">
                        <div style="min-width: 160px">{{ t.name }}</div>
                        <v-text-field v-model.number="perk.events[t.id]" type="number" min="0" max="100"
                            label="% off" density="compact" hide-details suffix="%"
                            style="max-width: 150px" :disabled="!surfaceLive.event" />
                        <span v-if="perk.events[t.id] === 100" class="text-caption text-success">Free entry</span>
                    </div>
                </v-card>

                <v-alert v-if="perkError" type="error" variant="tonal" class="mb-4">{{ perkError }}</v-alert>
                <v-btn color="primary" :loading="savingPerks" @click="savePerks">Save discounts</v-btn>
            </template>
        </v-window-item>
        </v-window>

        <v-snackbar v-model="toast" :timeout="4000" color="success" location="top">{{ toastText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import {
    SeasonPassService,
    type EmployeeEventTypeOption,
    type EmployeePassProductOption,
    type EmployeePassRosterItem,
} from '@/services/SeasonPassService'

const service = new SeasonPassService()

const tab = ref<'passes' | 'discounts'>('passes')

const rows = ref<EmployeePassRosterItem[]>([])
const products = ref<EmployeePassProductOption[]>([])
const eventTypes = ref<EmployeeEventTypeOption[]>([])
// Which surfaces honour a per-pass benefit today. Server-supplied so the page can't promise a
// discount the till would ignore.
const surfaceLive = ref<Record<string, boolean>>({
    event: true, retail: true, rental: true, concession: false,
})
const loading = ref(false)
const loaded = ref(false)
const loadError = ref<string | null>(null)
const initialLoading = computed(() => loading.value && !loaded.value)

const search = ref('')
const stateFilter = ref<'all' | 'none' | 'active' | 'attention'>('all')
const hideInactiveStaff = ref(false)
const stateOptions = [
    { value: 'all', title: 'All' },
    { value: 'active', title: 'Active passes' },
    { value: 'attention', title: 'Needs attention' },
    { value: 'none', title: 'No pass' },
]

const saving = ref(false)
const actionError = ref<string | null>(null)
const toast = ref(false)
const toastText = ref('')

// Perks are stored as benefit rows; the tab edits them as plain percentages and converts on
// save (10000 bps = 100% = free), which is the vocabulary tenants already use elsewhere.
const perkProductId = ref<string | null>(null)
const perk = ref<{ retail: number | null; rental: number | null; concession: number | null; events: Record<string, number | null> }>(
    { retail: null, rental: null, concession: null, events: {} })
const savingPerks = ref(false)
const perkError = ref<string | null>(null)

const perkProduct = computed(() => products.value.find(p => p.id === perkProductId.value) ?? null)

const issueOpen = ref(false)
const issueTarget = ref<EmployeePassRosterItem | null>(null)
const issueProductId = ref<string | null>(null)
const revokeOpen = ref(false)
const revokeTarget = ref<EmployeePassRosterItem | null>(null)
const revokeReason = ref('')

const productItems = computed(() => products.value.map(p => ({
    value: p.id,
    title: p.isActive ? p.name : `${p.name} (inactive)`,
})))
const selectedProductPrice = computed(() =>
    products.value.find(p => p.id === issueProductId.value)?.priceCents ?? 0)

// "Needs attention" is anything issued that will not currently get someone through the gate.
const needsAttention = (r: EmployeePassRosterItem) =>
    r.passState === 'pending_payment' || r.passState === 'not_registered' || r.passState === 'inactive_employee'

const activeCount = computed(() => rows.value.filter(r => r.passState === 'active').length)
const needsAttentionCount = computed(() => rows.value.filter(needsAttention).length)
const noPassCount = computed(() => rows.value.filter(r => r.passState === 'none').length)

const filtered = computed(() => rows.value.filter(r => {
    if (hideInactiveStaff.value && !r.isActiveEmployee) return false
    if (stateFilter.value === 'active' && r.passState !== 'active') return false
    if (stateFilter.value === 'none' && r.passState !== 'none') return false
    if (stateFilter.value === 'attention' && !needsAttention(r)) return false
    const q = search.value.trim().toLowerCase()
    if (!q) return true
    return (r.name ?? '').toLowerCase().includes(q) || r.email.toLowerCase().includes(q)
}))

function stateLabel(s: EmployeePassRosterItem['passState']) {
    switch (s) {
        case 'none': return 'No pass'
        case 'pending_payment': return 'Awaiting payment'
        case 'not_registered': return 'Not registered'
        case 'inactive_employee': return 'Invalid, inactive'
        default: return 'Active'
    }
}
function stateColor(s: EmployeePassRosterItem['passState']) {
    switch (s) {
        case 'active': return 'success'
        case 'inactive_employee': return 'error'
        case 'none': return undefined
        default: return 'warning'
    }
}
function stateHelp(s: EmployeePassRosterItem['passState']) {
    switch (s) {
        case 'none': return 'Eligible, but no pass has been approved. This is the default.'
        case 'pending_payment': return 'Issued but not paid for yet, so it will not admit anyone.'
        case 'not_registered': return 'Issued, but needs a photo and a signed waiver before it will scan.'
        case 'inactive_employee': return 'The staff account is inactive, so this pass no longer works.'
        default: return 'Approved, registered, and admitting.'
    }
}
function prettyRole(role: string | null) {
    if (!role) return '-'
    return role.replace(/^tenant_/, '').replace(/_/g, ' ')
}
function formatDate(utc: string) {
    return dayjs(utc).format('MMM D, YYYY')
}

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const { data } = await service.employeePassRoster()
        rows.value = data.data.rows
        products.value = data.data.products
        eventTypes.value = data.data.eventTypes ?? []
        if (data.data.surfaceLive) surfaceLive.value = data.data.surfaceLive
        if (!perkProductId.value || !products.value.some(p => p.id === perkProductId.value)) {
            perkProductId.value = (products.value.find(p => p.isActive) ?? products.value[0])?.id ?? null
        }
        hydratePerks()
        loaded.value = true
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            ?? 'Could not load the employee pass roster. Use Refresh to try again.'
    } finally {
        loading.value = false
    }
}

// Benefit rows -> the tab's percentage fields. Only 'percent' rows are shown: the employee
// perks this page writes are always percentages, so a legacy fixed-amount row is left untouched
// rather than being silently rewritten as a percentage on the next save.
function hydratePerks() {
    const p = perkProduct.value
    const next = { retail: null as number | null, rental: null as number | null, concession: null as number | null, events: {} as Record<string, number | null> }
    for (const b of p?.benefits ?? []) {
        if (b.discountKind !== 'percent') continue
        const pct = Math.round(b.discountValue / 100)
        if (b.benefitType === 'retail') next.retail = pct
        else if (b.benefitType === 'rental') next.rental = pct
        else if (b.benefitType === 'concession') next.concession = pct
        else if (b.benefitType === 'event' && b.scopeId) next.events[b.scopeId] = pct
    }
    perk.value = next
}

async function savePerks() {
    if (!perkProductId.value) return
    savingPerks.value = true
    perkError.value = null
    try {
        const benefits: Array<{ benefitType: string; scopeId: string | null; discountKind: string; discountValue: number }> = []
        const add = (type: string, pct: number | null | undefined, scopeId: string | null = null) => {
            const n = Number(pct)
            if (!Number.isFinite(n) || n <= 0) return
            benefits.push({ benefitType: type, scopeId, discountKind: 'percent', discountValue: Math.round(n * 100) })
        }
        add('retail', perk.value.retail)
        add('rental', perk.value.rental)
        add('concession', perk.value.concession)
        for (const t of eventTypes.value) add('event', perk.value.events[t.id], t.id)

        await service.updateEmployeeBenefits(perkProductId.value, benefits)
        toastText.value = 'Discounts saved.'
        toast.value = true
        await load()
    } catch (err: any) {
        perkError.value = err.response?.data?.error
            ?? 'Could not save the discounts. Nothing was changed; check the values and try again.'
    } finally {
        savingPerks.value = false
    }
}

function openIssue(r: EmployeePassRosterItem) {
    issueTarget.value = r
    actionError.value = null
    // Default to the first ACTIVE product: issuing against an inactive one is almost never meant.
    issueProductId.value = (products.value.find(p => p.isActive) ?? products.value[0])?.id ?? null
    issueOpen.value = true
}

async function confirmIssue() {
    if (!issueTarget.value || !issueProductId.value) return
    saving.value = true
    actionError.value = null
    try {
        await service.issueEmployeePass(issueTarget.value.userId, issueProductId.value)
        issueOpen.value = false
        toastText.value = 'Pass issued. They still need to add a photo and sign the waiver.'
        toast.value = true
        await load()
    } catch (err: any) {
        actionError.value = err.response?.data?.error
            ?? 'Could not issue the pass. Nothing was changed; try again.'
    } finally {
        saving.value = false
    }
}

function openRevoke(r: EmployeePassRosterItem) {
    revokeTarget.value = r
    revokeReason.value = ''
    actionError.value = null
    revokeOpen.value = true
}

async function confirmRevoke() {
    if (!revokeTarget.value?.passPurchaseId || !revokeReason.value.trim()) return
    saving.value = true
    actionError.value = null
    try {
        await service.revokeEmployeePass(revokeTarget.value.passPurchaseId, revokeReason.value.trim())
        revokeOpen.value = false
        toastText.value = 'Pass revoked.'
        toast.value = true
        await load()
    } catch (err: any) {
        actionError.value = err.response?.data?.error
            ?? 'Could not revoke the pass. Nothing was changed; try again.'
    } finally {
        saving.value = false
    }
}

onMounted(load)
</script>

<style scoped>
.stat-tile { min-width: 150px; }
</style>
