<template>
    <v-container>
        <div class="d-flex align-center mb-6">
            <h1 class="text-h4">Season Passes</h1>
            <v-spacer></v-spacer>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">New Pass</v-btn>
        </div>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 36px"></th>
                        <th>Name</th>
                        <th style="width: 120px">Price</th>
                        <th style="width: 200px">Valid</th>
                        <th style="width: 160px">Kind</th>
                        <th style="width: 90px">Active</th>
                        <th style="width: 160px" class="text-right"></th>
                    </tr>
                </thead>
                <draggable tag="tbody" :list="visibleRows" item-key="id" handle=".drag-handle"
                    :animation="180" ghost-class="drag-ghost" @end="onReorderEnd">
                    <template #item="{ element: p }">
                        <tr>
                            <td class="drag-handle-cell">
                                <v-icon class="drag-handle" color="grey">mdi-drag-vertical</v-icon>
                            </td>
                            <td>
                                <strong>{{ p.name }}</strong>
                                <div v-if="p.description" class="text-caption text-medium-emphasis">{{ p.description }}</div>
                            </td>
                            <td>${{ (p.priceCents / 100).toFixed(2) }}</td>
                            <td>{{ formatDate(p.validFromDate) }} – {{ formatDate(p.validToDate) }}</td>
                            <td>
                                <span v-if="p.kind === 'unlimited'">Unlimited</span>
                                <span v-else-if="p.kind === 'days_of_week'">Weekdays: {{ daysLabel(p.validDaysOfWeek) }}</span>
                                <span v-else-if="p.kind === 'credits'">{{ p.totalCredits }} credits</span>
                            </td>
                            <td>
                                <v-icon v-if="p.isActive" color="success">mdi-check</v-icon>
                                <v-icon v-else color="grey">mdi-close</v-icon>
                            </td>
                            <td class="text-right">
                                <v-btn variant="text" size="small" @click="openEdit(p)">Edit</v-btn>
                                <v-btn variant="text" size="small" color="error" @click="remove(p)">Delete</v-btn>
                            </td>
                        </tr>
                    </template>
                </draggable>
                <tbody v-if="!loading && !loadError && products.length === 0">
                    <tr>
                        <td colspan="7" class="text-center text-medium-emphasis py-8">No season passes yet.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="dialog" max-width="720" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ editing ? 'Edit Pass' : 'New Pass' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="dialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.name" label="Name" density="compact"></v-text-field>
                    <v-textarea v-model="form.description" label="Description (optional)" rows="2" density="compact" class="mt-6"></v-textarea>
                    <v-row class="mt-2">
                        <v-col cols="12" md="4">
                            <v-text-field v-model.number="form.priceDollars" type="number" step="0.01" min="1"
                                label="Price (USD)" prefix="$" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="4">
                            <v-text-field v-model="form.validFromDate" type="date" label="Valid from" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="4">
                            <v-text-field v-model="form.validToDate" type="date" label="Valid to" density="compact"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-select v-model="form.kind" :items="kindOptions" item-title="title" item-value="value" class="mt-6"
                        label="Type" density="compact"></v-select>
                    <div v-if="form.kind === 'days_of_week'" class="mb-3">
                        <div class="text-caption text-medium-emphasis mb-1">Valid on these days:</div>
                        <v-btn-toggle v-model="form.validDaysOfWeek" multiple density="compact" color="primary">
                            <v-btn :value="0">Sun</v-btn>
                            <v-btn :value="1">Mon</v-btn>
                            <v-btn :value="2">Tue</v-btn>
                            <v-btn :value="3">Wed</v-btn>
                            <v-btn :value="4">Thu</v-btn>
                            <v-btn :value="5">Fri</v-btn>
                            <v-btn :value="6">Sat</v-btn>
                        </v-btn-toggle>
                    </div>
                    <v-text-field v-if="form.kind === 'credits'" v-model.number="form.totalCredits" type="number" min="1"
                        label="Total credits" density="compact" class="mt-4"></v-text-field>

                    <v-divider class="my-3"></v-divider>
                    <div class="text-subtitle-2 mb-1">What this pass includes</div>
                    <p class="text-caption text-medium-emphasis mb-2">
                        Tick an event type to include it, then choose whether holders get in free or
                        at a discount. This is what riders see on the Season Passes page, and it's
                        applied automatically at checkout.
                    </p>
                    <div v-for="t in eventTypes" :key="t.id" class="d-flex align-center ga-3 mb-1">
                        <v-checkbox :model-value="!!benefitFor(t.id)" hide-details density="compact"
                            :label="t.name" class="flex-grow-1"
                            @update:model-value="toggleEventBenefit(t.id, $event)"></v-checkbox>
                        <template v-if="benefitFor(t.id)">
                            <v-select :model-value="benefitFor(t.id)!.discountValue === 10000 ? 'free' : 'discount'"
                                :items="[{ title: 'Included free', value: 'free' }, { title: 'Discount', value: 'discount' }]"
                                density="compact" hide-details variant="outlined" style="max-width: 150px"
                                @update:model-value="setEventBenefitMode(t.id, $event)"></v-select>
                            <v-text-field v-if="benefitFor(t.id)!.discountValue !== 10000"
                                :model-value="benefitFor(t.id)!.discountValue / 100"
                                type="number" min="1" max="99" suffix="% off"
                                density="compact" hide-details variant="outlined" style="max-width: 120px"
                                @update:model-value="setEventBenefitPercent(t.id, $event)"></v-text-field>
                        </template>
                    </div>
                    <p v-if="eventTypes.length === 0" class="text-caption text-medium-emphasis">
                        No event types yet — add them under Settings before setting up pass benefits.
                    </p>

                    <template v-if="branding.bikeShopEnabled">
                        <div class="text-subtitle-2 mt-3 mb-1">Bike shop perks</div>
                        <p class="text-caption text-medium-emphasis mb-2">
                            Percent off for pass holders, applied automatically at the shop register and
                            rental counter when their account email is on the sale. 0 = no perk.
                        </p>
                        <div class="d-flex ga-3">
                            <v-text-field :model-value="surfacePercent('rental')" type="number" min="0" max="100"
                                label="Rentals" suffix="% off" density="compact" hide-details style="max-width: 150px"
                                @update:model-value="setSurfacePercent('rental', $event)"></v-text-field>
                            <v-text-field :model-value="surfacePercent('retail')" type="number" min="0" max="100"
                                label="Shop purchases" suffix="% off" density="compact" hide-details style="max-width: 170px"
                                @update:model-value="setSurfacePercent('retail', $event)"></v-text-field>
                        </div>
                    </template>

                    <v-divider class="my-3"></v-divider>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="form.riderPaidServiceChargePct" type="number" min="0" max="100"
                                label="Rider pays % of service charge" suffix="%" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-switch v-model="form.requiresWaiver" label="Requires waiver" hide-details color="primary"></v-switch>
                        </v-col>
                    </v-row>
                    <v-switch v-model="form.isActive" label="Active" hide-details color="primary"></v-switch>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="dialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="save">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import draggable from 'vuedraggable'
import dayjs from 'dayjs'
import { useDragReorder } from '@/composables/useDragReorder'
import { SeasonPassService, type SeasonPassProduct, type UpsertSeasonPassProduct, type SeasonPassBenefit } from '@/services/SeasonPassService'
import { EventTypeService, type EventType } from '@/services/EventTypeService'
import { useConfirm } from '@/composables/useConfirm'
import { branding } from '@/stores/branding'

const service = new SeasonPassService()
const eventTypeService = new EventTypeService()
const confirm = useConfirm()

const products = ref<SeasonPassProduct[]>([])
const { visibleRows, onReorderEnd } = useDragReorder<SeasonPassProduct>({
    rows: products,
    save: items => service.reorderProducts(items),
    onSuccess: () => flash('Order saved.', 'success'),
    onError: async err => {
        flash((err as any)?.response?.data?.error || 'Failed to save order — refreshing.', 'error')
        await load()
    },
})
const eventTypes = ref<EventType[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const dialog = ref(false)
const editing = ref<SeasonPassProduct | null>(null)
const saving = ref(false)

const kindOptions = [
    { title: 'Unlimited', value: 'unlimited' },
    { title: 'Days of week', value: 'days_of_week' },
    { title: 'Credits (limited rides)', value: 'credits' },
]

const form = ref({
    name: '',
    description: '' as string | null,
    priceDollars: 200,
    validFromDate: dayjs().format('YYYY-MM-DD'),
    validToDate: dayjs().add(6, 'month').format('YYYY-MM-DD'),
    kind: 'unlimited' as 'unlimited' | 'days_of_week' | 'credits',
    validDaysOfWeek: [] as number[],
    totalCredits: 10,
    requiresWaiver: true,
    riderPaidServiceChargePct: 100,
    isActive: true,
    benefits: [] as SeasonPassBenefit[],
})

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

function formatDate(iso: string): string { return dayjs(iso).format('MMM D, YYYY') }
function daysLabel(days: number[] | null): string {
    if (!days || days.length === 0) return '—'
    const names = ['Sun','Mon','Tue','Wed','Thu','Fri','Sat']
    return days.sort().map(d => names[d]).join(', ')
}
// Benefits are stored generically (events today, F&B / rentals / buddy passes as those
// surfaces get wired), so the event editor filters to its own slice.
function benefitFor(eventTypeId: string): SeasonPassBenefit | undefined {
    return form.value.benefits.find(b => b.benefitType === 'event' && b.scopeId === eventTypeId)
}
function toggleEventBenefit(eventTypeId: string, on: boolean | null) {
    if (on) {
        // Default to included-free: that's what the old checkbox meant, so ticking a box keeps
        // behaving the way tenants already expect.
        if (!benefitFor(eventTypeId)) {
            form.value.benefits.push({
                benefitType: 'event', scopeId: eventTypeId,
                discountKind: 'percent', discountValue: 10000, quantity: null,
            })
        }
    } else {
        form.value.benefits = form.value.benefits.filter(
            b => !(b.benefitType === 'event' && b.scopeId === eventTypeId))
    }
}
function setEventBenefitMode(eventTypeId: string, mode: unknown) {
    const b = benefitFor(eventTypeId)
    if (!b) return
    // 10000 bps = 100% = free. Switching to Discount seeds 50% rather than 0, which the
    // server rejects as a half-filled row.
    b.discountValue = mode === 'free' ? 10000 : 5000
}
// Whole-surface perks (rentals / bike shop retail): at most one benefit per surface, percent only.
function surfacePercent(type: 'rental' | 'retail'): number {
    const b = form.value.benefits.find(x => x.benefitType === type)
    return b ? b.discountValue / 100 : 0
}
function setSurfacePercent(type: 'rental' | 'retail', percent: string | number) {
    const n = Math.round(Number(percent))
    form.value.benefits = form.value.benefits.filter(x => x.benefitType !== type)
    if (Number.isFinite(n) && n > 0) {
        form.value.benefits.push({
            benefitType: type, scopeId: null, discountKind: 'percent',
            discountValue: Math.min(100, n) * 100, quantity: null,
        })
    }
}

function setEventBenefitPercent(eventTypeId: string, percent: string | number) {
    const b = benefitFor(eventTypeId)
    if (!b) return
    const n = Math.round(Number(percent))
    if (!Number.isFinite(n)) return
    // Clamp to 1-99: 100 is "Included free" (a separate mode) and 0 would be a benefit that
    // grants nothing.
    b.discountValue = Math.min(99, Math.max(1, n)) * 100
}
onMounted(async () => {
    loadError.value = null
    try {
        const [t, p] = await Promise.all([eventTypeService.list(), service.listForAdmin()])
        eventTypes.value = (t.data as any).data
        products.value = (p.data as any).data
    } catch (err: any) {
        const msg = err.response?.data?.error ?? 'Couldn’t load season passes. Refresh to try again.'
        loadError.value = msg
        flash(msg, 'error')
    }
})

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const r = await service.listForAdmin()
        products.value = (r.data as any).data
    } catch (err: any) {
        const msg = err.response?.data?.error ?? 'Couldn’t load season passes. Refresh to try again.'
        loadError.value = msg
        flash(msg, 'error')
    } finally { loading.value = false }
}

function openCreate() {
    editing.value = null
    form.value = {
        name: '', description: '',
        priceDollars: 200,
        validFromDate: dayjs().format('YYYY-MM-DD'),
        validToDate: dayjs().add(6, 'month').format('YYYY-MM-DD'),
        kind: 'unlimited',
        validDaysOfWeek: [],
        totalCredits: 10,
        requiresWaiver: true,
        riderPaidServiceChargePct: 100,
        isActive: true,
        benefits: [],
    }
    dialog.value = true
}

function openEdit(p: SeasonPassProduct) {
    editing.value = p
    form.value = {
        name: p.name,
        description: p.description ?? '',
        priceDollars: p.priceCents / 100,
        validFromDate: dayjs(p.validFromDate).format('YYYY-MM-DD'),
        validToDate: dayjs(p.validToDate).format('YYYY-MM-DD'),
        kind: p.kind,
        validDaysOfWeek: p.validDaysOfWeek ?? [],
        totalCredits: p.totalCredits ?? 10,
        requiresWaiver: p.requiresWaiver,
        riderPaidServiceChargePct: p.riderPaidServiceChargeBps / 100,
        isActive: p.isActive,
        // Copy each benefit, not just the array: the editor mutates discountValue in place, and
        // sharing the objects with the loaded product would edit the list behind the dialog
        // (leaving stale values on screen after a Cancel).
        benefits: (p.benefits ?? []).map(b => ({ ...b })),
    }
    dialog.value = true
}

async function save() {
    if (!form.value.name.trim()) { flash('Name is required.', 'error'); return }
    if (form.value.validFromDate && form.value.validToDate && form.value.validFromDate > form.value.validToDate) {
        flash('"Valid from" must be on or before "Valid to".', 'error'); return
    }
    if (form.value.kind === 'days_of_week' && (!form.value.validDaysOfWeek || form.value.validDaysOfWeek.length === 0)) {
        flash('Pick at least one day of the week for this pass.', 'error'); return
    }
    if (form.value.kind === 'credits' && (!form.value.totalCredits || form.value.totalCredits < 1)) {
        flash('A credits pass needs at least 1 credit.', 'error'); return
    }
    try {
        saving.value = true
        const body: UpsertSeasonPassProduct = {
            name: form.value.name.trim(),
            description: form.value.description && form.value.description.trim().length > 0 ? form.value.description.trim() : null,
            priceCents: Math.round(form.value.priceDollars * 100),
            validFromDate: form.value.validFromDate,
            validToDate: form.value.validToDate,
            kind: form.value.kind,
            validDaysOfWeek: form.value.kind === 'days_of_week' ? form.value.validDaysOfWeek : null,
            totalCredits: form.value.kind === 'credits' ? form.value.totalCredits : null,
            requiresWaiver: form.value.requiresWaiver,
            riderPaidServiceChargeBps: Math.round((form.value.riderPaidServiceChargePct || 0) * 100),
            isActive: form.value.isActive,
            sortOrder: 100,
            benefits: form.value.benefits,
        }
        if (editing.value) await service.update(editing.value.id, body)
        else await service.create(body)
        dialog.value = false
        await load()
        flash('Pass saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally { saving.value = false }
}

async function remove(p: SeasonPassProduct) {
    if (!await confirm({ message: `Delete "${p.name}"?`, confirmText: 'Delete', confirmColor: 'error' })) return
    try {
        await service.deleteProduct(p.id)
        await load()
        flash('Pass deleted.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>

<style scoped>
.drag-handle-cell { padding-left: 4px !important; padding-right: 0 !important; }
.drag-handle { cursor: grab; }
.drag-handle:active { cursor: grabbing; }
.drag-ghost { opacity: 0.35; background: rgba(25, 118, 210, 0.08); }
</style>
