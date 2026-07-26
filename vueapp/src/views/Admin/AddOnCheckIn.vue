<template>
    <v-container fluid>
        <div class="d-flex align-center mb-2 flex-wrap ga-2">
            <h1 class="text-h5">Add-on Check-in</h1>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-refresh" :loading="loading" @click="load">Refresh</v-btn>
        </div>
        <p class="text-body-2 text-medium-emphasis mb-4">
            Camping, parking, pit vehicles and anything else sold as an add-on. Tick people off as
            they turn up. You can also scan a rider's QR at Scan Tickets, which checks in their
            add-ons alongside their entry.
        </p>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <v-card variant="outlined" class="pa-3 mb-4">
            <div class="d-flex ga-3 flex-wrap align-start">
                <v-select v-model="productId" :items="productItems" item-title="title" item-value="value"
                    label="Add-on" density="compact" hide-details clearable style="flex: 1 1 220px"
                    @update:model-value="load" />
                <v-select v-model="arrival" :items="arrivalItems" label="Arrival" density="compact"
                    hide-details clearable style="flex: 0 1 180px" @update:model-value="load" />
                <v-text-field v-model="fromDate" type="date" label="From" density="compact" hide-details
                    style="flex: 0 1 170px" :disabled="!!query.trim()" @change="load" />
                <v-text-field v-model="toDate" type="date" label="To" density="compact" hide-details
                    style="flex: 0 1 170px" :disabled="!!query.trim()" @change="load" />
                <v-text-field v-model="query" label="Search name or email" density="compact" hide-details
                    clearable prepend-inner-icon="mdi-account-search" style="flex: 1 1 220px"
                    @keyup.enter="load" @click:clear="onClearSearch" />
                <v-btn color="primary" variant="tonal" :loading="loading" class="mt-1" @click="load">
                    Find
                </v-btn>
            </div>
            <!-- Searching by name has to reach someone whose camping is filed under another
                 weekend, so the server drops the window. Say so rather than leaving the greyed-out
                 date fields to be interpreted. -->
            <div v-if="query.trim()" class="text-caption text-medium-emphasis mt-2">
                Searching by name looks across all dates, so the date range is ignored.
            </div>
        </v-card>

        <div class="d-flex align-center flex-wrap ga-4 mb-2">
            <!-- The ratio only means something across everyone. Filtered to one arrival state it
                 would read "0 of 12 arrived" while showing 12 people who haven't arrived, so the
                 filtered view gets a plain count instead. -->
            <div v-if="!arrival" class="text-body-2">
                <strong>{{ data?.arrivedCount ?? 0 }}</strong> of
                <strong>{{ data?.totalCount ?? 0 }}</strong> arrived
            </div>
            <div v-else class="text-body-2">
                <strong>{{ data?.totalCount ?? 0 }}</strong>
                {{ arrival === 'arrived' ? 'arrived' : 'still to arrive' }}
            </div>
            <v-progress-linear v-if="!arrival && (data?.totalCount ?? 0) > 0" :model-value="arrivedPercent"
                color="success" height="6" rounded style="max-width: 240px" />
            <v-spacer />
            <v-chip v-if="data?.truncated" size="small" color="warning" variant="tonal">
                Showing the first 500. Narrow the filters to see the rest.
            </v-chip>
        </div>

        <v-card variant="outlined" :loading="loading">
            <div class="table-scroll">
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th>Customer</th>
                            <th>Add-on</th>
                            <th>Event</th>
                            <th class="text-center">Arrived</th>
                            <th class="text-right">Action</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-if="!loading && rows.length === 0">
                            <td colspan="5" class="text-center text-medium-emphasis py-6">
                                No add-ons match. Widen the dates, or clear the filters.
                            </td>
                        </tr>
                        <tr v-for="r in rows" :key="r.purchaseId" :class="{ 'arrived-row': r.arrived }">
                            <td>
                                <div class="font-weight-medium">{{ r.purchaserName || '(no name)' }}</div>
                                <div class="text-caption text-medium-emphasis">{{ r.purchaserEmail }}</div>
                            </td>
                            <td>
                                {{ r.productName }}
                                <span v-if="r.quantity > 1" class="font-weight-bold">&times;{{ r.quantity }}</span>
                                <div v-if="r.variantLabel" class="text-caption text-medium-emphasis">
                                    {{ r.variantLabel }}
                                </div>
                            </td>
                            <td>
                                <template v-if="r.eventTitle">
                                    {{ r.eventTitle }}
                                    <div v-if="r.eventStartsAtUtc" class="text-caption text-medium-emphasis">
                                        {{ formatDate(r.eventStartsAtUtc) }}
                                    </div>
                                </template>
                                <!-- Bought at the counter, so there is no event to file it under.
                                     Shows the purchase date instead of an empty cell. -->
                                <template v-else>
                                    <span class="text-medium-emphasis">No event</span>
                                    <div class="text-caption text-medium-emphasis">
                                        Bought {{ formatDate(r.purchasedAtUtc) }}
                                    </div>
                                </template>
                            </td>
                            <td class="text-center">
                                <template v-if="r.arrived">
                                    <v-icon color="success" size="20">mdi-check-circle</v-icon>
                                    <div class="text-caption text-medium-emphasis">
                                        {{ r.arrivedAtUtc ? formatDate(r.arrivedAtUtc) : '' }}
                                        <span v-if="r.arrivedByName"> &middot; {{ r.arrivedByName }}</span>
                                    </div>
                                </template>
                                <span v-else class="text-medium-emphasis">Not yet</span>
                            </td>
                            <td class="text-right text-no-wrap">
                                <v-btn v-if="!r.arrived" size="small" color="primary" variant="tonal"
                                    prepend-icon="mdi-check" :loading="savingId === r.purchaseId"
                                    :disabled="!!savingId" @click="setArrived(r, true)">
                                    Check in
                                </v-btn>
                                <v-btn v-else size="small" variant="text" color="medium-emphasis"
                                    :loading="savingId === r.purchaseId" :disabled="!!savingId"
                                    @click="setArrived(r, false)">
                                    Undo
                                </v-btn>
                            </td>
                        </tr>
                    </tbody>
                </v-table>
            </div>
        </v-card>

        <v-snackbar v-model="toast" :timeout="4000" :color="toastColor" location="top">{{ toastText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { branding } from '@/stores/branding'
import { useConfirm } from '@/composables/useConfirm'
import {
    ExtraService,
    type ExtraCheckInItem,
    type ExtraCheckInResponse,
    type ExtraCheckInProductOption,
} from '@/services/ExtraService'

const service = new ExtraService()
const confirm = useConfirm()

const data = ref<ExtraCheckInResponse | null>(null)
const rows = computed(() => data.value?.items ?? [])
const products = ref<ExtraCheckInProductOption[]>([])
const loading = ref(false)
const loadError = ref('')
const savingId = ref<string | null>(null)

const productId = ref<string | null>(null)
const arrival = ref<'arrived' | 'not_arrived' | null>(null)
const query = ref('')
// Opens on a window around today rather than all history: the people turning up are the point,
// and camping is usually bought well in advance.
const fromDate = ref(dayjs().subtract(3, 'day').format('YYYY-MM-DD'))
const toDate = ref(dayjs().add(14, 'day').format('YYYY-MM-DD'))

const arrivalItems = [
    { title: 'Not arrived', value: 'not_arrived' },
    { title: 'Arrived', value: 'arrived' },
]
const productItems = computed(() => products.value.map(p => ({
    title: p.isActive ? p.name : `${p.name} (inactive)`,
    value: p.id as string | null,
})))
const arrivedPercent = computed(() => {
    const total = data.value?.totalCount ?? 0
    return total === 0 ? 0 : Math.round(((data.value?.arrivedCount ?? 0) / total) * 100)
})

const toast = ref(false)
const toastText = ref('')
const toastColor = ref<'success' | 'error'>('success')
function flash(text: string, color: 'success' | 'error' = 'success') {
    toastText.value = text
    toastColor.value = color
    toast.value = true
}

function formatDate(utc: string) {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('MMM D, h:mm A')
}

function onClearSearch() {
    query.value = ''
    load()
}

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        const { data: res } = await service.checkInList({
            productId: productId.value,
            from: query.value.trim() ? null : `${fromDate.value}T00:00:00`,
            to: query.value.trim() ? null : `${toDate.value}T23:59:59`,
            q: query.value.trim() || null,
            arrival: arrival.value,
        })
        data.value = res.data
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            ?? 'Could not load the add-on list. Use Refresh to try again.'
    } finally {
        loading.value = false
    }
}

async function setArrived(row: ExtraCheckInItem, checkedIn: boolean) {
    // Undoing is the destructive direction (it takes an arrival off the record), so it asks.
    if (!checkedIn && !await confirm({
        message: `Undo check-in for ${row.purchaserName || row.purchaserEmail}? `
            + 'Their arrival time and who checked them in will be cleared.',
        confirmText: 'Undo check-in',
    })) return

    savingId.value = row.purchaseId
    try {
        const { data: res } = await service.setCheckIn(row.purchaseId, checkedIn)
        // Patch the row in place so a long list doesn't jump back to the top mid-queue.
        row.arrived = res.data.arrived
        row.status = res.data.status
        row.arrivedAtUtc = res.data.arrivedAtUtc
        if (data.value) {
            data.value.arrivedCount += checkedIn ? 1 : -1
        }
        flash(checkedIn
            ? `${row.purchaserName || 'Customer'} checked in for ${row.productName}.`
            : 'Check-in undone.')
    } catch (err: any) {
        loadError.value = ''
        flash(err.response?.data?.error
            ?? (checkedIn
                ? 'Could not check this add-on in. Nothing was changed; refresh and try again.'
                : 'Could not undo this check-in. Nothing was changed; refresh and try again.'), 'error')
    } finally {
        savingId.value = null
    }
}

onMounted(async () => {
    try {
        const { data: f } = await service.checkInFilters()
        products.value = f.data.products
    } catch (err: any) {
        // The filter is a convenience; the list below still works unfiltered, so this must not
        // take the page down.
        flash(err.response?.data?.error
            ?? 'Could not load the add-on filter. The list below still works.', 'error')
    }
    await load()
})
</script>

<style scoped>
.table-scroll { overflow-x: auto; }
.arrived-row { opacity: 0.72; }
</style>
