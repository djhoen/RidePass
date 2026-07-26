<template>
    <v-dialog :model-value="modelValue" max-width="560" @update:model-value="$emit('update:modelValue', $event)">
        <v-card v-if="product">
            <v-card-title class="d-flex align-center">
                <span>{{ variant ? 'Edit variant' : 'New variant' }} — {{ product.name }}</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" @click="close"></v-btn>
            </v-card-title>
            <v-card-text>
                <v-row dense>
                    <v-col cols="4"><v-text-field v-model="form.size" label="Size" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="4"><v-text-field v-model="form.color" label="Color" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="4"><v-text-field v-model="form.gender" label="Gender" density="compact" hide-details></v-text-field></v-col>
                </v-row>
                <v-row dense class="mt-2">
                    <v-col cols="4"><v-text-field v-model="form.sku" label="SKU" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="4"><v-text-field v-model="form.barcode" label="Barcode" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="4"><v-text-field v-model="form.mpn" label="Mfr part # (MPN)" density="compact" hide-details></v-text-field></v-col>
                </v-row>
                <!-- The one field that leaves this tenant. Everything else about the product,
                     including its name, stays private, so the hint says which is which. -->
                <v-row dense class="mt-2">
                    <v-col cols="12">
                        <v-text-field v-model="form.manufacturerName" label="Manufacturer's name for this part"
                            density="compact" persistent-hint
                            placeholder="e.g. Bontrager Standard Tube 700x25"
                            hint="Optional. The name on the box, not your own. Shared with other RidePass shops so a scan of this barcode identifies the part; your own product name is never shared."></v-text-field>
                    </v-col>
                </v-row>
                <v-row dense class="mt-2">
                    <v-col cols="4"><v-text-field v-model.number="salePrice" type="number" min="0" step="0.01" label="Sale price" prefix="$" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="4"><v-text-field v-model.number="msrp" type="number" min="0" step="0.01" label="MSRP" prefix="$" density="compact" hide-details persistent-hint hint="Compare-at"></v-text-field></v-col>
                    <v-col cols="4"><v-text-field v-model.number="cost" type="number" min="0" step="0.01" label="Cost" prefix="$" density="compact" hide-details></v-text-field></v-col>
                </v-row>
                <v-row v-if="product.isRentable" dense class="mt-2">
                    <v-col cols="6"><v-text-field v-model.number="dailyRate" type="number" min="0" step="0.01" label="Daily rental rate" prefix="$" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="6"><v-text-field v-model.number="deposit" type="number" min="0" step="0.01" label="Deposit" prefix="$" density="compact" hide-details></v-text-field></v-col>
                </v-row>

                <v-text-field v-model.number="lowStockAt" type="number" min="0"
                    label="Low-stock alert at (blank = off)" density="compact" class="mt-4" hide-details
                    hint="Managers get one alert when available stock falls to this number"></v-text-field>

                <!-- Reorder planning is a pool concept: serialized units are bought individually. -->
                <template v-if="form.trackingKind === 'pool'">
                    <v-row dense class="mt-2">
                        <v-col cols="6">
                            <v-text-field v-model.number="form.reorderPoint" type="number" min="0"
                                label="Reorder point" density="compact" hide-details persistent-hint
                                hint="At/below this, it shows on the reorder list"></v-text-field>
                        </v-col>
                        <v-col cols="6">
                            <v-text-field v-model.number="form.reorderLevel" type="number" min="0"
                                label="Reorder up to" density="compact" hide-details persistent-hint
                                hint="Suggested order tops stock up to here"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-text-field v-model="form.vendorPartNumber" label="Vendor part # (optional)"
                        density="compact" class="mt-4" hide-details></v-text-field>
                </template>

                <v-select v-if="!variant" v-model="form.trackingKind"
                    :items="[{ title: 'Pool (counted quantity)', value: 'pool' }, { title: 'Serialized (individual units)', value: 'serialized' }]"
                    label="Inventory tracking" density="compact" class="mt-4" hide-details></v-select>
                <div v-else class="text-caption text-medium-emphasis mt-4">
                    Tracking: {{ form.trackingKind === 'serialized' ? 'Serialized' : 'Pool' }} (fixed after creation)
                </div>

                <v-switch v-model="form.isActive" label="Active" color="primary" hide-details density="compact" class="mt-2"></v-switch>
                <div v-if="error" class="text-error text-body-2 mt-2">{{ error }}</div>
            </v-card-text>
            <v-card-actions>
                <v-spacer></v-spacer>
                <v-btn :disabled="saving" @click="close">Cancel</v-btn>
                <v-btn color="primary" :loading="saving" @click="save">Save</v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { BikeShopService, type ShopProduct, type ShopVariant, type UpsertShopVariant } from '@/services/BikeShopService'

const props = defineProps<{
    modelValue: boolean
    product: ShopProduct | null
    variant: ShopVariant | null
    /**
     * Seeds for a NEW variant when the user got here by scanning a barcode at the register that
     * the shared parts library recognised. Saves retyping a 14-digit number and the manufacturer's
     * wording they were just shown. Ignored when editing, where the row is the truth.
     */
    prefill?: { barcode?: string | null; manufacturerName?: string | null } | null
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void; (e: 'saved'): void; (e: 'flash', text: string, color?: 'success' | 'error'): void }>()

const service = new BikeShopService()
const saving = ref(false)
const error = ref('')

const form = ref<UpsertShopVariant>(blank())
// Dollar-facing inputs, converted to cents on save.
const salePrice = ref<number | null>(null)
const msrp = ref<number | null>(null)
const cost = ref<number | null>(null)
const dailyRate = ref<number | null>(null)
const deposit = ref<number | null>(null)
const lowStockAt = ref<number | null>(null)

function blank(): UpsertShopVariant {
    return { sku: null, barcode: null, size: null, color: null, gender: null, salePriceCents: null,
        msrpCents: null, dailyRateCents: null, depositCents: 0, costCents: null, mpn: null,
        manufacturerName: null, trackingKind: 'pool',
        lowStockThreshold: null, reorderPoint: null, reorderLevel: null, vendorPartNumber: null, isActive: true }
}
const toCents = (d: number | null) => (d == null || isNaN(d) ? null : Math.round(d * 100))
const toDollars = (c: number | null) => (c == null ? null : c / 100)
const normInt = (n: number | null | undefined) => (n == null || isNaN(n) ? null : Math.max(0, Math.round(n)))

watch(() => props.modelValue, (open) => {
    if (!open) return
    error.value = ''
    const v = props.variant
    form.value = v
        ? { sku: v.sku, barcode: v.barcode, size: v.size, color: v.color, gender: v.gender, salePriceCents: v.salePriceCents,
            msrpCents: v.msrpCents, dailyRateCents: v.dailyRateCents, depositCents: v.depositCents, costCents: v.costCents,
            mpn: v.mpn, manufacturerName: v.manufacturerName, trackingKind: v.trackingKind,
            lowStockThreshold: v.lowStockThreshold, reorderPoint: v.reorderPoint, reorderLevel: v.reorderLevel,
            vendorPartNumber: v.vendorPartNumber, isActive: v.isActive }
        : {
            ...blank(),
            barcode: props.prefill?.barcode ?? null,
            manufacturerName: props.prefill?.manufacturerName ?? null,
        }
    salePrice.value = toDollars(form.value.salePriceCents)
    msrp.value = toDollars(form.value.msrpCents)
    lowStockAt.value = form.value.lowStockThreshold
    cost.value = toDollars(form.value.costCents)
    dailyRate.value = toDollars(form.value.dailyRateCents)
    deposit.value = toDollars(form.value.depositCents) ?? 0
})

function close() { emit('update:modelValue', false) }

async function save() {
    error.value = ''
    if (!props.product) return
    saving.value = true
    try {
        const body: UpsertShopVariant = {
            ...form.value,
            sku: form.value.sku?.trim() || null,
            barcode: form.value.barcode?.trim() || null,
            size: form.value.size?.trim() || null,
            color: form.value.color?.trim() || null,
            gender: form.value.gender?.trim() || null,
            salePriceCents: toCents(salePrice.value),
            msrpCents: toCents(msrp.value),
            mpn: form.value.mpn?.trim() || null,
            manufacturerName: form.value.manufacturerName?.trim() || null,
            costCents: toCents(cost.value),
            dailyRateCents: props.product.isRentable ? toCents(dailyRate.value) : null,
            depositCents: toCents(deposit.value) ?? 0,
            lowStockThreshold: lowStockAt.value != null && !isNaN(lowStockAt.value) ? Math.max(0, Math.round(lowStockAt.value)) : null,
            // Reorder planning is pool-only; clear it for serialized so a hidden value can't linger.
            reorderPoint: form.value.trackingKind === 'pool' ? normInt(form.value.reorderPoint) : null,
            reorderLevel: form.value.trackingKind === 'pool' ? normInt(form.value.reorderLevel) : null,
            vendorPartNumber: form.value.trackingKind === 'pool' ? (form.value.vendorPartNumber?.trim() || null) : null,
        }
        if (props.variant) await service.updateVariant(props.variant.id, body)
        else await service.createVariant(props.product.id, body)
        emit('flash', 'Variant saved.')
        emit('saved')
        close()
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not save the variant. Please try again.'
    } finally { saving.value = false }
}
</script>
