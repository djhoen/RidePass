<template>
    <div>
        <div v-if="!eventId" class="text-medium-emphasis text-body-2 py-4">
            Save the event first, then add admissions here.
        </div>
        <template v-else>
            <div v-if="!loading && visibleRows.length === 0" class="text-medium-emphasis text-body-2 py-3">
                No {{ kindLabelText.toLowerCase() }} tiers yet.
            </div>

            <v-table v-if="visibleRows.length > 0" density="compact">
                <thead>
                    <tr>
                        <th style="width: 36px"></th>
                        <th>Name</th>
                        <th style="width: 100px">Price</th>
                        <th style="width: 100px">Inventory</th>
                        <th style="width: 90px">Sold</th>
                        <th style="width: 90px">Active</th>
                        <th style="width: 120px" class="text-right"></th>
                    </tr>
                </thead>
                <draggable tag="tbody" :list="visibleRows" item-key="id" handle=".drag-handle"
                    :animation="180" ghost-class="drag-ghost" @end="onReorderEnd">
                    <template #item="{ element: t }">
                        <tr>
                            <td class="drag-handle-cell">
                                <v-icon class="drag-handle" color="grey">mdi-drag-vertical</v-icon>
                            </td>
                            <td>
                                {{ t.name }}
                                <div v-if="t.bundledCouponCount" class="text-caption text-success">
                                    <v-icon size="x-small" class="mr-1">mdi-tag-multiple</v-icon>
                                    {{ t.bundledCouponCount }} bundled coupon{{ t.bundledCouponCount === 1 ? '' : 's' }}
                                </div>
                            </td>
                            <td>${{ (t.priceCents / 100).toFixed(2) }}</td>
                            <td>{{ t.inventory ?? '∞' }}</td>
                            <td>{{ t.sold ?? 0 }}</td>
                            <td>
                                <v-icon v-if="t.isActive" color="success">mdi-check</v-icon>
                                <v-icon v-else color="grey">mdi-close</v-icon>
                            </td>
                            <td class="text-right">
                                <v-btn variant="text" size="small" @click="openEdit(t)">Edit</v-btn>
                                <v-btn variant="text" size="small" color="error" @click="remove(t)">Delete</v-btn>
                            </td>
                        </tr>
                    </template>
                </draggable>
            </v-table>

            <v-btn color="primary" class="mt-3" prepend-icon="mdi-plus" @click="openCreate">
                Add {{ kindLabelText }}
            </v-btn>
        </template>

        <v-dialog v-model="tierDialog" max-width="480">
            <v-card>
                <v-card-title>{{ editing ? `Edit ${kindLabelText}` : `Add ${kindLabelText}` }}</v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.name" label="Name"
                        :placeholder="kind === 'race_entry' ? 'e.g. Pro 250 class' : 'e.g. Adult spectator'"
                        density="compact"></v-text-field>
                    <v-row class="mt-2">
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="form.priceDollars" type="number" step="0.5" min="0.5"
                                label="Price (USD)" prefix="$" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="form.inventory" type="number" min="1"
                                label="Inventory (blank = unlimited)" density="compact"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-switch v-model="form.isActive" label="Active" hide-details class="mt-2"></v-switch>
                    <v-text-field v-model.number="form.riderPaidServiceChargePct" type="number" step="1" min="0" max="100"
                        label="Rider pays this % of the service charge" suffix="%" density="compact" class="mt-3"
                        hint="100% = added to rider's bill. 0% = absorbed by you."
                        persistent-hint></v-text-field>

                    <!-- Bundled coupons — race-entry only. Codes are pinned to this event,
                         so scope and expiration aren't configurable: they apply to spectator
                         tickets for the same race and stay valid until the race happens. -->
                    <template v-if="kind === 'race_entry'">
                        <v-divider class="my-4"></v-divider>
                        <div class="text-subtitle-2 mb-1">Bundled coupons (optional)</div>
                        <p class="text-caption text-medium-emphasis mb-2">
                            Auto-generate coupon codes for the buyer to share. Codes are valid only for this race.
                        </p>
                        <v-row dense>
                            <v-col cols="12" md="4">
                                <v-text-field v-model.number="form.bundledCount" type="number" min="0" max="100"
                                    label="Codes per entry" density="compact"
                                    hint="0 or blank = no bundle." persistent-hint :hide-details="false"></v-text-field>
                            </v-col>
                            <v-col cols="12" md="4">
                                <v-select v-model="form.bundledKind" :items="bundledKindOptions"
                                    item-title="label" item-value="value" label="Discount type"
                                    density="compact" :disabled="(form.bundledCount ?? 0) === 0"></v-select>
                            </v-col>
                            <v-col cols="12" md="4">
                                <v-text-field v-if="form.bundledKind === 'percent'"
                                    v-model.number="form.bundledPercent" type="number" min="1" max="100"
                                    suffix="%" label="Percent off" density="compact"
                                    :disabled="(form.bundledCount ?? 0) === 0"></v-text-field>
                                <v-text-field v-else v-model.number="form.bundledDollars" type="number"
                                    min="0.5" step="0.5" prefix="$" label="Amount off" density="compact"
                                    :disabled="(form.bundledCount ?? 0) === 0"></v-text-field>
                            </v-col>
                        </v-row>
                    </template>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="tierDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="save">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import draggable from 'vuedraggable'
import { useDragReorder } from '@/composables/useDragReorder'
import { TicketService, type TicketTier } from '@/services/TicketService'

type AdmissionKind = 'spectator_pass' | 'race_entry'
type BundledKind = 'percent' | 'amount'

// Component renders ONLY tiers of the given kind. Used twice in the events admin
// dialog (once for spectator passes, once for race entries) so each tab has its
// own focused view + add button.
const props = defineProps<{ eventId: string | null; kind: AdmissionKind }>()

const service = new TicketService()

const allTiers = ref<TicketTier[]>([])
// `visibleRows` is this kind's subset — drag-drop reorders within the kind
// and `onReorderEnd` interleaves back into `allTiers` so the other kind's
// tiers hold their canonical slot.
const { visibleRows, onReorderEnd } = useDragReorder<TicketTier>({
    rows: allTiers,
    filter: t => (t.kind ?? 'spectator_pass') === props.kind,
    filterDeps: [() => props.kind],
    save: items => {
        if (!props.eventId) return Promise.resolve()
        return service.reorderTiers(props.eventId, items)
    },
    onSuccess: () => flash('Order saved.', 'success'),
    onError: async err => {
        flash((err as any)?.response?.data?.error || 'Failed to save order — refreshing.', 'error')
        await load()
    },
})
const loading = ref(false)
const tierDialog = ref(false)
const editing = ref<TicketTier | null>(null)
const saving = ref(false)

const bundledKindOptions: { value: BundledKind; label: string }[] = [
    { value: 'percent', label: 'Percent off' },
    { value: 'amount',  label: 'Amount off' },
]
const kindLabelText = computed(() => props.kind === 'race_entry' ? 'Race Class' : 'Ticket')

const form = ref({
    name: '',
    priceDollars: 20,
    inventory: null as number | null,
    sortOrder: 100,
    isActive: true,
    riderPaidServiceChargePct: 100,
    bundledCount: null as number | null,
    bundledKind: 'percent' as BundledKind,
    bundledPercent: 20,
    bundledDollars: 5,
})

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

watch(() => props.eventId, () => { if (props.eventId) load() }, { immediate: true })

async function load() {
    if (!props.eventId) return
    loading.value = true
    try {
        const r = await service.listTiersForAdmin(props.eventId)
        allTiers.value = (r.data as any).data
    } finally {
        loading.value = false
    }
}

function openCreate() {
    editing.value = null
    form.value = {
        name: '', priceDollars: 20, inventory: null, sortOrder: 100, isActive: true,
        riderPaidServiceChargePct: 100,
        bundledCount: null, bundledKind: 'percent', bundledPercent: 20, bundledDollars: 5,
    }
    tierDialog.value = true
}

function openEdit(t: TicketTier) {
    editing.value = t
    form.value = {
        name: t.name,
        priceDollars: t.priceCents / 100,
        inventory: t.inventory,
        sortOrder: t.sortOrder,
        isActive: t.isActive,
        riderPaidServiceChargePct: (t.riderPaidServiceChargeBps ?? 10000) / 100,
        bundledCount: t.bundledCouponCount,
        bundledKind: (t.bundledCouponDiscountKind ?? 'percent') as BundledKind,
        bundledPercent: t.bundledCouponDiscountKind === 'percent' && t.bundledCouponDiscountValue
            ? Math.round(t.bundledCouponDiscountValue / 100) : 20,
        bundledDollars: t.bundledCouponDiscountKind === 'amount' && t.bundledCouponDiscountValue
            ? t.bundledCouponDiscountValue / 100 : 5,
    }
    tierDialog.value = true
}

async function save() {
    if (!props.eventId || !form.value.name.trim()) return
    try {
        saving.value = true
        const bundledCount = (props.kind === 'race_entry' && (form.value.bundledCount ?? 0) > 0)
            ? form.value.bundledCount : null
        const body = {
            kind: props.kind,
            name: form.value.name.trim(),
            priceCents: Math.round(form.value.priceDollars * 100),
            inventory: form.value.inventory || null,
            sortOrder: form.value.sortOrder,
            isActive: form.value.isActive,
            riderPaidServiceChargeBps: Math.round((form.value.riderPaidServiceChargePct || 0) * 100),
            bundledCouponCount: bundledCount,
            bundledCouponDiscountKind: bundledCount ? form.value.bundledKind : null,
            bundledCouponDiscountValue: bundledCount
                ? (form.value.bundledKind === 'percent'
                    ? Math.round(form.value.bundledPercent * 100)
                    : Math.round(form.value.bundledDollars * 100))
                : null,
            // Scope is locked to event_ticket and expiry is unlimited — codes are pinned
            // to this race event by the backend, so neither needs to be configurable.
            bundledCouponScope: bundledCount ? 'event_ticket' : null,
            bundledCouponExpiresInDays: null,
        }
        if (editing.value) {
            await service.updateTier(props.eventId, editing.value.id, body)
        } else {
            await service.createTier(props.eventId, body)
        }
        tierDialog.value = false
        await load()
        flash(`${kindLabelText.value} saved.`, 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function remove(t: TicketTier) {
    if (!props.eventId) return
    if (!confirm(`Delete "${t.name}"?`)) return
    try {
        await service.deleteTier(props.eventId, t.id)
        await load()
        flash(`${kindLabelText.value} deleted.`, 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

defineExpose({ refresh: load })
</script>

<style scoped>
.drag-handle-cell { padding-left: 4px !important; padding-right: 0 !important; }
.drag-handle { cursor: grab; }
.drag-handle:active { cursor: grabbing; }
.drag-ghost { opacity: 0.35; background: rgba(25, 118, 210, 0.08); }
</style>
