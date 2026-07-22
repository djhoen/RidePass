<template>
    <v-container>
        <div class="d-flex align-center mb-4 ga-3 flex-wrap">
            <h1 class="text-h4">Store Credit</h1>
            <v-chip v-if="!loading" variant="tonal" color="primary">{{ money(outstandingCents) }} outstanding</v-chip>
            <v-spacer></v-spacer>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">New account</v-btn>
        </div>

        <v-text-field v-model="query" label="Search by name, email, or phone" density="compact"
            prepend-inner-icon="mdi-magnify" clearable hide-details class="mb-4" style="max-width: 420px"
            @update:model-value="debouncedSearch"></v-text-field>

        <v-card v-if="loading" class="pa-6 text-center"><v-progress-circular indeterminate color="primary" /></v-card>
        <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>
        <v-card v-else-if="accounts.length === 0" class="pa-6 text-center text-medium-emphasis">
            No credit accounts yet. Credit appears here from deposit overages, refunds-to-credit, or manual grants.
        </v-card>
        <v-table v-else density="compact">
            <thead>
                <tr><th>Customer</th><th>Email</th><th>Phone</th><th class="text-right">Balance</th><th></th></tr>
            </thead>
            <tbody>
                <tr v-for="a in accounts" :key="a.id">
                    <td>{{ a.displayName || '(no name)' }}</td>
                    <td class="text-caption">{{ a.email || '' }}</td>
                    <td class="text-caption">{{ a.phone || '' }}</td>
                    <td class="text-right">{{ money(a.balanceCents) }}</td>
                    <td class="text-right">
                        <v-tooltip text="History & adjust" location="top">
                            <template #activator="{ props }">
                                <v-btn v-bind="props" size="x-small" variant="text" icon="mdi-history" @click="openDetail(a)"></v-btn>
                            </template>
                        </v-tooltip>
                    </td>
                </tr>
            </tbody>
        </v-table>

        <!-- ── Account detail: history + adjust ─────────────────────────── -->
        <v-dialog v-model="detailOpen" max-width="640">
            <v-card v-if="detail" class="d-flex flex-column" style="max-height: 90vh">
                <v-card-title class="d-flex align-center" style="flex: 0 0 auto">
                    <span>{{ detail.displayName || detail.email || detail.phone }}</span>
                    <v-chip size="small" class="ml-2" color="primary" variant="tonal">{{ money(detail.balanceCents) }}</v-chip>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="detailOpen = false"></v-btn>
                </v-card-title>
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <div class="d-flex align-center ga-2 flex-wrap mb-3">
                        <v-text-field v-model.number="adjustDollars" type="number" step="0.01" prefix="$"
                            label="Adjust (+ grant / - correct)" density="compact" hide-details style="max-width: 190px"></v-text-field>
                        <v-text-field v-model="adjustNote" label="Note" density="compact" hide-details style="min-width: 180px"
                            class="flex-grow-1"></v-text-field>
                        <v-btn variant="tonal" color="primary" :loading="adjusting" @click="doAdjust">Apply</v-btn>
                    </div>
                    <div v-if="detailError" class="text-error text-body-2 mb-2">{{ detailError }}</div>

                    <v-table density="compact">
                        <thead><tr><th>When</th><th>What</th><th class="text-right">Amount</th></tr></thead>
                        <tbody>
                            <tr v-for="e in entries" :key="e.id">
                                <td class="text-caption">{{ formatDate(e.createdAt) }}</td>
                                <td class="text-caption">{{ kindLabel(e.kind) }}<span v-if="e.note" class="text-medium-emphasis"> · {{ e.note }}</span></td>
                                <td class="text-right" :class="e.deltaCents < 0 ? 'text-error' : 'text-success'">
                                    {{ e.deltaCents < 0 ? '-' : '+' }}{{ money(Math.abs(e.deltaCents)) }}</td>
                            </tr>
                            <tr v-if="entries.length === 0"><td colspan="3" class="text-center text-medium-emphasis">No activity yet.</td></tr>
                        </tbody>
                    </v-table>
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- ── New account ──────────────────────────────────────────────── -->
        <v-dialog v-model="createOpen" max-width="440">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>New credit account</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="createOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="createForm.displayName" label="Customer name" density="compact" hide-details></v-text-field>
                    <v-text-field v-model="createForm.email" type="email" label="Email" density="compact" hide-details class="mt-4"></v-text-field>
                    <v-text-field v-model="createForm.phone" label="Phone" density="compact" hide-details class="mt-4"></v-text-field>
                    <p class="text-caption text-medium-emphasis mt-2">An email or phone is required so the account can be looked up at the register.</p>
                    <div v-if="createError" class="text-error text-body-2 mt-2">{{ createError }}</div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="creating" @click="createOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="creating" @click="doCreate">Create</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackColor" :timeout="3500">{{ snackText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { CreditService, type CreditAccount, type CreditEntry } from '@/services/CreditService'

const service = new CreditService()

const accounts = ref<CreditAccount[]>([])
const outstandingCents = ref(0)
const loading = ref(false)
const loadError = ref('')
const query = ref('')
let searchTimer: ReturnType<typeof setTimeout> | null = null

const snackbar = ref(false); const snackText = ref(''); const snackColor = ref<'success' | 'error'>('success')
function flash(t: string, c: 'success' | 'error' = 'success') { snackText.value = t; snackColor.value = c; snackbar.value = true }
function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
function formatDate(iso: string): string { return dayjs(iso).format('MMM D, h:mm a') }
function kindLabel(kind: CreditEntry['kind']): string {
    switch (kind) {
        case 'deposit_excess': return 'Deposit overage'
        case 'refund_to_credit': return 'Refund to credit'
        case 'loyalty_award': return 'Loyalty award'
        case 'manual_adjust': return 'Manual adjustment'
        case 'redeem': return 'Spent at register'
        case 'redeem_reversal': return 'Returned (sale failed/refunded)'
        default: return kind
    }
}

function debouncedSearch() {
    if (searchTimer) clearTimeout(searchTimer)
    searchTimer = setTimeout(reload, 300)
}

async function reload() {
    loading.value = accounts.value.length === 0
    loadError.value = ''
    try {
        const r = await service.searchAccounts(query.value?.trim() || null)
        accounts.value = r.data.data.accounts
        outstandingCents.value = r.data.data.outstandingCents
    } catch (e: any) {
        loadError.value = e.response?.data?.error || 'Could not load credit accounts. Refresh to try again.'
    } finally { loading.value = false }
}

// ── Detail + adjust ────────────────────────────────────────────────────────
const detailOpen = ref(false)
const detail = ref<CreditAccount | null>(null)
const entries = ref<CreditEntry[]>([])
const detailError = ref('')
const adjustDollars = ref<number | null>(null)
const adjustNote = ref('')
const adjusting = ref(false)

async function openDetail(a: CreditAccount) {
    detail.value = a
    entries.value = []
    detailError.value = ''
    adjustDollars.value = null
    adjustNote.value = ''
    detailOpen.value = true
    try {
        const r = await service.getEntries(a.id)
        detail.value = r.data.data.account
        entries.value = r.data.data.entries
    } catch (e: any) {
        detailError.value = e.response?.data?.error || 'Could not load the account history.'
    }
}

async function doAdjust() {
    if (!detail.value) return
    detailError.value = ''
    const cents = adjustDollars.value != null && !isNaN(adjustDollars.value)
        ? Math.round(adjustDollars.value * 100) : 0
    if (cents === 0) { detailError.value = 'Enter a non-zero amount.'; return }
    adjusting.value = true
    try {
        await service.adjust(detail.value.id, cents, adjustNote.value.trim() || null)
        flash(cents > 0 ? `Granted ${money(cents)}.` : `Removed ${money(-cents)}.`)
        await openDetail(detail.value)
        await reload()
    } catch (e: any) {
        detailError.value = e.response?.data?.error || 'Could not adjust the balance.'
    } finally { adjusting.value = false }
}

// ── Create ─────────────────────────────────────────────────────────────────
const createOpen = ref(false)
const creating = ref(false)
const createError = ref('')
const createForm = ref({ displayName: '', email: '', phone: '' })

function openCreate() {
    createForm.value = { displayName: '', email: '', phone: '' }
    createError.value = ''
    createOpen.value = true
}

async function doCreate() {
    createError.value = ''
    creating.value = true
    try {
        await service.createAccount({
            displayName: createForm.value.displayName.trim() || null,
            email: createForm.value.email.trim() || null,
            phone: createForm.value.phone.trim() || null,
        })
        createOpen.value = false
        flash('Credit account created.')
        await reload()
    } catch (e: any) {
        createError.value = e.response?.data?.error || 'Could not create the account.'
    } finally { creating.value = false }
}

onMounted(reload)
</script>
