<template>
    <v-container style="max-width: 760px">
        <h1 class="text-h4 mb-4">Redeem Tickets</h1>

        <v-card class="mb-4 pa-4">
            <v-card-title>Scan QR</v-card-title>
            <v-card-text>
                <div id="qr-reader" class="reader-surface mb-3"></div>
                <div class="d-flex ga-2">
                    <v-btn v-if="!scanning" color="primary" @click="startScan">Start Camera</v-btn>
                    <v-btn v-else color="error" @click="stopScan">Stop Camera</v-btn>
                </div>
                <v-divider class="my-4"></v-divider>
                <p class="text-caption text-medium-emphasis mb-2">Or paste a token / redeem URL:</p>
                <div class="d-flex ga-2">
                    <v-text-field v-model="manualInput" label="Token or URL" density="compact" hide-details></v-text-field>
                    <v-btn color="primary" :loading="loading" @click="lookupManual">Look Up</v-btn>
                </div>
            </v-card-text>
        </v-card>

        <v-card v-if="order" class="mb-4 pa-4">
            <v-card-title class="d-flex align-center flex-wrap ga-2">
                Order
                <v-chip v-if="redeemableCount > 0" size="small" color="success">
                    {{ redeemableCount }} redeemable
                </v-chip>
            </v-card-title>
            <v-card-text>
                <div class="text-body-2 mb-1"><strong>{{ order.purchaserName }}</strong></div>
                <div class="text-body-2 text-medium-emphasis mb-3">{{ order.purchaserEmail }}</div>

                <p v-if="order.items.length === 0" class="text-medium-emphasis">No items found.</p>

                <div v-for="item in order.items" :key="item.purchaseId"
                     class="order-row d-flex align-start py-2 ga-3">
                    <v-checkbox v-model="selectedIds" :value="item.purchaseId"
                        :disabled="!item.isRedeemableToday"
                        hide-details density="compact" class="mt-0"></v-checkbox>
                    <div class="flex-grow-1" style="min-width: 0">
                        <div class="text-body-1">
                            <strong>{{ item.itemName }}</strong>
                            <span class="text-medium-emphasis ml-2">${{ (item.amountCents / 100).toFixed(2) }}</span>
                            <v-chip size="x-small" class="ml-2" :color="statusColor(item.status)">{{ item.status }}</v-chip>
                            <v-chip size="x-small" class="ml-1" variant="tonal">{{ kindLabel(item.kind) }}</v-chip>
                        </div>
                        <div v-if="item.redeemedAtUtc" class="text-caption text-medium-emphasis">
                            Redeemed {{ formatInTenant(item.redeemedAtUtc) }}
                            <span v-if="item.redeemedByName"> by {{ item.redeemedByName }}</span>
                        </div>
                        <div v-else-if="!item.isRedeemableToday && item.notRedeemableReason" class="text-caption text-warning">
                            {{ item.notRedeemableReason }}
                        </div>
                    </div>
                </div>

                <div class="d-flex align-center mt-4 ga-2 flex-wrap">
                    <v-btn v-if="redeemableCount > 0" variant="text" size="small" @click="selectAllRedeemable">
                        Select all redeemable
                    </v-btn>
                    <v-btn v-if="selectedIds.length > 0" variant="text" size="small" @click="selectedIds = []">
                        Clear
                    </v-btn>
                    <v-spacer></v-spacer>
                    <v-btn color="success" :loading="redeeming" :disabled="selectedIds.length === 0" @click="redeemSelected">
                        Redeem {{ selectedIds.length }} {{ selectedIds.length === 1 ? 'item' : 'items' }}
                    </v-btn>
                </div>
            </v-card-text>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onBeforeUnmount } from 'vue'
import dayjs from 'dayjs'
import { Html5Qrcode } from 'html5-qrcode'
import { TicketService, type OrderLookup } from '@/services/TicketService'
import { branding } from '@/stores/branding'

const service = new TicketService()

const manualInput = ref('')
const order = ref<OrderLookup | null>(null)
const orderToken = ref<string | null>(null)        // the originally-scanned token
const selectedIds = ref<string[]>([])
const loading = ref(false)
const redeeming = ref(false)
const scanning = ref(false)

let scanner: Html5Qrcode | null = null

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const redeemableCount = computed(() =>
    order.value?.items.filter(i => i.isRedeemableToday).length ?? 0)

async function startScan() {
    try {
        scanner = new Html5Qrcode('qr-reader')
        await scanner.start(
            { facingMode: 'environment' },
            { fps: 10, qrbox: { width: 260, height: 260 } },
            onDecoded,
            () => {},
        )
        scanning.value = true
    } catch (err: any) {
        flash(err?.message || 'Failed to start camera.', 'error')
    }
}

async function stopScan() {
    if (!scanner) return
    try { await scanner.stop(); await scanner.clear() } catch {}
    scanner = null
    scanning.value = false
}

async function onDecoded(decodedText: string) {
    const token = extractToken(decodedText)
    if (!token) return
    await stopScan()
    await loadOrder(token)
}

function extractToken(raw: string): string | null {
    const direct = raw.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i)
    return direct ? direct[0] : null
}

async function lookupManual() {
    const token = extractToken(manualInput.value)
    if (!token) { flash('No token found in input.', 'error'); return }
    await loadOrder(token)
}

async function loadOrder(token: string) {
    try {
        loading.value = true
        const r = await service.orderLookup(token)
        order.value = (r.data as any).data
        orderToken.value = token
        // Auto-select everything redeemable so the staff can just click Redeem.
        selectedIds.value = order.value?.items
            .filter(i => i.isRedeemableToday)
            .map(i => i.purchaseId) ?? []
    } catch (err: any) {
        flash(err.response?.data?.error || 'Order not found.', 'error')
        order.value = null
        orderToken.value = null
        selectedIds.value = []
    } finally {
        loading.value = false
    }
}

function selectAllRedeemable() {
    selectedIds.value = order.value?.items
        .filter(i => i.isRedeemableToday)
        .map(i => i.purchaseId) ?? []
}

async function redeemSelected() {
    if (!order.value || !orderToken.value || selectedIds.value.length === 0) return
    redeeming.value = true
    try {
        const items = order.value.items
            .filter(i => selectedIds.value.includes(i.purchaseId))
            .map(i => ({ kind: i.kind, purchaseId: i.purchaseId }))
        const r = await service.redeemBulk({ orderToken: orderToken.value, items })
        const data = (r.data as any).data
        if (data.errors?.length) flash(data.errors.join(' '), 'error')
        else flash(`Redeemed ${data.redeemedCount}.`, 'success')
        // Refresh the order so the redeemed rows now show as redeemed.
        await loadOrder(orderToken.value)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Redeem failed.', 'error')
    } finally {
        redeeming.value = false
    }
}

function kindLabel(kind: string): string {
    switch (kind) {
        case 'pass': return 'Pass'
        case 'event_ticket': return 'Race Entry'
        case 'extras': return 'Add-on'
        case 'membership': return 'Membership'
        default: return kind
    }
}

function formatInTenant(utc: string): string {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('YYYY-MM-DD HH:mm')
}

function statusColor(status: string): string {
    switch (status) {
        case 'paid': return 'success'
        case 'pending': return 'warning'
        case 'failed': return 'error'
        case 'refunded': return 'grey'
        case 'redeemed': return 'primary'
        default: return 'default'
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onBeforeUnmount(() => { if (scanner) stopScan() })
</script>

<style scoped>
.reader-surface {
    width: 100%;
    max-width: 420px;
    min-height: 260px;
    border: 1px dashed rgba(0, 0, 0, 0.2);
    border-radius: 6px;
    margin: 0 auto;
    background: #f5f5f5;
}
.order-row + .order-row {
    border-top: 1px solid rgba(0, 0, 0, 0.06);
}
</style>
