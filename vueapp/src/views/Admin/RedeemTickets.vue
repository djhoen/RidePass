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

        <v-card v-if="preview" class="mb-4 pa-4">
            <v-card-title class="d-flex align-center flex-wrap ga-2">
                <span>{{ preview.kind === 'day_pass' ? 'Day Pass' : 'Event Ticket' }}</span>
                <v-chip size="small" :color="statusColor(preview.status)">{{ preview.status }}</v-chip>
                <v-chip v-if="preview.status === 'paid' && !preview.isRedeemableToday" size="small" color="warning">
                    Not redeemable today
                </v-chip>
            </v-card-title>
            <v-card-text>
                <!-- Event-ticket details (richer) -->
                <template v-if="preview.kind === 'event_ticket'">
                    <div class="text-h6 mb-1">{{ preview.eventTitle }}</div>
                    <div class="text-subtitle-2 text-medium-emphasis mb-2">Tier: {{ preview.tierName }}</div>
                    <div class="mb-1">
                        <v-icon size="small" class="mr-1">mdi-clock-outline</v-icon>
                        <template v-if="preview.eventAllDay">
                            {{ formatDate(preview.eventStartsAtUtc!) }}<template v-if="spansMultipleDays(preview)"> – {{ formatDate(preview.eventEndsAtUtc!) }}</template>
                            (all day)
                        </template>
                        <template v-else>
                            {{ formatInTenant(preview.eventStartsAtUtc!) }} – {{ formatInTenant(preview.eventEndsAtUtc!) }}
                        </template>
                    </div>
                    <div v-if="preview.eventLocationLabel" class="mb-1">
                        <v-icon size="small" class="mr-1">mdi-map-marker-outline</v-icon>
                        {{ preview.eventLocationLabel }}
                    </div>
                    <div v-if="preview.eventDescription" class="text-body-2 text-medium-emphasis mb-3" style="white-space: pre-wrap">
                        {{ preview.eventDescription }}
                    </div>
                </template>

                <!-- Day-pass details -->
                <template v-else>
                    <div class="text-h6 mb-1">{{ preview.itemName }}</div>
                    <div v-if="preview.validOnDate" class="text-body-2 mb-1">
                        <v-icon size="small" class="mr-1">mdi-calendar-check</v-icon>
                        Valid on {{ preview.validOnDate.substring(0, 10) }}
                    </div>
                </template>

                <v-divider class="my-3"></v-divider>

                <div class="text-body-2 mb-1"><strong>{{ preview.purchaserName }}</strong></div>
                <div class="text-body-2 text-medium-emphasis mb-1">{{ preview.purchaserEmail }}</div>
                <div class="text-body-2 mb-1">${{ (preview.amountCents / 100).toFixed(2) }}</div>
                <div class="text-caption text-medium-emphasis mb-4">
                    Purchased {{ formatInTenant(preview.createdAtUtc) }} ({{ branding.timezone }})
                </div>

                <v-btn v-if="preview.status === 'paid' && preview.isRedeemableToday" color="success" :loading="redeeming" @click="redeem">
                    Redeem Now
                </v-btn>
                <v-alert v-else-if="preview.status === 'paid' && !preview.isRedeemableToday" type="warning" density="compact">
                    {{ preview.notRedeemableReason }}
                </v-alert>
                <v-alert v-else-if="preview.status === 'redeemed'" type="info" density="compact">
                    Already redeemed.
                </v-alert>
                <v-alert v-else type="warning" density="compact">
                    Cannot redeem — status is "{{ preview.status }}".
                </v-alert>
            </v-card-text>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onBeforeUnmount } from 'vue'
import dayjs from 'dayjs'
import { Html5Qrcode } from 'html5-qrcode'
import { TicketService, type RedemptionPreview } from '@/services/TicketService'
import { branding } from '@/stores/branding'

const service = new TicketService()

const manualInput = ref('')
const preview = ref<RedemptionPreview | null>(null)
const loading = ref(false)
const redeeming = ref(false)
const scanning = ref(false)

let scanner: Html5Qrcode | null = null

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

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
    await doPreview(token)
}

function extractToken(raw: string): string | null {
    const direct = raw.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i)
    return direct ? direct[0] : null
}

async function lookupManual() {
    const token = extractToken(manualInput.value)
    if (!token) { flash('No token found in input.', 'error'); return }
    await doPreview(token)
}

async function doPreview(token: string) {
    try {
        loading.value = true
        const r = await service.preview(token)
        preview.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Not found.', 'error')
        preview.value = null
    } finally {
        loading.value = false
    }
}

async function redeem() {
    if (!preview.value) return
    try {
        redeeming.value = true
        const r = await service.redeem(preview.value.redemptionToken)
        preview.value = (r.data as any).data
        flash('Redeemed!', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Redeem failed.', 'error')
    } finally {
        redeeming.value = false
    }
}

function formatInTenant(utc: string): string {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('YYYY-MM-DD HH:mm')
}

function formatDate(utc: string): string {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('YYYY-MM-DD')
}

function spansMultipleDays(p: RedemptionPreview): boolean {
    if (!p.eventStartsAtUtc || !p.eventEndsAtUtc) return false
    const tz = branding.timezone || 'UTC'
    return dayjs.utc(p.eventStartsAtUtc).tz(tz).format('YYYY-MM-DD')
        !== dayjs.utc(p.eventEndsAtUtc).tz(tz).format('YYYY-MM-DD')
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
</style>
