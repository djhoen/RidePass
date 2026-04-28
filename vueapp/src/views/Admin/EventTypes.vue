<template>
    <v-container>
        <div class="d-flex align-center mb-6">
            <h1 class="text-h4">Event Types</h1>
            <v-spacer></v-spacer>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">Add Custom Type</v-btn>
        </div>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 60px">Color</th>
                        <th>Name</th>
                        <th style="width: 120px">Code</th>
                        <th style="width: 110px">Kind</th>
                        <th style="width: 180px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="row in rows" :key="row.id">
                        <td>
                            <span class="color-swatch" :style="{ backgroundColor: row.color }"></span>
                        </td>
                        <td>{{ row.name }}</td>
                        <td><code>{{ row.code }}</code></td>
                        <td>
                            <v-chip v-if="row.isSystem" size="small" color="primary">system</v-chip>
                            <v-chip v-else size="small">custom</v-chip>
                        </td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" @click="openEdit(row)">Edit</v-btn>
                            <v-btn v-if="!row.isSystem" variant="text" size="small" color="error" @click="remove(row)">Delete</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && rows.length === 0">
                        <td colspan="5" class="text-center text-medium-emphasis py-8">No event types.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="dialog" max-width="500">
            <v-card>
                <v-card-title>{{ editing ? 'Edit Event Type' : 'Add Custom Event Type' }}</v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.name" label="Name" density="compact" :rules="[required]"></v-text-field>
                    <label class="text-subtitle-2 d-block mb-2 mt-2">Color</label>
                    <v-color-picker v-model="form.color" :modes="['hex']" mode="hex" hide-inputs hide-canvas-actions
                        show-swatches swatches-max-height="100"></v-color-picker>
                    <v-text-field v-model="form.color" label="Color hex" density="compact" class="mt-2" hide-details></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="dialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="save">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { EventTypeService, type EventType } from '@/services/EventTypeService'

const service = new EventTypeService()

const rows = ref<EventType[]>([])
const loading = ref(false)
const dialog = ref(false)
const editing = ref<EventType | null>(null)
const saving = ref(false)
const form = ref({ name: '', color: '#1976D2', sortOrder: 100 })

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const required = (v: string) => !!v || 'Required'

function normalizeHex(hex: string): string {
    const v = (hex || '').trim()
    if (/^#[0-9A-Fa-f]{6}$/.test(v)) return v.toUpperCase()
    if (/^#[0-9A-Fa-f]{8}$/.test(v)) return v.substring(0, 7).toUpperCase()
    return v
}

onMounted(load)

async function load() {
    loading.value = true
    try {
        const r = await service.list()
        rows.value = (r.data as any).data
    } finally {
        loading.value = false
    }
}

function openCreate() {
    editing.value = null
    form.value = { name: '', color: '#1976D2', sortOrder: 100 }
    dialog.value = true
}

function openEdit(row: EventType) {
    editing.value = row
    form.value = { name: row.name, color: row.color, sortOrder: row.sortOrder }
    dialog.value = true
}

async function save() {
    if (!form.value.name.trim()) return
    try {
        saving.value = true
        const body = { name: form.value.name.trim(), color: normalizeHex(form.value.color), sortOrder: form.value.sortOrder }
        if (editing.value) {
            await service.update(editing.value.id, body)
        } else {
            await service.create(body)
        }
        dialog.value = false
        await load()
        flash('Event type saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function remove(row: EventType) {
    if (!confirm(`Delete "${row.name}"?`)) return
    try {
        await service.delete(row.id)
        await load()
        flash('Event type deleted.', 'success')
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

<style scoped>
.color-swatch {
    display: inline-block;
    width: 28px;
    height: 28px;
    border-radius: 4px;
    border: 1px solid rgba(0, 0, 0, 0.1);
}
</style>
