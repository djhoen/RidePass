<template>
    <div>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h2 class="text-h5">F&amp;B Staff Sales</h2>
            <v-spacer></v-spacer>
            <v-select v-model="preset" :items="presetOptions" label="Range" density="compact" hide-details
                style="max-width: 200px" @update:model-value="applyPreset"></v-select>
            <v-text-field v-model="rangeFrom" type="date" label="From" density="compact" hide-details
                style="max-width: 160px" @change="preset = 'custom'"></v-text-field>
            <v-text-field v-model="rangeTo" type="date" label="To" density="compact" hide-details
                style="max-width: 160px" @change="preset = 'custom'"></v-text-field>
            <v-btn color="primary" :loading="loading" @click="load">Refresh</v-btn>
        </div>

        <v-card v-if="report">
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Employee</th>
                        <th style="width: 80px" class="text-right">Orders</th>
                        <th style="width: 120px" class="text-right">Gross</th>
                        <th style="width: 110px" class="text-right">Net</th>
                        <th style="width: 110px" class="text-right">Tips</th>
                        <th style="width: 110px" class="text-right">Cash</th>
                        <th style="width: 110px" class="text-right">Card</th>
                        <th style="width: 110px" class="text-right">Avg order</th>
                        <th style="width: 120px" class="text-right">Refunds</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="(r, idx) in report.rows" :key="idx">
                        <td>{{ r.name }}</td>
                        <td class="text-right">{{ r.ordersCount }}</td>
                        <td class="text-right">{{ money(r.grossSalesCents) }}</td>
                        <td class="text-right">{{ money(r.netSalesCents) }}</td>
                        <td class="text-right">{{ money(r.tipCents) }}</td>
                        <td class="text-right">{{ money(r.cashCents) }}</td>
                        <td class="text-right">{{ money(r.cardCents) }}</td>
                        <td class="text-right">{{ money(r.avgOrderValueCents) }}</td>
                        <td class="text-right">{{ r.refundedCount }} · {{ money(r.refundedCents) }}</td>
                    </tr>
                    <tr v-if="report.rows.length === 0">
                        <td colspan="9" class="text-center text-medium-emphasis py-6">No F&amp;B sales in range.</td>
                    </tr>
                </tbody>
                <tfoot v-if="report.rows.length">
                    <tr class="font-weight-bold">
                        <td>Total</td>
                        <td class="text-right">{{ totals.orders }}</td>
                        <td class="text-right">{{ money(totals.gross) }}</td>
                        <td class="text-right">{{ money(totals.net) }}</td>
                        <td class="text-right">{{ money(totals.tips) }}</td>
                        <td class="text-right">{{ money(totals.cash) }}</td>
                        <td class="text-right">{{ money(totals.card) }}</td>
                        <td></td>
                        <td class="text-right">{{ totals.refundCount }} · {{ money(totals.refunds) }}</td>
                    </tr>
                </tfoot>
            </v-table>
            <div class="text-caption text-medium-emphasis px-4 py-2">
                Attributed to the employee who rang the sale. "Refunds" are that employee's sales that were
                later refunded, not who processed the refund. "Unattributed" covers sales with no signed-in cashier.
            </div>
        </v-card>

        <v-snackbar v-model="snackbar" color="error" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { ReportsService, type ConcessionEmployeeReport } from '@/services/ReportsService'
import { branding } from '@/stores/branding'

const service = new ReportsService()

const presetOptions = [
    { title: 'Today', value: 'today' },
    { title: 'Last 7 days', value: '7d' },
    { title: 'Last 30 days', value: '30d' },
    { title: 'This month', value: 'thismonth' },
    { title: 'Last month', value: 'lastmonth' },
    { title: 'Custom', value: 'custom' },
]
const preset = ref<string>('7d')

const today = dayjs()
const rangeFrom = ref(today.subtract(6, 'day').format('YYYY-MM-DD'))
const rangeTo = ref(today.format('YYYY-MM-DD'))

const report = ref<ConcessionEmployeeReport | null>(null)
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')

onMounted(load)

function tz() { return branding.timezone || 'UTC' }
function money(cents: number) { return `$${(cents / 100).toFixed(2)}` }

const totals = computed(() => {
    const rows = report.value?.rows ?? []
    return {
        orders: rows.reduce((s, r) => s + r.ordersCount, 0),
        gross: rows.reduce((s, r) => s + r.grossSalesCents, 0),
        net: rows.reduce((s, r) => s + r.netSalesCents, 0),
        tips: rows.reduce((s, r) => s + r.tipCents, 0),
        cash: rows.reduce((s, r) => s + r.cashCents, 0),
        card: rows.reduce((s, r) => s + r.cardCents, 0),
        refundCount: rows.reduce((s, r) => s + r.refundedCount, 0),
        refunds: rows.reduce((s, r) => s + r.refundedCents, 0),
    }
})

function applyPreset(v: string) {
    const t = dayjs()
    switch (v) {
        case 'today':
            rangeFrom.value = t.format('YYYY-MM-DD'); rangeTo.value = t.format('YYYY-MM-DD'); break
        case '7d':
            rangeFrom.value = t.subtract(6, 'day').format('YYYY-MM-DD'); rangeTo.value = t.format('YYYY-MM-DD'); break
        case '30d':
            rangeFrom.value = t.subtract(29, 'day').format('YYYY-MM-DD'); rangeTo.value = t.format('YYYY-MM-DD'); break
        case 'thismonth':
            rangeFrom.value = t.startOf('month').format('YYYY-MM-DD'); rangeTo.value = t.endOf('month').format('YYYY-MM-DD'); break
        case 'lastmonth':
            rangeFrom.value = t.subtract(1, 'month').startOf('month').format('YYYY-MM-DD')
            rangeTo.value = t.subtract(1, 'month').endOf('month').format('YYYY-MM-DD'); break
    }
    load()
}

async function load() {
    loading.value = true
    try {
        if (rangeFrom.value && rangeTo.value && rangeFrom.value > rangeTo.value) {
            snackbarText.value = '"From" must be on or before "To".'
            snackbar.value = true
            return
        }
        const fromUtc = dayjs.tz(rangeFrom.value + 'T00:00', tz()).utc().toISOString()
        const toUtc = dayjs.tz(rangeTo.value + 'T00:00', tz()).add(1, 'day').utc().toISOString()
        const r = await service.getConcessionEmployees(fromUtc, toUtc)
        report.value = (r.data as any).data
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Failed to load the staff sales report.'
        snackbar.value = true
    } finally {
        loading.value = false
    }
}
</script>
