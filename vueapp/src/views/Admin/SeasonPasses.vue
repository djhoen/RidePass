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
                    <div class="text-subtitle-2 mb-1">Event-type perks</div>
                    <p class="text-caption text-medium-emphasis mb-2">
                        Pick event types this pass can be used at. 100% = included, smaller numbers = discount on the ticket price.
                        (Discount application at checkout ships in a follow-up — for now, this is informational and used to gate reservations.)
                    </p>
                    <div v-for="t in eventTypes" :key="t.id" class="d-flex align-center ga-2 mb-1">
                        <v-checkbox :model-value="!!perkFor(t.id)" hide-details density="compact"
                            :label="t.name" @update:model-value="togglePerk(t.id, $event)"></v-checkbox>
                        <v-text-field v-if="perkFor(t.id)" :model-value="perkFor(t.id)?.discountPercent"
                            @update:model-value="setPerkDiscount(t.id, Number($event))" type="number" min="0" max="100"
                            suffix="%" density="compact" hide-details style="max-width: 100px"></v-text-field>
                    </div>

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
import { SeasonPassService, type SeasonPassProduct, type UpsertSeasonPassProduct, type SeasonPassPerk } from '@/services/SeasonPassService'
import { EventTypeService, type EventType } from '@/services/EventTypeService'
import { useConfirm } from '@/composables/useConfirm'

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
    perks: [] as SeasonPassPerk[],
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
function perkFor(eventTypeId: string): SeasonPassPerk | undefined {
    return form.value.perks.find(p => p.eventTypeId === eventTypeId)
}
function togglePerk(eventTypeId: string, on: boolean | null) {
    if (on) {
        if (!perkFor(eventTypeId)) form.value.perks.push({ eventTypeId, discountPercent: 100 })
    } else {
        form.value.perks = form.value.perks.filter(p => p.eventTypeId !== eventTypeId)
    }
}
function setPerkDiscount(eventTypeId: string, value: number) {
    const p = perkFor(eventTypeId)
    if (p) p.discountPercent = Math.max(0, Math.min(100, value || 0))
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
        perks: [],
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
        perks: [...(p.perks ?? [])],
    }
    dialog.value = true
}

async function save() {
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
            perks: form.value.perks,
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
