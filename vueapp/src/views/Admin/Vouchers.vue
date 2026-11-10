<template>
    <v-container style="max-width: 960px">
        <div class="rp-pagehead">
            <div>
                <div class="rp-pagehead-title">Vouchers</div>
                <div class="rp-pagehead-sub">Free day passes to give away. Each code admits one rider once: they use it at checkout
                    online, or the gate types it in at Gate Sale. The rider needs a profile and a signed waiver like anyone else.</div>
            </div>
        </div>

        <v-tabs v-if="mine?.canManage" v-model="tab" density="compact" class="mb-4">
            <v-tab value="mine">Mine</v-tab>
            <v-tab value="staff">Staff allowances</v-tab>
            <v-tab value="vendor">Vendor lists</v-tab>
            <v-tab value="given">All given by staff</v-tab>
        </v-tabs>

        <div v-if="loading" class="text-center pa-6"><v-progress-circular indeterminate></v-progress-circular></div>

        <!-- Mine -->
        <template v-else-if="tab === 'mine' && mine">
            <v-card class="pa-4 mb-4">
                <div class="d-flex align-center flex-wrap ga-3">
                    <div>
                        <div class="text-overline">Your vouchers in {{ mine.year }}</div>
                        <div class="text-h5" v-if="mine.unlimited">Unlimited</div>
                        <div class="text-h5" v-else>{{ mine.remaining }} left
                            <span class="text-body-2 text-medium-emphasis">of {{ mine.yearlyAllowance ?? 0 }}</span></div>
                        <div class="text-caption text-medium-emphasis">{{ mine.issuedThisYear }} given this year</div>
                    </div>
                    <v-spacer></v-spacer>
                    <v-btn color="primary" prepend-icon="mdi-gift-outline" :disabled="!mine.unlimited && (mine.remaining ?? 0) < 1"
                        @click="openGive">Give vouchers</v-btn>
                </div>
                <p v-if="!mine.unlimited && (mine.remaining ?? 0) < 1" class="text-caption text-medium-emphasis mt-2 mb-0">
                    You have none left this year. An Admin sets how many each staff member can give.
                </p>
            </v-card>
            <BatchTable :batches="mine.batches" :show-issuer="false" empty="You haven't given any vouchers yet."
                @open="openCodes" />
        </template>

        <!-- Staff allowances -->
        <template v-else-if="tab === 'staff'">
            <v-card class="pa-2">
                <v-table density="comfortable">
                    <thead>
                        <tr><th>Staff member</th><th>Allowance a year</th><th class="text-right">Given</th><th class="text-right">Used</th>
                            <th class="text-right">Left</th><th></th></tr>
                    </thead>
                    <tbody>
                        <tr v-for="s in staff" :key="s.userId">
                            <td>{{ s.name }}<div class="text-caption text-medium-emphasis">{{ s.email }}</div></td>
                            <td>
                                <span v-if="s.isAdmin">Unlimited <span class="text-caption text-medium-emphasis">(Admin)</span></span>
                                <span v-else-if="s.unlimited">Unlimited</span>
                                <span v-else>{{ s.yearlyAllowance ?? 0 }}</span>
                            </td>
                            <td class="text-right">{{ s.issuedThisYear }}</td>
                            <td class="text-right">{{ s.redeemedThisYear }}</td>
                            <td class="text-right">{{ s.remaining === null ? '—' : s.remaining }}</td>
                            <td class="text-right">
                                <v-btn v-if="!s.isAdmin" size="small" variant="text" @click="openAllowance(s)">Change</v-btn>
                            </td>
                        </tr>
                        <tr v-if="staff.length === 0"><td colspan="6" class="text-medium-emphasis">No staff yet.</td></tr>
                    </tbody>
                </v-table>
            </v-card>
            <p class="text-caption text-medium-emphasis mt-2">Counts are for this calendar year. A code voided before anyone
                used it goes back on the giver's allowance. Admins are always unlimited.</p>
        </template>

        <!-- Vendor lists -->
        <template v-else-if="tab === 'vendor'">
            <div class="d-flex mb-3">
                <v-spacer></v-spacer>
                <v-btn color="primary" prepend-icon="mdi-plus" @click="openVendor">New vendor list</v-btn>
            </div>
            <BatchTable :batches="vendorBatches" :show-issuer="true" empty="No vendor lists yet. Make one for a sponsor or partner, e.g. 10 for Red Bull."
                @open="openCodes" />
        </template>

        <!-- Everything staff have given -->
        <template v-else-if="tab === 'given'">
            <BatchTable :batches="employeeBatches" :show-issuer="true" empty="No staff have given vouchers yet." @open="openCodes" />
        </template>

        <!-- Give vouchers / new vendor list -->
        <v-dialog v-model="issueDialog" max-width="520">
            <v-card>
                <v-card-title>{{ issueKind === 'vendor' ? 'New vendor list' : 'Give vouchers' }}</v-card-title>
                <v-card-text>
                    <template v-if="!issued">
                        <v-text-field v-model="form.label" :label="issueKind === 'vendor' ? 'For (e.g. Red Bull)' : 'Who are they for?'"
                            density="compact" class="mb-2"></v-text-field>
                        <v-text-field v-model.number="form.count" type="number" min="1" :max="issueKind === 'vendor' ? 500 : maxGive"
                            label="How many codes" density="compact" class="mb-2"
                            :hint="issueKind === 'vendor' ? 'Up to 500.' : mine?.unlimited ? 'Up to 25 at a time.' : `You have ${mine?.remaining ?? 0} left.`"
                            persistent-hint></v-text-field>
                        <v-text-field v-model="form.email" label="Email the codes to (optional)" type="email" density="compact"
                            class="mb-2"></v-text-field>
                        <v-text-field v-model="form.expiresOn" label="Last day they work (optional)" type="date" density="compact"
                            class="mb-2"></v-text-field>
                        <v-textarea v-model="form.note" label="Note (optional, goes in the email)" rows="2" auto-grow density="compact"></v-textarea>
                        <div v-if="issueError" class="text-error text-body-2">{{ issueError }}</div>
                    </template>
                    <template v-else>
                        <v-alert type="success" variant="tonal" density="compact" class="mb-3">
                            {{ issued.codes.length }} code{{ issued.codes.length === 1 ? '' : 's' }} for {{ issued.batch.label }}.
                            <span v-if="issued.emailed">Emailed to {{ issued.batch.recipientEmail }}.</span>
                            <span v-else-if="issued.batch.recipientEmail">The email could not be sent; copy the codes below.</span>
                        </v-alert>
                        <div class="code-list">
                            <div v-for="c in issued.codes" :key="c" class="code">{{ c }}</div>
                        </div>
                    </template>
                </v-card-text>
                <v-card-actions>
                    <template v-if="!issued">
                        <v-spacer></v-spacer>
                        <v-btn variant="text" @click="issueDialog = false">Cancel</v-btn>
                        <v-btn color="primary" variant="flat" :loading="issuing" :disabled="!canIssue" @click="issue">
                            {{ issueKind === 'vendor' ? 'Make codes' : 'Give' }}
                        </v-btn>
                    </template>
                    <template v-else>
                        <v-btn variant="text" prepend-icon="mdi-content-copy" @click="copy(issued.codes)">Copy codes</v-btn>
                        <v-spacer></v-spacer>
                        <v-btn color="primary" variant="flat" @click="issueDialog = false">Done</v-btn>
                    </template>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- One batch's codes -->
        <v-dialog v-model="codesDialog" max-width="720" scrollable>
            <v-card v-if="openBatch">
                <v-card-title>{{ openBatch.label }}</v-card-title>
                <v-card-subtitle>{{ openBatch.codeCount }} code{{ openBatch.codeCount === 1 ? '' : 's' }} ·
                    {{ openBatch.kind === 'vendor' ? 'vendor list' : 'from ' + openBatch.issuedByName }} ·
                    {{ formatTenantDate(openBatch.createdAtUtc) }}</v-card-subtitle>
                <v-card-text>
                    <div v-if="codesLoading" class="text-center pa-4"><v-progress-circular indeterminate></v-progress-circular></div>
                    <v-table v-else density="compact">
                        <thead><tr><th>Code</th><th>Status</th><th>Used by</th><th></th></tr></thead>
                        <tbody>
                            <tr v-for="c in codes" :key="c.id">
                                <td class="code-cell">{{ c.code }}</td>
                                <td><v-chip size="x-small" variant="tonal" :color="statusColor(c.status)">{{ statusLabel(c.status) }}</v-chip></td>
                                <td>
                                    <template v-if="c.status === 'redeemed'">
                                        {{ c.riderName || c.redeemedByName }}
                                        <div class="text-caption text-medium-emphasis">{{ c.eventTitle }} · {{ formatTenantDateTime(c.redeemedAtUtc, 'MMM D, h:mm A') }}</div>
                                    </template>
                                    <span v-else-if="c.expiresAtUtc" class="text-caption text-medium-emphasis">Good through {{ formatTenantDate(lastDay(c.expiresAtUtc)) }}</span>
                                </td>
                                <td class="text-right">
                                    <v-btn v-if="c.status === 'unused' && canVoid" size="x-small" variant="text" @click="voidOne(c)">Void</v-btn>
                                </td>
                            </tr>
                        </tbody>
                    </v-table>
                </v-card-text>
                <v-card-actions>
                    <v-btn variant="text" size="small" prepend-icon="mdi-content-copy"
                        @click="copy(codes.filter(c => c.status === 'unused').map(c => c.code))">Copy unused</v-btn>
                    <v-btn variant="text" size="small" prepend-icon="mdi-download" @click="downloadCsv">CSV</v-btn>
                    <v-btn v-if="canVoid && codes.some(c => c.status === 'unused')" variant="text" size="small" color="error"
                        @click="voidAll">Void unused</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="codesDialog = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Allowance -->
        <v-dialog v-model="allowanceDialog" max-width="420">
            <v-card v-if="allowanceFor">
                <v-card-title>{{ allowanceFor.name }}</v-card-title>
                <v-card-text>
                    <v-switch v-model="allowanceUnlimited" label="Unlimited" color="primary" density="compact" hide-details class="mb-2"></v-switch>
                    <v-text-field v-if="!allowanceUnlimited" v-model.number="allowanceNumber" type="number" min="0" max="1000"
                        label="Vouchers a year" density="compact"
                        :hint="`${allowanceFor.issuedThisYear} given so far this year.`" persistent-hint></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="allowanceDialog = false">Cancel</v-btn>
                    <v-btn color="primary" variant="flat" :loading="savingAllowance"
                        :disabled="!allowanceUnlimited && !(allowanceNumber >= 0 && allowanceNumber <= 1000)"
                        @click="saveAllowance">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch, defineComponent, h } from 'vue'
import { VTable, VChip, VCard, VBtn } from 'vuetify/components'
import { VoucherService, type MyVouchers, type VoucherBatch, type VoucherCode, type StaffVoucher, type IssuedVouchers } from '@/services/VoucherService'
import { formatTenantDate, formatTenantDateTime } from '@/helpers/TenantTime'
import { useConfirm } from '@/composables/useConfirm'
import { useRoute, useRouter } from 'vue-router'

const svc = new VoucherService()
const confirm = useConfirm()
const route = useRoute()
const router = useRouter()

const validTabs = ['mine', 'staff', 'vendor', 'given'] as const
type Tab = typeof validTabs[number]
const tab = ref<Tab>((validTabs as readonly string[]).includes(String(route.query.tab)) ? route.query.tab as Tab : 'mine')
watch(tab, t => { void router.replace({ query: { ...route.query, tab: t === 'mine' ? undefined : t } }); void loadTab() })

const loading = ref(true)
const mine = ref<MyVouchers | null>(null)
const staff = ref<StaffVoucher[]>([])
const vendorBatches = ref<VoucherBatch[]>([])
const employeeBatches = ref<VoucherBatch[]>([])

// A small table shared by the Mine, Vendor and All-given tabs.
const BatchTable = defineComponent({
    props: { batches: { type: Array as () => VoucherBatch[], required: true }, showIssuer: Boolean, empty: String },
    emits: ['open'],
    setup(props, { emit }) {
        return () => h(VCard, { class: 'pa-2' }, () => props.batches.length === 0
            ? h('div', { class: 'text-medium-emphasis pa-4' }, props.empty)
            : h(VTable, { density: 'comfortable' }, () => [
                h('thead', h('tr', [
                    h('th', 'Given'), h('th', 'For'), props.showIssuer ? h('th', 'By') : null,
                    h('th', { class: 'text-right' }, 'Codes'), h('th', { class: 'text-right' }, 'Used'), h('th', ''),
                ])),
                h('tbody', props.batches.map(b => h('tr', { key: b.id }, [
                    h('td', formatTenantDate(b.createdAtUtc)),
                    h('td', [b.label, b.recipientEmail ? h('div', { class: 'text-caption text-medium-emphasis' }, b.recipientEmail) : null]),
                    props.showIssuer ? h('td', b.issuedByName) : null,
                    h('td', { class: 'text-right' }, [String(b.codeCount),
                        b.voidedCount > 0 ? h('div', { class: 'text-caption text-medium-emphasis' }, `${b.voidedCount} voided`) : null]),
                    h('td', { class: 'text-right' }, [h(VChip, { size: 'x-small', variant: 'tonal',
                        color: b.redeemedCount > 0 ? 'success' : undefined }, () => `${b.redeemedCount} of ${b.codeCount - b.voidedCount}`)]),
                    h('td', { class: 'text-right' }, h(VBtn, { size: 'small', variant: 'text', onClick: () => emit('open', b) }, () => 'Codes')),
                ]))),
            ]))
    },
})

async function loadMine() {
    const r = await svc.mine()
    mine.value = r.data.data
    if (!mine.value.canManage && tab.value !== 'mine') tab.value = 'mine'
}

async function loadTab() {
    try {
        if (tab.value === 'staff') staff.value = (await svc.staff()).data.data
        else if (tab.value === 'vendor') vendorBatches.value = (await svc.batches('vendor')).data.data
        else if (tab.value === 'given') employeeBatches.value = (await svc.batches('employee')).data.data
    } catch (e: any) {
        flash(e?.response?.data?.error || 'Could not load vouchers.', 'error')
    }
}

onMounted(async () => {
    try {
        await loadMine()
        if (tab.value !== 'mine') await loadTab()
    } catch (e: any) {
        flash(e?.response?.data?.error || 'Could not load vouchers.', 'error')
    } finally {
        loading.value = false
    }
})

// ── Give / vendor list ─────────────────────────────────────────────
const issueDialog = ref(false)
const issueKind = ref<'employee' | 'vendor'>('employee')
const issuing = ref(false)
const issueError = ref<string | null>(null)
const issued = ref<IssuedVouchers | null>(null)
const form = ref({ label: '', count: 1, email: '', expiresOn: '', note: '' })
const maxGive = computed(() => mine.value?.unlimited ? 25 : Math.min(25, mine.value?.remaining ?? 0))
const canIssue = computed(() => {
    const max = issueKind.value === 'vendor' ? 500 : maxGive.value
    return form.value.label.trim().length > 0 && Number.isInteger(form.value.count) && form.value.count >= 1 && form.value.count <= max
})

function resetForm(count: number) {
    form.value = { label: '', count, email: '', expiresOn: '', note: '' }
    issued.value = null
    issueError.value = null
}
function openGive() { issueKind.value = 'employee'; resetForm(1); issueDialog.value = true }
function openVendor() { issueKind.value = 'vendor'; resetForm(10); issueDialog.value = true }

async function issue() {
    issuing.value = true
    issueError.value = null
    const body = {
        count: form.value.count,
        recipientEmail: form.value.email.trim() || null,
        note: form.value.note.trim() || null,
        expiresOn: form.value.expiresOn || null,
    }
    try {
        const r = issueKind.value === 'vendor'
            ? await svc.createVendor({ label: form.value.label.trim(), ...body })
            : await svc.give({ recipientName: form.value.label.trim(), ...body })
        issued.value = r.data.data
        await loadMine()
        if (issueKind.value === 'vendor') vendorBatches.value = (await svc.batches('vendor')).data.data
    } catch (e: any) {
        issueError.value = e?.response?.data?.error || 'Could not make the codes.'
    } finally {
        issuing.value = false
    }
}

// ── Codes of a batch ───────────────────────────────────────────────
const codesDialog = ref(false)
const codesLoading = ref(false)
const openBatch = ref<VoucherBatch | null>(null)
const codes = ref<VoucherCode[]>([])
// A giver may void their own gift; vendor lists and other people's are an Admin's.
const canVoid = computed(() => !!openBatch.value && (!!mine.value?.canManage
    || (openBatch.value.kind === 'employee' && tab.value === 'mine')))

async function openCodes(b: VoucherBatch) {
    openBatch.value = b
    codes.value = []
    codesDialog.value = true
    codesLoading.value = true
    try {
        codes.value = (await svc.codes(b.id)).data.data
    } catch (e: any) {
        flash(e?.response?.data?.error || 'Could not load the codes.', 'error')
    } finally {
        codesLoading.value = false
    }
}

async function refreshAfterVoid() {
    if (openBatch.value) codes.value = (await svc.codes(openBatch.value.id)).data.data
    await loadMine()
    await loadTab()
}

async function voidOne(c: VoucherCode) {
    if (!openBatch.value) return
    const ok = await confirm({ title: `Void ${c.code}?`, message: 'Nobody will be able to use this code.', confirmText: 'Void' })
    if (!ok) return
    try {
        await svc.void(openBatch.value.id, c.id)
        await refreshAfterVoid()
        flash('Code voided.', 'success')
    } catch (e: any) {
        flash(e?.response?.data?.error || 'Could not void the code.', 'error')
    }
}

async function voidAll() {
    if (!openBatch.value) return
    const n = codes.value.filter(c => c.status === 'unused').length
    const ok = await confirm({ title: `Void ${n} unused code${n === 1 ? '' : 's'}?`,
        message: 'Codes already used stay as they are.', confirmText: 'Void' })
    if (!ok) return
    try {
        const r = await svc.void(openBatch.value.id, null)
        await refreshAfterVoid()
        flash(`${r.data.data.voided} code${r.data.data.voided === 1 ? '' : 's'} voided.`, 'success')
    } catch (e: any) {
        flash(e?.response?.data?.error || 'Could not void the codes.', 'error')
    }
}

async function downloadCsv() {
    if (!openBatch.value) return
    try {
        const { blob, filename } = await svc.downloadCsv(openBatch.value.id)
        const url = URL.createObjectURL(blob)
        const a = document.createElement('a')
        a.href = url
        a.download = filename
        a.click()
        URL.revokeObjectURL(url)
    } catch {
        flash('Could not download the codes.', 'error')
    }
}

// ── Allowances ─────────────────────────────────────────────────────
const allowanceDialog = ref(false)
const allowanceFor = ref<StaffVoucher | null>(null)
const allowanceUnlimited = ref(false)
const allowanceNumber = ref(0)
const savingAllowance = ref(false)

function openAllowance(s: StaffVoucher) {
    allowanceFor.value = s
    allowanceUnlimited.value = s.unlimited
    allowanceNumber.value = s.yearlyAllowance ?? 0
    allowanceDialog.value = true
}

async function saveAllowance() {
    if (!allowanceFor.value) return
    savingAllowance.value = true
    try {
        await svc.setAllowance(allowanceFor.value.userId, allowanceUnlimited.value ? null : allowanceNumber.value, allowanceUnlimited.value)
        allowanceDialog.value = false
        staff.value = (await svc.staff()).data.data
        flash('Allowance saved.', 'success')
    } catch (e: any) {
        flash(e?.response?.data?.error || 'Could not save the allowance.', 'error')
    } finally {
        savingAllowance.value = false
    }
}

// ── helpers ────────────────────────────────────────────────────────
function statusLabel(s: VoucherCode['status']) {
    return s === 'redeemed' ? 'Used' : s === 'voided' ? 'Voided' : s === 'expired' ? 'Expired' : 'Unused'
}
function statusColor(s: VoucherCode['status']) {
    return s === 'redeemed' ? 'success' : s === 'unused' ? 'primary' : undefined
}
// Codes stop at the end of their last day; show that day, not the next midnight.
function lastDay(utc: string) { return new Date(new Date(utc).getTime() - 60_000).toISOString() }

async function copy(list: string[]) {
    try {
        await navigator.clipboard.writeText(list.join('\n'))
        flash(`Copied ${list.length} code${list.length === 1 ? '' : 's'}.`, 'success')
    } catch {
        flash('Copy failed: select the codes and copy them by hand.', 'error')
    }
}

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')
function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>

<style scoped>
.code-list {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
    gap: 6px;
}
.code, .code-cell {
    font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
    font-weight: 600;
    letter-spacing: 0.02em;
}
.code {
    border: 1px solid var(--rp-line);
    border-radius: 8px;
    padding: 6px 10px;
    background: var(--rp-panel2);
}
</style>
