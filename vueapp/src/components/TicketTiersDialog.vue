<template>
    <v-dialog :model-value="modelValue" @update:model-value="$emit('update:modelValue', $event)" max-width="720">
        <v-card>
            <v-card-title>
                Ticket Tiers
                <span v-if="eventTitle" class="text-body-2 text-medium-emphasis ml-2">— {{ eventTitle }}</span>
            </v-card-title>
            <v-card-text>
                <v-table>
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
                        <tr v-for="t in tiers" :key="t.id">
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
                        <tr v-if="!loading && tiers.length === 0">
                            <td colspan="6" class="text-center text-medium-emphasis py-4">No tiers yet.</td>
                        </tr>
                    </tbody>
                </v-table>
                <v-btn color="primary" class="mt-3" prepend-icon="mdi-plus" @click="openCreate">Add Tier</v-btn>
            </v-card-text>
            <v-card-actions>
                <v-spacer></v-spacer>
                <v-btn @click="close">Close</v-btn>
            </v-card-actions>
        </v-card>

        <v-dialog v-model="tierDialog" max-width="480">
            <v-card>
                <v-card-title>{{ editing ? 'Edit Tier' : 'Add Tier' }}</v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.name" label="Name" density="compact"></v-text-field>
                    <v-row>
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
import { ref, watch } from 'vue'
import { TicketService, type TicketTier } from '@/services/TicketService'

const props = defineProps<{ modelValue: boolean; eventId: string | null; eventTitle?: string }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void }>()

const service = new TicketService()

const tiers = ref<TicketTier[]>([])
const loading = ref(false)
const tierDialog = ref(false)
const editing = ref<TicketTier | null>(null)
const saving = ref(false)

const form = ref({
    name: '',
    priceDollars: 20,
    inventory: null as number | null,
    sortOrder: 100,
    isActive: true,
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
    form.value = { name: '', priceDollars: 20, inventory: null, sortOrder: 100, isActive: true }
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
    }
    tierDialog.value = true
}

async function save() {
    if (!props.eventId || !form.value.name.trim()) return
    try {
        saving.value = true
        const body = {
            name: form.value.name.trim(),
            priceCents: Math.round(form.value.priceDollars * 100),
            inventory: form.value.inventory || null,
            sortOrder: form.value.sortOrder,
            isActive: form.value.isActive,
        }
        if (editing.value) {
            await service.updateTier(props.eventId, editing.value.id, body)
        } else {
            await service.createTier(props.eventId, body)
        }
        tierDialog.value = false
        await load()
        flash('Tier saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function remove(t: TicketTier) {
    if (!props.eventId) return
    if (!confirm(`Delete tier "${t.name}"?`)) return
    try {
        await service.deleteTier(props.eventId, t.id)
        await load()
        flash('Tier deleted.', 'success')
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
