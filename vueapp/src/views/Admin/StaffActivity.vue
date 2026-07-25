<template>
    <v-container>
        <h1 class="text-h4 mb-1">Staff Activity</h1>
        <p class="text-body-2 text-medium-emphasis mb-4">
            Money movements and data exports recorded as they happen: refunds, voided sales, store
            credit adjustments, and rider exports. Times are shown in this track's timezone.
        </p>

        <!-- Everyone can see their own; only audit.view sees the whole track. -->
        <div v-if="canSeeAll" class="sub-tabs mb-4">
            <button class="sub-tab" :class="{ active: scope === 'all' }" @click="setScope('all')">Everyone</button>
            <button class="sub-tab" :class="{ active: scope === 'mine' }" @click="setScope('mine')">Just me</button>
        </div>

        <div class="d-flex align-center flex-wrap ga-2 mb-3">
            <v-select v-if="scope === 'all'" v-model="actionFilter" :items="actionItems"
                label="Action" density="compact" variant="outlined" hide-details clearable
                style="max-width: 260px"></v-select>
            <v-text-field v-model="fromDate" type="date" label="From" density="compact"
                variant="outlined" hide-details style="max-width: 170px"></v-text-field>
            <v-text-field v-model="toDate" type="date" label="To" density="compact"
                variant="outlined" hide-details style="max-width: 170px"></v-text-field>
            <v-btn color="primary" variant="tonal" :loading="loading" @click="load">Apply</v-btn>
            <v-spacer></v-spacer>
            <v-btn variant="text" prepend-icon="mdi-refresh" :loading="loading" @click="load">Refresh</v-btn>
        </div>

        <v-alert v-if="loadError" type="error" variant="tonal" density="compact" class="mb-3">
            {{ loadError }}
        </v-alert>

        <v-card>
            <v-table density="compact">
                <thead>
                    <tr>
                        <th style="width: 170px">When</th>
                        <th v-if="scope === 'all'" style="width: 200px">Who</th>
                        <th style="width: 190px">Action</th>
                        <th>What happened</th>
                        <th style="width: 140px">From</th>
                        <th style="width: 40px"></th>
                    </tr>
                </thead>
                <tbody>
                    <template v-for="a in rows" :key="a.id">
                        <tr>
                            <td class="text-no-wrap">{{ formatInTenant(a.createdAtUtc) }}</td>
                            <td v-if="scope === 'all'">
                                <div>{{ a.actorEmail || 'Unknown' }}</div>
                                <div class="text-caption text-medium-emphasis">{{ roleLabel(a.actorRole) }}</div>
                            </td>
                            <td>
                                <v-chip size="x-small" :color="actionColor(a.action)" variant="tonal">
                                    {{ actionLabel(a.action) }}
                                </v-chip>
                            </td>
                            <td>{{ a.summary }}</td>
                            <td class="text-no-wrap">
                                <v-tooltip v-if="!a.ipAddress || isProxyIp(a.ipAddress)" location="top">
                                    <template #activator="{ props }">
                                        <span v-bind="props" class="text-medium-emphasis">not recorded</span>
                                    </template>
                                    <span>Logged before real client addresses were captured.</span>
                                </v-tooltip>
                                <span v-else>{{ a.ipAddress }}</span>
                            </td>
                            <td>
                                <v-btn v-if="a.metadata" :icon="expanded === a.id ? 'mdi-chevron-up' : 'mdi-chevron-down'"
                                    size="x-small" variant="text"
                                    @click="expanded = expanded === a.id ? null : a.id"></v-btn>
                            </td>
                        </tr>
                        <tr v-if="expanded === a.id && a.metadata">
                            <td :colspan="scope === 'all' ? 6 : 5" class="pa-0">
                                <pre class="detail-json">{{ prettyJson(a.metadata) }}</pre>
                            </td>
                        </tr>
                    </template>
                    <tr v-if="!loading && rows.length === 0">
                        <td :colspan="scope === 'all' ? 6 : 5" class="text-center text-medium-emphasis py-8">
                            Nothing recorded for this period.
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <p v-if="rows.length >= 500" class="text-caption text-medium-emphasis mt-2">
            Showing the most recent 500 entries. Narrow the dates to see further back.
        </p>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { StaffActivityService, type StaffActivityItem } from '@/services/StaffActivityService'
import { branding } from '@/stores/branding'
import authHelper from '@/helpers/AuthHelper'
import { Perm } from '@/helpers/TenantPermissions'

const service = new StaffActivityService()

const rows = ref<StaffActivityItem[]>([])
const loading = ref(false)
const loadError = ref('')
const expanded = ref<string | null>(null)

const canSeeAll = computed(() => authHelper.hasPermission(Perm.AuditView))
const scope = ref<'all' | 'mine'>('mine')

const actionFilter = ref<string | null>(null)
const actionItems = ref<{ title: string; value: string }[]>([])
const fromDate = ref(dayjs().subtract(30, 'day').format('YYYY-MM-DD'))
const toDate = ref(dayjs().format('YYYY-MM-DD'))

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')
function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

function setScope(next: 'all' | 'mine') {
    if (scope.value === next) return
    scope.value = next
    actionFilter.value = null
    load()
}

function formatInTenant(utc: string): string {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('MMM D, YYYY h:mm A')
}

/** Entries written before the forwarded-headers fix all carry the proxy's loopback address. */
function isProxyIp(ip: string | null): boolean {
    return ip === '127.0.0.1' || ip === '::1'
}

function actionLabel(action: string): string {
    switch (action) {
        case 'purchase.refund': return 'Refund'
        case 'purchase.cancel': return 'Cancelled sale'
        case 'concession.refund': return 'F&B refund'
        case 'shop.refund': return 'Shop refund'
        case 'credit.manual_adjust': return 'Credit adjusted'
        case 'concession.manager_pin_failed': return 'PIN failed'
        case 'report.export_trackside': return 'Rider export'
        default: return action
    }
}

/** Money leaving and data leaving are the two things worth spotting at a glance. */
function actionColor(action: string): string {
    if (action.endsWith('.refund') || action === 'credit.manual_adjust') return 'warning'
    if (action === 'purchase.cancel') return 'orange'
    if (action === 'concession.manager_pin_failed') return 'error'
    if (action.startsWith('report.export')) return 'info'
    return 'default'
}

function roleLabel(role: string | null): string {
    if (!role) return ''
    return role.replace(/^tenant_/, '').replace(/_/g, ' ')
}

function prettyJson(raw: string): string {
    try {
        return JSON.stringify(JSON.parse(raw), null, 2)
    } catch {
        // Metadata is written by the server as JSON, but never let a malformed row break the row.
        return raw
    }
}

async function loadActions() {
    if (!canSeeAll.value) return
    try {
        const r = await service.actions()
        actionItems.value = r.data.data.map(a => ({ title: actionLabel(a), value: a }))
    } catch {
        // The dropdown is a convenience; the table below is the point, and it loads separately.
        actionItems.value = []
    }
}

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        const fromUtc = fromDate.value ? dayjs(fromDate.value).startOf('day').toISOString() : null
        // Exclusive upper bound on the server, so add a day to make the To date inclusive.
        const toUtc = toDate.value ? dayjs(toDate.value).add(1, 'day').startOf('day').toISOString() : null
        const take = 500
        const r = scope.value === 'all'
            ? await service.list({ action: actionFilter.value, fromUtc, toUtc, take })
            : await service.mine({ fromUtc, toUtc, take })
        rows.value = r.data.data
    } catch (err: any) {
        const msg = err.response?.data?.error
            || 'Could not load staff activity. Check your connection and try again.'
        loadError.value = msg
        flash(msg, 'error')
        rows.value = []
    } finally {
        loading.value = false
    }
}

onMounted(async () => {
    // Admins land on the whole track; everyone else only ever has their own.
    scope.value = canSeeAll.value ? 'all' : 'mine'
    await Promise.all([loadActions(), load()])
})
</script>

<style scoped>
.sub-tabs {
    display: inline-flex;
    gap: 4px;
    padding: 4px;
    border-radius: 999px;
    background: rgba(var(--v-theme-on-surface), 0.06);
}
.sub-tab {
    padding: 4px 14px;
    border-radius: 999px;
    font-size: 0.875rem;
    font-weight: 500;
    color: rgba(var(--v-theme-on-surface), 0.7);
    transition: background-color 0.15s, color 0.15s;
}
.sub-tab.active {
    background: rgb(var(--v-theme-surface));
    color: rgb(var(--v-theme-primary));
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.12);
}
.detail-json {
    margin: 0;
    padding: 12px 16px;
    font-size: 0.75rem;
    line-height: 1.5;
    white-space: pre-wrap;
    word-break: break-word;
    background: rgba(var(--v-theme-on-surface), 0.04);
}
</style>
