<template>
    <v-container>
        <h1 class="text-h4 mb-6">My Passes</h1>

        <v-progress-circular v-if="loading" indeterminate color="primary"></v-progress-circular>

        <div v-else-if="purchases.length === 0" class="text-medium-emphasis">
            You haven't bought anything yet.
            <router-link to="/BuyPass">Buy a day pass</router-link>?
        </div>

        <v-row v-else>
            <v-col v-for="p in purchases" :key="p.kind + p.id" cols="12" md="6" lg="4">
                <v-card variant="outlined" class="pa-4">
                    <div class="d-flex align-center mb-2">
                        <v-chip size="small" :color="p.kind === 'day_pass' ? 'primary' : 'secondary'">
                            {{ p.kind === 'day_pass' ? 'Day Pass' : 'Ticket' }}
                        </v-chip>
                        <v-spacer></v-spacer>
                        <v-chip size="small" :color="statusColor(p.status)">{{ p.status }}</v-chip>
                    </div>
                    <div class="text-subtitle-1 font-weight-bold mb-1">{{ p.itemName }}</div>
                    <div class="text-caption text-medium-emphasis mb-3">
                        <div v-if="p.validOnDate">Valid on {{ p.validOnDate.substring(0,10) }}</div>
                        <div v-if="p.eventStartsAtUtc">Event: {{ formatInTenant(p.eventStartsAtUtc) }}</div>
                        <div>Purchased {{ formatInTenant(p.createdAtUtc) }}</div>
                        <div>${{ (p.amountCents / 100).toFixed(2) }}</div>
                    </div>
                    <v-btn v-if="!expanded[p.id]" variant="text" size="small" @click="expanded[p.id] = true">
                        Show QR
                    </v-btn>
                    <div v-else class="text-center">
                        <QrCode :value="redeemUrl(p.redemptionToken)" :size="220" />
                        <v-btn variant="text" size="small" class="mt-2" @click="expanded[p.id] = false">Hide</v-btn>
                    </div>
                </v-card>
            </v-col>
        </v-row>
    </v-container>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import dayjs from 'dayjs'
import { TicketService, type MyPurchase } from '@/services/TicketService'
import { branding } from '@/stores/branding'
import QrCode from '@/components/QrCode.vue'

const service = new TicketService()

const purchases = ref<MyPurchase[]>([])
const loading = ref(true)
const expanded = reactive<Record<string, boolean>>({})

onMounted(load)

async function load() {
    loading.value = true
    try {
        const r = await service.getMyPurchases()
        purchases.value = (r.data as any).data
    } finally {
        loading.value = false
    }
}

function redeemUrl(token: string): string {
    return `${window.location.protocol}//${window.location.host}/redeem/${token}`
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
</script>
