<template>
    <v-container style="max-width: 720px">
        <h1 class="text-h4 mb-4">My Season Passes</h1>

        <v-card v-if="loading" class="pa-6 text-center">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </v-card>
        <v-card v-else-if="passes.length === 0" class="pa-6 text-center text-medium-emphasis">
            You don't have any season passes yet.
            <div v-if="branding.seasonPassesEnabled" class="mt-3">
                <v-btn to="/SeasonPasses" color="primary">Browse passes</v-btn>
            </div>
            <div v-else class="text-caption mt-3">
                This track isn't selling season passes right now.
            </div>
        </v-card>

        <v-card v-for="p in passes" :key="p.id" class="mb-4 pa-4">
            <div class="d-flex align-center ga-4 flex-wrap">
                <QrCode :value="String(p.redemptionToken)" :size="160" />
                <div class="flex-grow-1">
                    <strong class="text-h6">{{ p.productName }}</strong>
                    <div class="text-caption mt-1">
                        Valid {{ formatDate(p.validFromDate) }} – {{ formatDate(p.validToDate) }}
                    </div>
                    <div class="text-caption">
                        <span v-if="p.productKind === 'unlimited'">Unlimited rides</span>
                        <span v-else-if="p.productKind === 'days_of_week'">{{ daysLabel(p.validDaysOfWeek) }} only</span>
                        <span v-else-if="p.productKind === 'credits'">{{ p.creditsRemaining ?? 0 }} credits remaining</span>
                    </div>
                    <v-chip size="small" :color="p.status === 'paid' ? 'success' : 'warning'" class="mt-2">
                        {{ p.status }}
                    </v-chip>
                </div>
            </div>
            <p class="text-caption text-medium-emphasis mt-3">
                Show this QR to the gate worker on your event day.
            </p>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { SeasonPassService, type MySeasonPass } from '@/services/SeasonPassService'
import QrCode from '@/components/QrCode.vue'
import { branding } from '@/stores/branding'

const service = new SeasonPassService()
const passes = ref<MySeasonPass[]>([])
const loading = ref(false)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

function formatDate(iso: string): string { return dayjs(iso).format('MMM D, YYYY') }
function daysLabel(days: number[] | null): string {
    if (!days || days.length === 0) return 'Selected days'
    const names = ['Sun','Mon','Tue','Wed','Thu','Fri','Sat']
    return days.slice().sort().map(d => names[d]).join('/')
}

onMounted(async () => {
    // A season-pass checkout using a redirect-based payment method (3DS, wallet) lands back here.
    // Surface the outcome so a failed payment isn't silently shown as "no new pass" and a succeeded
    // one explains the brief delay before the pass appears (the webhook finalizes it).
    const params = new URLSearchParams(window.location.search)
    const redirectStatus = params.get('redirect_status')
    if (params.get('payment_intent') && redirectStatus) {
        snackbarText.value = redirectStatus === 'succeeded'
            ? 'Payment received. Your pass will appear here shortly.'
            : 'Your payment was not completed. Please try again.'
        snackbarColor.value = redirectStatus === 'succeeded' ? 'success' : 'error'
        snackbar.value = true
        history.replaceState(null, '', window.location.pathname)
    }

    loading.value = true
    try {
        const r = await service.listMine()
        passes.value = (r.data as any).data
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Failed to load passes.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        loading.value = false
    }
})
</script>
