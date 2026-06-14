<template>
    <v-container>
        <div class="d-flex align-center mb-4 flex-wrap ga-3">
            <h1 class="text-h4">Rental Counter</h1>
            <v-spacer></v-spacer>
            <v-text-field v-model="fromDate" type="date" label="From" density="compact" hide-details style="max-width: 180px"></v-text-field>
            <v-text-field v-model="toDate" type="date" label="To" density="compact" hide-details style="max-width: 180px"></v-text-field>
            <v-select v-model="statusFilter" :items="statusOptions" label="Status" density="compact" hide-details
                clearable style="max-width: 160px"></v-select>
            <v-btn variant="text" @click="load">Refresh</v-btn>
        </div>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Rental</th>
                        <th>Rider</th>
                        <th style="width: 220px">Dates</th>
                        <th style="width: 90px">Qty</th>
                        <th style="width: 110px">Status</th>
                        <th style="width: 200px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="r in rows" :key="r.id">
                        <td>
                            <strong>{{ r.productName }}</strong>
                        </td>
                        <td>
                            {{ r.purchaserName }}
                            <div class="text-caption text-medium-emphasis">{{ r.purchaserEmail }}</div>
                        </td>
                        <td>{{ formatDate(r.startDate) }} → {{ formatDate(r.endDate) }}</td>
                        <td>{{ r.quantity }}</td>
                        <td><v-chip size="small" :color="statusColor(r.status)">{{ r.status }}</v-chip></td>
                        <td class="text-right">
                            <v-btn v-if="r.status === 'paid'" size="small" color="primary"
                                :loading="busyId === r.id" @click="openMarkOut(r)">Mark out</v-btn>
                            <v-btn v-else-if="r.status === 'out'" size="small" color="success"
                                :loading="busyId === r.id" @click="openReturn(r)">Mark returned</v-btn>
                            <span v-else class="text-caption text-medium-emphasis">—</span>
                        </td>
                    </tr>
                    <tr v-if="!loading && rows.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-8">
                            No rentals in this window.
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="markOutOpen" max-width="640" persistent>
            <v-card v-if="markingOut">
                <v-card-title class="d-flex align-center">
                    <span>Mark out "{{ markingOut.productName }}"</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="markOutOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-3">
                        Rider: <strong>{{ markingOut.purchaserName }}</strong>
                        · {{ formatDate(markingOut.startDate) }} → {{ formatDate(markingOut.endDate) }}
                    </p>
                    <template v-if="markingOut.assignedItems.length > 0">
                        <p class="text-caption text-medium-emphasis mb-2">
                            Snap a photo of each unit before handover. Optional but recommended for damage disputes.
                        </p>
                        <div v-for="(item, i) in markingOut.assignedItems" :key="item.purchaseItemId"
                            class="mb-4 unit-block">
                            <div class="text-subtitle-2 mb-1">
                                <v-icon size="small" class="mr-1">mdi-tag</v-icon>{{ item.label || 'Unit' }}
                            </div>
                            <PhotoCapture v-model="markOutForm[i].photoDataUrl" />
                            <v-text-field v-model="markOutForm[i].notes" label="Condition note (optional)"
                                density="compact" class="mt-2"></v-text-field>
                        </div>
                    </template>
                    <p v-else class="text-caption text-medium-emphasis">
                        Pool inventory — no per-unit photos needed.
                    </p>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="markOutOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="markOutSaving" @click="confirmMarkOut">Mark Out</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="returnOpen" max-width="640" persistent>
            <v-card v-if="returning">
                <v-card-title class="d-flex align-center">
                    <span>Mark "{{ returning.productName }}" returned</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="returnOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-2">
                        Rider: <strong>{{ returning.purchaserName }}</strong>
                        ({{ returning.quantity }} unit{{ returning.quantity === 1 ? '' : 's' }})
                    </p>
                    <template v-if="returning.assignedItems.length > 0">
                        <p class="text-caption text-medium-emphasis mb-2">
                            Snap a return photo of each unit. Compare against the checkout photo if you want to flag damage.
                        </p>
                        <div v-for="(item, i) in returning.assignedItems" :key="item.purchaseItemId"
                            class="mb-4 unit-block">
                            <div class="d-flex align-center mb-2">
                                <v-icon size="small" class="mr-1">mdi-tag</v-icon>
                                <strong>{{ item.label || 'Unit' }}</strong>
                                <v-spacer></v-spacer>
                                <img v-if="item.checkoutPhotoDataUrl" :src="item.checkoutPhotoDataUrl"
                                    class="checkout-thumb" alt="Checkout photo"
                                    @click="zoomImage(item.checkoutPhotoDataUrl)" />
                            </div>
                            <PhotoCapture v-model="returnItemForms[i].photoDataUrl" />
                            <v-text-field v-model="returnItemForms[i].notes" label="Return condition (optional)"
                                density="compact" class="mt-2"></v-text-field>
                        </div>
                    </template>
                    <v-divider class="my-3"></v-divider>
                    <v-textarea v-model="returnForm.conditionNotes" label="Overall notes (optional)"
                        rows="2" density="compact" class="mt-4"></v-textarea>
                    <v-alert v-if="returning.depositCents > 0" type="info" variant="tonal" density="compact" class="my-3">
                        Deposit on file: <strong>${{ (returning.depositCents / 100).toFixed(2) }}</strong>.
                        Leave at $0 to refund the full deposit. Enter an amount to keep some/all for damage.
                    </v-alert>
                    <v-text-field v-if="returning.depositCents > 0" v-model.number="depositCapturedDollars"
                        type="number" min="0" :max="returning.depositCents / 100" step="0.01"
                        label="Deposit kept ($)" density="compact" class="mt-4"></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="returnOpen = false">Cancel</v-btn>
                    <v-btn color="success" :loading="returnSaving" @click="confirmReturn">
                        Confirm Return
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="zoomOpen" max-width="700">
            <v-card>
                <v-img :src="zoomSrc"></v-img>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { RentalService, type CounterRental, type PerItemConditionInput } from '@/services/RentalService'
import { branding } from '@/stores/branding'
import PhotoCapture from '@/components/PhotoCapture.vue'

const service = new RentalService()

const fromDate = ref(dayjs().subtract(1, 'day').format('YYYY-MM-DD'))
const toDate = ref(dayjs().add(7, 'day').format('YYYY-MM-DD'))
const statusFilter = ref<string | null>(null)
const statusOptions = ['paid', 'out', 'returned', 'damaged', 'cancelled', 'failed']

const rows = ref<CounterRental[]>([])
const loading = ref(false)
const busyId = ref<string | null>(null)

const markOutOpen = ref(false)
const markingOut = ref<CounterRental | null>(null)
const markOutForm = ref<{ photoDataUrl: string | null; notes: string }[]>([])
const markOutSaving = ref(false)

const returnOpen = ref(false)
const returning = ref<CounterRental | null>(null)
const returnForm = ref({ conditionNotes: '' })
const returnItemForms = ref<{ photoDataUrl: string | null; notes: string }[]>([])
const depositCapturedDollars = ref(0)
const returnSaving = ref(false)

const zoomOpen = ref(false)
const zoomSrc = ref<string>('')
function zoomImage(src: string | null) {
    if (!src) return
    zoomSrc.value = src
    zoomOpen.value = true
}

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(load)

function tz(): string { return branding.timezone || 'UTC' }
function formatDate(d: string): string { return dayjs(d).format('MMM D') }
function statusColor(s: string): string {
    if (s === 'paid') return 'primary'
    if (s === 'out') return 'warning'
    if (s === 'returned') return 'success'
    if (s === 'damaged') return 'error'
    return 'grey'
}

async function load() {
    loading.value = true
    try {
        const fromUtc = dayjs.tz(fromDate.value + 'T00:00', tz()).utc().toISOString()
        const toUtc = dayjs.tz(toDate.value + 'T23:59', tz()).utc().toISOString()
        const r = await service.listForCounter({
            fromUtc, toUtc,
            status: statusFilter.value || undefined,
        })
        rows.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load.', 'error')
    } finally {
        loading.value = false
    }
}

function openMarkOut(r: CounterRental) {
    markingOut.value = r
    markOutForm.value = r.assignedItems.map(a => ({
        photoDataUrl: a.checkoutPhotoDataUrl,
        notes: a.checkoutNotes || '',
    }))
    markOutOpen.value = true
}

async function confirmMarkOut() {
    if (!markingOut.value) return
    markOutSaving.value = true
    try {
        const items: PerItemConditionInput[] = markingOut.value.assignedItems.map((a, i) => ({
            purchaseItemId: a.purchaseItemId,
            photoDataUrl: markOutForm.value[i]?.photoDataUrl || null,
            notes: markOutForm.value[i]?.notes?.trim() || null,
        }))
        await service.markOut(markingOut.value.id, { items })
        flash(`Marked out: ${markingOut.value.productName}.`, 'success')
        markOutOpen.value = false
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Mark-out failed.', 'error')
    } finally {
        markOutSaving.value = false
    }
}

function openReturn(r: CounterRental) {
    returning.value = r
    returnForm.value = { conditionNotes: '' }
    returnItemForms.value = r.assignedItems.map(a => ({
        photoDataUrl: a.returnPhotoDataUrl,
        notes: a.returnNotes || '',
    }))
    depositCapturedDollars.value = 0
    returnOpen.value = true
}

async function confirmReturn() {
    if (!returning.value) return
    returnSaving.value = true
    try {
        const items: PerItemConditionInput[] = returning.value.assignedItems.map((a, i) => ({
            purchaseItemId: a.purchaseItemId,
            photoDataUrl: returnItemForms.value[i]?.photoDataUrl || null,
            notes: returnItemForms.value[i]?.notes?.trim() || null,
        }))
        await service.markReturned(returning.value.id, {
            conditionNotes: returnForm.value.conditionNotes.trim() || null,
            depositCapturedCents: Math.round((depositCapturedDollars.value || 0) * 100),
            items,
        })
        const damaged = depositCapturedDollars.value > 0
        flash(damaged ? 'Returned (deposit partially kept).' : 'Returned. Deposit refunded.', 'success')
        returnOpen.value = false
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Return failed.', 'error')
    } finally {
        returnSaving.value = false
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>

<style scoped>
.unit-block {
    border: 1px solid rgba(0, 0, 0, 0.08);
    border-radius: 6px;
    padding: 12px;
    background: rgba(0, 0, 0, 0.02);
}
.checkout-thumb {
    height: 60px;
    width: 60px;
    object-fit: cover;
    border-radius: 4px;
    cursor: zoom-in;
    border: 1px solid rgba(0, 0, 0, 0.1);
}
</style>
