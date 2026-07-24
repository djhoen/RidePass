<template>
    <!-- A customer's bike-shop footprint: sales, rentals, work orders, credit balance. Embedded
         on the admin CustomerDetail page (by userId) and in the work-order editor's history
         dialog (by email/phone query for walk-ins). -->
    <div>
        <div v-if="loading" class="text-center py-4"><v-progress-circular indeterminate color="primary" size="24" /></div>
        <v-alert v-else-if="error" type="error" variant="tonal" density="compact">{{ error }}</v-alert>
        <template v-else-if="history">
            <div v-if="history.creditBalanceCents > 0" class="mb-3">
                <v-chip color="primary" variant="tonal" prepend-icon="mdi-wallet-giftcard">
                    {{ money(history.creditBalanceCents) }} store credit
                </v-chip>
            </div>

            <div class="text-subtitle-2 mb-1">Service history</div>
            <v-table v-if="history.workOrders.length" density="compact" class="mb-3">
                <tbody>
                    <tr v-for="w in history.workOrders" :key="w.id">
                        <td class="text-caption" style="width: 110px">{{ formatDate(w.createdAt) }}</td>
                        <td class="text-caption">{{ w.customerBikeDesc || '(shop unit)' }}</td>
                        <td style="width: 120px"><v-chip size="x-small">{{ w.status.replace('_', ' ') }}</v-chip></td>
                    </tr>
                </tbody>
            </v-table>
            <p v-else class="text-caption text-medium-emphasis mb-3">No work orders yet.</p>

            <div class="text-subtitle-2 mb-1">Shop purchases</div>
            <v-table v-if="history.sales.length" density="compact" class="mb-3">
                <tbody>
                    <tr v-for="s in history.sales" :key="s.id">
                        <td class="text-caption" style="width: 110px">{{ formatDate(s.createdAt) }}</td>
                        <td class="text-caption">{{ s.isRepair ? 'Repair bill-out' : 'Retail' }}{{ s.orderNumber != null ? ` #${s.orderNumber}` : '' }}</td>
                        <td class="text-right" style="width: 90px">{{ money(s.totalCents) }}</td>
                        <td style="width: 100px"><v-chip size="x-small" :color="s.status === 'paid' ? 'success' : s.status === 'refunded' ? 'grey' : 'warning'">{{ s.status }}</v-chip></td>
                    </tr>
                </tbody>
            </v-table>
            <p v-else class="text-caption text-medium-emphasis mb-3">No shop purchases yet.</p>

            <div class="text-subtitle-2 mb-1">Rentals</div>
            <v-table v-if="history.rentals.length" density="compact">
                <tbody>
                    <tr v-for="r in history.rentals" :key="r.id">
                        <td class="text-caption" style="width: 110px">{{ formatDate(r.startsAt) }}</td>
                        <td class="text-right" style="width: 90px">{{ money(r.totalCents) }}</td>
                        <td style="width: 100px"><v-chip size="x-small" :color="r.status === 'returned' ? 'success' : r.status === 'out' ? 'indigo' : 'grey'">{{ r.status }}</v-chip></td>
                    </tr>
                </tbody>
            </v-table>
            <p v-else class="text-caption text-medium-emphasis">No rentals yet.</p>
        </template>
    </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import { formatTenantDate } from '@/helpers/TenantTime'
import { BikeShopService, type ShopCustomerHistory } from '@/services/BikeShopService'

const props = defineProps<{
    userId?: string | null
    query?: string | null
}>()

const service = new BikeShopService()
const history = ref<ShopCustomerHistory | null>(null)
const loading = ref(false)
const error = ref('')

function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
function formatDate(iso: string): string { return formatTenantDate(iso, 'MMM D, YYYY') }

async function load() {
    if (!props.userId && !props.query?.trim()) { history.value = null; return }
    loading.value = true
    error.value = ''
    try {
        history.value = (await service.customerHistory({
            userId: props.userId ?? null, query: props.query?.trim() || null,
        })).data.data
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not load the shop history. Refresh to try again.'
    } finally { loading.value = false }
}

watch(() => [props.userId, props.query], load)
onMounted(load)
</script>
