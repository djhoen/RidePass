<template>
    <v-container>
        <h1 class="text-h4 mb-4">Reconcile</h1>

        <div class="d-flex align-center mb-3 flex-wrap ga-3">
            <v-text-field v-model="reconcileFrom" type="date" label="From (UTC)" density="compact" hide-details
                style="max-width: 180px"></v-text-field>
            <v-text-field v-model="reconcileTo" type="date" label="To (UTC, exclusive)" density="compact" hide-details
                style="max-width: 180px"></v-text-field>
            <v-btn color="primary" :loading="loadingReconcile" @click="loadReconciliation">Run</v-btn>
            <v-spacer></v-spacer>
            <p class="text-caption text-medium-emphasis mb-0">
                Compares Stripe balance_transactions against our ledger for the chosen period.
            </p>
        </div>

        <v-alert v-if="reconciliation && !reconciliation.stripeConfigured" type="warning" variant="tonal" class="mb-4">
            Stripe credentials are not configured on the server, so the Stripe column is unavailable.
            Ledger totals are still shown.
        </v-alert>

        <v-row v-if="reconciliation" class="mb-4">
            <v-col cols="12" md="6">
                <v-card>
                    <v-card-title>Stripe (balance_transactions)</v-card-title>
                    <v-card-text>
                        <div v-if="!reconciliation.stripe" class="text-medium-emphasis">Not available.</div>
                        <v-table v-else density="compact">
                            <tbody>
                                <tr><td>Transactions</td><td class="text-right">{{ reconciliation.stripe.count }}</td></tr>
                                <tr><td>Gross</td><td class="text-right">${{ (reconciliation.stripe.grossCents / 100).toFixed(2) }}</td></tr>
                                <tr><td>Stripe fee</td><td class="text-right">${{ (reconciliation.stripe.feeCents / 100).toFixed(2) }}</td></tr>
                                <tr><td>Net to platform</td><td class="text-right"><strong>${{ (reconciliation.stripe.netCents / 100).toFixed(2) }}</strong></td></tr>
                            </tbody>
                        </v-table>
                    </v-card-text>
                </v-card>
            </v-col>
            <v-col cols="12" md="6">
                <v-card>
                    <v-card-title>RidePass ledger</v-card-title>
                    <v-card-text>
                        <v-table density="compact">
                            <tbody>
                                <tr><td>Entries</td><td class="text-right">{{ reconciliation.ledger.count }}</td></tr>
                                <tr><td>Gross</td><td class="text-right">${{ (reconciliation.ledger.grossCents / 100).toFixed(2) }}</td></tr>
                                <tr><td>Stripe fee (recorded)</td><td class="text-right">${{ (reconciliation.ledger.stripeFeeCents / 100).toFixed(2) }}</td></tr>
                                <tr><td>RidePass cut</td><td class="text-right">${{ (reconciliation.ledger.ridepassCutCents / 100).toFixed(2) }}</td></tr>
                                <tr><td>Net to tenants</td><td class="text-right"><strong>${{ (reconciliation.ledger.netToTenantCents / 100).toFixed(2) }}</strong></td></tr>
                            </tbody>
                        </v-table>
                    </v-card-text>
                </v-card>
            </v-col>
        </v-row>

        <v-card v-if="reconciliation && reconciliation.stripe">
            <v-card-title>Gap analysis</v-card-title>
            <v-card-text>
                <p class="text-caption text-medium-emphasis mb-3">
                    Non-zero gaps mean Stripe and our ledger disagree for the period. A small fee gap is normal
                    (we estimate Stripe's fee at sale time; the actual fee can drift by a few cents).
                    Large or persistent gaps deserve investigation.
                </p>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th>Metric</th>
                            <th class="text-right">Stripe</th>
                            <th class="text-right">Ledger</th>
                            <th class="text-right">Gap (Stripe − Ledger)</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr>
                            <td>Gross</td>
                            <td class="text-right">${{ (reconciliation.stripe.grossCents / 100).toFixed(2) }}</td>
                            <td class="text-right">${{ (reconciliation.ledger.grossCents / 100).toFixed(2) }}</td>
                            <td class="text-right" :class="gapClass(reconciliation.gaps.grossGap)">
                                ${{ (reconciliation.gaps.grossGap / 100).toFixed(2) }}
                            </td>
                        </tr>
                        <tr>
                            <td>Stripe fee</td>
                            <td class="text-right">${{ (reconciliation.stripe.feeCents / 100).toFixed(2) }}</td>
                            <td class="text-right">${{ (reconciliation.ledger.stripeFeeCents / 100).toFixed(2) }}</td>
                            <td class="text-right" :class="gapClass(reconciliation.gaps.feeGap)">
                                ${{ (reconciliation.gaps.feeGap / 100).toFixed(2) }}
                            </td>
                        </tr>
                        <tr>
                            <td>Net (Stripe net vs. our gross − recorded fee)</td>
                            <td class="text-right">${{ (reconciliation.stripe.netCents / 100).toFixed(2) }}</td>
                            <td class="text-right">${{ (reconciliation.gaps.expectedStripeNet / 100).toFixed(2) }}</td>
                            <td class="text-right" :class="gapClass(reconciliation.gaps.netGap)">
                                ${{ (reconciliation.gaps.netGap / 100).toFixed(2) }}
                            </td>
                        </tr>
                    </tbody>
                </v-table>
            </v-card-text>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import dayjs from 'dayjs'
import { SuperAdminService, type ReconciliationResult } from '@/services/SuperAdminService'

const service = new SuperAdminService()

const reconciliation = ref<ReconciliationResult | null>(null)
const loadingReconcile = ref(false)
const reconcileFrom = ref(dayjs.utc().startOf('month').subtract(1, 'month').format('YYYY-MM-DD'))
const reconcileTo = ref(dayjs.utc().startOf('month').format('YYYY-MM-DD'))

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

async function loadReconciliation() {
    loadingReconcile.value = true
    try {
        const fromUtc = dayjs.utc(reconcileFrom.value).toISOString()
        const toUtc = dayjs.utc(reconcileTo.value).toISOString()
        const r = await service.getReconciliation(fromUtc, toUtc)
        reconciliation.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load reconciliation.', 'error')
    } finally {
        loadingReconcile.value = false
    }
}

function gapClass(cents: number): string {
    if (cents === 0) return 'text-success'
    if (Math.abs(cents) < 100) return 'text-warning'
    return 'text-error'
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
