<template>
    <v-container>
        <h1 class="text-h4 mb-6">Checkout</h1>

        <v-row>
            <v-col cols="12" md="8">
                <v-card class="mb-4">
                    <v-card-title>Billing Address</v-card-title>
                    <v-card-text>
                        <v-row>
                            <v-col cols="12" sm="6">
                                <v-text-field v-model="billing.firstName" label="First Name" required></v-text-field>
                            </v-col>
                            <v-col cols="12" sm="6">
                                <v-text-field v-model="billing.lastName" label="Last Name" required></v-text-field>
                            </v-col>
                            <v-col cols="12">
                                <v-text-field v-model="billing.address1" label="Address Line 1"
                                    required></v-text-field>
                            </v-col>
                            <v-col cols="12">
                                <v-text-field v-model="billing.address2" label="Address Line 2"></v-text-field>
                            </v-col>
                            <v-col cols="12" sm="5">
                                <v-text-field v-model="billing.city" label="City" required></v-text-field>
                            </v-col>
                            <v-col cols="12" sm="3">
                                <v-text-field v-model="billing.state" label="State" required></v-text-field>
                            </v-col>
                            <v-col cols="12" sm="4">
                                <v-text-field v-model="billing.zip" label="Zip Code" required></v-text-field>
                            </v-col>
                        </v-row>
                    </v-card-text>
                </v-card>

                <v-card class="mb-4">
                    <v-card-title>
                        Shipping Address
                        <v-checkbox v-model="sameAsBilling" label="Same as billing" density="compact"
                            hide-details class="ml-4 d-inline-flex"></v-checkbox>
                    </v-card-title>
                    <v-card-text v-if="!sameAsBilling">
                        <v-row>
                            <v-col cols="12" sm="6">
                                <v-text-field v-model="shipping.firstName" label="First Name"
                                    required></v-text-field>
                            </v-col>
                            <v-col cols="12" sm="6">
                                <v-text-field v-model="shipping.lastName" label="Last Name" required></v-text-field>
                            </v-col>
                            <v-col cols="12">
                                <v-text-field v-model="shipping.address1" label="Address Line 1"
                                    required></v-text-field>
                            </v-col>
                            <v-col cols="12">
                                <v-text-field v-model="shipping.address2" label="Address Line 2"></v-text-field>
                            </v-col>
                            <v-col cols="12" sm="5">
                                <v-text-field v-model="shipping.city" label="City" required></v-text-field>
                            </v-col>
                            <v-col cols="12" sm="3">
                                <v-text-field v-model="shipping.state" label="State" required></v-text-field>
                            </v-col>
                            <v-col cols="12" sm="4">
                                <v-text-field v-model="shipping.zip" label="Zip Code" required></v-text-field>
                            </v-col>
                        </v-row>
                    </v-card-text>
                </v-card>
            </v-col>

            <v-col cols="12" md="4">
                <v-card>
                    <v-card-title>Order Summary</v-card-title>
                    <v-card-text>
                        <div class="d-flex justify-space-between mb-2">
                            <span>Subtotal</span>
                            <span>{{ filters.currency(subtotal) }}</span>
                        </div>
                        <div class="d-flex justify-space-between mb-2">
                            <span>Shipping</span>
                            <span>{{ filters.currency(shippingCost) }}</span>
                        </div>
                        <v-divider class="my-3"></v-divider>
                        <div class="d-flex justify-space-between text-h6">
                            <span>Total</span>
                            <span>{{ filters.currency(subtotal + shippingCost) }}</span>
                        </div>
                    </v-card-text>
                    <v-card-actions>
                        <v-btn color="primary" block size="large" :loading="loading"
                            @click="placeOrder">Place Order</v-btn>
                    </v-card-actions>
                </v-card>
            </v-col>
        </v-row>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import filters from '@/helpers/Filters'

const router = useRouter()

const loading = ref(false)
const sameAsBilling = ref(true)
const subtotal = ref(129.97)
const shippingCost = ref(9.99)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('success')

const emptyAddress = () => ({
    firstName: '', lastName: '', address1: '', address2: '', city: '', state: '', zip: ''
})

const billing = ref(emptyAddress())
const shipping = ref(emptyAddress())

async function placeOrder() {
    try {
        loading.value = true
        // Stub: would call OrderService to create order
        snackbarText.value = 'Order placed successfully!'
        snackbarColor.value = 'success'
        snackbar.value = true
        setTimeout(() => router.push('/User/OrderHistory'), 1500)
    } catch (error: any) {
        snackbarText.value = error.response?.data?.message || 'Failed to place order.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        loading.value = false
    }
}
</script>
