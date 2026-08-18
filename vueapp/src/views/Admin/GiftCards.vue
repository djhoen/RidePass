<template>
    <v-container>
        <div class="d-flex align-center mb-4 ga-3 flex-wrap">
            <h1 class="text-h4">Gift Cards</h1>
            <v-spacer></v-spacer>
            <v-btn v-if="canManage" variant="tonal" prepend-icon="mdi-file-upload-outline" @click="importOpen = true">Import</v-btn>
        </div>

        <v-card>
            <v-card-text>
                <div class="d-flex ga-3 flex-wrap align-center mb-2">
                    <v-text-field v-model="search" density="compact" hide-details clearable
                        prepend-inner-icon="mdi-magnify" style="max-width: 360px"
                        placeholder="Exact code, or part of a name / email" @keyup.enter="resetAndLoad"
                        @click:clear="search = ''; resetAndLoad()"></v-text-field>
                    <v-select v-model="status" :items="statusItems" density="compact" hide-details clearable
                        label="Status" style="max-width: 180px" @update:model-value="resetAndLoad"></v-select>
                    <v-btn size="small" variant="text" @click="resetAndLoad">Search</v-btn>
                </div>

                <div v-if="loading" class="text-center py-8"><v-progress-circular indeterminate /></div>
                <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>
                <template v-else>
                    <v-table density="compact">
                        <thead>
                            <tr>
                                <th>Code</th><th>Recipient</th><th class="text-right">Balance</th>
                                <th class="text-right">Original</th><th>Status</th><th>Source</th><th>Created</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="c in rows" :key="c.id" class="row-click" @click="openDetail(c.id)">
                                <td class="font-mono">{{ c.codeMasked }}</td>
                                <td>
                                    {{ c.recipientName || c.recipientEmail || (c.imported ? '—' : c.buyerName || '—') }}
                                </td>
                                <td class="text-right font-weight-medium">{{ money(c.balanceCents) }}</td>
                                <td class="text-right text-medium-emphasis">{{ money(c.initialAmountCents) }}</td>
                                <td><v-chip size="x-small" :color="statusColor(c.status)" variant="tonal">{{ c.status }}</v-chip></td>
                                <td>
                                    <v-chip v-if="c.imported" size="x-small" variant="tonal" color="secondary">
                                        Imported
                                        <v-tooltip v-if="c.importedFrom" activator="parent" location="top">{{ c.importedFrom }}</v-tooltip>
                                    </v-chip>
                                    <span v-else class="text-caption text-medium-emphasis">Purchased</span>
                                </td>
                                <td class="text-caption text-medium-emphasis">{{ formatWhen(c.createdAt) }}</td>
                            </tr>
                        </tbody>
                    </v-table>
                    <div v-if="rows.length === 0" class="text-center text-medium-emphasis py-8">
                        {{ search || status ? 'No gift cards match.' : 'No gift cards yet. Cards bought online appear here; use Import to bring balances over from a previous system.' }}
                    </div>
                    <div v-if="pageCount > 1" class="d-flex justify-center mt-2">
                        <v-pagination v-model="page" :length="pageCount" density="compact"
                            @update:model-value="load"></v-pagination>
                    </div>
                </template>
            </v-card-text>
        </v-card>

        <!-- Detail -->
        <v-dialog v-model="detailOpen" max-width="560">
            <v-card v-if="detailLoading" class="pa-8 text-center"><v-progress-circular indeterminate /></v-card>
            <v-card v-else-if="detail">
                <v-card-title class="d-flex align-center">
                    Gift card
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="detailOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div class="d-flex align-center ga-3 mb-3">
                        <span class="text-h6 font-mono">{{ detail.code }}</span>
                        <v-chip size="small" :color="statusColor(detail.status)" variant="tonal">{{ detail.status }}</v-chip>
                        <v-chip v-if="detail.imported" size="small" variant="tonal" color="secondary">Imported</v-chip>
                    </div>
                    <div class="d-flex justify-space-between text-body-1">
                        <span class="text-medium-emphasis">Balance</span>
                        <span class="font-weight-bold">{{ money(detail.balanceCents) }} of {{ money(detail.initialAmountCents) }}</span>
                    </div>
                    <v-divider class="my-3"></v-divider>
                    <div v-if="detail.imported" class="text-body-2 mb-1">
                        Imported {{ detail.importedAt ? formatWhen(detail.importedAt) : '' }}
                        <span v-if="detail.importedFrom" class="text-medium-emphasis">from {{ detail.importedFrom }}</span>
                    </div>
                    <template v-else>
                        <div class="text-body-2">Bought by {{ detail.buyerName || '—' }}
                            <span v-if="detail.buyerEmail" class="text-medium-emphasis">({{ detail.buyerEmail }})</span></div>
                    </template>
                    <div v-if="detail.recipientName || detail.recipientEmail" class="text-body-2">
                        For {{ detail.recipientName || '—' }}
                        <span v-if="detail.recipientEmail" class="text-medium-emphasis">({{ detail.recipientEmail }})</span>
                    </div>
                    <div class="text-caption text-medium-emphasis mt-1">Created {{ formatWhen(detail.createdAt) }}</div>

                    <template v-if="detail.redemptions.length">
                        <v-divider class="my-3"></v-divider>
                        <div class="text-subtitle-2 mb-1">Redemptions</div>
                        <div v-for="(r, i) in detail.redemptions" :key="i" class="d-flex justify-space-between text-body-2">
                            <span class="text-medium-emphasis">{{ kindLabel(r.sourceKind) }} · {{ formatWhen(r.redeemedAt) }}</span>
                            <span>-{{ money(r.amountCents) }}</span>
                        </div>
                    </template>
                </v-card-text>
                <v-card-actions>
                    <v-btn v-if="detail.status === 'active' && canManage" variant="text" color="error"
                        :loading="voiding" @click="voidCard">Void card</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="detailOpen = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <GiftCardImportDialog v-model="importOpen" @imported="resetAndLoad" @flash="flash" />

        <v-snackbar v-model="snackbar" :color="snackColor" :timeout="3500">{{ snackText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { GiftCardService, type GiftCardAdminRow, type GiftCardAdminDetail } from '@/services/GiftCardService'
import GiftCardImportDialog from '@/components/GiftCardImportDialog.vue'
import { useConfirm } from '@/composables/useConfirm'
import { formatTenantDateTime } from '@/helpers/TenantTime'
import authHelper from '@/helpers/AuthHelper'
import { Perm } from '@/helpers/TenantPermissions'

const service = new GiftCardService()
const confirm = useConfirm()

const rows = ref<GiftCardAdminRow[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = 25
const search = ref('')
const status = ref<string | null>(null)
const loading = ref(false)
const loadError = ref('')
const importOpen = ref(false)

const snackbar = ref(false)
const snackText = ref('')
const snackColor = ref<'success' | 'error'>('success')
function flash(text: string, color: 'success' | 'error' = 'success') {
    snackText.value = text; snackColor.value = color; snackbar.value = true
}

const statusItems = [
    { title: 'Active', value: 'active' }, { title: 'Depleted', value: 'depleted' },
    { title: 'Pending', value: 'pending' }, { title: 'Refunded', value: 'refunded' },
    { title: 'Void', value: 'void' },
]
const pageCount = computed(() => Math.max(1, Math.ceil(total.value / pageSize)))
// Import + void live behind settings.manage server-side; hide the buttons to match.
const canManage = computed(() => authHelper.hasPermission(Perm.SettingsManage))

function money(cents: number) { return `$${(cents / 100).toFixed(2)}` }
function formatWhen(iso: string) { return formatTenantDateTime(iso, 'MMM D, YYYY h:mm A') }
function statusColor(s: string) {
    return s === 'active' ? 'success' : s === 'depleted' ? 'grey' : s === 'pending' ? 'warning' : 'error'
}
function kindLabel(k: string) {
    return ({ pass: 'Day pass', event_ticket: 'Event ticket', season_pass: 'Season pass', rental: 'Rental', shop_sale: 'Bike shop' } as Record<string, string>)[k] ?? k
}

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        const r = (await service.adminList({
            search: search.value.trim() || undefined,
            status: status.value || undefined,
            page: page.value, pageSize,
        }) as any).data.data
        rows.value = r.items
        total.value = r.total
    } catch (err: any) {
        loadError.value = err.response?.data?.error || 'Could not load gift cards. Refresh to try again.'
    } finally { loading.value = false }
}

function resetAndLoad() { page.value = 1; load() }

// ── Detail + void ─────────────────────────────────────────────────────────────
const detailOpen = ref(false)
const detailLoading = ref(false)
const detail = ref<GiftCardAdminDetail | null>(null)
const voiding = ref(false)

async function openDetail(id: string) {
    detailOpen.value = true
    detailLoading.value = true
    detail.value = null
    try {
        detail.value = (await service.adminGet(id) as any).data.data
    } catch (err: any) {
        detailOpen.value = false
        flash(err.response?.data?.error || 'Could not load that gift card.', 'error')
    } finally { detailLoading.value = false }
}

async function voidCard() {
    if (!detail.value) return
    if (!await confirm({
        title: 'Void gift card?',
        message: `Void ${detail.value.code} with ${money(detail.value.balanceCents)} remaining? It can no longer be redeemed. This can't be undone.`,
        confirmText: 'Void card', confirmColor: 'error',
    })) return
    voiding.value = true
    try {
        await service.adminVoid(detail.value.id)
        flash('Gift card voided.', 'success')
        detailOpen.value = false
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not void the card. It may have already been spent or voided.', 'error')
    } finally { voiding.value = false }
}

onMounted(load)
</script>

<style scoped>
.row-click { cursor: pointer; }
.font-mono { font-family: monospace; }
</style>
