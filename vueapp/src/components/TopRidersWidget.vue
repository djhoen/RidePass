<template>
    <v-card height="100%">
        <v-card-title class="text-subtitle-1 d-flex align-center flex-wrap ga-2">
            Top Riders
            <v-spacer></v-spacer>
            <v-btn-toggle v-model="metric" density="compact" mandatory color="primary" variant="outlined">
                <v-btn size="small" value="days">Days</v-btn>
                <v-btn size="small" value="spent">Spent</v-btn>
            </v-btn-toggle>
            <v-btn-toggle v-model="period" density="compact" mandatory color="primary" variant="outlined">
                <v-btn size="small" value="month">Month</v-btn>
                <v-btn size="small" value="year">Year</v-btn>
            </v-btn-toggle>
        </v-card-title>

        <v-card-text class="pa-0">
            <div v-if="loading" class="text-center py-6">
                <v-progress-circular indeterminate size="24"></v-progress-circular>
            </div>
            <v-list v-else density="compact">
                <v-list-item v-for="(r, idx) in riders" :key="r.userId" link
                    @click="openCustomer(r.userId)">
                    <template #prepend>
                        <span class="text-caption text-medium-emphasis mr-3" style="width: 18px; text-align: right;">
                            {{ idx + 1 }}
                        </span>
                    </template>
                    <v-list-item-title>{{ r.firstName }} {{ r.lastName }}</v-list-item-title>
                    <v-list-item-subtitle class="text-caption">{{ r.email }}</v-list-item-subtitle>
                    <template #append>
                        <span class="font-weight-medium">
                            {{ metric === 'days' ? r.days : '$' + (r.spentCents / 100).toFixed(0) }}
                        </span>
                    </template>
                </v-list-item>
                <v-list-item v-if="!loading && riders.length === 0">
                    <v-list-item-subtitle class="text-medium-emphasis text-center">
                        No paid activity from waiver-signed riders {{ period === 'year' ? 'this year' : 'this month' }} yet.
                    </v-list-item-subtitle>
                </v-list-item>
            </v-list>
        </v-card-text>
    </v-card>
</template>

<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { CustomerService, type TopRiderDto } from '@/services/CustomerService'

const router = useRouter()
const service = new CustomerService()

const metric = ref<'days' | 'spent'>('days')
const period = ref<'month' | 'year'>('month')
const riders = ref<TopRiderDto[]>([])
const loading = ref(false)

onMounted(load)
// Reload when either toggle changes — small enough payload that this is fine,
// no debounce needed.
watch([metric, period], load)

async function load() {
    loading.value = true
    try {
        const r = await service.topRiders(metric.value, period.value, 10)
        riders.value = (r.data as any).data
    } finally {
        loading.value = false
    }
}

function openCustomer(userId: string) {
    router.push({ name: 'AdminCustomerDetail', params: { userId } })
}
</script>
