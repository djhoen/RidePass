<template>
    <v-dialog :model-value="modelValue" max-width="640" @update:model-value="$emit('update:modelValue', $event)">
        <v-card v-if="variant">
            <v-card-title class="d-flex align-center">
                <span>Units</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" @click="close"></v-btn>
            </v-card-title>
            <v-card-text>
                <v-table density="compact">
                    <thead>
                        <tr><th>Label</th><th>Serial</th><th>Status</th><th></th></tr>
                    </thead>
                    <tbody>
                        <tr v-for="i in items" :key="i.id">
                            <td>{{ i.label }}</td>
                            <td class="text-caption">{{ i.serial || '—' }}</td>
                            <td>
                                <v-chip size="x-small" :color="statusColor(i.status)">{{ statusLabel(i.status) }}</v-chip>
                                <div v-if="isForSale(i) && listingFor(i)" class="text-caption text-muted mt-1 text-no-wrap">
                                    {{ money(listingFor(i)!.askingPriceCents) }} · {{ gradeLabel(listingFor(i)!.conditionGrade) }}
                                </div>
                            </td>
                            <td class="text-right text-no-wrap">
                                <!-- A for_sale unit is owned by its listing: edit the listing, not the unit (the plain
                                     unit PUT would reset its status). -->
                                <v-btn v-if="isForSale(i)" size="x-small" variant="text" color="primary"
                                    :loading="openingListingFor === i.id" @click="editListing(i)">Edit listing</v-btn>
                                <template v-else>
                                    <v-btn v-if="canList(i) && listingsAvailable" size="x-small" variant="text" color="primary"
                                        prepend-icon="mdi-tag-outline" @click="listForSale(i)">List for sale</v-btn>
                                    <v-btn size="x-small" variant="text" icon="mdi-pencil" @click="edit(i)"></v-btn>
                                </template>
                            </td>
                        </tr>
                        <tr v-if="items.length === 0"><td colspan="4" class="text-center text-medium-emphasis py-3">No units yet.</td></tr>
                    </tbody>
                </v-table>

                <v-divider class="my-3"></v-divider>
                <div class="text-subtitle-2 mb-2">{{ editingId ? 'Edit unit' : 'Add unit' }}</div>
                <v-row dense>
                    <v-col cols="6"><v-text-field v-model="form.label" label="Label" placeholder="Bike #3" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="6"><v-text-field v-model="form.serial" label="Serial" density="compact" hide-details></v-text-field></v-col>
                </v-row>
                <v-row dense class="mt-2">
                    <v-col cols="12" sm="6">
                        <!-- Status is admin-settable only among these; sold / rented_out belong to transactions
                             and for_sale to the listing, so none of them is offered here. -->
                        <v-select v-if="editingId" v-model="form.status"
                            :items="['available', 'maintenance', 'retired']" label="Status" density="compact" hide-details></v-select>
                    </v-col>
                    <v-col cols="12" sm="6">
                        <v-text-field v-model.number="form.acquiredDollars" label="Paid (cost)" type="number" prefix="$" min="0" step="0.01"
                            inputmode="decimal" autocomplete="off" density="compact" hide-details
                            placeholder="Optional"></v-text-field>
                    </v-col>
                </v-row>
                <v-text-field v-model="form.notes" label="Notes" density="compact" class="mt-4" hide-details></v-text-field>
                <div v-if="error" class="text-error text-body-2 mt-2">{{ error }}</div>
                <div class="d-flex ga-2 mt-3">
                    <v-btn v-if="editingId" size="small" variant="text" @click="resetForm">Cancel edit</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn size="small" color="primary" :loading="saving" @click="save">{{ editingId ? 'Save unit' : 'Add unit' }}</v-btn>
                </div>
            </v-card-text>
        </v-card>

        <ListingDialog v-model="listingDialog" :item="listingItem" :variant="variant" :product-name="productName"
            :listing="editingListing" @saved="onListingChanged" @unlisted="onListingChanged"
            @flash="(t, c) => emit('flash', t, c)" />
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { BikeShopService, type ShopVariant, type ShopItem, type ShopItemStatus } from '@/services/BikeShopService'
import { ShopListingService, ITEM_STATUS_FOR_SALE, isMissingEndpoint, gradeLabel, type ListingForItem, type ShopListing } from '@/services/ShopListingService'
import ListingDialog from '@/components/bikeshop/ListingDialog.vue'

const props = defineProps<{ modelValue: boolean; variant: ShopVariant | null; productName?: string | null }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void; (e: 'changed'): void; (e: 'flash', text: string, color?: 'success' | 'error'): void }>()

const service = new BikeShopService()
const listingService = new ShopListingService()
const items = ref<ShopItem[]>([])
const saving = ref(false)
const error = ref('')
const editingId = ref<string | null>(null)
const form = ref<{ label: string; serial: string; notes: string; status: ShopItemStatus; acquiredDollars: number | null | '' }>(
    { label: '', serial: '', notes: '', status: 'available', acquiredDollars: null })

// `for_sale` is set by the listing endpoints and is not in the (other session's) ShopItemStatus
// union yet, so compare on the raw string.
function isForSale(i: ShopItem): boolean { return (i.status as string) === ITEM_STATUS_FOR_SALE }
function canList(i: ShopItem): boolean { return i.status === 'available' || i.status === 'maintenance' || i.status === 'retired' }
function statusLabel(s: string) { return s === ITEM_STATUS_FOR_SALE ? 'for sale' : s }
function statusColor(s: string) {
    return s === 'available' ? 'success' : s === 'sold' ? '' : s === 'rented_out' ? 'info' : s === ITEM_STATUS_FOR_SALE ? 'primary' : 'warning'
}
function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
function hasAmount(d: unknown): d is number { return typeof d === 'number' && Number.isFinite(d) }

function resetForm() { editingId.value = null; form.value = { label: '', serial: '', notes: '', status: 'available', acquiredDollars: null }; error.value = '' }
function edit(i: ShopItem) {
    editingId.value = i.id
    form.value = {
        label: i.label, serial: i.serial ?? '', notes: i.notes ?? '', status: i.status,
        acquiredDollars: i.acquiredCostCents != null ? i.acquiredCostCents / 100 : null,
    }
}

watch(() => props.modelValue, async (open) => { if (open) { resetForm(); await load() } })

async function load() {
    if (!props.variant) return
    // Clear first: the dialog is reused across variants, so a failed load must never leave the
    // previous variant's units on screen (editing them would mutate the wrong variant's stock).
    items.value = []
    listingsByItem.value = {}
    try { items.value = (await service.listItems(props.variant.id)).data.data }
    catch (e: any) { emit('flash', e.response?.data?.error || 'Could not load units.', 'error'); return }
    await loadListingInfo()
}

function close() { emit('update:modelValue', false) }

async function save() {
    error.value = ''
    if (!props.variant) return
    if (!form.value.label.trim()) { error.value = 'A label is required.'; return }
    saving.value = true
    try {
        const body = {
            label: form.value.label.trim(), serial: form.value.serial.trim() || null, notes: form.value.notes.trim() || null,
            status: form.value.status,
            // The server overwrites the cost on every PUT; carrying it keeps the listing's margin honest.
            acquiredCostCents: hasAmount(form.value.acquiredDollars) ? Math.round(form.value.acquiredDollars * 100) : null,
        }
        if (editingId.value) await service.updateItem(editingId.value, body)
        else await service.createItem(props.variant.id, body)
        emit('flash', 'Unit saved.')
        emit('changed')
        resetForm()
        await load()
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not save the unit. Please try again.'
    } finally { saving.value = false }
}

// ── Listings (used / ex-rental sales) ──────────────────────────────────────
// Active listings keyed by unit id, so a for_sale row can show its price + grade. A 404 means the
// backend isn't there yet: hide "List for sale" and carry on as before.
const listingsByItem = ref<Record<string, ListingForItem>>({})
const listingsAvailable = ref(true)
const listingDialog = ref(false)
const listingItem = ref<ShopItem | null>(null)
const editingListing = ref<ShopListing | null>(null)
const openingListingFor = ref<string | null>(null)

function listingFor(i: ShopItem): ListingForItem | null { return listingsByItem.value[i.id] ?? null }

async function loadListingInfo() {
    const ids = items.value.filter(isForSale).map(i => i.id)
    if (ids.length === 0) return
    try {
        listingsByItem.value = await listingService.forItems(ids)
        listingsAvailable.value = true
    } catch (e: any) {
        if (isMissingEndpoint(e)) listingsAvailable.value = false
        // Anything else: the row still shows "for sale", just without the price.
    }
}

function listForSale(i: ShopItem) {
    listingItem.value = i
    editingListing.value = null
    listingDialog.value = true
}

// No GET ShopListing/{id}: the active list is short and carries the full DTO (photos included).
async function editListing(i: ShopItem) {
    openingListingFor.value = i.id
    try {
        const active = await listingService.list('active')
        const l = active.find(x => x.itemId === i.id)
        if (!l) { emit('flash', 'Could not find the listing for that unit. Refresh and try again.', 'error'); return }
        listingItem.value = i
        editingListing.value = l
        listingDialog.value = true
    } catch (e: any) {
        emit('flash', e.response?.data?.error || (isMissingEndpoint(e) ? 'Used sales are not available on this server yet.' : 'Could not load the listing.'), 'error')
    } finally { openingListingFor.value = null }
}

// Listing created / changed / unlisted: the unit's status moved, so both this table and the
// Catalog's counts are stale.
async function onListingChanged() {
    emit('changed')
    await load()
}
</script>
