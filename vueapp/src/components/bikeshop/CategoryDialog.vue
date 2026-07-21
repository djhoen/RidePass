<template>
    <v-dialog :model-value="modelValue" max-width="440" @update:model-value="$emit('update:modelValue', $event)">
        <v-card>
            <v-card-title class="d-flex align-center">
                <span>{{ category ? 'Edit category' : 'New category' }}</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" @click="close"></v-btn>
            </v-card-title>
            <v-card-text>
                <v-text-field v-model="name" label="Name" density="compact" hide-details></v-text-field>
                <v-switch v-model="isActive" label="Active" color="primary" hide-details density="compact" class="mt-2"></v-switch>
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
import { BikeShopService, type ShopCategory } from '@/services/BikeShopService'

const props = defineProps<{ modelValue: boolean; category: ShopCategory | null }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void; (e: 'saved'): void; (e: 'flash', text: string, color?: 'success' | 'error'): void }>()

const service = new BikeShopService()
const saving = ref(false)
const error = ref('')
const name = ref('')
const isActive = ref(true)

watch(() => props.modelValue, (open) => {
    if (!open) return
    error.value = ''
    name.value = props.category?.name ?? ''
    isActive.value = props.category?.isActive ?? true
})

function close() { emit('update:modelValue', false) }

async function save() {
    error.value = ''
    if (!name.value.trim()) { error.value = 'Name is required.'; return }
    saving.value = true
    try {
        const body = { name: name.value.trim(), parentId: null, sortOrder: props.category?.sortOrder ?? 100, isActive: isActive.value }
        if (props.category) await service.updateCategory(props.category.id, body)
        else await service.createCategory(body)
        emit('flash', 'Category saved.')
        emit('saved')
        close()
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not save the category. Please try again.'
    } finally { saving.value = false }
}
</script>
