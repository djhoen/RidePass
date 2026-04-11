<template>
    <v-container>
        <h1 class="text-h4 mb-6">Coupon Management</h1>

        <Spinner v-model="loading" />

        <v-card class="mb-4">
            <v-card-title>Create Coupon</v-card-title>
            <v-card-text>
                <v-form @submit.prevent="createCoupon">
                    <v-row>
                        <v-col cols="12" sm="4">
                            <v-text-field v-model="form.code" label="Code" required></v-text-field>
                        </v-col>
                        <v-col cols="12" sm="4">
                            <v-select v-model="form.type" label="Type"
                                :items="['Percentage', 'FixedAmount']" required></v-select>
                        </v-col>
                        <v-col cols="12" sm="4">
                            <v-text-field v-model.number="form.amount" label="Amount" type="number"
                                required></v-text-field>
                        </v-col>
                        <v-col cols="12" sm="3">
                            <v-text-field v-model="form.startDate" label="Start Date" type="date"></v-text-field>
                        </v-col>
                        <v-col cols="12" sm="3">
                            <v-text-field v-model="form.endDate" label="End Date" type="date"></v-text-field>
                        </v-col>
                        <v-col cols="12" sm="3">
                            <v-text-field v-model.number="form.maxUses" label="Max Uses" type="number"></v-text-field>
                        </v-col>
                        <v-col cols="12" sm="3">
                            <v-text-field v-model.number="form.maxUsesPerUser" label="Max Per User"
                                type="number"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-btn type="submit" color="primary" :loading="saving">Create</v-btn>
                </v-form>
            </v-card-text>
        </v-card>

        <v-card v-if="!loading">
            <v-card-title>Coupons</v-card-title>
            <v-table>
                <thead>
                    <tr>
                        <th>Code</th>
                        <th>Type</th>
                        <th>Amount</th>
                        <th>Start</th>
                        <th>End</th>
                        <th>Max Uses</th>
                        <th>Used</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="coupon in coupons" :key="coupon.id">
                        <td>{{ coupon.code }}</td>
                        <td>{{ coupon.type }}</td>
                        <td>{{ coupon.type === 'Percentage' ? coupon.amount + '%' : filters.currency(coupon.amount) }}</td>
                        <td>{{ filters.date(coupon.startDate) }}</td>
                        <td>{{ filters.date(coupon.endDate) }}</td>
                        <td>{{ coupon.maxUses || 'Unlimited' }}</td>
                        <td>{{ coupon.usedCount }}</td>
                    </tr>
                </tbody>
            </v-table>
            <v-card-text v-if="coupons.length === 0">No coupons found.</v-card-text>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { CouponService } from '@/services/CouponService'
import filters from '@/helpers/Filters'
import Spinner from '@/components/Spinner.vue'

const couponService = new CouponService()

const coupons = ref<any[]>([])
const form = ref({
    code: '', type: 'Percentage', amount: 0, startDate: '', endDate: '', maxUses: null, maxUsesPerUser: null
})
const loading = ref(false)
const saving = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('success')

onMounted(async () => {
    await loadCoupons()
})

async function loadCoupons() {
    try {
        loading.value = true
        const response = await couponService.getCoupons()
        coupons.value = response.data
    } catch {
        snackbarText.value = 'Failed to load coupons.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        loading.value = false
    }
}

async function createCoupon() {
    try {
        saving.value = true
        await couponService.createCoupon(form.value)
        form.value = { code: '', type: 'Percentage', amount: 0, startDate: '', endDate: '', maxUses: null, maxUsesPerUser: null }
        await loadCoupons()
        snackbarText.value = 'Coupon created!'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch {
        snackbarText.value = 'Failed to create coupon.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        saving.value = false
    }
}
</script>
