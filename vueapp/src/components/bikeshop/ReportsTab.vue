<template>
    <div>
        <v-tabs v-model="report" density="compact" class="mb-3">
            <v-tab value="valuation">Valuation</v-tab>
            <v-tab value="sales">Sales &amp; margin</v-tab>
            <v-tab value="labortime">Labor time</v-tab>
            <v-tab value="dead">Dead stock</v-tab>
        </v-tabs>

        <!-- ── Valuation ────────────────────────────────────────────────── -->
        <div v-if="report === 'valuation'">
            <div v-if="valLoading" class="text-center py-6"><v-progress-circular indeterminate color="primary" /></div>
            <v-alert v-else-if="valError" type="error" variant="tonal">{{ valError }}</v-alert>
            <template v-else>
                <div class="d-flex align-center mb-3 ga-2 flex-wrap">
                    <v-chip variant="tonal" color="primary">Cost value {{ money(valTotals.cost) }}</v-chip>
                    <v-chip variant="tonal">Retail value {{ money(valTotals.retail) }}</v-chip>
                    <v-spacer></v-spacer>
                    <v-btn size="small" variant="tonal" prepend-icon="mdi-download"
                        @click="exportCsv('valuation', valuation, ['productName', 'variantLabel', 'sku', 'categoryName', 'trackingKind', 'onHand', 'costCents', 'salePriceCents', 'costValueCents', 'retailValueCents'])">CSV</v-btn>
                </div>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th>Product</th><th>SKU</th><th>Category</th>
                            <th class="text-right">On hand</th><th class="text-right">Unit cost</th>
                            <th class="text-right">Cost value</th><th class="text-right">Retail value</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="r in valuation" :key="r.variantId">
                            <td>{{ r.productName }}<span v-if="r.variantLabel" class="text-medium-emphasis"> ({{ r.variantLabel }})</span></td>
                            <td class="text-caption">{{ r.sku || '' }}</td>
                            <td class="text-caption">{{ r.categoryName || '' }}</td>
                            <td class="text-right">{{ r.onHand }}</td>
                            <td class="text-right">{{ r.costCents != null ? money(r.costCents) : '?' }}</td>
                            <td class="text-right">{{ money(r.costValueCents) }}</td>
                            <td class="text-right">{{ money(r.retailValueCents) }}</td>
                        </tr>
                        <tr v-if="valuation.length === 0"><td colspan="7" class="text-center text-medium-emphasis py-4">No active products.</td></tr>
                    </tbody>
                </v-table>
                <p class="text-caption text-medium-emphasis mt-2">
                    Serialized products count owned units (available, in maintenance, or out on rental)
                    at each unit's acquired cost. A "?" unit cost means no cost is on file for that item.
                </p>
            </template>
        </div>

        <!-- ── Sales & margin ───────────────────────────────────────────── -->
        <div v-else-if="report === 'sales'">
            <div class="d-flex align-center ga-2 mb-3 flex-wrap">
                <v-text-field v-model="salesFrom" type="date" label="From" density="compact" hide-details style="max-width: 170px"></v-text-field>
                <v-text-field v-model="salesTo" type="date" label="To" density="compact" hide-details style="max-width: 170px"></v-text-field>
                <v-btn variant="tonal" :loading="salesLoading" @click="loadSales">Run</v-btn>
                <v-spacer></v-spacer>
                <v-btn v-if="salesRows.length" size="small" variant="tonal" prepend-icon="mdi-download"
                    @click="exportCsv('sales-margin', salesRows, ['productName', 'variantLabel', 'sku', 'units', 'revenueCents', 'cogsCents'])">CSV</v-btn>
            </div>
            <v-alert v-if="salesError" type="error" variant="tonal" class="mb-3">{{ salesError }}</v-alert>
            <template v-else-if="!salesLoading">
                <div class="d-flex align-center mb-3 ga-2 flex-wrap">
                    <v-chip variant="tonal" color="primary">Revenue {{ money(salesTotals.revenue) }}</v-chip>
                    <v-chip variant="tonal">COGS {{ money(salesTotals.cogs) }}</v-chip>
                    <v-chip variant="tonal" color="success">Margin {{ money(salesTotals.revenue - salesTotals.cogs) }}
                        ({{ marginPct(salesTotals.revenue, salesTotals.cogs) }})</v-chip>
                </div>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th>Product</th><th>SKU</th>
                            <th class="text-right">Units</th><th class="text-right">Revenue</th>
                            <th class="text-right">COGS</th><th class="text-right">Margin</th><th class="text-right">Margin %</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="(r, i) in salesRows" :key="i">
                            <td>{{ r.productName }}<span v-if="r.variantLabel" class="text-medium-emphasis"> ({{ r.variantLabel }})</span></td>
                            <td class="text-caption">{{ r.sku || '' }}</td>
                            <td class="text-right">{{ r.units }}</td>
                            <td class="text-right">{{ money(r.revenueCents) }}</td>
                            <td class="text-right">{{ money(r.cogsCents) }}</td>
                            <td class="text-right">{{ money(r.revenueCents - r.cogsCents) }}</td>
                            <td class="text-right">{{ marginPct(r.revenueCents, r.cogsCents) }}</td>
                        </tr>
                        <tr v-if="salesRows.length === 0"><td colspan="7" class="text-center text-medium-emphasis py-4">No paid sales in this range.</td></tr>
                    </tbody>
                </v-table>
                <p class="text-caption text-medium-emphasis mt-2">
                    Revenue is discounted goods before tax. Labor bills out with no cost, so repairs
                    read as pure margin. Sales from before cost snapshots use the current unit cost.
                </p>
            </template>
        </div>

        <!-- ── Labor time: estimated vs actual ──────────────────────────── -->
        <div v-else-if="report === 'labortime'">
            <div class="d-flex align-center ga-2 mb-3 flex-wrap">
                <v-text-field v-model="ltFrom" type="date" label="From" density="compact" hide-details style="max-width: 170px"></v-text-field>
                <v-text-field v-model="ltTo" type="date" label="To" density="compact" hide-details style="max-width: 170px"></v-text-field>
                <v-btn size="small" color="primary" variant="tonal" @click="loadLaborTime">Run</v-btn>
                <v-spacer></v-spacer>
                <v-btn v-if="ltRows.length" size="small" variant="tonal" prepend-icon="mdi-download"
                    @click="exportCsv('labor-time', ltRows, ['createdAt', 'customerName', 'bikeLabel', 'techName', 'estimatedMinutes', 'actualMinutes'])">CSV</v-btn>
            </div>
            <div v-if="ltLoading" class="text-center py-6"><v-progress-circular indeterminate color="primary" /></div>
            <v-alert v-else-if="ltError" type="error" variant="tonal">{{ ltError }}</v-alert>
            <template v-else>
                <div class="d-flex align-center mb-3 ga-2 flex-wrap">
                    <v-chip variant="tonal">Est {{ fmtMins(ltTotals.est) }}</v-chip>
                    <v-chip variant="tonal">Actual {{ fmtMins(ltTotals.actual) }}</v-chip>
                    <v-chip variant="tonal" :color="ltTotals.actual > ltTotals.est ? 'error' : 'success'">
                        {{ ltTotals.actual > ltTotals.est ? 'Over' : 'Under' }} by {{ fmtMins(Math.abs(ltTotals.actual - ltTotals.est)) }}
                    </v-chip>
                    <span class="text-caption text-medium-emphasis">{{ ltRows.length }} jobs</span>
                </div>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th>When</th><th>Customer</th><th>Bike</th><th>Tech</th>
                            <th class="text-right">Est</th><th class="text-right">Actual</th><th class="text-right">Variance</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="r in ltRows" :key="r.workOrderId">
                            <td class="text-caption">{{ formatDate(r.createdAt) }}</td>
                            <td>{{ r.customerName }}</td>
                            <td class="text-caption">{{ r.bikeLabel || '—' }}</td>
                            <td class="text-caption">{{ r.techName || '—' }}</td>
                            <td class="text-right">{{ r.estimatedMinutes ? fmtMins(r.estimatedMinutes) : '—' }}</td>
                            <td class="text-right">{{ fmtMins(r.actualMinutes) }}</td>
                            <td class="text-right" :class="varianceClass(r)">{{ varianceLabel(r) }}</td>
                        </tr>
                        <tr v-if="ltRows.length === 0"><td colspan="7" class="text-center text-medium-emphasis py-4">
                            No jobs with recorded time in this range. Set estimates on labor lines and run the timer to populate this.</td></tr>
                    </tbody>
                </v-table>
            </template>
        </div>

        <!-- ── Dead stock ───────────────────────────────────────────────── -->
        <div v-else>
            <div class="d-flex align-center ga-2 mb-3 flex-wrap">
                <v-select v-model="deadDays" :items="[30, 60, 90, 180, 365]" label="No sales in (days)"
                    density="compact" hide-details style="max-width: 190px" @update:model-value="loadDead"></v-select>
                <v-spacer></v-spacer>
                <v-btn v-if="deadRows.length" size="small" variant="tonal" prepend-icon="mdi-download"
                    @click="exportCsv('dead-stock', deadRows, ['productName', 'variantLabel', 'sku', 'onHand', 'costValueCents', 'lastSoldAt'])">CSV</v-btn>
            </div>
            <div v-if="deadLoading" class="text-center py-6"><v-progress-circular indeterminate color="primary" /></div>
            <v-alert v-else-if="deadError" type="error" variant="tonal">{{ deadError }}</v-alert>
            <template v-else>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th>Product</th><th>SKU</th>
                            <th class="text-right">On hand</th><th class="text-right">Cost tied up</th><th>Last sold</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="r in deadRows" :key="r.variantId">
                            <td>{{ r.productName }}<span v-if="r.variantLabel" class="text-medium-emphasis"> ({{ r.variantLabel }})</span></td>
                            <td class="text-caption">{{ r.sku || '' }}</td>
                            <td class="text-right">{{ r.onHand }}</td>
                            <td class="text-right">{{ money(r.costValueCents) }}</td>
                            <td class="text-caption">{{ r.lastSoldAt ? formatDate(r.lastSoldAt) : 'never' }}</td>
                        </tr>
                        <tr v-if="deadRows.length === 0"><td colspan="5" class="text-center text-medium-emphasis py-4">
                            Nothing sitting still. Everything in stock has sold within {{ deadDays }} days.</td></tr>
                    </tbody>
                </v-table>
            </template>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import dayjs from 'dayjs'
import { formatTenantDate } from '@/helpers/TenantTime'
import { BikeShopService, type ShopValuationRow, type ShopSalesReportRow, type ShopDeadStockRow, type ShopLaborTimeRow } from '@/services/BikeShopService'

const service = new BikeShopService()
const report = ref<'valuation' | 'sales' | 'labortime' | 'dead'>('valuation')

function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
function formatDate(iso: string): string { return formatTenantDate(iso, 'MMM D, YYYY') }
function fmtMins(m: number): string {
    if (!m) return '0m'
    if (m < 60) return `${m}m`
    const h = Math.floor(m / 60), r = m % 60
    return r ? `${h}h ${r}m` : `${h}h`
}
function marginPct(revenue: number, cogs: number): string {
    return revenue > 0 ? `${(((revenue - cogs) / revenue) * 100).toFixed(1)}%` : ''
}

// ── Valuation ──────────────────────────────────────────────────────────────
const valuation = ref<ShopValuationRow[]>([])
const valLoading = ref(false)
const valError = ref('')
const valTotals = computed(() => ({
    cost: valuation.value.reduce((s, r) => s + r.costValueCents, 0),
    retail: valuation.value.reduce((s, r) => s + r.retailValueCents, 0),
}))
async function loadValuation() {
    valLoading.value = true
    valError.value = ''
    try { valuation.value = (await service.valuationReport()).data.data }
    catch (e: any) { valError.value = e.response?.data?.error || 'Could not load the valuation report. Refresh to try again.' }
    finally { valLoading.value = false }
}

// ── Sales & margin ─────────────────────────────────────────────────────────
const salesFrom = ref(dayjs().subtract(30, 'day').format('YYYY-MM-DD'))
const salesTo = ref(dayjs().format('YYYY-MM-DD'))
const salesRows = ref<ShopSalesReportRow[]>([])
const salesLoading = ref(false)
const salesError = ref('')
const salesTotals = computed(() => ({
    revenue: salesRows.value.reduce((s, r) => s + r.revenueCents, 0),
    cogs: salesRows.value.reduce((s, r) => s + r.cogsCents, 0),
}))
async function loadSales() {
    salesLoading.value = true
    salesError.value = ''
    try {
        const r = await service.salesReport(
            dayjs(salesFrom.value).startOf('day').toISOString(),
            dayjs(salesTo.value).endOf('day').toISOString())
        salesRows.value = r.data.data.rows
    } catch (e: any) {
        salesError.value = e.response?.data?.error || 'Could not load the sales report. Check the dates and try again.'
    } finally { salesLoading.value = false }
}

// ── Labor time (estimated vs actual) ────────────────────────────────────────
const ltFrom = ref(dayjs().subtract(30, 'day').format('YYYY-MM-DD'))
const ltTo = ref(dayjs().format('YYYY-MM-DD'))
const ltRows = ref<ShopLaborTimeRow[]>([])
const ltLoading = ref(false)
const ltError = ref('')
const ltTotals = computed(() => ({
    est: ltRows.value.reduce((s, r) => s + r.estimatedMinutes, 0),
    actual: ltRows.value.reduce((s, r) => s + r.actualMinutes, 0),
}))
function varianceLabel(r: ShopLaborTimeRow): string {
    if (!r.estimatedMinutes) return '—'
    const d = r.actualMinutes - r.estimatedMinutes
    return `${d >= 0 ? '+' : '−'}${fmtMins(Math.abs(d))}`
}
function varianceClass(r: ShopLaborTimeRow): string {
    if (!r.estimatedMinutes) return 'text-medium-emphasis'
    return r.actualMinutes > r.estimatedMinutes * 1.1 ? 'text-error'
        : r.actualMinutes < r.estimatedMinutes * 0.9 ? 'text-success' : ''
}
async function loadLaborTime() {
    ltLoading.value = true
    ltError.value = ''
    try {
        const r = await service.laborTimeReport(
            dayjs(ltFrom.value).startOf('day').toISOString(),
            dayjs(ltTo.value).endOf('day').toISOString())
        ltRows.value = r.data.data.rows
    } catch (e: any) {
        ltError.value = e.response?.data?.error || 'Could not load the labor time report. Check the dates and try again.'
    } finally { ltLoading.value = false }
}

// ── Dead stock ─────────────────────────────────────────────────────────────
const deadDays = ref(60)
const deadRows = ref<ShopDeadStockRow[]>([])
const deadLoading = ref(false)
const deadError = ref('')
async function loadDead() {
    deadLoading.value = true
    deadError.value = ''
    try { deadRows.value = (await service.deadStockReport(deadDays.value)).data.data.rows }
    catch (e: any) { deadError.value = e.response?.data?.error || 'Could not load the dead stock report. Refresh to try again.' }
    finally { deadLoading.value = false }
}

// ── CSV export ─────────────────────────────────────────────────────────────
function exportCsv(name: string, rows: any[], cols: string[]) {
    const esc = (v: unknown) => {
        const s = v == null ? '' : String(v)
        return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
    }
    const csv = [cols.join(','), ...rows.map(r => cols.map(c => esc(r[c])).join(','))].join('\n')
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }))
    const a = document.createElement('a')
    a.href = url
    a.download = `${name}-${dayjs().format('YYYY-MM-DD')}.csv`
    a.click()
    URL.revokeObjectURL(url)
}

watch(report, (r) => {
    if (r === 'sales' && salesRows.value.length === 0) loadSales()
    if (r === 'labortime' && ltRows.value.length === 0) loadLaborTime()
    if (r === 'dead' && deadRows.value.length === 0) loadDead()
})
onMounted(loadValuation)
</script>
