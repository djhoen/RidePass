<template>
    <v-container>
        <h1 class="text-h4 mb-6">Order Management</h1>

        <v-card class="mb-4">
            <v-card-text>
                <v-form @submit.prevent="searchOrders">
                    <v-row align="center">
                        <v-col cols="12" sm="3">
                            <v-text-field v-model="search.orderId" label="Order ID" density="compact"
                                hide-details></v-text-field>
                        </v-col>
                        <v-col cols="12" sm="3">
                            <v-text-field v-model="search.email" label="Customer Email" density="compact"
                                hide-details></v-text-field>
                        </v-col>
                        <v-col cols="12" sm="2">
                            <v-select v-model="search.status" label="Status" :items="statuses" density="compact"
                                hide-details clearable></v-select>
                        </v-col>
                        <v-col cols="12" sm="2">
                            <v-text-field v-model="search.dateFrom" label="Date From" type="date" density="compact"
                                hide-details></v-text-field>
                        </v-col>
                        <v-col cols="12" sm="2">
                            <v-btn type="submit" color="primary" :loading="loading">Search</v-btn>
                        </v-col>
                    </v-row>
                </v-form>
            </v-card-text>
        </v-card>

        <v-card v-if="orders.length > 0">
            <v-table>
                <thead>
                    <tr>
                        <th>Order ID</th>
                        <th>Customer</th>
                        <th>Date</th>
                        <th class="text-right">Total</th>
                        <th>Status</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="order in orders" :key="order.id">
                        <td>#{{ order.id }}</td>
                        <td>{{ order.customerEmail }}</td>
                        <td>{{ filters.date(order.orderDate) }}</td>
                        <td class="text-right">{{ filters.currency(order.total) }}</td>
                        <td><v-chip size="small">{{ order.status }}</v-chip></td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" :to="`/Admin/Order/${order.id}`">View</v-btn>
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-snackbar v-model="snackbar" color="error" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { OrderService } from '@/services/OrderService'
import filters from '@/helpers/Filters'

const orderService = new OrderService()

const search = ref({ orderId: '', email: '', status: '', dateFrom: '' })
const orders = ref<any[]>([])
const statuses = ['Pending', 'Processing', 'Shipped', 'Delivered', 'Cancelled']
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')

async function searchOrders() {
    try {
        loading.value = true
        const response = await orderService.searchOrders(search.value)
        orders.value = response.data
    } catch {
        snackbarText.value = 'Failed to search orders.'
        snackbar.value = true
    } finally {
        loading.value = false
    }
}
</script>
