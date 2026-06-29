<template>
    <div>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h2 class="text-h5">Void / Comp report</h2>
            <v-spacer></v-spacer>
            <v-select v-model="preset" :items="presetOptions" label="Range" density="compact" hide-details
                style="max-width: 200px" @update:model-value="applyPreset"></v-select>
            <v-text-field v-model="rangeFrom" type="date" label="From" density="compact" hide-details
                style="max-width: 160px" @change="preset = 'custom'"></v-text-field>
            <v-text-field v-model="rangeTo" type="date" label="To" density="compact" hide-details
                style="max-width: 160px" @change="preset = 'custom'"></v-text-field>
            <v-btn color="primary" :loading="loading" @click="load">Refresh</v-btn>
        </div>

        <template v-if="report">
            <!-- Summary tiles -->
            <v-row class="mb-4">
                <v-col cols="12" sm="6" md="3">
                    <v-card><v-card-text>
                        <div class="text-caption text-medium-emphasis">Total comped</div>
                        <div class="text-h4">{{ money(report.totalCompCents) }}</div>
                    </v-card-text></v-card>
                </v-col>
                <v-col cols="12" sm="6" md="3">
                    <v-card><v-card-text>
                        <div class="text-caption text-medium-emphasis">Count</div>
                        <div class="text-h4">{{ report.count }}</div>
                    </v-card-text></v-card>
                </v-col>
            </v-row>

            <v-card>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th style="width: 150px">Date / time</th>
                            <th style="width: 90px">Order #</th>
                            <th>Reason</th>
                            <th style="width: 130px" class="text-right">Amount comped</th>
                            <th style="width: 120px" class="text-right">Total paid</th>
                            <th>Cashier</th>
                            <th>Approved by</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="r in report.rows" :key="r.saleId">
                            <td>{{ formatDate(r.createdAt) }}</td>
                            <td>{{ r.orderNumber != null ? '#' + r.orderNumber : '-' }}</td>
                            <td>{{ r.compReasonLabel || '-' }}</td>
                            <td class="text-right">{{ money(r.discountCents) }}</td>
                            <td class="text-right">{{ money(r.totalCents) }}</td>
                            <td>{{ r.cashierName || '-' }}</td>
                            <td>{{ r.authorizedByName || '-' }}</td>
                        </tr>
                        <tr v-if="report.count === 0">
                            <td colspan="7" class="text-center text-medium-emphasis py-4">No comps in this range.</td>
                        </tr>
                    </tbody>
                </v-table>
            </v-card>
        </template>

        <v-snackbar v-model="snackbar" color="error" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { ConcessionService, type ConcessionCompReport } from '@/services/ConcessionService'
import { branding } from '@/stores/branding'

const service = new ConcessionService()

const presetOptions = [
    { title: 'Today', value: 'today' },
    { title: 'Last 7 days', value: '7d' },
    { title: 'Last 30 days', value: '30d' },
    { title: 'This month', value: 'thismonth' },
    { title: 'Last month', value: 'lastmonth' },
    { title: 'Custom', value: 'custom' },
]
const preset = ref<string>('30d')

const today = dayjs()
const rangeFrom = ref(today.subtract(29, 'day').format('YYYY-MM-DD'))
const rangeTo = ref(today.format('YYYY-MM-DD'))

const report = ref<ConcessionCompReport | null>(null)
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')

onMounted(load)

function tz() { return branding.timezone || 'UTC' }
function money(cents: number) { return `$${(cents / 100).toFixed(2)}` }

function applyPreset(v: string) {
    const t = dayjs()
    switch (v) {
        case 'today':
            rangeFrom.value = t.format('YYYY-MM-DD')
            rangeTo.value = t.format('YYYY-MM-DD')
            break
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
        // rangeTo is inclusive; add a day for the exclusive upper bound the server's `< to` expects.
        const toUtc = dayjs.tz(rangeTo.value + 'T00:00', tz()).add(1, 'day').utc().toISOString()
        const r = await service.compReport(fromUtc, toUtc)
        report.value = (r.data as any).data
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Could not load the comp report.'
        snackbar.value = true
    } finally {
        loading.value = false
    }
}

function formatDate(utc: string): string {
    return dayjs.utc(utc).tz(tz()).format('YYYY-MM-DD HH:mm')
}
</script>
