<template>
    <v-dialog :model-value="modelValue" max-width="480" @update:model-value="$emit('update:modelValue', $event)">
        <v-card v-if="product">
            <v-card-title class="d-flex align-center">
                <span>Generate variants: {{ product.name }}</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" :disabled="busy" @click="close"></v-btn>
            </v-card-title>
            <v-card-text>
                <v-text-field v-model="sizes" label="Sizes (comma-separated)" placeholder="S, M, L, XL"
                    density="compact" hide-details></v-text-field>
                <v-text-field v-model="colors" label="Colors (comma-separated, optional)" placeholder="Red, Blue"
                    density="compact" hide-details class="mt-4"></v-text-field>
                <v-text-field v-model="skuPrefix" label="SKU prefix (optional)" placeholder="JRS"
                    density="compact" hide-details class="mt-4"></v-text-field>
                <v-row dense class="mt-2">
                    <v-col cols="6">
                        <v-text-field v-model.number="priceDollars" type="number" min="0" step="0.01" prefix="$"
                            label="Price" density="compact" hide-details></v-text-field>
                    </v-col>
                    <v-col cols="6">
                        <v-text-field v-model.number="costDollars" type="number" min="0" step="0.01" prefix="$"
                            label="Cost (optional)" density="compact" hide-details></v-text-field>
                    </v-col>
                </v-row>
                <v-text-field v-model.number="lowStockAt" type="number" min="1" label="Low stock alert at (optional)"
                    density="compact" hide-details class="mt-4"></v-text-field>
                <p class="text-caption text-medium-emphasis mt-2">
                    Creates {{ comboCount }} pool variant{{ comboCount === 1 ? '' : 's' }} at zero
                    stock{{ skuPrefix.trim() ? `, SKUs like ${exampleSku}` : '' }}. Combinations that
                    already exist are skipped.
                </p>
                <div v-if="error" class="text-error text-body-2 mt-2">{{ error }}</div>
            </v-card-text>
            <v-card-actions>
                <v-spacer></v-spacer>
                <v-btn :disabled="busy" @click="close">Cancel</v-btn>
                <v-btn color="primary" :loading="busy" :disabled="comboCount === 0" @click="generate">
                    Create {{ comboCount }}
                </v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { BikeShopService, type ShopProduct } from '@/services/BikeShopService'

const props = defineProps<{ modelValue: boolean; product: ShopProduct | null }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void; (e: 'saved'): void; (e: 'flash', text: string, color?: 'success' | 'error'): void }>()

const service = new BikeShopService()
const sizes = ref('')
const colors = ref('')
const skuPrefix = ref('')
const priceDollars = ref<number | null>(null)
const costDollars = ref<number | null>(null)
const lowStockAt = ref<number | null>(null)
const busy = ref(false)
const error = ref('')

watch(() => props.modelValue, (open) => {
    if (open) {
        sizes.value = ''
        colors.value = ''
        skuPrefix.value = ''
        priceDollars.value = null
        costDollars.value = null
        lowStockAt.value = null
        error.value = ''
    }
})

function parseList(raw: string): string[] {
    return [...new Set(raw.split(',').map(s => s.trim()).filter(Boolean).map(s => s.toUpperCase()))]
}
const sizeList = computed(() => parseList(sizes.value))
const colorList = computed(() => parseList(colors.value))
const comboCount = computed(() => {
    const s = sizeList.value.length
    const c = colorList.value.length
    return s === 0 && c === 0 ? 0 : Math.max(s, 1) * Math.max(c, 1)
})
const exampleSku = computed(() => {
    const bits = [skuPrefix.value.trim().toUpperCase(), sizeList.value[0], colorList.value[0]].filter(Boolean)
    return bits.join('-')
})

function close() { emit('update:modelValue', false) }

async function generate() {
    if (!props.product) return
    error.value = ''
    busy.value = true
    try {
        const cents = (v: number | null) => v != null && !isNaN(v) ? Math.round(v * 100) : null
        const r = await service.generateVariants(props.product.id, {
            sizes: sizeList.value,
            colors: colorList.value,
            skuPrefix: skuPrefix.value.trim() || null,
            salePriceCents: cents(priceDollars.value),
            costCents: cents(costDollars.value),
            depositCents: 0,
            lowStockThreshold: lowStockAt.value && lowStockAt.value > 0 ? lowStockAt.value : null,
        })
        const d = r.data.data
        emit('flash', `Created ${d.created} variant${d.created === 1 ? '' : 's'}${d.skipped ? ` (${d.skipped} already existed)` : ''}.`, 'success')
        emit('saved')
        close()
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not generate the variants.'
    } finally { busy.value = false }
}
</script>
