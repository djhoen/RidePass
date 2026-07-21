<template>
    <v-dialog :model-value="modelValue" max-width="440" @update:model-value="$emit('update:modelValue', $event)">
        <v-card v-if="variant">
            <v-card-title class="d-flex align-center">
                <span>Adjust stock</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" @click="close"></v-btn>
            </v-card-title>
            <v-card-text>
                <p class="text-body-2 text-medium-emphasis mb-3">
                    On hand now: <strong>{{ variant.stockOnHand }}</strong>. Use a negative number to remove stock
                    (shrinkage, correction) or a positive number to add it.
                </p>
                <v-text-field v-model.number="delta" type="number" label="Adjustment (+/-)" density="compact" hide-details></v-text-field>
                <v-text-field v-model="note" label="Reason" placeholder="e.g. initial count, damaged" density="compact" class="mt-4" hide-details></v-text-field>
                <div v-if="error" class="text-error text-body-2 mt-2">{{ error }}</div>
            </v-card-text>
            <v-card-actions>
                <v-spacer></v-spacer>
                <v-btn :disabled="saving" @click="close">Cancel</v-btn>
                <v-btn color="primary" :loading="saving" @click="save">Apply</v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { BikeShopService, type ShopVariant } from '@/services/BikeShopService'

const props = defineProps<{ modelValue: boolean; variant: ShopVariant | null }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void; (e: 'saved'): void; (e: 'flash', text: string, color?: 'success' | 'error'): void }>()

const service = new BikeShopService()
const saving = ref(false)
const error = ref('')
const delta = ref<number | null>(null)
const note = ref('')

watch(() => props.modelValue, (open) => { if (open) { error.value = ''; delta.value = null; note.value = '' } })

function close() { emit('update:modelValue', false) }

async function save() {
    error.value = ''
    if (!props.variant) return
    if (!delta.value || isNaN(delta.value)) { error.value = 'Enter a non-zero adjustment.'; return }
    if (!note.value.trim()) { error.value = 'A reason is required.'; return }
    saving.value = true
    try {
        await service.adjustStock(props.variant.id, { delta: Math.round(delta.value), note: note.value.trim() })
        emit('flash', 'Stock adjusted.')
        emit('saved')
        close()
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not adjust stock. Please try again.'
    } finally { saving.value = false }
}
</script>
