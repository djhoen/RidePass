<template>
    <div>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h2 class="text-h5">Revenue by Department</h2>
            <v-spacer></v-spacer>
            <v-select v-model="preset" :items="presetOptions" label="Range" density="compact" hide-details
                style="max-width: 200px" @update:model-value="applyPreset"></v-select>
            <v-text-field v-model="rangeFrom" type="date" label="From" density="compact" hide-details
                style="max-width: 160px" @change="preset = 'custom'"></v-text-field>
            <v-text-field v-model="rangeTo" type="date" label="To" density="compact" hide-details
                style="max-width: 160px" @change="preset = 'custom'"></v-text-field>
            <v-btn color="primary" :loading="loading" @click="load">Refresh</v-btn>
            <v-btn variant="outlined" prepend-icon="mdi-file-delimited-outline"
                :disabled="loading || !report" @click="exportCsv">Export CSV</v-btn>
        </div>

        <v-alert v-if="errorText" type="error" variant="tonal" class="mb-4" :text="errorText"></v-alert>

        <v-skeleton-loader v-if="loading && !report" type="card, table"></v-skeleton-loader>

        <template v-else-if="report">
            <!-- One tile per department. Zero-activity departments never render, so a track with
                 no bike shop or no training program simply doesn't see those headings. -->
            <v-row class="mb-2">
                <v-col v-for="d in report.departments" :key="d.key" cols="12" sm="6"
                    :md="tileWidth">
                    <v-card><v-card-text>
                        <div class="text-caption text-medium-emphasis">{{ d.label }}</div>
                        <div class="text-h4">{{ money(d.netRevenueCents) }}</div>
                        <div class="text-caption text-medium-emphasis">
                            {{ pct(d.pctOfTotal) }} of revenue &middot;
                            {{ d.saleCount }} {{ d.saleCount === 1 ? 'sale' : 'sales' }}
                        </div>
                    </v-card-text></v-card>
                </v-col>
                <v-col v-if="report.departments.length === 0" cols="12">
                    <v-alert type="info" variant="tonal"
                        text="No revenue was recorded in this range."></v-alert>
                </v-col>
            </v-row>

            <v-card v-if="report.departments.length">
                <v-card-title class="text-subtitle-1">Departments</v-card-title>
                <v-card-subtitle class="pb-2">
                    Net revenue is what was earned: gross less sales tax and tips, after refunds.
                    The categories under each department are the same accounts the QuickBooks
                    journal entry credits.
                </v-card-subtitle>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th>Department</th>
                            <th class="text-right" style="width: 90px">Sales</th>
                            <th class="text-right" style="width: 130px">Gross</th>
                            <th class="text-right" style="width: 120px">Refunds</th>
                            <th class="text-right" style="width: 110px">Tax</th>
                            <th class="text-right" style="width: 110px">Tips</th>
                            <th class="text-right" style="width: 140px">Net revenue</th>
                            <th class="text-right" style="width: 90px">Share</th>
                        </tr>
                    </thead>
                    <tbody>
                        <template v-for="d in report.departments" :key="d.key">
                            <tr class="dept-row" @click="toggle(d.key)">
                                <td class="font-weight-medium">
                                    <v-icon size="small" class="mr-1">
                                        {{ expanded.has(d.key) ? 'mdi-chevron-down' : 'mdi-chevron-right' }}
                                    </v-icon>
                                    {{ d.label }}
                                </td>
                                <td class="text-right">{{ d.saleCount }}</td>
                                <td class="text-right">{{ money(d.grossCents) }}</td>
                                <td class="text-right">{{ money(d.refundCents) }}</td>
                                <td class="text-right">{{ money(d.taxCents) }}</td>
                                <td class="text-right">{{ money(d.tipCents) }}</td>
                                <td class="text-right font-weight-medium">{{ money(d.netRevenueCents) }}</td>
                                <td class="text-right">{{ pct(d.pctOfTotal) }}</td>
                            </tr>
                            <tr v-for="c in (expanded.has(d.key) ? d.categories : [])" :key="d.key + c.key"
                                class="cat-row">
                                <td class="pl-8 text-medium-emphasis">{{ c.label }}</td>
                                <td class="text-right text-medium-emphasis">{{ c.saleCount }}</td>
                                <td class="text-right text-medium-emphasis">{{ money(c.grossCents) }}</td>
                                <td class="text-right text-medium-emphasis">{{ money(c.refundCents) }}</td>
                                <td class="text-right text-medium-emphasis">{{ money(c.taxCents) }}</td>
                                <td class="text-right text-medium-emphasis">{{ money(c.tipCents) }}</td>
                                <td class="text-right text-medium-emphasis">{{ money(c.netRevenueCents) }}</td>
                                <td class="text-right text-medium-emphasis"></td>
                            </tr>
                        </template>
                    </tbody>
                    <tfoot>
                        <tr class="font-weight-bold">
                            <td>Total</td>
                            <td class="text-right">{{ report.saleCount }}</td>
                            <td class="text-right">{{ money(report.grossCents) }}</td>
                            <td class="text-right">{{ money(report.refundCents) }}</td>
                            <td class="text-right">{{ money(report.taxCents) }}</td>
                            <td class="text-right">{{ money(report.tipCents) }}</td>
                            <td class="text-right">{{ money(report.netRevenueCents) }}</td>
                            <td class="text-right">{{ report.netRevenueCents === 0 ? '' : '100.0%' }}</td>
                        </tr>
                    </tfoot>
                </v-table>
            </v-card>
        </template>

        <v-snackbar v-model="snackbar" color="error" :timeout="6000">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { ReportsService, type RevenueByDepartmentReport } from '@/services/ReportsService'
import { branding } from '@/stores/branding'
import { downloadCsvSections, csvMoney, type CsvSection } from '@/helpers/csv'

const service = new ReportsService()

// Same presets and same default as Sales Summary and the Tax report, so moving between the three
// panes does not silently change the period you are looking at.
const presetOptions = [
    { title: 'Last 7 days', value: '7d' },
    { title: 'Last 30 days', value: '30d' },
    { title: 'This month', value: 'thismonth' },
    { title: 'Last month', value: 'lastmonth' },
    { title: 'Year to date', value: 'ytd' },
    { title: 'Custom', value: 'custom' },
]
const preset = ref<string>('thismonth')

const today = dayjs()
const rangeFrom = ref(today.startOf('month').format('YYYY-MM-DD'))
const rangeTo = ref(today.format('YYYY-MM-DD'))

const report = ref<RevenueByDepartmentReport | null>(null)
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const errorText = ref('')
const expanded = ref<Set<string>>(new Set())

function tz() { return branding.timezone || 'UTC' }
function money(cents: number) {
    const sign = cents < 0 ? '-' : ''
    return `${sign}$${(Math.abs(cents) / 100).toFixed(2)}`
}
function pct(v: number) { return `${v.toFixed(1)}%` }

// Tiles share the row evenly rather than being pinned to a fixed width: a track with two
// departments gets two half-width tiles instead of two narrow ones marooned on the left.
const tileWidth = computed(() => {
    const n = report.value?.departments.length ?? 0
    if (n <= 1) return 12
    if (n === 2) return 6
    if (n === 3) return 4
    return 3
})

function toggle(key: string) {
    // Reassigned rather than mutated: a Set mutated in place does not trigger Vue's reactivity.
    const next = new Set(expanded.value)
    if (next.has(key)) next.delete(key)
    else next.add(key)
    expanded.value = next
}

function applyPreset(v: string) {
    const t = dayjs()
    switch (v) {
        case '7d':
            rangeFrom.value = t.subtract(6, 'day').format('YYYY-MM-DD')
            rangeTo.value = t.format('YYYY-MM-DD')
            break
        case '30d':
            rangeFrom.value = t.subtract(29, 'day').format('YYYY-MM-DD')
            rangeTo.value = t.format('YYYY-MM-DD')
            break
        case 'thismonth':
            rangeFrom.value = t.startOf('month').format('YYYY-MM-DD')
            rangeTo.value = t.endOf('month').format('YYYY-MM-DD')
            break
        case 'lastmonth':
            rangeFrom.value = t.subtract(1, 'month').startOf('month').format('YYYY-MM-DD')
            rangeTo.value = t.subtract(1, 'month').endOf('month').format('YYYY-MM-DD')
            break
        case 'ytd':
            rangeFrom.value = t.startOf('year').format('YYYY-MM-DD')
            rangeTo.value = t.format('YYYY-MM-DD')
            break
    }
    load()
}

async function load() {
    if (rangeFrom.value && rangeTo.value && rangeFrom.value > rangeTo.value) {
        errorText.value = '"From" must be on or before "To".'
        snackbarText.value = errorText.value
        snackbar.value = true
        return
    }
    loading.value = true
    errorText.value = ''
    try {
        const fromUtc = dayjs.tz(rangeFrom.value + 'T00:00', tz()).utc().toISOString()
        // rangeTo is inclusive; add a day for the exclusive upper bound the query expects.
        const toUtc = dayjs.tz(rangeTo.value + 'T00:00', tz()).add(1, 'day').utc().toISOString()
        const r = await service.getRevenueByDepartment(fromUtc, toUtc)
        report.value = (r.data as any).data
        // Nothing is auto-expanded on a reload beyond what the admin had open, but a single
        // department is always worth showing broken out.
        if (report.value && report.value.departments.length === 1) {
            expanded.value = new Set([report.value.departments[0].key])
        }
    } catch (err: any) {
        // The previous range's numbers stay on screen behind the banner rather than being blanked
        // to zeros, which would read as "this period earned nothing".
        errorText.value = err.response?.data?.error
            || 'Could not load revenue by department for this range. Check your connection and press Refresh.'
        snackbarText.value = errorText.value
        snackbar.value = true
    } finally {
        loading.value = false
    }
}

function exportCsv() {
    const r = report.value
    if (!r) return
    const sections: CsvSection[] = [
        {
            title: 'Revenue by department',
            headers: ['Field', 'Value'],
            rows: [
                ['Venue', branding.displayName],
                ['From', rangeFrom.value],
                ['To', rangeTo.value],
                ['Timezone', r.timezone],
            ],
        },
        {
            title: 'Departments',
            headers: ['Department', 'Category', 'Sales', 'Refunds', 'Gross', 'Refunded', 'Tax', 'Tips', 'Net revenue', 'Share'],
            // Department line first, then its categories indented into the Category column, so
            // the file reads the same way the table does.
            rows: r.departments.flatMap(d => [
                [d.label, '', d.saleCount, d.refundCount, csvMoney(d.grossCents), csvMoney(d.refundCents),
                    csvMoney(d.taxCents), csvMoney(d.tipCents), csvMoney(d.netRevenueCents), pct(d.pctOfTotal)],
                ...d.categories.map(c => ['', c.label, c.saleCount, c.refundCount, csvMoney(c.grossCents),
                    csvMoney(c.refundCents), csvMoney(c.taxCents), csvMoney(c.tipCents),
                    csvMoney(c.netRevenueCents), '']),
            ]),
        },
        {
            title: 'Total',
            headers: ['Field', 'Value'],
            rows: [
                ['Sales', r.saleCount],
                ['Refunds', r.refundCount],
                ['Gross', csvMoney(r.grossCents)],
                ['Refunded', csvMoney(r.refundCents)],
                ['Tax', csvMoney(r.taxCents)],
                ['Tips', csvMoney(r.tipCents)],
                ['Net revenue', csvMoney(r.netRevenueCents)],
            ],
        },
    ]
    downloadCsvSections(`revenue-by-department-${rangeFrom.value}-to-${rangeTo.value}.csv`, sections)
}

onMounted(load)
</script>

<style scoped>
.dept-row {
    cursor: pointer;
}
.cat-row td {
    font-size: 0.85rem;
}
</style>
