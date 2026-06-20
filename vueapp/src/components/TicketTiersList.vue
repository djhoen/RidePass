<template>
    <div>
        <div v-if="!loading && visibleRows.length === 0" class="text-medium-emphasis text-body-2 py-3">
            No {{ kindLabelText.toLowerCase() }}s yet.
        </div>

        <v-table v-if="visibleRows.length > 0" density="compact">
            <thead>
                <tr>
                    <th style="width: 36px"></th>
                    <th>Name</th>
                    <th v-if="isGate" style="width: 110px">Audience</th>
                    <th v-if="isGate" style="width: 90px">Required</th>
                    <th style="width: 100px">Price</th>
                    <th style="width: 100px">Inventory</th>
                    <th style="width: 80px">Sold</th>
                    <th style="width: 80px">Active</th>
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
                            <div v-if="t.ladderGroup" class="text-caption text-info">
                                <v-icon size="x-small" class="mr-1">mdi-stairs-up</v-icon>
                                {{ t.ladderGroup }} · {{ stepTriggerLabel(t) }}
                            </div>
                        </td>
                        <td v-if="isGate" class="text-capitalize">{{ t.audience }}</td>
                        <td v-if="isGate">
                            <v-icon v-if="t.required" color="success" size="small">mdi-check</v-icon>
                            <span v-else class="text-medium-emphasis">—</span>
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

        <v-dialog v-model="tierDialog" max-width="480">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ editing ? `Edit ${kindLabelText}` : `Add ${kindLabelText}` }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="tierDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.name" label="Description"
                        :placeholder="namePlaceholder" density="compact"></v-text-field>

                    <template v-if="isGate">
                        <v-select v-model="form.audience" :items="audienceOptions" item-title="label" item-value="value"
                            label="Entrant Type" density="compact" class="mt-4"
                            hint="Rider gate fees gate riders; spectator gate fees admit spectators."
                            persistent-hint></v-select>
                        <v-switch v-model="form.required" color="primary" density="compact" hide-details class="mt-3"
                            :label="form.audience === 'rider'
                                ? 'Required — riders must buy one (race class + one rider gate fee)'
                                : 'Required — spectators must buy one to attend'"></v-switch>
                    </template>

                    <v-row class="mt-2">
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="form.priceDollars" type="number" step="0.5" :min="priceMin"
                                label="Price (USD)" prefix="$" density="compact"
                                :hint="isGate ? '0 allowed (free kids gate)' : ''" :persistent-hint="isGate"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="form.inventory" type="number" min="1"
                                label="Inventory (blank = unlimited)" density="compact"
                                append-inner-icon="mdi-help-circle-outline"
                                @click:append-inner="openHelp('inventory')"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-switch v-model="form.isActive" label="Active" hide-details class="mt-2"></v-switch>
                    <v-text-field v-model.number="form.riderPaidServiceChargePct" type="number" step="1" min="0" max="100"
                        label="Buyer pays this % of the service charge" suffix="%" density="compact" class="mt-3"
                        hint="100% = added to rider's bill. 0% = absorbed by you."
                        persistent-hint
                        append-inner-icon="mdi-help-circle-outline"
                        @click:append-inner="openHelp('serviceCharge')"></v-text-field>

                    <!-- Dynamic pricing: group tiers into a price ladder. Steps sharing a group
                         escalate the price; the buyer sees the cheapest step whose trigger fired. -->
                    <v-divider class="my-4"></v-divider>
                    <div class="text-subtitle-2 mb-1">Dynamic pricing (optional)</div>
                    <p class="text-caption text-medium-emphasis mb-2">
                        Give two or more tiers the same price group to make a ladder: the buyer always
                        sees the cheapest step whose trigger has been reached. Leave blank for a fixed price.
                    </p>
                    <v-text-field v-model="form.ladderGroup" label="Price group (blank = fixed price)"
                        placeholder="e.g. early-bird" density="compact"></v-text-field>
                    <template v-if="form.ladderGroup.trim()">
                        <v-select v-model="form.triggerType" :items="triggerOptions" item-title="label" item-value="value"
                            label="This step's price applies…" density="compact" class="mt-3"></v-select>
                        <v-text-field v-if="form.triggerType === 'sold'" v-model.number="form.minSold" type="number" min="0"
                            label="After this many sold (across the group)" density="compact" class="mt-2"></v-text-field>
                        <v-text-field v-if="form.triggerType === 'days'" v-model.number="form.daysBefore" type="number" min="0"
                            label="Days before the event it kicks in" density="compact" class="mt-2"></v-text-field>
                        <v-text-field v-if="form.triggerType === 'date'" v-model="form.effectiveDate" type="datetime-local"
                            label="Date/time it kicks in" density="compact" class="mt-2"></v-text-field>
                    </template>

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

        <!-- Field help -->
        <v-dialog v-model="helpDialog" max-width="420">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ helpContent.title }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="helpDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p v-for="(para, i) in helpContent.body" :key="i" class="text-body-2 mb-2">{{ para }}</p>
                </v-card-text>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000" location="top">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import draggable from 'vuedraggable'
import { useDragReorder } from '@/composables/useDragReorder'
import { TicketService, type TicketTier } from '@/services/TicketService'

type AdmissionKind = 'race_entry' | 'gate_fee'
type Audience = 'rider' | 'spectator'
type BundledKind = 'percent' | 'amount'
// Dynamic-pricing step trigger: when this step's price takes effect.
type TriggerType = 'none' | 'sold' | 'days' | 'date'

// Renders ONLY tiers of the given kind. Used in the events admin dialog for race
// classes (race_entry) and gate fees (gate_fee). When eventId is null the component
// runs in BUFFER mode: rows are held locally (no API) so they can be created before
// the event exists; the parent reads them via getBuffered() and persists on save.
const props = defineProps<{ eventId: string | null; kind: AdmissionKind }>()

const service = new TicketService()
const isGate = computed(() => props.kind === 'gate_fee')
const isBuffer = computed(() => !props.eventId)

const allTiers = ref<TicketTier[]>([])
const { visibleRows, onReorderEnd } = useDragReorder<TicketTier>({
    rows: allTiers,
    filter: t => (t.kind ?? 'gate_fee') === props.kind,
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

// Field help modals (the ? icons next to Inventory + Service charge).
const helpDialog = ref(false)
const helpTopic = ref<'inventory' | 'serviceCharge'>('inventory')
const HELP: Record<'inventory' | 'serviceCharge', { title: string; body: string[] }> = {
    inventory: {
        title: 'Inventory',
        body: [
            'Inventory caps how many of this item can be sold. Leave it blank for unlimited.',
            'For race events, the inventory you set on each race class adds up to the event’s overall capacity.',
            'Once an item sells out, buyers see it as “Sold out” and can join the waitlist for it instead.',
        ],
    },
    serviceCharge: {
        title: 'Service charge',
        body: [
            'A service charge (the platform + payment processing fee) applies to each sale. This setting controls how much of that fee the buyer pays versus how much you absorb.',
            '100% — the fee is added on top of the price, so the buyer pays it and you keep the full ticket price.',
            '0% — you absorb the fee; it comes out of your revenue and the buyer pays only the listed price.',
        ],
    },
}
const helpContent = computed(() => HELP[helpTopic.value])
function openHelp(topic: 'inventory' | 'serviceCharge') {
    helpTopic.value = topic
    helpDialog.value = true
}
let tmpSeq = 0

const bundledKindOptions: { value: BundledKind; label: string }[] = [
    { value: 'percent', label: 'Percent off' },
    { value: 'amount', label: 'Amount off' },
]
const audienceOptions: { value: Audience; label: string }[] = [
    { value: 'rider', label: 'Rider' },
    { value: 'spectator', label: 'Spectator' },
]
const kindLabelText = computed(() => props.kind === 'race_entry' ? 'Race Class' : 'Gate Fee')
const namePlaceholder = computed(() =>
    props.kind === 'race_entry' ? 'e.g. Pro 250 class'
        : 'e.g. Rider gate / Child gate (7 and under)')
const priceMin = computed(() => isGate.value ? 0 : 0.5)

const form = ref({
    name: '',
    audience: 'rider' as Audience,
    required: false,
    priceDollars: 20,
    inventory: null as number | null,
    sortOrder: 100,
    isActive: true,
    riderPaidServiceChargePct: 100,
    bundledCount: null as number | null,
    bundledKind: 'percent' as BundledKind,
    bundledPercent: 20,
    bundledDollars: 5,
    ladderGroup: '',
    triggerType: 'none' as TriggerType,
    minSold: 0,
    daysBefore: 30,
    effectiveDate: '',
})

const triggerOptions: { value: TriggerType; label: string }[] = [
    { value: 'none', label: 'From the start (base price)' },
    { value: 'sold', label: 'After this many are sold' },
    { value: 'days', label: 'Starting N days before the event' },
    { value: 'date', label: 'Starting on a specific date' },
]

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

function blankForm() {
    return {
        name: '', audience: (isGate.value ? 'rider' : 'rider') as Audience, required: false,
        priceDollars: isGate.value ? 15 : 20, inventory: null as number | null, sortOrder: 100, isActive: true,
        riderPaidServiceChargePct: 100,
        bundledCount: null as number | null, bundledKind: 'percent' as BundledKind, bundledPercent: 20, bundledDollars: 5,
        ladderGroup: '', triggerType: 'none' as TriggerType, minSold: 0, daysBefore: 30, effectiveDate: '',
    }
}

function openCreate() {
    editing.value = null
    form.value = blankForm()
    tierDialog.value = true
}

function openEdit(t: TicketTier) {
    editing.value = t
    form.value = {
        name: t.name,
        audience: (t.audience ?? 'rider') as Audience,
        required: !!t.required,
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
        ladderGroup: t.ladderGroup ?? '',
        triggerType: (t.minSold != null ? 'sold'
            : t.effectiveDaysBefore != null ? 'days'
            : t.effectiveAtUtc != null ? 'date' : 'none') as TriggerType,
        minSold: t.minSold ?? 0,
        daysBefore: t.effectiveDaysBefore ?? 30,
        effectiveDate: t.effectiveAtUtc ? toLocalInput(t.effectiveAtUtc) : '',
    }
    tierDialog.value = true
}

// Build the persisted/buffered tier shape from the form. Race classes are always
// rider-audience and never themselves "required" (the gate fee carries that rule).
function formToTier(): Omit<TicketTier, 'id' | 'eventId' | 'sold'> {
    const isRace = props.kind === 'race_entry'
    const bundledCount = (isRace && (form.value.bundledCount ?? 0) > 0) ? form.value.bundledCount : null
    const group = form.value.ladderGroup.trim() || null
    const tt = form.value.triggerType
    return {
        kind: props.kind,
        audience: isRace ? 'rider' : form.value.audience,
        required: isRace ? false : form.value.required,
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
        bundledCouponScope: bundledCount ? 'event_ticket' : null,
        bundledCouponExpiresInDays: null,
        ladderGroup: group,
        minSold: group && tt === 'sold' ? (form.value.minSold ?? 0) : null,
        effectiveDaysBefore: group && tt === 'days' ? (form.value.daysBefore ?? 0) : null,
        effectiveAtUtc: group && tt === 'date' && form.value.effectiveDate
            ? new Date(form.value.effectiveDate).toISOString() : null,
    }
}

async function save() {
    if (!form.value.name.trim()) return
    const body = formToTier()

    // Buffer mode: hold locally; the parent persists on event create.
    if (isBuffer.value) {
        if (editing.value) {
            Object.assign(editing.value, body)
        } else {
            allTiers.value.push({ ...body, id: `tmp-${++tmpSeq}`, eventId: '', sold: 0 } as TicketTier)
        }
        tierDialog.value = false
        flash(`${kindLabelText.value} added.`, 'success')
        return
    }

    try {
        saving.value = true
        if (editing.value) {
            await service.updateTier(props.eventId!, editing.value.id, body)
        } else {
            await service.createTier(props.eventId!, body)
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
    if (!confirm(`Delete "${t.name}"?`)) return
    if (isBuffer.value) {
        allTiers.value = allTiers.value.filter(x => x.id !== t.id)
        flash(`${kindLabelText.value} removed.`, 'success')
        return
    }
    try {
        await service.deleteTier(props.eventId!, t.id)
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

// UTC ISO -> a value for <input type="datetime-local"> in the admin's local time.
function toLocalInput(iso: string): string {
    const d = new Date(iso)
    const pad = (n: number) => String(n).padStart(2, '0')
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}
// One-line description of a ladder step's trigger, for the tier list.
function stepTriggerLabel(t: TicketTier): string {
    if (t.minSold != null) return `after ${t.minSold} sold`
    if (t.effectiveDaysBefore != null) return `${t.effectiveDaysBefore}d before event`
    if (t.effectiveAtUtc != null) return `from ${new Date(t.effectiveAtUtc).toLocaleDateString()}`
    return 'base price'
}

// Parent (EventDialog) reads buffered rows after creating the event and POSTs each.
function getBuffered(): Array<Omit<TicketTier, 'id' | 'eventId' | 'sold'>> {
    return visibleRows.value.map(t => ({
        kind: t.kind,
        audience: t.audience,
        required: t.required,
        name: t.name,
        priceCents: t.priceCents,
        inventory: t.inventory,
        sortOrder: t.sortOrder,
        isActive: t.isActive,
        riderPaidServiceChargeBps: t.riderPaidServiceChargeBps,
        bundledCouponCount: t.bundledCouponCount,
        bundledCouponDiscountKind: t.bundledCouponDiscountKind,
        bundledCouponDiscountValue: t.bundledCouponDiscountValue,
        bundledCouponScope: t.bundledCouponScope,
        bundledCouponExpiresInDays: t.bundledCouponExpiresInDays,
        ladderGroup: t.ladderGroup,
        minSold: t.minSold,
        effectiveDaysBefore: t.effectiveDaysBefore,
        effectiveAtUtc: t.effectiveAtUtc,
    }))
}

// After the parent creates a brand-new event, persist every buffered row to it.
async function persistTo(eventId: string) {
    for (const body of getBuffered()) {
        await service.createTier(eventId, body)
    }
}

defineExpose({ refresh: load, getBuffered, persistTo })
</script>

<style scoped>
.drag-handle-cell { padding-left: 4px !important; padding-right: 0 !important; }
.drag-handle { cursor: grab; }
.drag-handle:active { cursor: grabbing; }
.drag-ghost { opacity: 0.35; background: rgba(25, 118, 210, 0.08); }
</style>
