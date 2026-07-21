<template>
    <v-dialog :model-value="modelValue" max-width="600" @update:model-value="$emit('update:modelValue', $event)">
        <v-card v-if="variant">
            <v-card-title class="d-flex align-center">
                <span>Units</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" @click="close"></v-btn>
            </v-card-title>
            <v-card-text>
                <v-table density="compact">
                    <thead>
                        <tr><th>Label</th><th>Serial</th><th>Status</th><th></th></tr>
                    </thead>
                    <tbody>
                        <tr v-for="i in items" :key="i.id">
                            <td>{{ i.label }}</td>
                            <td class="text-caption">{{ i.serial || '—' }}</td>
                            <td><v-chip size="x-small" :color="statusColor(i.status)">{{ i.status }}</v-chip></td>
                            <td class="text-right"><v-btn size="x-small" variant="text" icon="mdi-pencil" @click="edit(i)"></v-btn></td>
                        </tr>
                        <tr v-if="items.length === 0"><td colspan="4" class="text-center text-medium-emphasis py-3">No units yet.</td></tr>
                    </tbody>
                </v-table>

                <v-divider class="my-3"></v-divider>
                <div class="text-subtitle-2 mb-2">{{ editingId ? 'Edit unit' : 'Add unit' }}</div>
                <v-row dense>
                    <v-col cols="6"><v-text-field v-model="form.label" label="Label" placeholder="Bike #3" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="6"><v-text-field v-model="form.serial" label="Serial" density="compact" hide-details></v-text-field></v-col>
                </v-row>
                <v-select v-if="editingId" v-model="form.status"
                    :items="['available', 'maintenance', 'retired']" label="Status" density="compact" class="mt-4" hide-details></v-select>
                <v-text-field v-model="form.notes" label="Notes" density="compact" class="mt-4" hide-details></v-text-field>
                <div v-if="error" class="text-error text-body-2 mt-2">{{ error }}</div>
                <div class="d-flex ga-2 mt-3">
                    <v-btn v-if="editingId" size="small" variant="text" @click="resetForm">Cancel edit</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn size="small" color="primary" :loading="saving" @click="save">{{ editingId ? 'Save unit' : 'Add unit' }}</v-btn>
                </div>
            </v-card-text>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { BikeShopService, type ShopVariant, type ShopItem, type ShopItemStatus } from '@/services/BikeShopService'

const props = defineProps<{ modelValue: boolean; variant: ShopVariant | null }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void; (e: 'changed'): void; (e: 'flash', text: string, color?: 'success' | 'error'): void }>()

const service = new BikeShopService()
const items = ref<ShopItem[]>([])
const saving = ref(false)
const error = ref('')
const editingId = ref<string | null>(null)
const form = ref<{ label: string; serial: string; notes: string; status: ShopItemStatus }>({ label: '', serial: '', notes: '', status: 'available' })

function statusColor(s: string) {
    return s === 'available' ? 'success' : s === 'sold' ? 'blue-grey' : s === 'rented_out' ? 'indigo' : 'warning'
}
function resetForm() { editingId.value = null; form.value = { label: '', serial: '', notes: '', status: 'available' }; error.value = '' }
function edit(i: ShopItem) { editingId.value = i.id; form.value = { label: i.label, serial: i.serial ?? '', notes: i.notes ?? '', status: i.status } }

watch(() => props.modelValue, async (open) => { if (open) { resetForm(); await load() } })

async function load() {
    if (!props.variant) return
    // Clear first: the dialog is reused across variants, so a failed load must never leave the
    // previous variant's units on screen (editing them would mutate the wrong variant's stock).
    items.value = []
    try { items.value = (await service.listItems(props.variant.id)).data.data }
    catch (e: any) { emit('flash', e.response?.data?.error || 'Could not load units.', 'error') }
}

function close() { emit('update:modelValue', false) }

async function save() {
    error.value = ''
    if (!props.variant) return
    if (!form.value.label.trim()) { error.value = 'A label is required.'; return }
    saving.value = true
    try {
        const body = { label: form.value.label.trim(), serial: form.value.serial.trim() || null, notes: form.value.notes.trim() || null, status: form.value.status }
        if (editingId.value) await service.updateItem(editingId.value, body)
        else await service.createItem(props.variant.id, body)
        emit('flash', 'Unit saved.')
        emit('changed')
        resetForm()
        await load()
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not save the unit. Please try again.'
    } finally { saving.value = false }
}
</script>
