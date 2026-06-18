<template>
    <v-container style="max-width: 640px">
        <h1 class="text-h4 mb-4">Redeem</h1>

        <v-progress-circular v-if="loading" indeterminate color="primary"></v-progress-circular>

        <v-card v-else-if="preview" class="pa-4">
            <v-card-title class="d-flex align-center flex-wrap ga-2">
                <span>{{ preview.kind === 'pass' ? 'Pass' : 'Event Ticket' }}</span>
                <v-chip size="small" :color="statusColor(preview.status)">{{ preview.status }}</v-chip>
                <v-chip v-if="preview.status === 'paid' && !preview.isRedeemableToday" size="small" color="warning">
                    Not redeemable today
                </v-chip>
            </v-card-title>
            <v-card-text>
                <template v-if="preview.kind === 'event_ticket'">
                    <v-alert v-if="!preview.registrationComplete" type="warning" variant="tonal" density="compact" class="mb-3">
                        <strong>Registration not finished.</strong> Rider details / required waiver haven't been
                        completed for this entry , collect the signed waiver before allowing them on track.
                    </v-alert>
                    <div class="text-h6 mb-1">{{ preview.eventTitle }}</div>
                    <div class="text-subtitle-2 text-medium-emphasis mb-2">
                        Tier: {{ preview.tierName }}<span v-if="preview.raceNumber"> · #{{ preview.raceNumber }}</span>
                    </div>
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

                <v-btn v-if="preview.status === 'paid' && preview.isRedeemableToday" color="success" :loading="redeeming" class="mt-4" @click="redeem">
                    Redeem Now
                </v-btn>
                <v-alert v-else-if="preview.status === 'paid' && !preview.isRedeemableToday" type="warning" density="compact" class="mt-4">
                    {{ preview.notRedeemableReason }}
                </v-alert>
                <v-alert v-else-if="preview.status === 'redeemed'" type="info" density="compact" class="mt-4">
                    Already redeemed.
                </v-alert>
                <v-alert v-else type="warning" density="compact" class="mt-4">
                    Cannot redeem — status is "{{ preview.status }}".
                </v-alert>
            </v-card-text>
        </v-card>

        <v-alert v-else-if="error" type="error">{{ error }}</v-alert>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import { TicketService, type RedemptionPreview } from '@/services/TicketService'
import { branding } from '@/stores/branding'

const route = useRoute()
const service = new TicketService()

const loading = ref(true)
const redeeming = ref(false)
const preview = ref<RedemptionPreview | null>(null)
const error = ref<string | null>(null)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(async () => {
    const token = route.params.token as string
    try {
        const r = await service.preview(token)
        preview.value = (r.data as any).data
    } catch (err: any) {
        error.value = err.response?.data?.error || 'Not found.'
    } finally {
        loading.value = false
    }
})

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
</script>
