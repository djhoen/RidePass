<template>
    <v-dialog :model-value="modelValue" max-width="480" @update:model-value="$emit('update:modelValue', $event)">
        <v-card>
            <v-card-title class="d-flex align-center">
                <span>{{ supplier ? 'Edit supplier' : 'New supplier' }}</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" @click="close"></v-btn>
            </v-card-title>
            <v-card-text>
                <v-text-field v-model="form.name" label="Name" density="compact" hide-details></v-text-field>
                <v-text-field v-model="form.contactName" label="Contact name" density="compact" class="mt-4" hide-details></v-text-field>
                <v-row dense class="mt-2">
                    <v-col cols="6"><v-text-field v-model="form.email" label="Email" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="6"><v-text-field v-model="form.phone" label="Phone" density="compact" hide-details></v-text-field></v-col>
                </v-row>
                <v-text-field v-model="form.notes" label="Notes" density="compact" class="mt-4" hide-details></v-text-field>
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
import { BikeShopService, type ShopSupplier } from '@/services/BikeShopService'

const props = defineProps<{ modelValue: boolean; supplier: ShopSupplier | null }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void; (e: 'saved'): void; (e: 'flash', text: string, color?: 'success' | 'error'): void }>()

const service = new BikeShopService()
const saving = ref(false)
const error = ref('')
const form = ref({ name: '', contactName: '', email: '', phone: '', notes: '', isActive: true })

watch(() => props.modelValue, (open) => {
    if (!open) return
    error.value = ''
    const s = props.supplier
    form.value = { name: s?.name ?? '', contactName: s?.contactName ?? '', email: s?.email ?? '', phone: s?.phone ?? '', notes: s?.notes ?? '', isActive: s?.isActive ?? true }
})

function close() { emit('update:modelValue', false) }

async function save() {
    error.value = ''
    if (!form.value.name.trim()) { error.value = 'Name is required.'; return }
    saving.value = true
    try {
        const body = {
            name: form.value.name.trim(),
            contactName: form.value.contactName.trim() || null,
            email: form.value.email.trim() || null,
            phone: form.value.phone.trim() || null,
            notes: form.value.notes.trim() || null,
            isActive: form.value.isActive,
        }
        if (props.supplier) await service.updateSupplier(props.supplier.id, body)
        else await service.createSupplier(body)
        emit('flash', 'Supplier saved.')
        emit('saved')
        close()
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not save the supplier. Please try again.'
    } finally { saving.value = false }
}
</script>
