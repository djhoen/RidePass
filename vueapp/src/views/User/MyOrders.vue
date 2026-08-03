<template>
    <v-container>
        <h1 class="text-h4 mb-1">My Orders</h1>
        <p class="text-body-2 text-medium-emphasis mb-6">
            Shop purchases from {{ branding.displayName }}. Show your order number at the shop counter to collect.
        </p>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <v-card v-if="loading" class="pa-8 text-center">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </v-card>

        <template v-else-if="!loadError">
            <v-card v-if="orders.length === 0" class="pa-8 text-center text-medium-emphasis">
                No shop orders yet.
                <div class="mt-3"><v-btn color="primary" variant="flat" size="large" to="/Shop">Browse the shop</v-btn></div>
            </v-card>

            <template v-else>
                <!-- Waiting at the counter: the thing the rider actually needs on screen. -->
                <template v-if="awaiting.length">
                    <h2 class="text-h6 mb-3">Ready for pickup</h2>
                    <v-row class="mb-6">
                        <v-col v-for="o in awaiting" :key="o.saleId" cols="12" md="6" lg="4">
                            <v-card variant="outlined" class="h-100">
                                <v-card-text>
                                    <v-chip size="small" color="warning" class="mb-3">Ready for pickup</v-chip>
                                    <div class="text-caption text-medium-emphasis">Order number</div>
                                    <div class="text-h4 font-weight-bold">#{{ o.orderNumber ?? '—' }}</div>
                                    <p class="text-body-2 text-medium-emphasis mt-2 mb-3">
                                        Show this number at the shop counter to pick up your order.
                                    </p>
                                    <OrderLines :order="o" />
                                </v-card-text>
                            </v-card>
                        </v-col>
                    </v-row>
                </template>

                <h2 v-if="awaiting.length" class="text-h6 mb-3">Order history</h2>
                <v-row>
                    <v-col v-for="o in history" :key="o.saleId" cols="12" md="6" lg="4">
                        <v-card variant="outlined" class="h-100">
                            <v-card-text>
                                <div class="d-flex align-center ga-2 mb-2">
                                    <v-chip size="x-small" :color="statusColor(o)" variant="tonal">{{ statusLabel(o) }}</v-chip>
                                    <v-spacer></v-spacer>
                                    <span class="text-caption text-medium-emphasis">{{ formatTenantDate(o.createdAtUtc) }}</span>
                                </div>
                                <div class="font-weight-medium">Order #{{ o.orderNumber ?? '—' }}</div>
                                <OrderLines :order="o" class="mt-2" />
                            </v-card-text>
                        </v-card>
                    </v-col>
                </v-row>
            </template>
        </template>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, h, type PropType } from 'vue'
import { BikeShopService, type MyShopOrder } from '@/services/BikeShopService'
import { branding } from '@/stores/branding'
import { formatTenantDate, formatTenantDateTime } from '@/helpers/TenantTime'

const service = new BikeShopService()
const orders = ref<MyShopOrder[]>([])
const loading = ref(true)
const loadError = ref('')

// Paid, bought online, not yet collected: these are the ones the rider has to act on.
const awaiting = computed(() => orders.value.filter(
    o => o.status === 'paid' && o.orderChannel === 'online' && !o.pickedUpAtUtc))
const history = computed(() => orders.value.filter(o => !awaiting.value.includes(o)))

function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }

function statusLabel(o: MyShopOrder): string {
    if (o.status === 'refunded') return 'Refunded'
    if (o.pickedUpAtUtc) return `Picked up ${formatTenantDate(o.pickedUpAtUtc)}`
    if (o.orderChannel === 'counter') return 'Bought in store'
    return 'Paid'
}
function statusColor(o: MyShopOrder): string {
    return o.status === 'refunded' ? 'grey' : o.pickedUpAtUtc ? 'success' : 'primary'
}

// Small inline renderer so both sections show identical line detail without a second file.
const OrderLines = (props: { order: MyShopOrder }) => h('div', { class: 'text-body-2' }, [
    ...props.order.lines.map(l => h('div', { class: 'd-flex justify-space-between' }, [
        h('span', `${l.quantity}x ${l.name}${l.variantLabel ? ` (${l.variantLabel})` : ''}`),
        h('span', money(l.unitPriceCents * l.quantity)),
    ])),
    h('div', { class: 'd-flex justify-space-between font-weight-medium mt-1' }, [
        h('span', 'Total'), h('span', money(props.order.totalCents)),
    ]),
])
OrderLines.props = { order: { type: Object as PropType<MyShopOrder>, required: true } }

onMounted(async () => {
    try {
        const r = await service.myShopOrders()
        orders.value = (r.data as any).data ?? []
    } catch (err: any) {
        // Never fall through to the empty state: "no orders" and "we could not load your
        // orders" mean very different things to someone checking whether they paid.
        loadError.value = err.response?.data?.error
            || 'Could not load your orders. Check your connection and refresh to try again.'
    } finally {
        loading.value = false
    }
})
</script>
