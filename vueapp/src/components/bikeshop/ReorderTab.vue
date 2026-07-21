<template>
    <div>
        <p class="text-caption text-medium-emphasis mb-3">
            Everything at or below its reorder point, grouped by supplier. Adjust the quantities, tick
            what to buy, and raise a purchase order in one click. Set a reorder point on a product's
            variant (pool stock only) to have it show up here.
        </p>

        <div v-if="loading" class="text-center py-6"><v-progress-circular indeterminate></v-progress-circular></div>
        <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>
        <v-card v-else-if="groups.length === 0" class="pa-6 text-center text-medium-emphasis">
            Nothing needs reordering. Stock is above every reorder point.
        </v-card>

        <v-card v-for="g in groups" :key="g.supplierId ?? 'none'" variant="outlined" class="mb-4">
            <div class="d-flex align-center ga-2 pa-3">
                <v-icon size="18" class="text-medium-emphasis">mdi-truck-outline</v-icon>
                <span class="text-subtitle-2">{{ g.supplierName || 'No supplier set' }}</span>
                <v-chip size="x-small" variant="tonal">{{ g.rows.length }}</v-chip>
                <v-spacer></v-spacer>
                <v-chip size="small" variant="text">{{ money(selectedCost(g)) }} · {{ selectedCount(g) }} picked</v-chip>
                <v-btn size="small" color="primary" variant="tonal" prepend-icon="mdi-file-document-plus-outline"
                    :loading="creating === (g.supplierId ?? 'none')" :disabled="selectedCount(g) === 0"
                    @click="createPo(g)">Create PO</v-btn>
            </div>
            <v-alert v-if="!g.supplierId" type="info" variant="tonal" density="compact" class="mx-3 mb-2">
                These products have no supplier. The PO is created without one; set it on the purchase order or on the product.
            </v-alert>
            <v-table density="compact">
                <thead>
                    <tr>
                        <th style="width: 40px">
                            <v-checkbox-btn :model-value="allPicked(g)" :indeterminate="somePicked(g)"
                                @update:model-value="toggleAll(g, $event)"></v-checkbox-btn>
                        </th>
                        <th>Product</th>
                        <th>Vendor #</th>
                        <th class="text-right">On hand</th>
                        <th class="text-right">Point</th>
                        <th class="text-right" style="width: 110px">Order qty</th>
                        <th class="text-right">Est. cost</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="r in g.rows" :key="r.variantId">
                        <td><v-checkbox-btn v-model="picked[r.variantId]"></v-checkbox-btn></td>
                        <td>
                            {{ r.productName }}
                            <span v-if="r.variantLabel" class="text-medium-emphasis">— {{ r.variantLabel }}</span>
                            <div v-if="r.sku" class="text-caption text-medium-emphasis">{{ r.sku }}</div>
                        </td>
                        <td class="text-caption text-medium-emphasis">{{ r.vendorPartNumber || '—' }}</td>
                        <td class="text-right">
                            {{ r.available }}
                            <v-icon v-if="r.available <= 0" size="12" color="error">mdi-alert</v-icon>
                        </td>
                        <td class="text-right text-medium-emphasis">{{ r.reorderPoint }}</td>
                        <td class="text-right">
                            <v-text-field v-model.number="qty[r.variantId]" type="number" min="1"
                                density="compact" hide-details variant="plain" style="width: 90px"
                                class="d-inline-block"></v-text-field>
                        </td>
                        <td class="text-right text-caption">
                            {{ r.costCents != null ? money(r.costCents * (qty[r.variantId] || 0)) : '—' }}
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-snackbar v-model="snack.show" :color="snack.color" :timeout="3500">{{ snack.text }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { BikeShopService, type ShopReorderRow } from '@/services/BikeShopService'

const emit = defineEmits<{ (e: 'flash', text: string, color?: 'success' | 'error'): void; (e: 'created'): void }>()

const service = new BikeShopService()
const rows = ref<ShopReorderRow[]>([])
const loading = ref(false)
const loadError = ref('')
const creating = ref<string | null>(null)
// Per-variant selection + editable order quantity.
const picked = reactive<Record<string, boolean>>({})
const qty = reactive<Record<string, number>>({})

const snack = ref({ show: false, text: '', color: 'success' as 'success' | 'error' })
function flash(text: string, color: 'success' | 'error' = 'success') { snack.value = { show: true, text, color }; emit('flash', text, color) }
function money(c: number): string { return `$${(c / 100).toFixed(2)}` }

interface Group { supplierId: string | null; supplierName: string | null; rows: ShopReorderRow[] }
const groups = computed<Group[]>(() => {
    const out: Group[] = []
    for (const r of rows.value) {
        let g = out.find(x => x.supplierId === r.supplierId)
        if (!g) { g = { supplierId: r.supplierId, supplierName: r.supplierName, rows: [] }; out.push(g) }
        g.rows.push(r)
    }
    return out
})

const selectedRows = (g: Group) => g.rows.filter(r => picked[r.variantId] && (qty[r.variantId] || 0) >= 1)
const selectedCount = (g: Group) => selectedRows(g).length
const selectedCost = (g: Group) => selectedRows(g).reduce((s, r) => s + (r.costCents ?? 0) * (qty[r.variantId] || 0), 0)
const allPicked = (g: Group) => g.rows.length > 0 && g.rows.every(r => picked[r.variantId])
const somePicked = (g: Group) => g.rows.some(r => picked[r.variantId]) && !allPicked(g)
function toggleAll(g: Group, on: boolean) { g.rows.forEach(r => { picked[r.variantId] = on }) }

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        rows.value = (await service.reorderWorklist()).data.data
        for (const r of rows.value) {
            // Default every row selected with its suggested quantity, ready to raise.
            if (picked[r.variantId] === undefined) picked[r.variantId] = true
            if (qty[r.variantId] === undefined) qty[r.variantId] = r.suggestedQty
        }
    } catch (e: any) {
        loadError.value = e.response?.data?.error || 'Could not load the reorder list. Refresh to try again.'
    } finally {
        loading.value = false
    }
}

async function createPo(g: Group) {
    const chosen = selectedRows(g)
    if (chosen.length === 0) return
    creating.value = g.supplierId ?? 'none'
    try {
        const r = await service.createReorderPo({
            supplierId: g.supplierId,
            lines: chosen.map(row => ({
                variantId: row.variantId,
                quantityOrdered: Math.max(1, Math.round(qty[row.variantId] || 1)),
                unitCostCents: row.costCents,
            })),
        })
        flash(`Purchase order created for ${g.supplierName || 'no supplier'}.`)
        emit('created')
        // Those items are now on order, so drop them from the worklist.
        await load()
        void r
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not create the purchase order.', 'error')
    } finally {
        creating.value = null
    }
}

onMounted(load)
defineExpose({ reload: load })
</script>
