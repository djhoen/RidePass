<template>
    <v-container>
        <div class="d-flex align-center mb-3 ga-3 flex-wrap">
            <h1 class="text-h4">Order History</h1>
            <v-spacer></v-spacer>
            <v-text-field v-model="dateFrom" type="date" label="From" density="compact" hide-details
                :max="dateTo || undefined" style="max-width: 160px" @update:model-value="load"></v-text-field>
            <v-text-field v-model="dateTo" type="date" label="To" density="compact" hide-details
                :min="dateFrom || undefined" style="max-width: 160px" @update:model-value="load"></v-text-field>
            <v-btn variant="text" size="small" @click="resetToday">Today</v-btn>
            <v-text-field v-model="query" label="Search order # or name" density="compact" hide-details clearable
                prepend-inner-icon="mdi-magnify" style="max-width: 280px"
                @keyup.enter="load" @click:clear="onClear"></v-text-field>
            <v-btn @click="load">Search</v-btn>
        </div>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 90px">Order #</th>
                        <th style="width: 170px">Placed</th>
                        <th>Customer</th>
                        <th style="width: 110px">Channel</th>
                        <th style="width: 100px" class="text-right">Total</th>
                        <th style="width: 160px">Status</th>
                        <th style="width: 80px"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="o in orders" :key="o.saleId" style="cursor: pointer" @click="openDetail(o.saleId)">
                        <td class="font-weight-bold">#{{ o.orderNumber ?? '—' }}</td>
                        <td>{{ new Date(o.createdAtUtc).toLocaleString() }}</td>
                        <td>
                            {{ o.customerName || (o.orderChannel === 'online' ? 'Online customer' : 'Walk-up') }}
                            <v-icon v-if="o.isRush" color="error" size="x-small" class="ml-1">mdi-flash</v-icon>
                        </td>
                        <td><v-chip size="x-small" variant="tonal">{{ o.orderChannel === 'online' ? 'Online' : 'Counter' }}</v-chip></td>
                        <td class="text-right">
                            {{ money(o.totalCents) }}
                            <div v-if="o.discountCents > 0" class="text-caption" style="color: rgb(var(--v-theme-success))">
                                -{{ money(o.discountCents) }}{{ o.discountKind === 'comp' ? ' comp' : ' off' }}
                            </div>
                        </td>
                        <td>
                            <v-chip size="x-small" variant="tonal"
                                :color="o.status === 'refunded' ? 'error' : fulfillmentColor(o.fulfillmentStatus)">
                                {{ o.status === 'refunded' ? 'Refunded' : fulfillmentLabel(o.fulfillmentStatus) }}
                            </v-chip>
                            <v-chip size="x-small" variant="tonal" class="ml-1">{{ o.paymentMethod === 'cash' ? 'Cash' : 'Card' }}</v-chip>
                        </td>
                        <td><v-btn variant="text" size="small" @click.stop="openDetail(o.saleId)">View</v-btn></td>
                    </tr>
                    <tr v-if="!loading && orders.length === 0">
                        <td colspan="7" class="text-center text-medium-emphasis py-8">No orders found.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- Order detail -->
        <v-dialog v-model="detailDialog" max-width="560">
            <v-card v-if="detail">
                <v-card-title class="d-flex align-center">
                    <span>Order #{{ detail.orderNumber ?? '—' }}</span>
                    <v-chip v-if="detail.status === 'refunded'" size="small" color="error" variant="flat" class="ml-2">Refunded</v-chip>
                    <v-spacer></v-spacer>
                    <v-btn v-if="detail.status !== 'refunded' && detail.fulfillmentStatus !== 'completed'"
                        size="small" variant="tonal" prepend-icon="mdi-flash" class="mr-2"
                        :color="detail.isRush ? 'error' : undefined" :loading="settingRush" @click="toggleRush">
                        {{ detail.isRush ? 'Un-rush' : 'Rush' }}
                    </v-btn>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="detailDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div class="text-caption text-medium-emphasis mb-3">
                        {{ new Date(detail.createdAtUtc).toLocaleString() }} ·
                        {{ detail.orderChannel === 'online' ? 'Online' : 'Counter' }} ·
                        {{ detail.paymentMethod === 'cash' ? 'Cash' : 'Card' }}
                        <template v-if="detail.customerName"> · {{ detail.customerName }}</template>
                    </div>

                    <div v-for="l in topLines(detail.lines)" :key="l.lineId" class="mb-2">
                        <div class="d-flex justify-space-between">
                            <span class="font-weight-medium">
                                {{ l.quantity }}× {{ l.name }}<span v-if="l.variantLabel"> ({{ l.variantLabel }})</span>
                                <span v-if="l.isCombo && l.comboTier" class="text-caption text-medium-emphasis"> · {{ l.comboTier }} combo</span>
                            </span>
                            <span>{{ money(l.lineTotalCents) }}</span>
                        </div>
                        <div v-if="l.discountCents > 0" class="text-caption ml-4" style="color: rgb(var(--v-theme-success))">
                            {{ l.discountLabel || 'Discount' }} (-{{ money(l.discountCents) }})
                        </div>
                        <div v-for="m in l.modifiers" :key="m" class="text-caption text-medium-emphasis ml-4">+ {{ m }}</div>
                        <div v-if="l.notes" class="text-caption font-italic ml-4">"{{ l.notes }}"</div>
                        <!-- combo component lines -->
                        <div v-for="c in childLines(detail.lines, l.lineId)" :key="c.lineId" class="text-body-2 ml-4">
                            {{ c.name }}<span v-if="c.variantLabel"> ({{ c.variantLabel }})</span>
                            <span v-for="m in c.modifiers" :key="m" class="text-caption text-medium-emphasis"> · {{ m }}</span>
                        </div>
                    </div>

                    <v-divider class="my-3"></v-divider>
                    <div class="d-flex justify-space-between text-body-2"><span>Subtotal</span><span>{{ money(detail.pricesIncludeTax ? detail.subtotalCents - detail.taxCents : detail.subtotalCents) }}</span></div>
                    <div v-if="detail.discountCents > 0" class="d-flex justify-space-between text-body-2" style="color: rgb(var(--v-theme-success))">
                        <span>{{ detail.discountLabel || 'Discount' }}<span v-if="detail.discountKind === 'comp'"> (comp)</span></span>
                        <span>-{{ money(detail.discountCents) }}</span>
                    </div>
                    <div v-if="detail.authorizedByName" class="text-caption text-medium-emphasis">
                        <v-icon size="x-small" class="mr-1">mdi-shield-check</v-icon>Approved by {{ detail.authorizedByName }}
                    </div>
                    <div v-if="detail.taxCents > 0" class="d-flex justify-space-between text-body-2"><span>Tax{{ detail.pricesIncludeTax ? ' (incl.)' : '' }}</span><span>{{ money(detail.taxCents) }}</span></div>
                    <div v-if="detail.tipCents > 0" class="d-flex justify-space-between text-body-2"><span>Tip</span><span>{{ money(detail.tipCents) }}</span></div>
                    <div class="d-flex justify-space-between text-h6 mt-1"><span>Total</span><span>{{ money(detail.totalCents) }}</span></div>

                    <v-divider class="my-3"></v-divider>
                    <div class="text-subtitle-2 mb-1">Send receipt</div>
                    <div class="d-flex align-center ga-2 flex-wrap">
                        <v-btn-toggle v-model="receiptChannel" density="compact" mandatory variant="outlined">
                            <v-btn value="sms" size="small">Text</v-btn>
                            <v-btn value="email" size="small">Email</v-btn>
                        </v-btn-toggle>
                        <v-text-field v-model="receiptDest" density="compact" hide-details
                            :placeholder="receiptChannel === 'sms' ? 'Phone number' : 'Email address'" style="min-width: 200px; flex: 1"></v-text-field>
                        <v-btn color="primary" size="small" :loading="sendingReceipt" :disabled="!receiptDest" @click="sendReceipt">Send</v-btn>
                    </div>
                </v-card-text>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snack.show" :color="snack.color" :timeout="3500">{{ snack.text }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ConcessionService, type OrderSummary, type OrderDetail, type OrderDetailLine } from '@/services/ConcessionService'

const svc = new ConcessionService()
const orders = ref<OrderSummary[]>([])
const loading = ref(false)
const query = ref('')
// Default to today (local yyyy-MM-dd); the server interprets these in the tenant's timezone.
function todayStr() {
    const d = new Date()
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
const dateFrom = ref(todayStr())
const dateTo = ref(todayStr())
const snack = ref({ show: false, text: '', color: 'error' })
function flash(text: string, color: 'error' | 'success' = 'error') { snack.value = { show: true, text, color } }
function money(c: number) { return `$${(c / 100).toFixed(2)}` }
function fulfillmentLabel(s: string) { return s === 'completed' ? 'Picked up' : s === 'ready' ? 'Ready' : 'Preparing' }
// Distinct color per stage: preparing = amber, ready = green, picked up = grey.
function fulfillmentColor(s: string) { return s === 'completed' ? 'grey' : s === 'ready' ? 'success' : 'warning' }

// Combo nesting helpers.
function topLines(lines: OrderDetailLine[]) { return lines.filter(l => !l.parentLineId) }
function childLines(lines: OrderDetailLine[], parentId: string) { return lines.filter(l => l.parentLineId === parentId) }

onMounted(load)
async function load() {
    loading.value = true
    try {
        const r = await svc.orders({
            q: query.value?.trim() || undefined,
            from: dateFrom.value || undefined,
            to: dateTo.value || undefined,
        })
        orders.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load orders. Refresh to try again.')
    } finally {
        loading.value = false
    }
}
function onClear() { query.value = ''; load() }
function resetToday() { dateFrom.value = todayStr(); dateTo.value = todayStr(); load() }

// ── Detail ───────────────────────────────────────────────────────────
const detailDialog = ref(false)
const detail = ref<OrderDetail | null>(null)
const receiptChannel = ref<'sms' | 'email'>('sms')
const receiptDest = ref('')
const sendingReceipt = ref(false)
const settingRush = ref(false)

// Cashiers can flag an in-progress order as rush (the cook screen prioritizes it).
async function toggleRush() {
    if (!detail.value) return
    const d = detail.value
    const next = !d.isRush
    settingRush.value = true
    try {
        await svc.setRush(d.saleId, next)
        d.isRush = next
        const row = orders.value.find(o => o.saleId === d.saleId)
        if (row) row.isRush = next
        flash(next ? 'Order marked rush.' : 'Rush cleared.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not update rush.')
    } finally {
        settingRush.value = false
    }
}

async function openDetail(saleId: string) {
    detail.value = null
    receiptDest.value = ''
    detailDialog.value = true
    try {
        detail.value = (await svc.order(saleId) as any).data.data
    } catch (err: any) {
        detailDialog.value = false
        flash(err.response?.data?.error || 'Could not load that order.')
    }
}

async function sendReceipt() {
    if (!detail.value || !receiptDest.value.trim()) return
    sendingReceipt.value = true
    try {
        await svc.sendReceipt(detail.value.saleId, receiptChannel.value, receiptDest.value.trim())
        flash('Receipt sent.', 'success')
        receiptDest.value = ''
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not send the receipt.')
    } finally {
        sendingReceipt.value = false
    }
}
</script>
