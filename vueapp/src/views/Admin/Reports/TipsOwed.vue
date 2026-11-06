<template>
    <div class="tips-owed">
        <div class="rp-pagehead">
            <div>
                <div class="rp-pagehead-title">Tips owed</div>
                <div class="rp-pagehead-sub">
                    Card tips from each person's shift report that have not been paid to them yet. Paying out logs the
                    payment and clears the balance; payroll payouts are recorded here but leave the cash drawer alone.
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

            <!-- Tip jar: cash outside the register, split among the people who worked and handed out on
                 the spot. Recorded for the daily tips sheet only: no liability, no ledger row. -->
            <v-card class="mb-4">
                <v-card-title class="text-subtitle-1 d-flex align-center flex-wrap ga-2">
                    Tip jar
                    <v-spacer></v-spacer>
                    <v-text-field v-model="jarDate" type="date" label="Date" density="compact" hide-details style="max-width: 180px"></v-text-field>
                    <v-btn color="primary" variant="tonal" size="small" prepend-icon="mdi-plus" :disabled="!jar" @click="openRecordJar">Record tip jar</v-btn>
                </v-card-title>
                <v-card-subtitle class="pb-2">
                    Cash from the jar, split evenly among the people who worked and taken right after the shift. It shows
                    on the End of Day tips sheet for the date.
                </v-card-subtitle>
                <v-alert v-if="jarError" type="warning" variant="tonal" density="compact" class="mx-4 mb-3" :text="jarError"></v-alert>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th style="width: 160px">Recorded</th>
                            <th class="text-right" style="width: 110px">Total</th>
                            <th>Split</th>
                            <th style="width: 160px">By</th>
                            <th>Note</th>
                            <th style="width: 56px"></th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="s in jar?.splits ?? []" :key="s.id">
                            <td>{{ formatTenantDateTime(s.createdAtUtc) }}</td>
                            <td class="text-right">{{ money(s.totalCents) }}</td>
                            <td>
                                <div class="d-flex flex-wrap ga-1 py-1">
                                    <span v-for="sh in s.shares" :key="sh.userId" class="rp-pill">{{ sh.name }} · {{ money(sh.amountCents) }}</span>
                                </div>
                            </td>
                            <td>{{ s.createdByName }}</td>
                            <td class="text-medium-emphasis">{{ s.note || '' }}</td>
                            <td class="text-right">
                                <v-btn icon size="small" variant="text" :loading="deletingJarId === s.id"
                                    :disabled="!!deletingJarId" aria-label="Remove this split" @click="deleteJar(s)">
                                    <v-icon>mdi-delete-outline</v-icon>
                                    <v-tooltip activator="parent" location="top">Remove this split</v-tooltip>
                                </v-btn>
                            </td>
                        </tr>
                        <tr v-if="jarLoading && !jar">
                            <td colspan="6" class="text-center py-4"><v-progress-circular indeterminate size="20" width="2"></v-progress-circular></td>
                        </tr>
                        <tr v-else-if="jar && jar.splits.length === 0">
                            <td colspan="6" class="text-center text-medium-emphasis py-4">No tip jar recorded for {{ prettyJarDate }}.</td>
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

        <!-- Record tip jar -->
        <v-dialog v-model="jarDialog" max-width="520" :persistent="savingJar">
            <v-card>
                <v-card-title class="d-flex align-center">
                    Record tip jar · {{ prettyJarDate }}
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="savingJar" @click="jarDialog = false" />
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model.number="jarDollars" label="Total in the jar" type="number" prefix="$" min="0" step="0.01"
                        inputmode="decimal" autocomplete="off" density="compact" autofocus :error-messages="jarAmountError" class="mb-3" />
                    <div class="text-caption text-medium-emphasis mb-2">Split between</div>
                    <div class="d-flex flex-wrap ga-2 mb-1">
                        <v-chip v-for="p in jarPeople" :key="p.userId" :variant="jarSelectedIds.includes(p.userId) ? 'flat' : 'outlined'"
                            :color="jarSelectedIds.includes(p.userId) ? 'primary' : undefined"
                            :prepend-icon="jarSelectedIds.includes(p.userId) ? 'mdi-check' : undefined"
                            @click="toggleJarPerson(p.userId)">{{ p.name }}</v-chip>
                        <span v-if="jarPeople.length === 0" class="text-caption text-medium-emphasis">No counter staff found.</span>
                    </div>
                    <p class="text-caption text-medium-emphasis mb-3">
                        Everyone who closed a shift that day is pre-selected. Tap a name to add or remove them.
                    </p>
                    <div v-if="jarShares.length" class="jar-preview mb-3">
                        <div v-for="sh in jarShares" :key="sh.userId" class="d-flex justify-space-between">
                            <span>{{ sh.name }}</span><span>{{ money(sh.amountCents) }}</span>
                        </div>
                        <div class="text-caption text-medium-emphasis mt-1">
                            Even split{{ jarRemainderCents ? `; the leftover ${jarRemainderCents}¢ goes one cent each to the first names listed` : '' }}.
                        </div>
                    </div>
                    <v-text-field v-model="jarNote" label="Note (optional)" autocomplete="off" density="compact" hide-details />
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" :disabled="savingJar" @click="jarDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="savingJar" :disabled="!canSaveJar" @click="saveJar">Save {{ money(jarCents) }}</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snack.show" :color="snack.color" :timeout="5000">{{ snack.text }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import dayjs from 'dayjs'
import {
    CashService, splitEvenly,
    type TipsOwedResponse, type TipsOwedRow, type TipPayoutSource, type TipJarResponse, type TipJarSplit,
} from '@/services/CashService'
import { formatTenantDateTime, tenantWallClockNow } from '@/helpers/TenantTime'
import { useConfirm } from '@/composables/useConfirm'

const service = new CashService()
const confirm = useConfirm()

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

// ── Tip jar ──────────────────────────────────────────────────────────
// The date is a tenant calendar date (yyyy-MM-dd): "today" at the track, never the laptop's, and
// never run through the UTC converters.
const jarDate = ref(tenantWallClockNow('YYYY-MM-DD'))
const jar = ref<TipJarResponse | null>(null)
const jarLoading = ref(false)
const jarError = ref('')
const prettyJarDate = computed(() => dayjs(jarDate.value).isValid() ? dayjs(jarDate.value).format('ddd, MMM D') : jarDate.value)

async function loadJar() {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(jarDate.value)) return   // the date input is mid-edit
    jarLoading.value = true
    jarError.value = ''
    try {
        jar.value = (await service.tipJar(jarDate.value)).data.data
    } catch (err: any) {
        jar.value = null
        jarError.value = err.response?.data?.error || 'Could not load the tip jar for this date.'
    } finally {
        jarLoading.value = false
    }
}
watch(jarDate, loadJar)

const jarDialog = ref(false)
const jarDollars = ref<number | null>(null)
// Ordered: the server hands any leftover cents to the first ids, so the preview lists them the same way.
const jarSelectedIds = ref<string[]>([])
const jarNote = ref('')
const savingJar = ref(false)
const deletingJarId = ref<string | null>(null)

// A cleared v-model.number field holds '' rather than null; only a real number counts as an amount.
function hasAmount(d: unknown): d is number { return typeof d === 'number' && Number.isFinite(d) }
const jarCents = computed(() => hasAmount(jarDollars.value) ? Math.round(jarDollars.value * 100) : 0)
const jarAmountError = computed(() => hasAmount(jarDollars.value) && jarCents.value <= 0 ? 'Enter an amount greater than $0.00.' : '')
// Every counter staff member, the people who closed a shift that day first.
const jarPeople = computed(() => {
    const j = jar.value
    if (!j) return []
    const worked = new Set(j.suggestedUserIds)
    return [...j.staff].sort((a, b) => Number(worked.has(b.userId)) - Number(worked.has(a.userId)) || a.name.localeCompare(b.name))
})
const jarShares = computed(() => {
    const names = new Map(jarPeople.value.map(p => [p.userId, p.name]))
    const amounts = splitEvenly(jarCents.value, jarSelectedIds.value.length)
    return jarSelectedIds.value.map((id, i) => ({ userId: id, name: names.get(id) ?? 'Staff', amountCents: amounts[i] }))
})
const jarRemainderCents = computed(() => jarSelectedIds.value.length ? jarCents.value % jarSelectedIds.value.length : 0)
const canSaveJar = computed(() => jarCents.value > 0 && jarSelectedIds.value.length > 0 && !jarAmountError.value)

function openRecordJar() {
    const j = jar.value
    if (!j) return
    jarDollars.value = null
    jarSelectedIds.value = j.suggestedUserIds.filter(id => j.staff.some(s => s.userId === id))
    jarNote.value = ''
    jarDialog.value = true
}
function toggleJarPerson(userId: string) {
    const i = jarSelectedIds.value.indexOf(userId)
    if (i >= 0) jarSelectedIds.value.splice(i, 1)
    else jarSelectedIds.value.push(userId)
}
async function saveJar() {
    if (!canSaveJar.value) return
    savingJar.value = true
    try {
        await service.recordTipJar({
            date: jarDate.value,
            totalCents: jarCents.value,
            userIds: [...jarSelectedIds.value],
            note: jarNote.value.trim(),
        })
        flash(`Recorded ${money(jarCents.value)} from the tip jar.`, 'success')
        jarDialog.value = false
        loadJar()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not record the tip jar. Refresh and try again.')
    } finally {
        savingJar.value = false
    }
}
async function deleteJar(s: TipJarSplit) {
    if (!await confirm({
        title: 'Remove this tip jar split?',
        message: `${money(s.totalCents)} split ${s.shares.length} way${s.shares.length === 1 ? '' : 's'} comes off the ${prettyJarDate.value} tips sheet. The cash itself is not affected.`,
        confirmText: 'Remove',
        confirmColor: 'error',
    })) return
    deletingJarId.value = s.id
    try {
        await service.deleteTipJar(s.id)
        flash('Tip jar split removed.', 'success')
        loadJar()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not remove the split.')
    } finally {
        deletingJarId.value = null
    }
}

onMounted(() => { load(); loadJar() })
</script>

<style scoped>
.owed-cell { font-weight: 600; }
/* The live split, boxed like the POS shift summary so the figures read before the Save button. */
.jar-preview { padding: 10px 12px; border-radius: var(--rp-radius-sm); background: var(--rp-panel2); }
</style>
