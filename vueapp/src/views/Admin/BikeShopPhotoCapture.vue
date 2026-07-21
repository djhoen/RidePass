<template>
    <v-container class="capture-root pa-3">
        <div v-if="loading" class="text-center py-10">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>

        <template v-else>
            <!-- Confirm WHAT you're photographing before shooting: a phone that scanned the
                 wrong tag should be obvious immediately. -->
            <div class="text-overline text-medium-emphasis">{{ isWorkOrder ? 'Work order' : 'Rental' }}</div>
            <h1 class="text-h6 font-weight-bold mb-1">{{ subjectTitle }}</h1>
            <div class="text-body-2 text-medium-emphasis mb-4">{{ subjectSubtitle }}</div>

            <v-card variant="outlined" class="pa-3 mb-4">
                <ConditionPhotos v-if="isWorkOrder" :work-order-id="id" stage="intake"
                    title="Intake photos"
                    hint="Photograph the bike as it arrived, especially any existing damage." />
                <template v-else>
                    <ConditionPhotos :rental-id="id" stage="intake"
                        title="Going out"
                        hint="Photograph the gear before it leaves." />
                    <v-divider class="my-4"></v-divider>
                    <ConditionPhotos :rental-id="id" stage="return"
                        title="Coming back"
                        hint="Photograph anything damaged on return." />
                </template>
            </v-card>

            <p class="text-caption text-medium-emphasis text-center">
                Photos save as you add them. You can close this when you're done.
            </p>
        </template>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import { BikeShopService, type ShopWorkOrder, type ShopRental } from '@/services/BikeShopService'
import ConditionPhotos from '@/components/bikeshop/ConditionPhotos.vue'

// Phone-first page reached by scanning the QR on the counter screen. It is an ordinary
// authenticated admin route: the scan carries a deep link, the router bounces to Login with
// ?next= if this phone hasn't signed in yet, and after that the session persists so later
// scans open straight here. No separate upload token, so no unauthenticated write path.
const route = useRoute()
const service = new BikeShopService()

const id = computed(() => String(route.params.id))
const isWorkOrder = computed(() => String(route.params.kind) === 'work-order')

const loading = ref(true)
const loadError = ref('')
const workOrder = ref<ShopWorkOrder | null>(null)
const rental = ref<ShopRental | null>(null)

const subjectTitle = computed(() => {
    if (isWorkOrder.value) return workOrder.value?.customerBikeDesc || 'Bike'
    const lines = (rental.value as any)?.lines ?? []
    return lines.map((l: any) => l.nameSnapshot).join(', ') || 'Rental'
})
const subjectSubtitle = computed(() => {
    if (isWorkOrder.value) {
        const w = workOrder.value
        if (!w) return ''
        return [w.customerName, w.status].filter(Boolean).join(' · ')
    }
    const r = rental.value
    if (!r) return ''
    const when = `${dayjs(r.startsAt).format('MMM D')} to ${dayjs(r.endsAt).format('MMM D')}`
    return [r.renterName || 'Walk-in', when, r.status].filter(Boolean).join(' · ')
})

onMounted(async () => {
    try {
        if (isWorkOrder.value) {
            workOrder.value = (await service.getWorkOrder(id.value)).data.data
        } else {
            rental.value = (await service.getRental(id.value)).data.data
        }
    } catch (e: any) {
        loadError.value = e.response?.data?.error
            || 'Could not open that record. Check the link or scan the code again.'
    } finally {
        loading.value = false
    }
})
</script>

<style scoped>
.capture-root { max-width: 720px; }
</style>
