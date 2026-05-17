<template>
    <v-dialog :model-value="modelValue" @update:model-value="$emit('update:modelValue', $event)" max-width="720">
        <v-card>
            <v-card-title>
                Admissions
                <span v-if="eventTitle" class="text-body-2 text-medium-emphasis ml-2">— {{ eventTitle }}</span>
            </v-card-title>
            <v-card-text>
                <div v-if="!loading && tiers.length === 0" class="text-center text-medium-emphasis py-4">
                    No admissions yet.
                </div>

                <template v-for="group in groupedTiers" :key="group.kind">
                    <div v-if="group.tiers.length > 0" class="mb-4">
                        <div class="d-flex align-center mb-2">
                            <v-chip size="small" :color="group.kind === 'race_entry' ? 'deep-orange' : 'primary'">
                                {{ group.label }}
                            </v-chip>
                            <span class="text-caption text-medium-emphasis ml-2">
                                ({{ group.tiers.length }} {{ group.tiers.length === 1 ? 'option' : 'options' }})
                            </span>
                        </div>
                        <v-table density="compact">
                            <thead>
                                <tr>
                                    <th>Name</th>
                                    <th style="width: 100px">Price</th>
                                    <th style="width: 100px">Inventory</th>
                                    <th style="width: 90px">Sold</th>
                                    <th style="width: 90px">Active</th>
                                    <th style="width: 120px" class="text-right"></th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr v-for="t in group.tiers" :key="t.id">
                                    <td>{{ t.name }}</td>
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
                            </tbody>
                        </v-table>
                    </div>
                </template>

                <v-btn color="primary" class="mt-2" prepend-icon="mdi-plus" @click="openCreate">Add Admission</v-btn>
            </v-card-text>
            <v-card-actions>
                <v-spacer></v-spacer>
                <v-btn @click="close">Close</v-btn>
            </v-card-actions>
        </v-card>

        <v-dialog v-model="tierDialog" max-width="480">
            <v-card>
                <v-card-title>{{ editing ? 'Edit Admission' : 'Add Admission' }}</v-card-title>
                <v-card-text>
                    <v-select v-model="form.kind" :items="kindOptions" item-title="label" item-value="value"
                        label="Type" density="compact" class="mb-3"
                        hint="Spectator Pass = pay to watch. Race Entry = pay to compete."
                        persistent-hint :hide-details="false"></v-select>
                    <v-text-field v-model="form.name" label="Name"
                        :placeholder="form.kind === 'race_entry' ? 'e.g. Pro 250 class' : 'e.g. Adult spectator'"
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

                    <!-- Bundled coupons — race-entry only. Each paid race entry mints N
                         coupon codes for the buyer to give to friends/family. -->
                    <template v-if="form.kind === 'race_entry'">
                        <v-divider class="my-4"></v-divider>
                        <div class="text-subtitle-2 mb-1">Bundled coupons (optional)</div>
                        <p class="text-caption text-medium-emphasis mb-2">
                            When a rider buys this race entry, auto-generate coupon codes they can share.
                        </p>
                        <v-row dense>
                            <v-col cols="6">
                                <v-text-field v-model.number="form.bundledCount" type="number" min="0" max="100"
                                    label="Codes per entry" density="compact"
                                    hint="0 or blank = no bundle." persistent-hint :hide-details="false"></v-text-field>
                            </v-col>
                            <v-col cols="6">
                                <v-text-field v-model.number="form.bundledExpiresInDays" type="number" min="1" max="365"
                                    label="Expires in days (optional)" density="compact"></v-text-field>
                            </v-col>
                        </v-row>
                        <v-row dense v-if="(form.bundledCount ?? 0) > 0">
                            <v-col cols="4">
                                <v-select v-model="form.bundledKind" :items="bundledKindOptions"
                                    item-title="label" item-value="value" label="Discount type"
                                    density="compact"></v-select>
                            </v-col>
                            <v-col cols="4">
                                <v-text-field v-if="form.bundledKind === 'percent'"
                                    v-model.number="form.bundledPercent" type="number" min="1" max="100"
                                    suffix="%" label="Percent off" density="compact"></v-text-field>
                                <v-text-field v-else v-model.number="form.bundledDollars" type="number"
                                    min="0.5" step="0.5" prefix="$" label="Amount off" density="compact"></v-text-field>
                            </v-col>
                            <v-col cols="4">
                                <v-select v-model="form.bundledScope" :items="bundledScopeOptions"
                                    item-title="label" item-value="value" label="Applies to"
                                    density="compact"></v-select>
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
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import { TicketService, type TicketTier } from '@/services/TicketService'

const props = defineProps<{ modelValue: boolean; eventId: string | null; eventTitle?: string }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void }>()

const service = new TicketService()

const tiers = ref<TicketTier[]>([])
const loading = ref(false)
const tierDialog = ref(false)
const editing = ref<TicketTier | null>(null)
const saving = ref(false)

type AdmissionKind = 'spectator_pass' | 'race_entry'
type BundledKind = 'percent' | 'amount'
type BundledScope = 'all' | 'pass' | 'event_ticket' | 'season_pass'
const kindOptions: { value: AdmissionKind; label: string }[] = [
    { value: 'spectator_pass', label: 'Spectator Pass' },
    { value: 'race_entry',     label: 'Race Entry' },
]
const bundledKindOptions: { value: BundledKind; label: string }[] = [
    { value: 'percent', label: 'Percent off' },
    { value: 'amount',  label: 'Amount off' },
]
const bundledScopeOptions: { value: BundledScope; label: string }[] = [
    { value: 'event_ticket', label: 'Event tickets' },
    { value: 'all',          label: 'Any purchase' },
    { value: 'pass',     label: 'Day passes' },
    { value: 'season_pass',  label: 'Season passes' },
]
function kindLabel(k: string): string {
    return kindOptions.find(o => o.value === k)?.label ?? k
}

// Two ordered groups for the admin view; spectator passes shown first.
const groupedTiers = computed(() => {
    const sortFn = (a: TicketTier, b: TicketTier) =>
        (a.sortOrder ?? 0) - (b.sortOrder ?? 0) || a.name.localeCompare(b.name)
    return [
        { kind: 'spectator_pass', label: 'Spectator Pass',
          tiers: tiers.value.filter(t => (t.kind ?? 'spectator_pass') === 'spectator_pass').sort(sortFn) },
        { kind: 'race_entry',     label: 'Race Entry',
          tiers: tiers.value.filter(t => t.kind === 'race_entry').sort(sortFn) },
    ]
})

const form = ref({
    kind: 'spectator_pass' as AdmissionKind,
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
    bundledScope: 'event_ticket' as BundledScope,
    bundledExpiresInDays: null as number | null,
})

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

watch(() => props.modelValue, (open) => {
    if (open && props.eventId) load()
})

async function load() {
    if (!props.eventId) return
    loading.value = true
    try {
        const r = await service.listTiersForAdmin(props.eventId)
        tiers.value = (r.data as any).data
    } finally {
        loading.value = false
    }
}

function close() { emit('update:modelValue', false) }

function openCreate() {
    editing.value = null
    form.value = {
        kind: 'spectator_pass', name: '', priceDollars: 20,
        inventory: null, sortOrder: 100, isActive: true, riderPaidServiceChargePct: 100,
        bundledCount: null, bundledKind: 'percent', bundledPercent: 20, bundledDollars: 5,
        bundledScope: 'event_ticket', bundledExpiresInDays: null,
    }
    tierDialog.value = true
}
function openEdit(t: TicketTier) {
    editing.value = t
    form.value = {
        kind: (t.kind ?? 'spectator_pass') as AdmissionKind,
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
        bundledScope: (t.bundledCouponScope ?? 'event_ticket') as BundledScope,
        bundledExpiresInDays: t.bundledCouponExpiresInDays,
    }
    tierDialog.value = true
}

async function save() {
    if (!props.eventId || !form.value.name.trim()) return
    try {
        saving.value = true
        const bundledCount = (form.value.kind === 'race_entry' && (form.value.bundledCount ?? 0) > 0)
            ? form.value.bundledCount : null
        const body = {
            kind: form.value.kind,
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
            bundledCouponScope: bundledCount ? form.value.bundledScope : null,
            bundledCouponExpiresInDays: bundledCount ? (form.value.bundledExpiresInDays || null) : null,
        }
        if (editing.value) {
            await service.updateTier(props.eventId, editing.value.id, body)
        } else {
            await service.createTier(props.eventId, body)
        }
        tierDialog.value = false
        await load()
        flash('Admission saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function remove(t: TicketTier) {
    if (!props.eventId) return
    if (!confirm(`Delete admission "${t.name}"?`)) return
    try {
        await service.deleteTier(props.eventId, t.id)
        await load()
        flash('Admission deleted.', 'success')
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
