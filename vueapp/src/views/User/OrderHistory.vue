<template>
    <v-container>
        <h1 class="text-h4 mb-6">Order History</h1>

        <Spinner v-model="loading" />

        <v-card v-if="!loading">
            <v-table>
                <thead>
                    <tr>
                        <th>Order ID</th>
                        <th>Date</th>
                        <th class="text-right">Total</th>
                        <th>Status</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="order in orders" :key="order.id">
                        <td>#{{ order.id }}</td>
                        <td>{{ filters.date(order.orderDate) }}</td>
                        <td class="text-right">{{ filters.currency(order.total) }}</td>
                        <td><v-chip size="small">{{ order.status }}</v-chip></td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" :to="`/OrderDetail/${order.id}`">View</v-btn>
                        </td>
                    </tr>
                </tbody>
            </v-table>

            <v-card-text v-if="orders.length === 0">
                No orders found.
            </v-card-text>
        </v-card>

        <v-snackbar v-model="snackbar" color="error" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { OrderService } from '@/services/OrderService'
import filters from '@/helpers/Filters'
import Spinner from '@/components/Spinner.vue'

const orderService = new OrderService()

const orders = ref<any[]>([])
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')

onMounted(async () => {
    try {
        loading.value = true
        const response = await orderService.getUserOrders()
        orders.value = response.data
    } catch {
        snackbarText.value = 'Failed to load orders.'
        snackbar.value = true
    } finally {
        loading.value = false
    }
})
</script>
