<template>
    <div>
        <div class="d-flex mb-3 ga-2 flex-wrap align-center">
            <v-text-field v-model="filterReference" density="compact" hide-details clearable
                prepend-inner-icon="mdi-magnify" label="PO number" style="max-width: 200px"></v-text-field>
            <v-select v-model="filterSupplierId" :items="supplierFilterItems" item-title="title"
                item-value="value" density="compact" hide-details clearable label="Supplier"
                style="max-width: 220px"></v-select>
            <!-- Received POs are done business and quickly outnumber the live ones, so they're out
                 by default. Selecting any status explicitly overrides that. -->
            <v-select v-model="filterStatuses" :items="statusFilterItems" item-title="title"
                item-value="value" density="compact" hide-details clearable chips closable-chips
                multiple label="Status" placeholder="Open + on order" persistent-placeholder
                style="min-width: 260px; max-width: 380px"></v-select>
            <v-spacer></v-spacer>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">New purchase order</v-btn>
        </div>

        <v-card v-if="orders.length === 0" class="pa-6 text-center text-medium-emphasis">
            No purchase orders yet. Create one to order and receive stock at cost.
        </v-card>
        <v-card v-else-if="visibleOrders.length === 0" class="pa-6 text-center text-medium-emphasis">
            No purchase orders match those filters.
            <div v-if="filterStatuses.length === 0" class="text-caption mt-1">
                Received orders are hidden by default — pick a status to include them.
            </div>
        </v-card>
        <v-table v-else density="compact">
            <thead>
                <tr>
                    <th v-for="c in columns" :key="c.key" :class="c.align === 'right' ? 'text-right' : ''"
                        class="sortable-col" @click="toggleSort(c.key)">
                        {{ c.label }}
                        <v-icon v-if="sortKey === c.key" size="14"
                            :icon="sortAsc ? 'mdi-arrow-up' : 'mdi-arrow-down'"></v-icon>
                    </th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
                <tr v-for="po in visibleOrders" :key="po.id">
                    <td>{{ supplierName(po.supplierId) }}</td>
                    <td class="text-caption">{{ po.reference || '—' }}</td>
                    <td><v-chip size="x-small" :color="statusColor(po.status)">{{ po.status }}</v-chip></td>
                    <td class="text-caption">
                        {{ po.expectedAt ? formatDate(po.expectedAt) : '—' }}
                        <!-- Something due in the past that hasn't fully arrived is the thing a
                             buyer actually wants to spot on this screen. -->
                        <v-chip v-if="isOverdue(po)" size="x-small" color="warning" class="ml-1">Late</v-chip>
                    </td>
                    <td class="text-caption">{{ po.lines?.length ?? '' }}</td>
                    <td class="text-right"><v-btn size="x-small" variant="text" icon="mdi-open-in-app" @click="openDetail(po.id)"></v-btn></td>
                </tr>
            </tbody>
        </v-table>

        <!-- ── Create dialog ───────────────────────────────────────────── -->
        <v-dialog v-model="createOpen" max-width="480">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>New purchase order</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="createOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-select v-model="createForm.supplierId" :items="suppliers" item-title="name" item-value="id"
                        label="Supplier" density="compact" clearable hide-details></v-select>
                    <v-text-field v-model="createForm.reference" label="Reference / vendor PO #" density="compact" class="mt-4" hide-details></v-text-field>
                    <v-text-field v-model="createForm.expectedAt" type="date" label="Expected" density="compact" class="mt-4" hide-details></v-text-field>
                    <v-text-field v-model="createForm.notes" label="Notes" density="compact" class="mt-4" hide-details></v-text-field>
                    <div v-if="createError" class="text-error text-body-2 mt-2">{{ createError }}</div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="creating" @click="createOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="creating" @click="create">Create</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- ── Detail / receive dialog ─────────────────────────────────── -->
        <v-dialog v-model="detailOpen" max-width="760">
            <v-card v-if="detail" class="d-flex flex-column" style="max-height: 90vh">
                <v-card-title class="d-flex align-center" style="flex: 0 0 auto">
                    <span>PO — {{ supplierName(detail.supplierId) }}</span>
                    <v-chip size="small" class="ml-2" :color="statusColor(detail.status)">{{ detail.status }}</v-chip>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="detailOpen = false"></v-btn>
                </v-card-title>
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <v-table density="compact">
                        <thead>
                            <tr><th>Item</th><th class="text-right">Ordered</th><th class="text-right">Received</th>
                                <th class="text-right">Unit cost</th><th style="width:200px"></th></tr>
                        </thead>
                        <tbody>
                            <tr v-for="l in detail.lines" :key="l.id">
                                <td>{{ variantName(l.variantId) }}</td>
                                <td class="text-right">{{ l.quantityOrdered }}</td>
                                <td class="text-right">
                                    {{ l.quantityReceived }}
                                    <v-chip v-if="l.quantityReceived >= l.quantityOrdered" size="x-small" color="success" class="ml-1">Full</v-chip>
                                </td>
                                <td class="text-right">{{ money(l.unitCostCents) }}</td>
                                <td class="text-right">
                                    <v-btn v-if="l.quantityReceived < l.quantityOrdered && detail.status !== 'cancelled'"
                                        size="x-small" color="primary" variant="tonal" @click="openReceive(l)">Receive</v-btn>
                                </td>
                            </tr>
                            <tr v-if="!detail.lines?.length"><td colspan="5" class="text-center text-medium-emphasis py-3">No lines yet.</td></tr>
                        </tbody>
                    </v-table>

                    <template v-if="detail.status === 'open' || detail.status === 'ordered' || detail.status === 'partial'">
                        <v-divider class="my-3"></v-divider>
                        <div class="text-subtitle-2 mb-2">Add line</div>
                        <v-row dense>
                            <v-col cols="6">
                                <v-select v-model="newLine.variantId" :items="variantItems" item-title="title" item-value="id"
                                    label="Item" density="compact" hide-details></v-select>
                            </v-col>
                            <v-col cols="2"><v-text-field v-model.number="newLine.qty" type="number" min="1" label="Qty" density="compact" hide-details></v-text-field></v-col>
                            <v-col cols="2"><v-text-field v-model.number="newLine.costDollars" type="number" min="0" step="0.01" label="Unit cost" prefix="$" density="compact" hide-details></v-text-field></v-col>
                            <v-col cols="2" class="d-flex align-center">
                                <v-btn color="primary" variant="tonal" block :loading="addingLine" @click="addLine">Add</v-btn>
                            </v-col>
                        </v-row>
                    </template>
                    <div v-if="detailError" class="text-error text-body-2 mt-2">{{ detailError }}</div>
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- ── Receive dialog (serial entry for serialized lines) ──────── -->
        <v-dialog v-model="receiveOpen" max-width="480">
            <v-card v-if="receivingLine">
                <v-card-title class="d-flex align-center">
                    <span>Receive — {{ variantName(receivingLine.variantId) }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="receiveOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model.number="receiveQty" type="number" min="1"
                        :max="receivingLine.quantityOrdered - receivingLine.quantityReceived"
                        :label="`Quantity (${receivingLine.quantityOrdered - receivingLine.quantityReceived} outstanding)`"
                        density="compact" hide-details></v-text-field>
                    <template v-if="receivingSerialized">
                        <p class="text-caption text-medium-emphasis mt-3 mb-1">
                            Serialized item — label each unit (serial optional):
                        </p>
                        <v-row v-for="(u, i) in serialUnits" :key="i" dense class="mt-1">
                            <v-col cols="6"><v-text-field v-model="u.label" :label="`Unit ${i + 1} label`" density="compact" hide-details></v-text-field></v-col>
                            <v-col cols="6"><v-text-field v-model="u.serial" label="Serial" density="compact" hide-details></v-text-field></v-col>
                        </v-row>
                    </template>
                    <div v-if="receiveError" class="text-error text-body-2 mt-2">{{ receiveError }}</div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="receiving" @click="receiveOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="receiving" @click="receive">Receive</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import dayjs from 'dayjs'
import { BikeShopService, type ShopProduct, type ShopSupplier, type ShopPurchaseOrder, type ShopPoLine } from '@/services/BikeShopService'

const props = defineProps<{ products: ShopProduct[]; suppliers: ShopSupplier[] }>()
const emit = defineEmits<{ (e: 'flash', text: string, color?: 'success' | 'error'): void; (e: 'stock-changed'): void }>()

const service = new BikeShopService()
const orders = ref<ShopPurchaseOrder[]>([])

function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
function formatDate(iso: string): string { return dayjs(iso).format('MMM D, YYYY') }
// ── Filters + sorting ───────────────────────────────────────────────────────
const filterReference = ref('')
const filterSupplierId = ref<string | null>(null)
// Empty = the default view: everything still in flight. 'received' and 'cancelled' are finished
// business and would otherwise bury the handful of POs anyone acts on. Selecting any status
// (or several) replaces that default entirely.
const filterStatuses = ref<string[]>([])
const OPEN_STATUSES = ['open', 'ordered', 'partial']

const supplierFilterItems = computed(() =>
    props.suppliers.map(s => ({ value: s.id, title: s.name })))
const statusFilterItems = [
    { value: 'open', title: 'Open (draft)' },
    { value: 'ordered', title: 'Ordered' },
    { value: 'partial', title: 'Partially received' },
    { value: 'received', title: 'Received' },
    { value: 'cancelled', title: 'Cancelled' },
]

const columns = [
    { key: 'supplier', label: 'Supplier' },
    { key: 'reference', label: 'Reference' },
    { key: 'status', label: 'Status' },
    { key: 'expected', label: 'Expected' },
    { key: 'lines', label: 'Lines' },
]
const sortKey = ref('expected')
const sortAsc = ref(true)
function toggleSort(key: string) {
    if (sortKey.value === key) sortAsc.value = !sortAsc.value
    else { sortKey.value = key; sortAsc.value = true }
}

// A PO past its expected date that hasn't fully arrived. Cancelled/received are done.
function isOverdue(po: ShopPurchaseOrder): boolean {
    if (!po.expectedAt) return false
    if (po.status === 'received' || po.status === 'cancelled') return false
    return new Date(po.expectedAt).getTime() < Date.now()
}

function sortValue(po: ShopPurchaseOrder, key: string): string | number {
    switch (key) {
        case 'supplier': return supplierName(po.supplierId).toLowerCase()
        case 'reference': return (po.reference ?? '').toLowerCase()
        case 'status': return po.status
        // Undated orders sort last ascending rather than pretending to be 1970.
        case 'expected': return po.expectedAt ? new Date(po.expectedAt).getTime() : Number.MAX_SAFE_INTEGER
        case 'lines': return po.lines?.length ?? 0
        default: return ''
    }
}

const visibleOrders = computed(() => {
    const ref_ = filterReference.value?.trim().toLowerCase()
    const rows = orders.value.filter(po => {
        if (filterStatuses.value.length > 0) { if (!filterStatuses.value.includes(po.status)) return false }
        else if (!OPEN_STATUSES.includes(po.status)) return false
        if (filterSupplierId.value && po.supplierId !== filterSupplierId.value) return false
        if (ref_ && !(po.reference ?? '').toLowerCase().includes(ref_)) return false
        return true
    })
    return [...rows].sort((a, b) => {
        const av = sortValue(a, sortKey.value), bv = sortValue(b, sortKey.value)
        if (av < bv) return sortAsc.value ? -1 : 1
        if (av > bv) return sortAsc.value ? 1 : -1
        return 0
    })
})

function supplierName(id: string | null): string { return id ? (props.suppliers.find(s => s.id === id)?.name ?? '—') : '(no supplier)' }
function statusColor(s: string) {
    return s === 'open' ? 'grey' : s === 'ordered' ? 'primary' : s === 'partial' ? 'warning'
        : s === 'received' ? 'success' : 'error'
}
const variantItems = computed(() =>
    props.products.filter(p => p.isActive).flatMap(p => p.variants.filter(v => v.isActive).map(v => ({
        id: v.id,
        title: `${p.name}${[v.size, v.color].filter(Boolean).length ? ' (' + [v.size, v.color].filter(Boolean).join('/') + ')' : ''}${v.sku ? ' · ' + v.sku : ''}`,
        trackingKind: v.trackingKind,
    }))))
function variantName(id: string): string { return variantItems.value.find(v => v.id === id)?.title ?? '(deleted item)' }

async function reload() {
    try { orders.value = (await service.listPurchaseOrders()).data.data }
    catch (e: any) { emit('flash', e.response?.data?.error || 'Could not load purchase orders.', 'error') }
}
onMounted(reload)

// ── Create ─────────────────────────────────────────────────────────────────
const createOpen = ref(false)
const creating = ref(false)
const createError = ref('')
const createForm = ref({ supplierId: null as string | null, reference: '', expectedAt: '', notes: '' })
function openCreate() {
    createForm.value = { supplierId: null, reference: '', expectedAt: '', notes: '' }
    createError.value = ''
    createOpen.value = true
}
async function create() {
    creating.value = true
    createError.value = ''
    try {
        const r = await service.createPurchaseOrder({
            supplierId: createForm.value.supplierId,
            reference: createForm.value.reference.trim() || null,
            notes: createForm.value.notes.trim() || null,
            expectedAt: createForm.value.expectedAt || null,
        } as any)
        createOpen.value = false
        await reload()
        await openDetail(r.data.data.id)
    } catch (e: any) {
        createError.value = e.response?.data?.error || 'Could not create the purchase order.'
    } finally { creating.value = false }
}

// ── Detail + lines ─────────────────────────────────────────────────────────
const detailOpen = ref(false)
const detail = ref<ShopPurchaseOrder | null>(null)
const detailError = ref('')
const newLine = ref({ variantId: null as string | null, qty: 1, costDollars: null as number | null })
const addingLine = ref(false)

async function openDetail(id: string) {
    detailError.value = ''
    try {
        detail.value = (await service.getPurchaseOrder(id)).data.data
        detailOpen.value = true
    } catch (e: any) {
        emit('flash', e.response?.data?.error || 'Could not load the purchase order.', 'error')
    }
}

async function addLine() {
    if (!detail.value) return
    detailError.value = ''
    if (!newLine.value.variantId) { detailError.value = 'Pick the item being ordered.'; return }
    const cost = newLine.value.costDollars
    if (cost == null || isNaN(cost)) { detailError.value = 'Enter the unit cost.'; return }
    addingLine.value = true
    try {
        await service.addPurchaseOrderLine(detail.value.id, {
            variantId: newLine.value.variantId,
            quantityOrdered: Math.max(1, Math.round(newLine.value.qty)),
            unitCostCents: Math.round(cost * 100),
        })
        newLine.value = { variantId: null, qty: 1, costDollars: null }
        await openDetail(detail.value.id)
        await reload()
    } catch (e: any) {
        detailError.value = e.response?.data?.error || 'Could not add the line.'
    } finally { addingLine.value = false }
}

// ── Receive ────────────────────────────────────────────────────────────────
const receiveOpen = ref(false)
const receiving = ref(false)
const receiveError = ref('')
const receivingLine = ref<ShopPoLine | null>(null)
const receiveQty = ref(1)
const serialUnits = ref<{ label: string; serial: string }[]>([])
const receivingSerialized = computed(() =>
    receivingLine.value ? variantItems.value.find(v => v.id === receivingLine.value!.variantId)?.trackingKind === 'serialized' : false)

function openReceive(l: ShopPoLine) {
    receivingLine.value = l
    receiveQty.value = l.quantityOrdered - l.quantityReceived
    receiveError.value = ''
    receiveOpen.value = true
}
watch([receiveQty, receivingSerialized], () => {
    if (!receivingSerialized.value) { serialUnits.value = []; return }
    const n = Math.max(1, Math.round(receiveQty.value || 1))
    while (serialUnits.value.length < n) serialUnits.value.push({ label: '', serial: '' })
    serialUnits.value = serialUnits.value.slice(0, n)
}, { immediate: true })

async function receive() {
    if (!receivingLine.value) return
    receiveError.value = ''
    const qty = Math.max(1, Math.round(receiveQty.value))
    if (receivingSerialized.value && serialUnits.value.some(u => !u.label.trim())) {
        receiveError.value = 'Every unit needs a label.'
        return
    }
    receiving.value = true
    try {
        await service.receivePurchaseOrderLine(receivingLine.value.id, {
            quantity: qty,
            serialUnits: receivingSerialized.value
                ? serialUnits.value.map(u => ({ label: u.label.trim(), serial: u.serial.trim() || null }))
                : null,
        })
        receiveOpen.value = false
        emit('flash', `Received ${qty} into stock.`)
        emit('stock-changed')
        if (detail.value) await openDetail(detail.value.id)
        await reload()
    } catch (e: any) {
        receiveError.value = e.response?.data?.error || 'Could not receive this line.'
    } finally { receiving.value = false }
}
</script>

<style scoped>
.sortable-col {
    cursor: pointer;
    user-select: none;
    white-space: nowrap;
}
.sortable-col:hover {
    background: rgba(var(--v-theme-on-surface), 0.04);
}
</style>
