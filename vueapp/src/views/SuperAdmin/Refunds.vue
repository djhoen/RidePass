<template>
    <v-container>
        <h1 class="text-h4 mb-4">
            Refunds
            <v-badge v-if="refunds.length > 0" :content="refunds.length" color="error" inline class="ml-2"></v-badge>
        </h1>

        <div class="d-flex align-center mb-3">
            <v-spacer></v-spacer>
            <v-btn variant="text" @click="loadRefunds">Refresh</v-btn>
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
                        <th style="width: 180px">Cancelled</th>
                        <th>Reason</th>
                        <th style="width: 160px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="r in refunds" :key="r.kind + ':' + r.id">
                        <td>
                            <v-chip size="x-small" :color="r.kind === 'pass' ? 'primary' : 'secondary'">
                                {{ r.kind === 'pass' ? 'Pass' : 'Ticket' }}
                            </v-chip>
                        </td>
                        <td><code>{{ r.tenantSubdomain }}</code></td>
                        <td>{{ r.itemName }}</td>
                        <td>
                            <div>{{ r.purchaserName }}</div>
                            <div class="text-caption text-medium-emphasis">{{ r.purchaserEmail }}</div>
                        </td>
                        <td>${{ (r.amountCents / 100).toFixed(2) }}</td>
                        <td>{{ r.cancelledAtUtc ? formatDate(r.cancelledAtUtc) : '—' }}</td>
                        <td>
                            <span v-if="r.cancellationReason">{{ r.cancellationReason }}</span>
                            <span v-else class="text-medium-emphasis">—</span>
                        </td>
                        <td class="text-right">
                            <v-btn size="small" color="primary" variant="tonal"
                                :disabled="!r.stripePaymentIntentId" :loading="processingId === r.kind + ':' + r.id"
                                @click="processRefund(r)">
                                Process Refund
                            </v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loadingRefunds && refunds.length === 0">
                        <td colspan="8" class="text-center text-medium-emphasis py-8">No refund requests pending.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { SuperAdminService, type RefundListItem } from '@/services/SuperAdminService'
import { useConfirm } from '@/composables/useConfirm'

const service = new SuperAdminService()
const confirm = useConfirm()

const refunds = ref<RefundListItem[]>([])
const loadingRefunds = ref(false)
const processingId = ref<string | null>(null)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(loadRefunds)

async function loadRefunds() {
    loadingRefunds.value = true
    try {
        const r = await service.listRefunds()
        refunds.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load refunds.', 'error')
    } finally {
        loadingRefunds.value = false
    }
}

async function processRefund(r: RefundListItem) {
    if (!r.stripePaymentIntentId) {
        flash('No Stripe payment intent on record — cannot refund automatically.', 'error')
        return
    }
    const ok = await confirm({
        title: 'Process refund?',
        message: `Refund $${(r.amountCents / 100).toFixed(2)} to ${r.purchaserEmail} via Stripe.`,
        confirmText: 'Refund',
        confirmColor: 'primary',
    })
    if (!ok) return
    try {
        processingId.value = r.kind + ':' + r.id
        if (r.kind === 'pass') {
            await service.processPassRefund(r.id)
        } else {
            await service.processTicketRefund(r.id)
        }
        flash('Refund processed.', 'success')
        await loadRefunds()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Refund failed.', 'error')
    } finally {
        processingId.value = null
    }
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
