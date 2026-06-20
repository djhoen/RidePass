<template>
    <v-container>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h1 class="text-h4">Customers</h1>
            <v-spacer></v-spacer>
            <v-text-field v-model="search" label="Search by name or email" density="compact" hide-details
                clearable style="max-width: 320px" prepend-inner-icon="mdi-magnify"
                @update:model-value="onSearchChanged"></v-text-field>
            <v-btn variant="text" @click="load">Refresh</v-btn>
        </div>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Email</th>
                        <th style="width: 110px" class="text-right">Purchases</th>
                        <th style="width: 130px" class="text-right">Total Spent</th>
                        <th style="width: 110px">Waiver</th>
                        <th style="width: 180px">Last Activity</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="c in customers" :key="c.userId" class="cursor-pointer"
                        @click="openDetail(c.userId)">
                        <td>{{ c.firstName }} {{ c.lastName }}</td>
                        <td>{{ c.email }}</td>
                        <td class="text-right">{{ c.totalPurchases }}</td>
                        <td class="text-right">${{ (c.totalSpentCents / 100).toFixed(2) }}</td>
                        <td>
                            <v-chip v-if="c.hasWaiverSigned" size="small" color="success">Signed</v-chip>
                            <span v-else class="text-medium-emphasis">—</span>
                        </td>
                        <td>{{ c.lastActivityAt ? formatWhen(c.lastActivityAt) : '—' }}</td>
                    </tr>
                    <tr v-if="!loading && !loadError && customers.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-8">
                            No customers match.
                        </td>
                    </tr>
                </tbody>
            </v-table>

            <div v-if="total > limit" class="d-flex justify-space-between align-center pa-3">
                <span class="text-caption text-medium-emphasis">
                    Showing {{ offset + 1 }}–{{ Math.min(offset + limit, total) }} of {{ total }}
                </span>
                <div>
                    <v-btn size="small" variant="text" :disabled="offset === 0" @click="prevPage">Prev</v-btn>
                    <v-btn size="small" variant="text" :disabled="offset + limit >= total" @click="nextPage">Next</v-btn>
                </div>
            </div>
        </v-card>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { CustomerService, type CustomerSummaryDto } from '@/services/CustomerService'
import { branding } from '@/stores/branding'

const router = useRouter()
const service = new CustomerService()

const search = ref('')
const customers = ref<CustomerSummaryDto[]>([])
const total = ref(0)
const limit = ref(50)
const offset = ref(0)
const loading = ref(false)
const loadError = ref<string | null>(null)

// Debounce the typed search so we don't hammer the API on every keystroke.
let searchTimer: ReturnType<typeof setTimeout> | null = null
function onSearchChanged() {
    if (searchTimer) clearTimeout(searchTimer)
    searchTimer = setTimeout(() => {
        offset.value = 0
        load()
    }, 300)
}

onMounted(load)

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const r = await service.list(search.value || undefined, limit.value, offset.value)
        const data = (r.data as any).data
        customers.value = data.items
        total.value = data.total
    } catch (err: any) {
        loadError.value = err.response?.data?.error ?? 'Couldn’t load customers. Refresh to try again.'
    } finally {
        loading.value = false
    }
}

function nextPage() {
    offset.value += limit.value
    load()
}

function prevPage() {
    offset.value = Math.max(0, offset.value - limit.value)
    load()
}

function openDetail(userId: string) {
    router.push({ name: 'AdminCustomerDetail', params: { userId } })
}

function tz() { return branding.timezone || 'UTC' }
function formatWhen(utc: string): string {
    return dayjs.utc(utc).tz(tz()).format('YYYY-MM-DD HH:mm')
}
</script>

<style scoped>
.cursor-pointer { cursor: pointer; }
.cursor-pointer:hover { background-color: rgba(0, 0, 0, 0.04); }
</style>
