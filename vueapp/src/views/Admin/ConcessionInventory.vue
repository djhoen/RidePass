<template>
    <div>
        <div class="d-flex align-center mb-3 ga-3 flex-wrap">
            <v-chip v-if="lowCount" color="warning" variant="flat" prepend-icon="mdi-alert">
                {{ lowCount }} low on stock
            </v-chip>
            <v-spacer></v-spacer>
            <v-btn variant="text" @click="loadAll">Refresh</v-btn>
        </div>

        <v-tabs v-model="tab" class="mb-3">
            <v-tab value="items">Items</v-tab>
            <v-tab value="counts">Stock takes</v-tab>
        </v-tabs>

        <v-window v-model="tab">
            <!-- Inventory items -->
            <v-window-item value="items">
                <div class="d-flex justify-end mb-2">
                    <v-btn color="primary" prepend-icon="mdi-plus" @click="openItem()">Add item</v-btn>
                </div>
                <v-card>
                    <v-table>
                        <thead>
                            <tr>
                                <th>Item</th><th>Unit</th><th class="text-right">Unit cost</th>
                                <th class="text-right">On hand</th><th class="text-right">Low at</th>
                                <th style="width: 220px" class="text-right"></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="i in items" :key="i.id" :class="{ 'text-medium-emphasis': !i.isActive }">
                                <td>
                                    {{ i.name }}
                                    <v-chip v-if="i.isLow" size="x-small" color="warning" variant="flat" class="ml-2"
                                        prepend-icon="mdi-alert">Low</v-chip>
                                </td>
                                <td>{{ i.unit }}</td>
                                <td class="text-right">{{ money(i.costCents) }}</td>
                                <td class="text-right" :class="{ 'text-warning font-weight-bold': i.isLow }">{{ i.onHand }}</td>
                                <td class="text-right text-medium-emphasis">{{ i.lowStockThreshold ?? '—' }}</td>
                                <td class="text-right">
                                    <v-btn variant="text" size="small" @click="openReceive(i)">Receive</v-btn>
                                    <v-btn variant="text" size="small" @click="openItem(i)">Edit</v-btn>
                                    <v-btn variant="text" size="small" color="error" @click="removeItem(i)">Delete</v-btn>
                                </td>
                            </tr>
                        </tbody>
                    </v-table>
                    <div v-if="!loading && items.length === 0" class="text-center text-medium-emphasis py-8">
                        No inventory items yet. Add ingredients/goods, then set recipes on each menu item.
                    </div>
                </v-card>
            </v-window-item>

            <!-- Stock takes -->
            <v-window-item value="counts">
                <div class="d-flex justify-end mb-2">
                    <v-btn color="primary" prepend-icon="mdi-clipboard-list" @click="openStockTake">New stock take</v-btn>
                </div>
                <v-card>
                    <v-table>
                        <thead>
                            <tr><th>Date</th><th>Note</th><th class="text-right">Variance</th><th style="width: 90px"></th></tr>
                        </thead>
                        <tbody>
                            <tr v-for="c in counts" :key="c.id">
                                <td>{{ new Date(c.createdAtUtc).toLocaleString() }}</td>
                                <td>{{ c.note || '(none)' }}</td>
                                <td class="text-right" :class="c.varianceCents < 0 ? 'text-error' : 'text-success'">{{ money(c.varianceCents) }}</td>
                                <td><v-btn variant="text" size="small" @click="openCountDetail(c.id)">View</v-btn></td>
                            </tr>
                        </tbody>
                    </v-table>
                    <div v-if="!loading && counts.length === 0" class="text-center text-medium-emphasis py-8">
                        No stock takes yet. Run one to compare counted stock against what sales should have used.
                    </div>
                </v-card>
            </v-window-item>
        </v-window>

        <!-- Add / edit item -->
        <v-dialog v-model="itemDialog" max-width="460">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ editingItem ? 'Edit item' : 'Add item' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="itemDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="itemForm.name" label="Name" density="compact" placeholder="Beef patty"></v-text-field>
                    <v-row class="mt-0">
                        <v-col cols="6"><v-text-field v-model="itemForm.unit" label="Unit" density="compact" class="mt-4" placeholder="each / oz / lb"></v-text-field></v-col>
                        <v-col cols="6"><v-text-field v-model.number="itemForm.costDollars" type="number" min="0" step="0.01" prefix="$" label="Unit cost" density="compact" class="mt-4"></v-text-field></v-col>
                    </v-row>
                    <v-text-field v-model.number="itemForm.onHand" type="number" step="0.001" label="On hand" density="compact" class="mt-4"></v-text-field>
                    <v-text-field v-model.number="itemForm.lowStockThreshold" type="number" min="0" step="0.001"
                        label="Low-stock threshold (optional)" density="compact" class="mt-4" clearable placeholder="off"
                        hint="Warn + alert managers when on-hand falls to or below this. Blank = no warning."
                        persistent-hint></v-text-field>
                    <v-switch v-model="itemForm.isActive" label="Active" color="primary" density="compact" hide-details class="mt-4"></v-switch>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="itemDialog = false">Cancel</v-btn>
                    <v-btn color="primary" variant="flat" :loading="saving" @click="saveItem">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Receive stock -->
        <v-dialog v-model="receiveDialog" max-width="360">
            <v-card v-if="receiveItem">
                <v-card-title class="d-flex align-center">
                    <span>Receive {{ receiveItem.name }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="receiveDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div class="text-body-2 text-medium-emphasis mb-2">Current on hand: {{ receiveItem.onHand }} {{ receiveItem.unit }}</div>
                    <v-text-field v-model.number="receiveQty" type="number" step="0.001" :suffix="receiveItem.unit"
                        label="Quantity received" density="compact" autofocus hide-details></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="receiveDialog = false">Cancel</v-btn>
                    <v-btn color="primary" variant="flat" :loading="saving" @click="saveReceive">Add to stock</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Stock take -->
        <v-dialog v-model="stockTakeDialog" fullscreen scrollable>
            <v-card class="d-flex flex-column">
                <v-toolbar color="primary" density="comfortable">
                    <v-toolbar-title>New stock take</v-toolbar-title>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" @click="stockTakeDialog = false"></v-btn>
                </v-toolbar>
                <v-card-text class="flex-grow-1" style="overflow-y: auto;">
                    <div style="max-width: 720px; margin: 0 auto;">
                        <v-text-field v-model="countNote" label="Note (optional)" density="compact" class="mb-2" hide-details></v-text-field>
                        <p class="text-caption text-medium-emphasis mb-2">
                            Enter the actual counted quantity for each item. Expected is what sales should have left.
                        </p>
                        <v-table density="compact">
                            <thead><tr><th>Item</th><th class="text-right">Expected</th><th style="width: 140px" class="text-right">Counted</th></tr></thead>
                            <tbody>
                                <tr v-for="row in countRows" :key="row.id">
                                    <td>{{ row.name }}</td>
                                    <td class="text-right text-medium-emphasis">{{ row.expected }} {{ row.unit }}</td>
                                    <td><v-text-field v-model.number="row.counted" type="number" step="0.001" density="compact" hide-details></v-text-field></td>
                                </tr>
                            </tbody>
                        </v-table>
                    </div>
                </v-card-text>
                <div class="pa-4 d-flex justify-end ga-2" style="border-top: 1px solid rgba(128,128,128,0.2);">
                    <v-btn variant="text" @click="stockTakeDialog = false">Cancel</v-btn>
                    <v-btn color="primary" variant="flat" :loading="saving" @click="submitStockTake">Save count</v-btn>
                </div>
            </v-card>
        </v-dialog>

        <!-- Count detail / variance -->
        <v-dialog v-model="detailDialog" max-width="640">
            <v-card v-if="detail">
                <v-card-title class="d-flex align-center">
                    <span>Stock take variance</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="detailDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div class="d-flex justify-space-between text-h6 mb-3">
                        <span>Total variance</span>
                        <span :class="detail.totalVarianceCents < 0 ? 'text-error' : 'text-success'">{{ money(detail.totalVarianceCents) }}</span>
                    </div>
                    <v-table density="compact">
                        <thead><tr><th>Item</th><th class="text-right">Expected</th><th class="text-right">Counted</th><th class="text-right">Variance</th><th class="text-right">$</th></tr></thead>
                        <tbody>
                            <tr v-for="(l, i) in detail.lines" :key="i">
                                <td>{{ l.name }}</td>
                                <td class="text-right text-medium-emphasis">{{ l.expectedQty }}</td>
                                <td class="text-right">{{ l.countedQty }}</td>
                                <td class="text-right" :class="l.variance < 0 ? 'text-error' : (l.variance > 0 ? 'text-success' : '')">{{ l.variance }} {{ l.unit }}</td>
                                <td class="text-right" :class="l.varianceCents < 0 ? 'text-error' : ''">{{ money(l.varianceCents) }}</td>
                            </tr>
                        </tbody>
                    </v-table>
                </v-card-text>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snack.show" :color="snack.color" :timeout="3500">{{ snack.text }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { ConcessionService, type ConcessionInventoryItem, type InventoryCountSummary, type InventoryCountDetail } from '@/services/ConcessionService'
import { useConfirm } from '@/composables/useConfirm'

// Lets the parent Food & Beverage page badge the Inventory tab with the live low-stock count.
const emit = defineEmits<{ (e: 'low-count', count: number): void }>()

const svc = new ConcessionService()
const confirm = useConfirm()
const tab = ref('items')
const loading = ref(false)
const saving = ref(false)
const items = ref<ConcessionInventoryItem[]>([])
const counts = ref<InventoryCountSummary[]>([])
const snack = ref({ show: false, text: '', color: 'error' })
function flash(text: string, color: 'error' | 'success' = 'error') { snack.value = { show: true, text, color } }
// Negative = loss. e.g. -$3.50
function money(cents: number) { return `${cents < 0 ? '-' : ''}$${(Math.abs(cents) / 100).toFixed(2)}` }

onMounted(loadAll)
async function loadAll() {
    loading.value = true
    try {
        const [it, ct] = await Promise.all([svc.inventoryItems(), svc.inventoryCounts()])
        items.value = (it.data as any).data
        counts.value = (ct.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load inventory. Refresh to try again.')
    } finally {
        loading.value = false
    }
}

// ── Item editor ──────────────────────────────────────────────────────
const itemDialog = ref(false)
const editingItem = ref<ConcessionInventoryItem | null>(null)
const itemForm = ref({ name: '', unit: 'each', costDollars: 0, onHand: 0, lowStockThreshold: null as number | null, isActive: true })
// How many active items are currently low on stock.
const lowCount = computed(() => items.value.filter(i => i.isActive && i.isLow).length)
// Keep the parent tab badge in sync as items load and are received/edited/counted. Not immediate:
// on mount items is still empty (count 0); the parent's own seed covers the badge until loadAll
// populates items, at which point this fires the real count (avoids a 0-flicker on first open).
watch(lowCount, (n) => emit('low-count', n))
function openItem(i?: ConcessionInventoryItem) {
    editingItem.value = i ?? null
    itemForm.value = i
        ? { name: i.name, unit: i.unit, costDollars: i.costCents / 100, onHand: i.onHand, lowStockThreshold: i.lowStockThreshold, isActive: i.isActive }
        : { name: '', unit: 'each', costDollars: 0, onHand: 0, lowStockThreshold: null, isActive: true }
    itemDialog.value = true
}
async function saveItem() {
    const name = itemForm.value.name.trim()
    if (!name) { flash('Name is required.'); return }
    saving.value = true
    try {
        const t = itemForm.value.lowStockThreshold
        const payload = {
            name, unit: itemForm.value.unit.trim() || 'each',
            costCents: Math.round((itemForm.value.costDollars || 0) * 100),
            onHand: Number(itemForm.value.onHand) || 0,
            lowStockThreshold: t === null || (t as any) === '' ? null : Number(t),
            isActive: itemForm.value.isActive,
        }
        if (editingItem.value) await svc.updateInventoryItem(editingItem.value.id, payload)
        else await svc.createInventoryItem(payload)
        itemDialog.value = false
        flash('Item saved.', 'success')
        await loadAll()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not save the item.')
    } finally {
        saving.value = false
    }
}
async function removeItem(i: ConcessionInventoryItem) {
    if (!await confirm({ title: 'Delete item?', message: `Delete "${i.name}"? It will be removed from any recipes that use it.`, confirmText: 'Delete', confirmColor: 'error' })) return
    try {
        await svc.removeInventoryItem(i.id)
        flash('Item deleted.', 'success')
        await loadAll()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not delete the item.')
    }
}

// ── Receive ──────────────────────────────────────────────────────────
const receiveDialog = ref(false)
const receiveItem = ref<ConcessionInventoryItem | null>(null)
const receiveQty = ref<number | null>(null)
function openReceive(i: ConcessionInventoryItem) { receiveItem.value = i; receiveQty.value = null; receiveDialog.value = true }
async function saveReceive() {
    if (!receiveItem.value || !receiveQty.value) { flash('Enter a quantity.'); return }
    saving.value = true
    try {
        await svc.receiveStock(receiveItem.value.id, Number(receiveQty.value))
        receiveDialog.value = false
        flash('Stock received.', 'success')
        await loadAll()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not receive stock.')
    } finally {
        saving.value = false
    }
}

// ── Stock take ───────────────────────────────────────────────────────
const stockTakeDialog = ref(false)
const countNote = ref('')
const countRows = ref<{ id: string; name: string; unit: string; expected: number; counted: number }[]>([])
function openStockTake() {
    countRows.value = items.value.filter(i => i.isActive)
        .map(i => ({ id: i.id, name: i.name, unit: i.unit, expected: i.onHand, counted: i.onHand }))
    countNote.value = ''
    stockTakeDialog.value = true
}
async function submitStockTake() {
    saving.value = true
    try {
        const { data } = await svc.createInventoryCount({
            note: countNote.value.trim() || null,
            lines: countRows.value.map(r => ({ inventoryItemId: r.id, countedQty: Number(r.counted) || 0 })),
        })
        stockTakeDialog.value = false
        flash('Stock take saved.', 'success')
        await loadAll()
        openCountDetail((data as any).data.id)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not save the stock take.')
    } finally {
        saving.value = false
    }
}

// ── Count detail ─────────────────────────────────────────────────────
const detailDialog = ref(false)
const detail = ref<InventoryCountDetail | null>(null)
async function openCountDetail(id: string) {
    try {
        detail.value = (await svc.inventoryCount(id) as any).data.data
        detailDialog.value = true
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load the stock take.')
    }
}
</script>
