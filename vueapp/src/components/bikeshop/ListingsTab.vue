<template>
    <div>
        <v-alert v-if="unavailable" type="info" variant="tonal" density="compact" class="mb-4">
            Used and ex-rental sales are not available on this server yet.
        </v-alert>

        <template v-else>
            <!-- KPIs across every listing, not just the filtered set: the chips narrow the table, not the numbers. -->
            <div class="d-flex ga-6 flex-wrap mb-3 px-1">
                <div class="rp-kpi">
                    <div class="rp-kpi-label">On the floor</div>
                    <div class="rp-kpi-value">{{ kpis.activeCount }}</div>
                </div>
                <div class="rp-kpi">
                    <div class="rp-kpi-label">Asking</div>
                    <div class="rp-kpi-value">{{ money(kpis.askingCents) }}</div>
                </div>
                <div class="rp-kpi">
                    <div class="rp-kpi-label">Sold</div>
                    <div class="rp-kpi-value">{{ kpis.soldCount }}<span v-if="kpis.soldCount" class="text-caption text-muted ml-2">{{ money(kpis.soldCents) }}</span></div>
                </div>
                <div v-if="kpis.marginKnown" class="rp-kpi">
                    <div class="rp-kpi-label">Margin on sold</div>
                    <div class="rp-kpi-value" :class="kpis.marginCents < 0 ? 'text-error' : ''">{{ money(kpis.marginCents) }}</div>
                </div>
            </div>

            <div class="d-flex align-center ga-2 flex-wrap mb-3">
                <v-chip-group v-model="filter" mandatory>
                    <v-chip value="active" filter variant="tonal" size="small">Active</v-chip>
                    <v-chip value="sold" filter variant="tonal" size="small">Sold</v-chip>
                    <v-chip value="unlisted" filter variant="tonal" size="small">Unlisted</v-chip>
                    <v-chip value="all" filter variant="tonal" size="small">All</v-chip>
                </v-chip-group>
                <v-spacer></v-spacer>
                <v-btn size="small" variant="text" prepend-icon="mdi-refresh" :loading="loading" @click="reload">Refresh</v-btn>
            </div>

            <v-card>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th style="width: 52px"></th>
                            <th>Listing</th>
                            <th style="width: 110px">Condition</th>
                            <th style="width: 150px" class="text-right">Asking</th>
                            <th style="width: 110px">State</th>
                            <th style="width: 90px">Online</th>
                            <th style="width: 100px"></th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-if="loading && listings.length === 0">
                            <td colspan="7" class="text-center py-6"><v-progress-circular indeterminate size="24"></v-progress-circular></td>
                        </tr>
                        <tr v-else-if="loadError">
                            <td colspan="7" class="text-center text-error py-6">{{ loadError }}</td>
                        </tr>
                        <tr v-else-if="rows.length === 0">
                            <td colspan="7" class="text-center text-medium-emphasis py-6">
                                {{ listings.length === 0
                                    ? 'Nothing listed yet. Open a serialized variant\'s units in the Catalog and choose "List for sale".'
                                    : 'No listings match that filter.' }}
                            </td>
                        </tr>
                        <tr v-for="l in rows" :key="l.id">
                            <td>
                                <v-avatar size="40" rounded="lg" style="background: var(--rp-raise)">
                                    <v-img v-if="l.coverUrl" :src="absoluteUrl(l.coverUrl) ?? ''" cover></v-img>
                                    <v-icon v-else size="20" class="text-muted2">mdi-bike</v-icon>
                                </v-avatar>
                            </td>
                            <td>
                                <div class="font-weight-medium">{{ l.title }}</div>
                                <div class="text-caption text-muted">
                                    {{ l.unitLabel }}<span v-if="l.serial"> · {{ l.serial }}</span>
                                    <span v-if="l.photos?.length"> · {{ l.photos.length }} photo{{ l.photos.length === 1 ? '' : 's' }}</span>
                                </div>
                                <div v-if="l.state === 'sold' && l.soldOrderNumber != null" class="text-caption text-muted">
                                    Order #{{ l.soldOrderNumber }}<span v-if="l.soldAtUtc"> · {{ formatTenantDate(l.soldAtUtc) }}</span>
                                </div>
                            </td>
                            <td><span class="rp-pill" :class="gradePillClass(l.conditionGrade)">{{ gradeLabel(l.conditionGrade) }}</span></td>
                            <td class="text-right">
                                <div class="tabular">{{ money(l.askingPriceCents) }}</div>
                                <div v-if="l.acquiredCostCents != null" class="text-caption text-muted tabular">
                                    cost {{ money(l.acquiredCostCents) }} ·
                                    <span :class="l.askingPriceCents - l.acquiredCostCents < 0 ? 'text-error' : ''">
                                        {{ marginLabel(l) }}
                                    </span>
                                </div>
                                <div v-else class="text-caption text-muted">cost unknown</div>
                            </td>
                            <td><span class="rp-pill" :class="statePillClass(l.state)">{{ l.state }}</span></td>
                            <td>
                                <v-switch :model-value="l.published" color="primary" density="compact" hide-details
                                    :disabled="!isEditable(l) || togglingId === l.id" :loading="togglingId === l.id"
                                    @update:model-value="togglePublished(l)"></v-switch>
                            </td>
                            <td class="text-right text-no-wrap">
                                <v-tooltip :text="isEditable(l) ? 'Edit listing' : 'View listing'" location="top">
                                    <template #activator="{ props: tp }">
                                        <v-btn v-bind="tp" size="x-small" variant="text" :icon="isEditable(l) ? 'mdi-pencil' : 'mdi-eye-outline'" @click="edit(l)"></v-btn>
                                    </template>
                                </v-tooltip>
                                <v-tooltip v-if="isEditable(l)" :text="`Unlist: ${unlistDestination(l.priorStatus)}`" location="top">
                                    <template #activator="{ props: tp }">
                                        <v-btn v-bind="tp" size="x-small" variant="text" icon="mdi-undo-variant"
                                            :loading="unlistingId === l.id" @click="unlist(l)"></v-btn>
                                    </template>
                                </v-tooltip>
                            </td>
                        </tr>
                    </tbody>
                </v-table>
            </v-card>
        </template>

        <!-- The dialog refreshes the row from GET ShopListing/{id} on open; @refreshed keeps the table in step. -->
        <ListingDialog v-model="dialogOpen" :listing="editing" @saved="onSaved" @refreshed="onSaved" @unlisted="onUnlisted"
            @flash="(t, c) => emit('flash', t, c)" />
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ShopListingService, isMissingEndpoint, gradeLabel, unlistDestination, type ShopListing, type ListingState, type ListingConditionGrade } from '@/services/ShopListingService'
import ListingDialog from '@/components/bikeshop/ListingDialog.vue'
import { absoluteUrl } from '@/helpers/ImageUrl'
import { formatTenantDate } from '@/helpers/TenantTime'
import { useConfirm } from '@/composables/useConfirm'

const emit = defineEmits<{
    (e: 'flash', text: string, color?: 'success' | 'error'): void
    /** A unit changed status (listed / unlisted): the Catalog's unit counts are stale. */
    (e: 'changed'): void
}>()

const service = new ShopListingService()
const confirm = useConfirm()

const listings = ref<ShopListing[]>([])
const loading = ref(false)
const loadError = ref('')
const unavailable = ref(false)
const filter = ref<'active' | 'sold' | 'unlisted' | 'all'>('active')
const togglingId = ref<string | null>(null)
const unlistingId = ref<string | null>(null)

function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
function isEditable(l: ShopListing) { return l.state === 'active' || l.state === 'reserved' }
function statePillClass(s: ListingState): string {
    return s === 'active' ? 'rp-pill--green' : s === 'reserved' ? 'rp-pill--blue' : s === 'sold' ? 'rp-pill--solid' : ''
}
function gradePillClass(g: ListingConditionGrade): string {
    return g === 'excellent' ? 'rp-pill--green' : g === 'fair' ? 'rp-pill--red' : ''
}
function marginLabel(l: ShopListing): string {
    const cost = l.acquiredCostCents ?? 0
    const margin = l.askingPriceCents - cost
    const pct = l.askingPriceCents > 0 ? Math.round((margin / l.askingPriceCents) * 100) : null
    return `${margin < 0 ? '-' : '+'}${money(Math.abs(margin))}${pct != null ? ` (${pct}%)` : ''}`
}

// "Active" includes reserved: a bike mid-checkout is still on the floor from the shop's view.
const rows = computed(() => listings.value.filter(l =>
    filter.value === 'all' ? true
        : filter.value === 'active' ? isEditable(l)
        : l.state === filter.value))

const kpis = computed(() => {
    const active = listings.value.filter(isEditable)
    const sold = listings.value.filter(l => l.state === 'sold')
    const costed = sold.filter(l => l.acquiredCostCents != null)
    return {
        activeCount: active.length,
        askingCents: active.reduce((s, l) => s + l.askingPriceCents, 0),
        soldCount: sold.length,
        soldCents: sold.reduce((s, l) => s + l.askingPriceCents, 0),
        marginKnown: costed.length > 0,
        marginCents: costed.reduce((s, l) => s + (l.askingPriceCents - (l.acquiredCostCents ?? 0)), 0),
    }
})

async function reload() {
    loading.value = true
    loadError.value = ''
    try {
        listings.value = await service.list('all')
        unavailable.value = false
    } catch (e: any) {
        if (isMissingEndpoint(e)) { unavailable.value = true; listings.value = [] }
        else loadError.value = e.response?.data?.error || 'Could not load listings. Check your connection and try again.'
    } finally { loading.value = false }
}

function replace(l: ShopListing) {
    const i = listings.value.findIndex(x => x.id === l.id)
    if (i >= 0) listings.value.splice(i, 1, l); else listings.value.unshift(l)
}

async function togglePublished(l: ShopListing) {
    togglingId.value = l.id
    try {
        const saved = await service.update(l.id, {
            askingPriceCents: l.askingPriceCents,
            conditionGrade: l.conditionGrade,
            conditionNotes: l.conditionNotes,
            title: l.title,
            published: !l.published,
        })
        replace({ ...l, ...saved, published: saved?.published ?? !l.published, photos: saved?.photos ?? l.photos })
        emit('flash', !l.published ? 'Shown online.' : 'Hidden from the store; still sellable at the counter.')
    } catch (e: any) {
        emit('flash', e.response?.data?.error || 'Could not update the listing.', 'error')
    } finally { togglingId.value = null }
}

async function unlist(l: ShopListing) {
    // Where it goes is where it was listed from (priorStatus): the fleet, the bench, or retirement.
    const ok = await confirm({
        title: 'Unlist this bike?',
        message: `${l.title} comes off the sales floor and the store, and the unit goes ${unlistDestination(l.priorStatus)}.`,
        confirmText: 'Unlist',
        confirmColor: 'error',
    })
    if (!ok) return
    unlistingId.value = l.id
    try {
        const r = await service.unlist(l.id)
        const after: ShopListing = { ...l, ...(r ?? {}), state: 'unlisted' }
        replace(after)
        emit('flash', `Unlisted: ${unlistDestination(after.priorStatus)}.`)
        emit('changed')
    } catch (e: any) {
        emit('flash', e.response?.data?.error
            || (e.response?.status === 409 ? 'This unit is sold or someone is checking it out right now; it cannot be unlisted.'
                : 'Could not unlist. Please try again.'), 'error')
    } finally { unlistingId.value = null }
}

// ── Edit dialog ────────────────────────────────────────────────────────────
const dialogOpen = ref(false)
const editing = ref<ShopListing | null>(null)
function edit(l: ShopListing) { editing.value = l; dialogOpen.value = true }
function onSaved(l: ShopListing) { replace(l) }
function onUnlisted(l: ShopListing) { replace(l); emit('changed') }

onMounted(reload)
defineExpose({ reload })
</script>
