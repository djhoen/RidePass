<template>
    <v-dialog :model-value="modelValue" max-width="440" @update:model-value="$emit('update:modelValue', $event)">
        <v-card>
            <v-card-title class="d-flex align-center">
                <span>{{ tax ? 'Edit tax category' : 'New tax category' }}</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" @click="close"></v-btn>
            </v-card-title>
            <v-card-text>
                <v-text-field v-model="name" label="Name" placeholder="Standard" density="compact" hide-details></v-text-field>
                <v-text-field v-model.number="ratePercent" type="number" min="0" max="100" step="0.01"
                    label="Rate" suffix="%" density="compact" class="mt-4" hide-details></v-text-field>
                <v-switch v-model="isDefault" label="Default (applied when a product has no category)"
                    color="primary" hide-details density="compact" class="mt-2"></v-switch>
                <v-switch v-model="isActive" label="Active" color="primary" hide-details density="compact"></v-switch>
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
import { BikeShopService, type ShopTaxCategory } from '@/services/BikeShopService'

const props = defineProps<{ modelValue: boolean; tax: ShopTaxCategory | null }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void; (e: 'saved'): void; (e: 'flash', text: string, color?: 'success' | 'error'): void }>()

const service = new BikeShopService()
const saving = ref(false)
const error = ref('')
const name = ref('')
const ratePercent = ref<number | null>(0)
const isDefault = ref(false)
const isActive = ref(true)

watch(() => props.modelValue, (open) => {
    if (!open) return
    error.value = ''
    name.value = props.tax?.name ?? ''
    ratePercent.value = props.tax ? props.tax.rateBps / 100 : 0
    isDefault.value = props.tax?.isDefault ?? false
    isActive.value = props.tax?.isActive ?? true
})

function close() { emit('update:modelValue', false) }

async function save() {
    error.value = ''
    if (!name.value.trim()) { error.value = 'Name is required.'; return }
    const pct = ratePercent.value
    if (pct == null || isNaN(pct) || pct < 0 || pct > 100) { error.value = 'Enter a rate between 0 and 100.'; return }
    saving.value = true
    try {
        const body = { name: name.value.trim(), rateBps: Math.round(pct * 100), isDefault: isDefault.value, sortOrder: props.tax?.sortOrder ?? 0, isActive: isActive.value }
        if (props.tax) await service.updateTaxCategory(props.tax.id, body)
        else await service.createTaxCategory(body)
        emit('flash', 'Tax category saved.')
        emit('saved')
        close()
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not save the tax category. Please try again.'
    } finally { saving.value = false }
}
</script>
