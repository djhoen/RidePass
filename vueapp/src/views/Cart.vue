<template>
    <v-container>
        <h1 class="text-h4 mb-6">Shopping Cart</h1>

        <v-row>
            <v-col cols="12" md="8">
                <v-alert v-if="items.length === 0" type="info" variant="tonal">
                    Your cart is empty.
                </v-alert>

                <v-card v-for="(item, index) in items" :key="index" class="mb-3">
                    <v-card-text>
                        <v-row align="center">
                            <v-col cols="12" sm="5">
                                <span class="text-subtitle-1 font-weight-medium">{{ item.name }}</span>
                            </v-col>
                            <v-col cols="4" sm="2">
                                <v-text-field v-model.number="item.quantity" type="number" min="1" density="compact"
                                    hide-details label="Qty" @update:model-value="recalculate"></v-text-field>
                            </v-col>
                            <v-col cols="4" sm="3" class="text-right">
                                {{ filters.currency(item.price * item.quantity) }}
                            </v-col>
                            <v-col cols="4" sm="2" class="text-right">
                                <v-btn icon="mdi-delete" variant="text" color="error" size="small"
                                    @click="removeItem(index)"></v-btn>
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
                        <v-text-field v-model="couponCode" label="Coupon Code" density="compact" class="mb-2"
                            append-inner-icon="mdi-check" @click:append-inner="applyCoupon"></v-text-field>
                        <div v-if="discount > 0" class="d-flex justify-space-between mb-2 text-success">
                            <span>Discount</span>
                            <span>-{{ filters.currency(discount) }}</span>
                        </div>
                        <v-divider class="my-3"></v-divider>
                        <div class="d-flex justify-space-between text-h6">
                            <span>Total</span>
                            <span>{{ filters.currency(total) }}</span>
                        </div>
                    </v-card-text>
                    <v-card-actions>
                        <v-btn color="primary" block size="large" :disabled="items.length === 0"
                            to="/Checkout">Checkout</v-btn>
                    </v-card-actions>
                </v-card>
            </v-col>
        </v-row>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import filters from '@/helpers/Filters'

const items = ref<any[]>([
    { name: 'Sample Product 1', price: 29.99, quantity: 1 },
    { name: 'Sample Product 2', price: 49.99, quantity: 2 }
])

const couponCode = ref('')
const discount = ref(0)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('success')

const subtotal = computed(() => items.value.reduce((sum, item) => sum + item.price * item.quantity, 0))
const total = computed(() => Math.max(0, subtotal.value - discount.value))

function removeItem(index: number) {
    items.value.splice(index, 1)
}

function recalculate() {
    // triggers reactivity via computed
}

function applyCoupon() {
    snackbarText.value = 'Coupon applied!'
    snackbarColor.value = 'success'
    snackbar.value = true
}
</script>
