<template>
    <v-container>
        <Spinner v-model="loading" />

        <template v-if="!loading && order">
            <v-btn variant="text" prepend-icon="mdi-arrow-left" to="/User/OrderHistory" class="mb-4">
                Back to Orders
            </v-btn>

            <h1 class="text-h4 mb-6">Order #{{ order.id }}</h1>

            <v-row>
                <v-col cols="12" md="8">
                    <v-card class="mb-4">
                        <v-card-title>Order Items</v-card-title>
                        <v-table>
                            <thead>
                                <tr>
                                    <th>Item</th>
                                    <th class="text-center">Qty</th>
                                    <th class="text-right">Price</th>
                                    <th class="text-right">Total</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr v-for="item in order.items" :key="item.id">
                                    <td>{{ item.name }}</td>
                                    <td class="text-center">{{ item.quantity }}</td>
                                    <td class="text-right">{{ filters.currency(item.price) }}</td>
                                    <td class="text-right">{{ filters.currency(item.price * item.quantity) }}</td>
                                </tr>
                            </tbody>
                        </v-table>
                    </v-card>
                </v-col>

                <v-col cols="12" md="4">
                    <v-card class="mb-4">
                        <v-card-title>Order Info</v-card-title>
                        <v-card-text>
                            <div class="mb-2"><strong>Status:</strong>
                                <v-chip size="small" class="ml-2">{{ order.status }}</v-chip>
                            </div>
                            <div class="mb-2"><strong>Date:</strong> {{ filters.date(order.orderDate) }}</div>
                            <v-divider class="my-3"></v-divider>
                            <div class="d-flex justify-space-between mb-1">
                                <span>Subtotal</span>
                                <span>{{ filters.currency(order.subtotal) }}</span>
                            </div>
                            <div class="d-flex justify-space-between mb-1">
                                <span>Shipping</span>
                                <span>{{ filters.currency(order.shipping) }}</span>
                            </div>
                            <div v-if="order.discount" class="d-flex justify-space-between mb-1 text-success">
                                <span>Discount</span>
                                <span>-{{ filters.currency(order.discount) }}</span>
                            </div>
                            <v-divider class="my-3"></v-divider>
                            <div class="d-flex justify-space-between text-h6">
                                <span>Total</span>
                                <span>{{ filters.currency(order.total) }}</span>
                            </div>
                        </v-card-text>
                    </v-card>
                </v-col>
            </v-row>
        </template>

        <v-snackbar v-model="snackbar" color="error" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { OrderService } from '@/services/OrderService'
import filters from '@/helpers/Filters'
import Spinner from '@/components/Spinner.vue'

const route = useRoute()
const orderService = new OrderService()

const order = ref<any>(null)
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')

onMounted(async () => {
    try {
        loading.value = true
        const id = Number(route.params.id)
        const response = await orderService.getOrder(id)
        order.value = response.data
    } catch {
        snackbarText.value = 'Failed to load order.'
        snackbar.value = true
    } finally {
        loading.value = false
    }
})
</script>
