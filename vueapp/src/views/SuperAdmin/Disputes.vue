<template>
    <v-container>
        <h1 class="text-h4 mb-4">
            Disputes
            <v-badge v-if="openDisputeCount > 0" :content="openDisputeCount" color="error" inline class="ml-2"></v-badge>
        </h1>

        <div class="d-flex align-center mb-3">
            <p class="text-caption text-medium-emphasis mb-0 mr-4">
                Chargebacks and inquiries captured from Stripe webhooks. Submit evidence in the Stripe Dashboard.
            </p>
            <v-spacer></v-spacer>
            <v-btn variant="text" @click="loadDisputes">Refresh</v-btn>
        </div>
        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 100px">Kind</th>
                        <th style="width: 160px">Tenant</th>
                        <th>Item</th>
                        <th>Purchaser</th>
                        <th style="width: 110px">Amount</th>
                        <th style="width: 140px">Reason</th>
                        <th style="width: 180px">Status</th>
                        <th style="width: 180px">Evidence Due</th>
                        <th style="width: 120px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="d in disputes" :key="d.id">
                        <td>
                            <v-chip v-if="d.kind !== 'unlinked'" size="x-small"
                                :color="d.kind === 'pass' ? 'primary' : 'secondary'">
                                {{ d.kind === 'pass' ? 'Pass' : 'Ticket' }}
                            </v-chip>
                            <v-chip v-else size="x-small" color="grey">Unlinked</v-chip>
                        </td>
                        <td><code>{{ d.tenantSubdomain }}</code></td>
                        <td>{{ d.itemName || '—' }}</td>
                        <td>
                            <div>{{ d.purchaserName || '—' }}</div>
                            <div class="text-caption text-medium-emphasis">{{ d.purchaserEmail }}</div>
                        </td>
                        <td>${{ (d.amountCents / 100).toFixed(2) }} {{ d.currency.toUpperCase() }}</td>
                        <td>{{ d.reason || '—' }}</td>
                        <td>
                            <v-chip size="small" :color="disputeStatusColor(d.status)">{{ d.status }}</v-chip>
                        </td>
                        <td>
                            <span v-if="d.evidenceDueByUtc" :class="evidenceDueClass(d.evidenceDueByUtc)">
                                {{ formatDate(d.evidenceDueByUtc) }}
                            </span>
                            <span v-else class="text-medium-emphasis">—</span>
                        </td>
                        <td class="text-right">
                            <v-btn size="small" variant="tonal" :href="stripeDisputeUrl(d.stripeDisputeId)"
                                target="_blank" rel="noopener">
                                Open in Stripe
                            </v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loadingDisputes && disputes.length === 0">
                        <td colspan="9" class="text-center text-medium-emphasis py-8">No disputes on record.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { SuperAdminService, type DisputeListItem } from '@/services/SuperAdminService'

const service = new SuperAdminService()

const disputes = ref<DisputeListItem[]>([])
const loadingDisputes = ref(false)
const openDisputeCount = computed(() => disputes.value.filter(d => isOpenDispute(d.status)).length)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(loadDisputes)

async function loadDisputes() {
    loadingDisputes.value = true
    try {
        const r = await service.listDisputes()
        disputes.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load disputes.', 'error')
    } finally {
        loadingDisputes.value = false
    }
}

function isOpenDispute(status: string): boolean {
    return status === 'needs_response' || status === 'warning_needs_response'
}

function disputeStatusColor(status: string): string {
    switch (status) {
        case 'needs_response':
        case 'warning_needs_response':
            return 'error'
        case 'under_review':
        case 'warning_under_review':
            return 'warning'
        case 'won':
            return 'success'
        case 'lost':
            return 'grey'
        case 'charge_refunded':
        case 'warning_closed':
            return 'default'
        default:
            return 'default'
    }
}

function evidenceDueClass(dueUtc: string): string {
    const hoursRemaining = dayjs.utc(dueUtc).diff(dayjs.utc(), 'hour')
    if (hoursRemaining <= 0) return 'text-error'
    if (hoursRemaining <= 48) return 'text-warning'
    return ''
}

function stripeDisputeUrl(stripeDisputeId: string): string {
    return `https://dashboard.stripe.com/disputes/${stripeDisputeId}`
}

function formatDate(utc: string): string {
    return dayjs.utc(utc).format('YYYY-MM-DD HH:mm')
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
