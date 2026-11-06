<template>
    <div class="tips-owed">
        <div class="rp-pagehead">
            <div>
                <div class="rp-pagehead-title">Tips owed</div>
                <div class="rp-pagehead-sub">
                    Tips staff could not take from their drawer at shift close. Paying out logs the payment and
                    clears the balance; payroll payouts are recorded here but leave the cash drawer alone.
                </div>
            </div>
            <div class="d-flex align-center flex-wrap ga-2">
                <v-btn color="primary" :loading="loading" @click="load">Refresh</v-btn>
            </div>
        </div>

        <v-alert v-if="errorText" type="error" variant="tonal" class="mb-4" :text="errorText"></v-alert>

        <template v-if="loading && !data">
            <v-skeleton-loader type="table"></v-skeleton-loader>
        </template>

        <template v-else-if="data">
            <v-row class="mb-2">
                <v-col cols="12" sm="6" md="3">
                    <v-card class="rp-kpi pa-4">
                        <div class="rp-kpi-label mt-0">Owed right now</div>
                        <div class="rp-kpi-value">{{ money(totalOwedCents) }}</div>
                        <div class="text-caption text-medium-emphasis mt-2">
                            {{ owedCount }} staff member{{ owedCount === 1 ? '' : 's' }} waiting
                        </div>
                    </v-card>
                </v-col>
                <v-col cols="12" sm="6" md="3">
                    <v-card class="rp-kpi pa-4">
                        <div class="rp-kpi-label mt-0">Paid by managers</div>
                        <div class="rp-kpi-value">{{ money(paidByManager90Cents) }}</div>
                        <div class="text-caption text-medium-emphasis mt-2">cash or payroll, last 90 days</div>
                    </v-card>
                </v-col>
            </v-row>

            <!-- Balances -->
            <v-card class="mb-4">
                <v-card-title class="text-subtitle-1">Staff balances</v-card-title>
                <v-card-subtitle class="pb-2">
                    Everyone owed tips, plus anyone who closed a shift in the last 90 days.
                </v-card-subtitle>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th>Name</th>
                            <th class="text-right" style="width: 120px">Owed</th>
                            <th style="width: 160px">Last shift</th>
                            <th class="text-right" style="width: 150px">
                                Awaiting close-out
                                <v-tooltip activator="parent" location="top">
                                    Tips on paid sales this person has not settled on a shift close yet
                                </v-tooltip>
                            </th>
                            <th class="text-right" style="width: 130px">Earned total</th>
                            <th class="text-right" style="width: 150px">Paid from drawer</th>
                            <th class="text-right" style="width: 150px">Paid by manager</th>
                            <th style="width: 120px"></th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="r in sortedRows" :key="r.userId">
                            <td>{{ r.name }}</td>
                            <td class="text-right" :class="r.owedCents > 0 ? 'owed-cell' : ''">{{ money(r.owedCents) }}</td>
                            <td>{{ r.lastShiftAtUtc ? formatTenantDateTime(r.lastShiftAtUtc) : '-' }}</td>
                            <td class="text-right text-medium-emphasis">{{ (r.unclaimedTipsCents ?? 0) > 0 ? money(r.unclaimedTipsCents) : '-' }}</td>
                            <td class="text-right">{{ money(r.earnedTotalCents) }}</td>
                            <td class="text-right">{{ money(r.paidFromDrawerCents) }}</td>
                            <td class="text-right">{{ money(r.paidByManagerCents) }}</td>
                            <td class="text-right">
                                <v-btn size="small" variant="tonal" prepend-icon="mdi-hand-coin-outline"
                                    :disabled="r.owedCents <= 0" @click="openPay(r)">Pay out</v-btn>
                            </td>
                        </tr>
                        <tr v-if="data.rows.length === 0">
                            <td colspan="8" class="text-center text-medium-emphasis py-4">
                                No tips are owed, nothing is waiting for a close-out, and nobody has closed a shift in the last 90 days.
                            </td>
                        </tr>
                    </tbody>
                </v-table>
            </v-card>

            <!-- History -->
            <v-card class="mb-4">
                <v-card-title class="text-subtitle-1">Payout history</v-card-title>
                <v-card-subtitle class="pb-2">
                    Every tip payment in the last 90 days. Drawer rows are tips staff took themselves at shift close.
                </v-card-subtitle>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th style="width: 160px">When</th>
                            <th>Staff</th>
                            <th class="text-right" style="width: 120px">Amount</th>
                            <th style="width: 110px">Source</th>
                            <th>Paid by</th>
                            <th>Note</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="p in data.payouts" :key="p.id">
                            <td>{{ formatTenantDateTime(p.createdAtUtc) }}</td>
                            <td>{{ p.name }}</td>
                            <td class="text-right">{{ money(p.amountCents) }}</td>
                            <td>{{ sourceLabel(p.source) }}</td>
                            <td>{{ p.paidByName }}</td>
                            <td class="text-medium-emphasis">{{ p.note || '' }}</td>
                        </tr>
                        <tr v-if="data.payouts.length === 0">
                            <td colspan="6" class="text-center text-medium-emphasis py-4">No tip payouts in the last 90 days.</td>
                        </tr>
                    </tbody>
                </v-table>
                <v-card-text v-if="data.payoutsTruncated" class="text-caption text-medium-emphasis pt-2">
                    Showing the newest {{ data.payouts.length }} payouts. The totals above cover the whole 90 days.
                </v-card-text>
            </v-card>
        </template>

        <!-- Pay out -->
        <v-dialog v-model="payDialog" max-width="420" :persistent="paying">
            <v-card v-if="payTarget">
                <v-card-title class="d-flex align-center">
                    Pay out tips
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="paying" @click="payDialog = false" />
                </v-card-title>
                <v-card-text>
                    <div class="d-flex justify-space-between text-h6 mb-3">
                        <span>{{ payTarget.name }}</span><span>owed {{ money(payTarget.owedCents) }}</span>
                    </div>
                    <v-text-field v-model.number="payDollars" label="Amount" type="number" prefix="$" min="0" step="0.01"
                        :max="payTarget.owedCents / 100" inputmode="decimal" autocomplete="off" density="compact" autofocus
                        :error-messages="payAmountError" class="mb-3" />
                    <div class="text-caption text-medium-emphasis mb-1">Paid how</div>
                    <v-btn-toggle v-model="paySource" mandatory divided density="comfortable" class="mb-3">
                        <v-btn value="cash" prepend-icon="mdi-cash">Cash</v-btn>
                        <v-btn value="payroll" prepend-icon="mdi-file-document-outline">Payroll</v-btn>
                    </v-btn-toggle>
                    <p class="text-caption text-medium-emphasis mb-3">
                        {{ paySource === 'cash'
                            ? 'Logged as cash paid out on today\'s End of Day. If you have a drawer open it counts against your expected cash.'
                            : 'Added to their next paycheck. Nothing leaves a drawer; the books move it from tips payable to payroll.' }}
                    </p>
                    <v-text-field v-model="payNote" label="Note (optional)" autocomplete="off" density="compact" hide-details />
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" :disabled="paying" @click="payDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="paying" :disabled="!!payAmountError || payCents <= 0" @click="confirmPay">
                        Confirm {{ money(payCents) }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snack.show" :color="snack.color" :timeout="5000">{{ snack.text }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { CashService, type TipsOwedResponse, type TipsOwedRow, type TipPayoutSource } from '@/services/CashService'
import { formatTenantDateTime } from '@/helpers/TenantTime'

const service = new CashService()

const data = ref<TipsOwedResponse | null>(null)
const loading = ref(false)
// Persistent so a failed load leaves an explanation on the page rather than a vanishing toast.
const errorText = ref('')
const snack = ref({ show: false, text: '', color: 'error' as 'error' | 'success' })

function flash(text: string, color: 'error' | 'success' = 'error') { snack.value = { show: true, text, color } }

function money(cents: number) {
    const sign = cents < 0 ? '-' : ''
    return `${sign}$${(Math.abs(cents) / 100).toFixed(2)}`
}
function sourceLabel(s: TipPayoutSource) {
    return s === 'drawer' ? 'Drawer' : s === 'payroll' ? 'Payroll' : 'Cash'
}

// Row order comes from the server (owed desc, awaiting close-out desc, name); the page does not re-sort.
const sortedRows = computed(() => data.value?.rows ?? [])
const totalOwedCents = computed(() => (data.value?.rows ?? []).reduce((s, r) => s + r.owedCents, 0))
const owedCount = computed(() => (data.value?.rows ?? []).filter(r => r.owedCents > 0).length)
// Summed server-side over the whole 90-day window, so the 500-row history cap never skews it.
const paidByManager90Cents = computed(() => data.value?.paidByManagerLast90Cents ?? 0)

async function load() {
    loading.value = true
    errorText.value = ''
    try {
        data.value = (await service.tipsOwed()).data.data
    } catch (err: any) {
        errorText.value = err.response?.data?.error || 'Could not load tips owed. Check your connection and press Refresh.'
    } finally {
        loading.value = false
    }
}

// ── Pay out ──────────────────────────────────────────────────────────
const payDialog = ref(false)
const payTarget = ref<TipsOwedRow | null>(null)
const payDollars = ref<number | null>(null)
const paySource = ref<'cash' | 'payroll'>('cash')
const payNote = ref('')
const paying = ref(false)

const payCents = computed(() => payDollars.value == null || !Number.isFinite(payDollars.value) ? 0 : Math.round(payDollars.value * 100))
const payAmountError = computed(() => {
    if (!payTarget.value) return ''
    if (payDollars.value == null) return ''
    if (payCents.value <= 0) return 'Enter an amount greater than $0.00.'
    if (payCents.value > payTarget.value.owedCents) return `Can't pay more than the ${money(payTarget.value.owedCents)} owed.`
    return ''
})

function openPay(r: TipsOwedRow) {
    payTarget.value = r
    payDollars.value = r.owedCents / 100
    paySource.value = 'cash'
    payNote.value = ''
    payDialog.value = true
}

async function confirmPay() {
    if (!payTarget.value || payAmountError.value || payCents.value <= 0) return
    paying.value = true
    try {
        const updated = (await service.payTipsOwed({
            userId: payTarget.value.userId,
            amountCents: payCents.value,
            source: paySource.value,
            note: payNote.value.trim(),
        })).data.data
        // Swap the returned row in so the balance updates at once; the history reloads behind it.
        if (data.value) data.value.rows = data.value.rows.map(r => r.userId === updated.userId ? updated : r)
        flash(`Paid ${money(payCents.value)} to ${payTarget.value.name}.`, 'success')
        payDialog.value = false
        load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not record the payout. Refresh and try again.')
    } finally {
        paying.value = false
    }
}

onMounted(load)
</script>

<style scoped>
.owed-cell { font-weight: 600; }
</style>
