<template>
    <v-container>
        <h1 class="text-h4 mb-2">QuickBooks</h1>
        <p class="text-body-2 text-medium-emphasis mb-6">
            Sync your RidePass sales into QuickBooks Online. Once a night we post one summarised
            journal entry per day into your company: revenue by category, sales tax and tips as
            liabilities, and processing fees as expenses. Nothing is posted twice, and only days that
            have fully ended in your track's timezone are sent.
        </p>

        <v-alert v-if="!loading && !status.isConfigured" type="warning" variant="tonal" density="comfortable" class="mb-6">
            QuickBooks isn't set up on this RidePass deployment yet. Contact support to enable it.
        </v-alert>

        <v-tabs v-model="tab" color="primary" class="mb-4">
            <v-tab value="connection">Connection</v-tab>
            <v-tab value="accounts" :disabled="!status.isConnected">
                Chart of accounts
                <v-chip v-if="status.isConnected && !status.mappingComplete" size="x-small" color="warning"
                    variant="flat" class="ml-2">{{ status.unmappedKeys.length }}</v-chip>
            </v-tab>
            <v-tab value="classes" :disabled="!status.isConnected">Profit centers</v-tab>
            <v-tab value="history" :disabled="!status.isConnected">Sync history</v-tab>
        </v-tabs>

        <v-window v-model="tab">
            <!-- ── Connection ─────────────────────────────────────────────────────── -->
            <v-window-item value="connection">
                <v-card class="pa-4">
                    <v-card-text class="px-0 pt-0">
                        <div v-if="loading" class="d-flex align-center ga-3 py-4">
                            <v-progress-circular indeterminate size="20" />
                            <span class="text-body-2">Loading QuickBooks status…</span>
                        </div>

                        <div v-else-if="!status.isConnected" class="d-flex flex-column ga-3">
                            <div class="d-flex align-center ga-2">
                                <v-icon color="grey">mdi-link-off</v-icon>
                                <span>No QuickBooks company connected.</span>
                            </div>
                            <div>
                                <v-btn color="primary" :disabled="!status.isConfigured" :loading="connectLoading"
                                    @click="connect">
                                    Connect QuickBooks
                                </v-btn>
                            </div>
                        </div>

                        <div v-else class="d-flex flex-column ga-3">
                            <div class="d-flex align-center ga-2 flex-wrap">
                                <v-icon :color="statusColor">{{ statusIcon }}</v-icon>
                                <span>
                                    <strong>{{ status.companyName || 'Connected' }}</strong>
                                    <code v-if="status.realmId" class="ml-2 text-caption">Realm {{ status.realmId }}</code>
                                </span>
                                <v-chip :color="statusColor" size="small">{{ statusLabel }}</v-chip>
                            </div>

                            <v-alert v-if="status.lastSyncError" type="error" variant="tonal" density="compact">
                                {{ status.lastSyncError }}
                            </v-alert>

                            <v-alert v-if="!status.mappingComplete" type="info" variant="tonal" density="compact">
                                Account mapping isn't finished, so nothing can post yet. Complete it in the
                                Chart of accounts tab.
                            </v-alert>

                            <div class="text-caption text-medium-emphasis">
                                Syncing sales from {{ formatDate(status.syncStartDate) }}.
                                <template v-if="status.lastSyncedDate">
                                    Posted through {{ formatDate(status.lastSyncedDate) }}.
                                </template>
                                <template v-else>Nothing posted yet.</template>
                            </div>

                            <v-switch v-model="syncEnabled" color="primary" density="compact" hide-details
                                :label="syncEnabled ? 'Nightly sync is on' : 'Nightly sync is paused'"
                                :loading="toggleLoading" @update:model-value="onToggleSync" />

                            <div class="d-flex ga-2 flex-wrap">
                                <v-btn color="primary" variant="tonal" :loading="syncLoading"
                                    :disabled="!status.mappingComplete" @click="syncNow">
                                    Sync now
                                </v-btn>
                                <v-btn variant="text" :loading="connectLoading" @click="connect">Reconnect</v-btn>
                                <v-btn variant="text" color="error" :loading="disconnectLoading" @click="disconnect">
                                    Disconnect
                                </v-btn>
                            </div>
                        </div>
                    </v-card-text>
                </v-card>
            </v-window-item>

            <!-- ── Account mapping ────────────────────────────────────────────────── -->
            <v-window-item value="accounts">
                <v-card class="pa-4">
                    <v-card-text class="px-0 pt-0">
                        <p class="text-body-2 text-medium-emphasis mb-2">
                            Tell us which of your accounts each kind of money belongs in. Every row needs an
                            account before we can post, a day whose accounts aren't all mapped is held back
                            rather than booked to the wrong place.
                        </p>

                        <v-alert v-if="!status.mappingComplete" type="info" variant="tonal" density="compact" class="mb-4">
                            {{ status.unmappedKeys.length }}
                            {{ status.unmappedKeys.length === 1 ? 'account still needs' : 'accounts still need' }}
                            to be mapped before syncing can start.
                        </v-alert>

                        <div v-if="accountsError" class="mb-4">
                            <v-alert type="error" variant="tonal" density="compact">
                                {{ accountsError }}
                                <template #append>
                                    <v-btn size="small" variant="text" @click="loadAccounts">Retry</v-btn>
                                </template>
                            </v-alert>
                        </div>

                        <template v-for="(group, gi) in mappingGroups" :key="group.title">
                            <div class="text-subtitle-2 mb-3" :class="gi === 0 ? '' : 'mt-6'">
                                {{ group.title }}
                                <span class="text-caption text-medium-emphasis ml-1">{{ group.caption }}</span>
                            </div>

                            <!-- Revenue grouped by the tenant's profit centers: one account per
                                 center, expanded to the member slots on save. -->
                            <template v-if="group.classification === 'Revenue' && centerGroups.length">
                                <div v-for="(g, i) in centerGroups" :key="g.center.id"
                                    class="d-flex align-center ga-3" :class="i === 0 ? '' : 'mt-4'">
                                    <!-- The center's color, so this screen names the same buckets
                                         the reports and charts show, in the same colors. -->
                                    <span class="swatch" :style="{ background: seriesColor(g.center.color, isDark) }"></span>
                                    <v-select v-model="centerAccounts[g.center.id]" :items="accountsFor('Revenue')"
                                        item-title="name" item-value="id" density="compact" variant="outlined"
                                        :label="g.center.name" :loading="accountsLoading" clearable
                                        :hint="centerHint(g)" persistent-hint />
                                </div>
                                <div v-for="m in ungroupedRevenueRows" :key="m.mappingKey">
                                    <v-select v-model="m.qboAccountId" :items="accountsFor('Revenue')"
                                        item-title="name" item-value="id" density="compact" variant="outlined"
                                        :label="m.label" :loading="accountsLoading" clearable
                                        class="mt-4" hide-details />
                                </div>
                            </template>

                            <template v-else>
                                <div v-for="(m, i) in group.rows" :key="m.mappingKey">
                                    <v-select v-model="m.qboAccountId" :items="accountsFor(m.expectedClassification)"
                                        item-title="name" item-value="id" density="compact" variant="outlined"
                                        :label="m.label" :loading="accountsLoading" clearable
                                        :class="i === 0 ? '' : 'mt-4'" hide-details />
                                </div>
                            </template>
                        </template>

                        <div class="d-flex ga-2 mt-6">
                            <v-btn color="primary" :loading="saveLoading" @click="saveMappings">Save mapping</v-btn>
                        </div>
                    </v-card-text>
                </v-card>
            </v-window-item>

            <!-- ── Profit centers (QBO classes) ───────────────────────────────────── -->
            <v-window-item value="classes">
                <v-card class="pa-4">
                    <v-card-text class="px-0 pt-0">
                        <p class="text-body-2 text-medium-emphasis mb-2">
                            Give each profit center a QuickBooks class and every sale we post gets tagged with
                            it, so a Profit &amp; Loss by Class in QuickBooks splits out your bike shop, food and
                            gate revenue the same way the Revenue by Department report does here. This is
                            optional: leave it blank and revenue still posts, just untagged.
                        </p>
                        <p class="text-body-2 text-medium-emphasis mb-4">
                            Only income lines are tagged. Sales tax, tips, gift card balances, processing fees
                            and cash movements belong to the whole business rather than one center, so they post
                            without a class and show under "Not Specified".
                        </p>

                        <div v-if="classesLoading" class="d-flex align-center ga-3 py-4">
                            <v-progress-circular indeterminate size="20" />
                            <span class="text-body-2">Loading your QuickBooks classes…</span>
                        </div>

                        <template v-else>
                            <v-alert v-if="classesError" type="error" variant="tonal" density="compact" class="mb-4">
                                {{ classesError }}
                                <template #append>
                                    <v-btn size="small" variant="text" @click="loadClasses">Retry</v-btn>
                                </template>
                            </v-alert>

                            <!-- Nothing below is usable until QuickBooks itself is tracking classes, so say
                                 exactly where the switch is rather than showing empty dropdowns. -->
                            <v-alert v-else-if="!classSettings.trackingEnabled" type="warning" variant="tonal"
                                density="comfortable" class="mb-4">
                                Class tracking is switched off in QuickBooks, so we can't tag anything yet. In
                                QuickBooks go to Settings &rarr; Account and settings &rarr; Advanced &rarr;
                                Categories, turn on <strong>Track classes</strong>, choose
                                <strong>Assign one to each row in transaction</strong>, then come back and
                                reload this page.
                            </v-alert>

                            <template v-else>
                                <!-- Per-transaction mode puts ONE class on the whole journal entry, which
                                     would flatten every center back into a single bucket. -->
                                <v-alert v-if="!classSettings.trackingPerLine" type="warning" variant="tonal"
                                    density="comfortable" class="mb-4">
                                    QuickBooks is set to one class per transaction. Because we post one summary
                                    journal entry per day, that can't separate your centers. Switch Categories to
                                    <strong>Assign one to each row in transaction</strong> in QuickBooks to get a
                                    real split.
                                </v-alert>

                                <v-alert v-if="!classSettings.classes.length" type="info" variant="tonal"
                                    density="compact" class="mb-4">
                                    Your QuickBooks company has class tracking on but no classes created yet. Add
                                    one per profit center in QuickBooks, then reload this page.
                                </v-alert>

                                <div v-for="(row, i) in classMappings" :key="row.bucketKey"
                                    class="d-flex align-center ga-3" :class="i === 0 ? '' : 'mt-4'">
                                    <!-- Same color the reports and charts draw this bucket in. -->
                                    <span class="swatch" :style="{ background: seriesColor(row.color, isDark) }"></span>
                                    <v-select v-model="row.qboClassId" :items="classSettings.classes"
                                        item-title="fullyQualifiedName" item-value="id" density="compact"
                                        variant="outlined" :label="row.label" clearable
                                        :disabled="!classSettings.classes.length"
                                        :hint="classHint(row)" persistent-hint />
                                </div>

                                <p v-if="usingBuiltInDepartments" class="text-caption text-medium-emphasis mt-4">
                                    These are the built-in departments. To use your own names and grouping,
                                    set them up under
                                    <router-link to="/Admin/Settings/ProfitCenters">Settings &rarr; Profit centers</router-link>.
                                </p>

                                <p class="text-caption text-medium-emphasis mt-4">
                                    Days already posted keep the classes they were posted with. To re-tag one,
                                    delete its journal entry in QuickBooks and retry the day from Sync history.
                                </p>

                                <div class="d-flex ga-2 mt-6">
                                    <v-btn color="primary" :loading="saveClassesLoading"
                                        :disabled="!classSettings.classes.length" @click="saveClassMappings">
                                        Save profit centers
                                    </v-btn>
                                </div>
                            </template>
                        </template>
                    </v-card-text>
                </v-card>
            </v-window-item>

            <!-- ── History ────────────────────────────────────────────────────────── -->
            <v-window-item value="history">
                <v-card class="pa-4">
                    <v-card-text class="px-0 pt-0">
                        <div class="table-scroll">
                            <v-table density="compact">
                                <thead>
                                    <tr>
                                        <th class="text-left">Business date</th>
                                        <th class="text-left">Status</th>
                                        <th class="text-right">Transactions</th>
                                        <th class="text-right">Total</th>
                                        <th class="text-left">Journal entry</th>
                                        <th></th>
                                    </tr>
                                </thead>
                                <tbody>
                                    <tr v-for="row in syncLog" :key="row.businessDate">
                                        <td>{{ formatDate(row.businessDate) }}</td>
                                        <td>
                                            <v-chip :color="rowColor(row.status)" size="x-small">{{ rowLabel(row.status) }}</v-chip>
                                            <div v-if="row.lastError" class="text-caption text-error mt-1">{{ row.lastError }}</div>
                                        </td>
                                        <td class="text-right">{{ row.entryCount }}</td>
                                        <td class="text-right">{{ formatMoney(row.totalDebitsCents) }}</td>
                                        <td>
                                            <code v-if="row.qboDocNumber" class="text-caption">{{ row.qboDocNumber }}</code>
                                            <span v-else class="text-medium-emphasis text-caption">, </span>
                                        </td>
                                        <td class="text-right">
                                            <v-btn v-if="row.status === 'failed'" size="x-small" variant="text"
                                                :loading="resyncing === row.businessDate" @click="resync(row.businessDate)">
                                                Retry
                                            </v-btn>
                                        </td>
                                    </tr>
                                    <tr v-if="!syncLog.length && !loading">
                                        <td colspan="6" class="text-center text-medium-emphasis py-4">
                                            Nothing posted yet. The first sync runs tonight, or use "Sync now".
                                        </td>
                                    </tr>
                                </tbody>
                            </v-table>
                        </div>
                    </v-card-text>
                </v-card>
            </v-window-item>
        </v-window>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="5000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { QuickBooksService, type QuickBooksStatus, type QboAccount, type QboMapping, type QboSyncLogRow, type QboClassSettings, type QboClassMapping } from '../../../services/QuickBooksService'
import { useTheme } from 'vuetify'
import { ProfitCenterService, type ProfitCenter } from '../../../services/ProfitCenterService'
import { seriesColor } from '../../../helpers/profitCenterColor'
import { useConfirm } from '../../../composables/useConfirm'

const service = new QuickBooksService()
const profitCenterService = new ProfitCenterService()
const theme = useTheme()
const isDark = computed(() => theme.current.value.dark)
const confirm = useConfirm()
const route = useRoute()
const router = useRouter()

const tab = ref('connection')
const loading = ref(true)
const connectLoading = ref(false)
const disconnectLoading = ref(false)
const toggleLoading = ref(false)
const syncLoading = ref(false)
const saveLoading = ref(false)
const accountsLoading = ref(false)
const resyncing = ref<string | null>(null)

const accountsError = ref('')
const syncEnabled = ref(false)

const status = ref<QuickBooksStatus>({
    isConfigured: false, isConnected: false, status: null, realmId: null, companyName: null,
    syncEnabled: false, syncStartDate: null, lastSyncedDate: null, lastSyncAtUtc: null,
    lastSyncError: null, connectedAtUtc: null, mappingComplete: false, unmappedKeys: [],
})
const accounts = ref<QboAccount[]>([])
const mappings = ref<QboMapping[]>([])
const syncLog = ref<QboSyncLogRow[]>([])

// The tenant's profit centers (Settings > Profit Centers). When configured, the revenue section
// asks for ONE account per center and expands it to the member slots on save. Empty = flat list.
const profitCenters = ref<ProfitCenter[]>([])
// Per-center account choice, keyed by center id.
const centerAccounts = ref<Record<string, string | null>>({})
// True when a center's member slots currently point at different accounts (pre-centers drift).
const centerMixed = ref<Record<string, boolean>>({})

// Profit centers -> QuickBooks classes. Optional, and the only part of this screen that needs a
// QBO preference read, so it loads lazily the first time the tab is opened rather than on mount.
const classSettings = ref<QboClassSettings>({ trackingEnabled: true, trackingPerLine: true, classes: [] })
const classMappings = ref<QboClassMapping[]>([])
const classesLoading = ref(false)
const classesLoaded = ref(false)
const classesError = ref('')
const saveClassesLoading = ref(false)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

// The accounts/history tabs only exist for a connected company; if the user disconnects while
// sitting on one, land them back on Connection instead of a disabled tab's stale panel.
watch(() => status.value.isConnected, connected => {
    if (!connected && tab.value !== 'connection') tab.value = 'connection'
})

// First visit to the Profit centers tab pulls the class list and the company's class-tracking
// preference. Both are QBO round-trips, so a track that never uses classes never pays for them.
watch(tab, async value => {
    if (value === 'classes' && !classesLoaded.value) await loadClasses()
})

function toast(text: string, color: 'success' | 'error' = 'success') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

/** Prefer the server's own message, it names the QuickBooks account or day that's wrong. */
function errText(err: any, fallback: string): string {
    return err?.response?.data?.error || fallback
}

// 'error' and 'expired' both mean "we can't post" but they need different words: expired is fixed
// by reconnecting, error might just be a bad account mapping.
const statusColor = computed(() => {
    switch (status.value.status) {
        case 'active': return status.value.mappingComplete ? 'success' : 'warning'
        case 'expired': return 'warning'
        case 'error': return 'error'
        case 'revoked': return 'error'
        default: return 'grey'
    }
})

const statusIcon = computed(() => {
    switch (status.value.status) {
        case 'active': return status.value.mappingComplete ? 'mdi-check-circle' : 'mdi-alert-circle-outline'
        case 'expired': return 'mdi-clock-alert-outline'
        default: return 'mdi-alert-circle'
    }
})

const statusLabel = computed(() => {
    switch (status.value.status) {
        case 'active': return status.value.mappingComplete ? 'Syncing' : 'Setup incomplete'
        case 'expired': return 'Reconnect needed'
        case 'revoked': return 'Access revoked'
        case 'error': return 'Attention needed'
        default: return 'Unknown'
    }
})

function rowColor(s: string) {
    return s === 'success' ? 'success' : s === 'failed' ? 'error' : 'grey'
}
function rowLabel(s: string) {
    return s === 'success' ? 'Posted' : s === 'failed' ? 'Failed' : 'No activity'
}

function formatDate(d: string | null) {
    if (!d) return ', '
    // The API sends a plain calendar date (the track's local business date). Parsing it with the
    // Date constructor would treat it as UTC midnight and shift it a day back in western
    // timezones, so split it rather than round-trip through Date.
    const [y, m, day] = d.slice(0, 10).split('-')
    return `${m}/${day}/${y}`
}

function formatMoney(cents: number) {
    return (cents / 100).toLocaleString(undefined, { style: 'currency', currency: 'USD' })
}

// The rows grouped by classification, in a fixed money-flow order, so the section heading says
// "these are income accounts" ONCE instead of a per-row hint under every dropdown repeating it.
const GROUP_ORDER: { classification: string; title: string; caption: string }[] = [
    { classification: 'Revenue',   title: 'Revenue',           caption: 'where each kind of sale is booked as income' },
    { classification: 'Liability', title: 'Liabilities',       caption: 'money you hold that isn\'t yours yet: tax, tips, gift card balances' },
    { classification: 'Asset',     title: 'Assets',            caption: 'where money sits before it reaches your bank' },
    { classification: 'Expense',   title: 'Fees & expenses',   caption: 'processing and platform fees' },
]

const mappingGroups = computed(() =>
    GROUP_ORDER
        .map(g => ({ ...g, rows: mappings.value.filter(m => m.expectedClassification === g.classification) }))
        // A classification QBO never asked us for (or a future one this build doesn't know) still
        // renders: anything unmatched lands in a trailing group rather than silently disappearing.
        .concat([{
            classification: '', title: 'Other', caption: '',
            rows: mappings.value.filter(m => !GROUP_ORDER.some(g => g.classification === m.expectedClassification)),
        }])
        .filter(g => g.rows.length > 0)
)

const revenueRows = computed(() => mappings.value.filter(m => m.expectedClassification === 'Revenue'))

// Only centers that actually own one of THIS tenant's required revenue slots render; a center
// whose streams the tenant can't use (feature off) would just be an empty picker.
const centerGroups = computed(() =>
    profitCenters.value
        .map(c => ({ center: c, rows: revenueRows.value.filter(m => c.revenueKeys.includes(m.mappingKey)) }))
        .filter(g => g.rows.length > 0))

const ungroupedRevenueRows = computed(() => {
    const grouped = new Set(centerGroups.value.flatMap(g => g.rows.map(r => r.mappingKey)))
    return revenueRows.value.filter(m => !grouped.has(m.mappingKey))
})

function centerHint(g: { center: ProfitCenter; rows: QboMapping[] }): string {
    const streams = g.rows.map(r => r.label).join(', ')
    return centerMixed.value[g.center.id]
        ? `Currently split across multiple accounts; pick one to unify. Includes: ${streams}`
        : `Includes: ${streams}`
}

// Seed each center's picker from the saved per-slot mappings: unanimous non-empty slots show
// their shared account; anything else starts blank (saving then keeps the per-slot values).
function initCenterAccounts() {
    const acc: Record<string, string | null> = {}
    const mixed: Record<string, boolean> = {}
    for (const g of centerGroups.value) {
        const ids = [...new Set(g.rows.map(r => r.qboAccountId).filter((id): id is string => !!id))]
        const anyBlank = g.rows.some(r => !r.qboAccountId)
        acc[g.center.id] = ids.length === 1 && !anyBlank ? ids[0] : null
        mixed[g.center.id] = ids.length > 1
    }
    centerAccounts.value = acc
    centerMixed.value = mixed
}

function classHint(row: QboClassMapping): string {
    const streams = row.revenueStreams.join(', ')
    return streams ? `Tags: ${streams}` : 'No revenue streams report under this center yet.'
}

// True when the tenant has never configured centers and is seeing the built-in departments, which
// is worth saying out loud: the names aren't theirs and they can change that.
const usingBuiltInDepartments = computed(() =>
    classMappings.value.length > 0 && classMappings.value.every(r => !r.isCustom))

function accountsFor(classification: string) {
    const matching = accounts.value.filter(a => a.classification === classification)
    // If QBO didn't classify anything (unusual, but possible on odd charts of accounts), showing
    // an empty dropdown would look broken and leave the tenant stuck. Fall back to everything.
    return matching.length ? matching : accounts.value
}

async function load() {
    loading.value = true
    try {
        const resp = await service.status()
        status.value = resp.data.data
        syncEnabled.value = status.value.syncEnabled

        if (status.value.isConnected) {
            await Promise.all([loadMappings(), loadSyncLog(), loadAccounts(), loadProfitCenters()])
            initCenterAccounts()
        }
    } catch (err: any) {
        toast(errText(err, 'Could not load your QuickBooks settings. Refresh the page to try again.'), 'error')
    } finally {
        loading.value = false
    }
}

async function loadAccounts() {
    accountsLoading.value = true
    accountsError.value = ''
    try {
        const resp = await service.accounts()
        accounts.value = resp.data.data
    } catch (err: any) {
        // Don't leave an empty dropdown looking like "you have no accounts", say what happened.
        accountsError.value = errText(err, 'Could not load your QuickBooks chart of accounts.')
    } finally {
        accountsLoading.value = false
    }
}

async function loadClasses() {
    classesLoading.value = true
    classesError.value = ''
    try {
        const [settings, mappings] = await Promise.all([service.classes(), service.classMappings()])
        classSettings.value = settings.data.data
        classMappings.value = mappings.data.data
        classesLoaded.value = true
    } catch (err: any) {
        // Never render an empty picker as "you have no classes"; say what actually failed.
        classesError.value = errText(err, 'Could not load your QuickBooks classes. Check the connection and try again.')
    } finally {
        classesLoading.value = false
    }
}

async function saveClassMappings() {
    saveClassesLoading.value = true
    try {
        await service.saveClassMappings(classMappings.value.map(row => ({
            bucketKey: row.bucketKey,
            qboClassId: row.qboClassId,
            qboClassName: classSettings.value.classes.find(c => c.id === row.qboClassId)?.fullyQualifiedName ?? null,
        })))
        toast('Profit centers saved. They apply to the next day we post.')
        await loadClasses()
    } catch (err: any) {
        toast(errText(err, 'Could not save your profit center classes.'), 'error')
    } finally {
        saveClassesLoading.value = false
    }
}

async function loadMappings() {
    try {
        const resp = await service.mappings()
        mappings.value = resp.data.data
    } catch (err: any) {
        toast(errText(err, 'Could not load your account mapping.'), 'error')
    }
}

async function loadProfitCenters() {
    try {
        const resp = await profitCenterService.get()
        profitCenters.value = resp.data.data.usingDefaults ? [] : resp.data.data.centers
    } catch (err: any) {
        // The flat per-slot list still works without them; say why the grouping is missing.
        profitCenters.value = []
        toast(errText(err, 'Could not load your profit centers; showing the ungrouped account list.'), 'error')
    }
}

async function loadSyncLog() {
    try {
        const resp = await service.syncLog()
        syncLog.value = resp.data.data
    } catch (err: any) {
        toast(errText(err, 'Could not load the QuickBooks sync history.'), 'error')
    }
}

async function connect() {
    connectLoading.value = true
    try {
        const resp = await service.connect()
        // Full navigation, not a popup: Intuit blocks its consent screen in an iframe, and the
        // callback comes back to the apex host before redirecting here.
        window.location.href = resp.data.data.authorizationUrl
    } catch (err: any) {
        toast(errText(err, 'Could not start the QuickBooks connection. Please try again.'), 'error')
        connectLoading.value = false
    }
}

async function disconnect() {
    const ok = await confirm({
        title: 'Disconnect QuickBooks?',
        message: 'New sales will stop syncing. Journal entries already posted stay in QuickBooks, and '
            + 'your account mapping is kept in case you reconnect.',
        confirmText: 'Disconnect',
        confirmColor: 'error',
    })
    if (!ok) return

    disconnectLoading.value = true
    try {
        await service.disconnect()
        // Drop the cached class list with the company it came from: reconnecting to a DIFFERENT
        // QuickBooks company must not show the old one's classes.
        classesLoaded.value = false
        classSettings.value = { trackingEnabled: true, trackingPerLine: true, classes: [] }
        classMappings.value = []
        toast('QuickBooks disconnected.')
        await load()
    } catch (err: any) {
        toast(errText(err, 'Could not disconnect QuickBooks. Please try again.'), 'error')
    } finally {
        disconnectLoading.value = false
    }
}

async function onToggleSync(value: boolean | null) {
    const enabled = !!value
    toggleLoading.value = true
    try {
        await service.setSyncEnabled(enabled)
        status.value.syncEnabled = enabled
        toast(enabled ? 'Nightly sync resumed.' : 'Nightly sync paused.')
    } catch (err: any) {
        syncEnabled.value = !enabled   // roll the switch back so it reflects the server
        toast(errText(err, 'Could not change the sync setting. Please try again.'), 'error')
    } finally {
        toggleLoading.value = false
    }
}

async function syncNow() {
    syncLoading.value = true
    try {
        const resp = await service.syncNow()
        const { posted, skipped } = resp.data.data
        toast(posted
            ? `Posted ${posted} ${posted === 1 ? 'day' : 'days'} to QuickBooks.`
            : `Already up to date${skipped ? ` (${skipped} ${skipped === 1 ? 'day' : 'days'} had no activity)` : ''}.`)
        await load()
    } catch (err: any) {
        toast(errText(err, 'The QuickBooks sync could not be completed.'), 'error')
        await load()   // surface the per-day error the server just recorded
    } finally {
        syncLoading.value = false
    }
}

async function resync(businessDate: string) {
    resyncing.value = businessDate
    try {
        await service.resync(businessDate)
        toast(`${formatDate(businessDate)} posted to QuickBooks.`)
        await load()
    } catch (err: any) {
        toast(errText(err, `Could not post ${formatDate(businessDate)} to QuickBooks.`), 'error')
        await loadSyncLog()
    } finally {
        resyncing.value = null
    }
}

async function saveMappings() {
    saveLoading.value = true
    try {
        // A slot inside a profit center takes the CENTER's chosen account; a center left blank
        // keeps each slot's existing value (so an unresolved "mixed" center is never clobbered).
        const centerByKey = new Map<string, string>()
        for (const g of centerGroups.value) {
            for (const r of g.rows) centerByKey.set(r.mappingKey, g.center.id)
        }
        await service.saveMappings(mappings.value.map(m => {
            const centerId = centerByKey.get(m.mappingKey)
            const accountId = (centerId && centerAccounts.value[centerId]) || m.qboAccountId
            return {
                mappingKey: m.mappingKey,
                qboAccountId: accountId,
                qboAccountName: accounts.value.find(a => a.id === accountId)?.name ?? null,
            }
        }))
        toast('Account mapping saved.')
        await load()
    } catch (err: any) {
        toast(errText(err, 'Could not save the account mapping.'), 'error')
    } finally {
        saveLoading.value = false
    }
}

onMounted(async () => {
    // The OAuth callback lands on the apex and bounces back here with a result flag. Consume it and
    // strip it from the URL so a refresh doesn't re-announce a stale outcome.
    const connected = route.query.qboConnected
    const oauthError = route.query.qboError
    if (connected || oauthError) {
        await router.replace({ query: {} })
        if (connected) toast('QuickBooks connected. Map your accounts in the Chart of accounts tab to start syncing.')
        if (oauthError) toast(String(oauthError), 'error')
    }
    await load()
    // Fresh from the OAuth callback, the next step is mapping accounts; put the user on that tab.
    if (connected && status.value.isConnected && !status.value.mappingComplete) tab.value = 'accounts'
})
</script>

<script lang="ts">
export default { name: 'AdminSettingsQuickBooks' }
</script>

<style scoped>
/* Wide table must scroll inside its own box rather than the page scrolling sideways. */
.table-scroll {
    overflow-x: auto;
}

/* Profit-center color beside its account picker. Sized to sit level with a compact v-select
   that carries a persistent hint, so the row reads as one line. */
.swatch {
    display: inline-block;
    flex: 0 0 auto;
    width: 14px;
    height: 14px;
    border-radius: 4px;
    margin-bottom: 22px;
    box-shadow: inset 0 0 0 1px rgba(var(--v-theme-on-surface), 0.2);
}
</style>
