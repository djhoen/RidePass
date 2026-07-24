<template>
    <div>
        <div class="d-flex mb-3 align-center">
            <p class="text-caption text-medium-emphasis mb-0">
                Counts cover pool-tracked items. Serialized units (bikes) are trued by fixing each
                unit's status instead.
            </p>
            <v-spacer></v-spacer>
            <v-btn color="primary" prepend-icon="mdi-clipboard-list-outline" :loading="creating" @click="create">
                Start stock take
            </v-btn>
        </div>
        <v-card v-if="counts.length === 0" class="pa-6 text-center text-medium-emphasis">
            No stock takes yet. Start one to walk the shop and true up your counts.
        </v-card>
        <v-table v-else density="compact">
            <thead><tr><th>Started</th><th>Status</th><th>Notes</th><th></th></tr></thead>
            <tbody>
                <tr v-for="c in counts" :key="c.id">
                    <td class="text-caption">{{ formatDate(c.startedAt) }}</td>
                    <td><v-chip size="x-small" :color="c.status === 'open' ? 'warning' : c.status === 'completed' ? 'success' : 'grey'">{{ c.status }}</v-chip></td>
                    <td class="text-caption">{{ c.notes || '—' }}</td>
                    <td class="text-right"><v-btn size="x-small" variant="text" icon="mdi-open-in-app" @click="openDetail(c.id)"></v-btn></td>
                </tr>
            </tbody>
        </v-table>

        <v-dialog v-model="detailOpen" max-width="680">
            <v-card v-if="detail" class="d-flex flex-column" style="max-height: 90vh">
                <v-card-title class="d-flex align-center" style="flex: 0 0 auto">
                    <span>Stock take — {{ formatDate(detail.startedAt) }}</span>
                    <v-chip size="small" class="ml-2" :color="detail.status === 'open' ? 'warning' : 'success'">{{ detail.status }}</v-chip>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="detailOpen = false"></v-btn>
                </v-card-title>
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <v-table density="compact">
                        <thead>
                            <tr><th>Item</th><th class="text-right">Expected</th><th class="text-right" style="width:120px">Counted</th>
                                <th class="text-right">Variance</th></tr>
                        </thead>
                        <tbody>
                            <tr v-for="l in detail.lines" :key="l.id">
                                <td>
                                    {{ l.productName }}<span v-if="l.variantLabel" class="text-medium-emphasis"> · {{ l.variantLabel }}</span>
                                    <div v-if="l.sku" class="text-caption text-medium-emphasis">{{ l.sku }}</div>
                                </td>
                                <td class="text-right">{{ l.expectedQty }}</td>
                                <td class="text-right">
                                    <v-text-field v-if="detail.status === 'open'" :model-value="l.countedQty"
                                        type="number" min="0" density="compact" hide-details variant="outlined"
                                        style="max-width: 100px; margin-left: auto"
                                        @change="(e: Event) => saveLine(l, (e.target as HTMLInputElement).value)"></v-text-field>
                                    <span v-else>{{ l.countedQty ?? '—' }}</span>
                                </td>
                                <td class="text-right" :class="varianceClass(l)">{{ varianceLabel(l) }}</td>
                            </tr>
                        </tbody>
                    </v-table>
                    <div v-if="detailError" class="text-error text-body-2 mt-2">{{ detailError }}</div>
                </v-card-text>
                <v-card-actions v-if="detail.status === 'open'" style="flex: 0 0 auto">
                    <v-btn variant="text" color="error" :disabled="completing" @click="cancel">Cancel count</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn color="primary" :loading="completing" @click="complete">
                        Complete &amp; apply {{ countedCount }} counted
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { formatTenantDateTime } from '@/helpers/TenantTime'
import { BikeShopService, type ShopStockCount, type ShopStockCountLine } from '@/services/BikeShopService'
import { useConfirm } from '@/composables/useConfirm'

const emit = defineEmits<{ (e: 'flash', text: string, color?: 'success' | 'error'): void; (e: 'stock-changed'): void }>()
const service = new BikeShopService()
const confirm = useConfirm()

const counts = ref<ShopStockCount[]>([])
const creating = ref(false)
const detailOpen = ref(false)
const detail = ref<ShopStockCount | null>(null)
const detailError = ref('')
const completing = ref(false)

function formatDate(iso: string): string { return formatTenantDateTime(iso, 'MMM D, YYYY h:mm A') }
function varianceLabel(l: ShopStockCountLine): string {
    if (l.countedQty == null) return '—'
    const d = l.countedQty - l.expectedQty
    return d === 0 ? '✓' : d > 0 ? `+${d}` : `${d}`
}
function varianceClass(l: ShopStockCountLine): string {
    if (l.countedQty == null) return 'text-medium-emphasis'
    const d = l.countedQty - l.expectedQty
    return d === 0 ? 'text-success' : 'text-error'
}
const countedCount = computed(() => detail.value?.lines?.filter(l => l.countedQty != null).length ?? 0)

async function reload() {
    try { counts.value = (await service.listStockCounts()).data.data }
    catch (e: any) { emit('flash', e.response?.data?.error || 'Could not load stock takes.', 'error') }
}
onMounted(reload)

async function create() {
    creating.value = true
    try {
        const r = await service.createStockCount(null)
        await reload()
        await openDetail(r.data.data.id)
    } catch (e: any) {
        emit('flash', e.response?.data?.error || 'Could not start a stock take.', 'error')
    } finally { creating.value = false }
}

async function openDetail(id: string) {
    detailError.value = ''
    try {
        detail.value = (await service.getStockCount(id)).data.data
        detailOpen.value = true
    } catch (e: any) {
        emit('flash', e.response?.data?.error || 'Could not load the stock take.', 'error')
    }
}

async function saveLine(l: ShopStockCountLine, raw: string) {
    const n = raw.trim() === '' ? null : Math.max(0, Math.round(Number(raw)))
    if (n != null && isNaN(n)) return
    try {
        await service.setStockCountLine(l.id, n)
        l.countedQty = n
    } catch (e: any) {
        detailError.value = e.response?.data?.error || 'Could not save that count.'
    }
}

async function complete() {
    if (!detail.value) return
    const uncounted = (detail.value.lines?.length ?? 0) - countedCount.value
    const ok = await confirm({
        title: 'Apply this stock take?',
        message: `${countedCount.value} counted item${countedCount.value === 1 ? '' : 's'} will be trued to their counted quantity`
            + (uncounted > 0 ? `; ${uncounted} uncounted item${uncounted === 1 ? ' is' : 's are'} left unchanged.` : '.'),
        confirmText: 'Apply',
    })
    if (!ok) return
    completing.value = true
    try {
        await service.completeStockCount(detail.value.id)
        detailOpen.value = false
        emit('flash', 'Stock take applied — counts trued up.')
        emit('stock-changed')
        await reload()
    } catch (e: any) {
        detailError.value = e.response?.data?.error || 'Could not complete the stock take.'
    } finally { completing.value = false }
}

async function cancel() {
    if (!detail.value) return
    try {
        await service.cancelStockCount(detail.value.id)
        detailOpen.value = false
        emit('flash', 'Stock take cancelled.')
        await reload()
    } catch (e: any) {
        detailError.value = e.response?.data?.error || 'Could not cancel the stock take.'
    }
}
</script>
