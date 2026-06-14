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
                        <th style="width: 36px"></th>
                        <th style="width: 80px">Preview</th>
                        <th>Name</th>
                        <th style="width: 120px">Code</th>
                        <th style="width: 110px">Kind</th>
                        <th style="width: 180px" class="text-right"></th>
                    </tr>
                </thead>
                <draggable tag="tbody" :list="visibleRows" item-key="id" handle=".drag-handle"
                    :animation="180" ghost-class="drag-ghost" @end="onReorderEnd">
                    <template #item="{ element: row }">
                        <tr>
                            <td class="drag-handle-cell">
                                <v-icon class="drag-handle" color="grey">mdi-drag-vertical</v-icon>
                            </td>
                            <td>
                                <div v-if="row.imageUrl" class="preview-img" :style="{ backgroundImage: `url(${absoluteUrl(row.imageUrl)})` }"></div>
                                <span v-else class="color-swatch" :style="{ backgroundColor: row.color }"></span>
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
                    </template>
                </draggable>
                <tbody v-if="!loading && rows.length === 0">
                    <tr>
                        <td colspan="6" class="text-center text-medium-emphasis py-8">No event types.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="dialog" max-width="500">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ editing ? 'Edit Event Type' : 'Add Custom Event Type' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="dialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.name" label="Name" density="compact" :rules="[required]"></v-text-field>
                    <label class="text-subtitle-2 d-block mb-2 mt-2">Default cover photo (optional)</label>
                    <p class="text-caption text-medium-emphasis mb-2">
                        Used as the card background on the home page when the individual event has no image.
                        If no photo, the color below fills the card.
                    </p>
                    <div class="d-flex align-center ga-3 mb-3">
                        <div v-if="form.imageUrl" class="image-preview"
                            :style="{ backgroundImage: `url(${absoluteUrl(form.imageUrl)})` }"></div>
                        <div v-else class="image-preview empty" :style="{ backgroundColor: form.color }">
                            <v-icon color="white">mdi-image-off-outline</v-icon>
                        </div>
                        <div class="d-flex flex-column ga-1">
                            <v-file-input v-model="uploadFile" label="Upload" accept="image/*"
                                density="compact" prepend-icon="mdi-upload" :loading="uploading"
                                @update:model-value="onImageSelected"></v-file-input>
                            <v-btn v-if="form.imageUrl" size="small" variant="text" color="error"
                                prepend-icon="mdi-delete" @click="form.imageUrl = null">Remove image</v-btn>
                        </div>
                    </div>
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
import draggable from 'vuedraggable'
import { useDragReorder } from '@/composables/useDragReorder'
import { EventTypeService, type EventType } from '@/services/EventTypeService'

const service = new EventTypeService()

const rows = ref<EventType[]>([])
const { visibleRows, onReorderEnd } = useDragReorder<EventType>({
    rows,
    save: items => service.reorder(items),
    onSuccess: () => flash('Order saved.', 'success'),
    onError: async err => {
        flash((err as any)?.response?.data?.error || 'Failed to save order — refreshing.', 'error')
        await load()
    },
})
const loading = ref(false)
const dialog = ref(false)
const editing = ref<EventType | null>(null)
const saving = ref(false)
const uploading = ref(false)
const uploadFile = ref<File | File[] | null>(null)
const form = ref<{ name: string; color: string; imageUrl: string | null; sortOrder: number }>(
    { name: '', color: '#1976D2', imageUrl: null, sortOrder: 100 }
)

const apiUrl: string = (import.meta as any).env?.VITE_API_ENDPOINT ?? ''
function apiOrigin(): string {
    try { return new URL(apiUrl, window.location.origin).origin } catch { return '' }
}
function absoluteUrl(url: string | null | undefined): string {
    if (!url) return ''
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}

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
    form.value = { name: '', color: '#1976D2', imageUrl: null, sortOrder: 100 }
    uploadFile.value = null
    dialog.value = true
}

function openEdit(row: EventType) {
    editing.value = row
    form.value = { name: row.name, color: row.color, imageUrl: row.imageUrl, sortOrder: row.sortOrder }
    uploadFile.value = null
    dialog.value = true
}

async function onImageSelected(v: File | File[] | null) {
    const f = Array.isArray(v) ? (v[0] ?? null) : v
    if (!f) return
    try {
        uploading.value = true
        const r = await service.uploadImage(f)
        form.value.imageUrl = r.data.data.imageUrl
    } catch (err: any) {
        flash(err.response?.data?.error || 'Upload failed.', 'error')
    } finally {
        uploading.value = false
        uploadFile.value = null
    }
}

async function save() {
    if (!form.value.name.trim()) return
    try {
        saving.value = true
        const body = {
            name: form.value.name.trim(),
            color: normalizeHex(form.value.color),
            imageUrl: form.value.imageUrl,
            sortOrder: form.value.sortOrder,
        }
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
.preview-img {
    width: 56px;
    height: 36px;
    border-radius: 4px;
    background-size: cover;
    background-position: center;
    border: 1px solid rgba(0, 0, 0, 0.1);
}
.image-preview {
    width: 100px;
    height: 70px;
    border-radius: 6px;
    background-size: cover;
    background-position: center;
    border: 1px solid rgba(0, 0, 0, 0.1);
    display: flex;
    align-items: center;
    justify-content: center;
}
.image-preview.empty {
    color: white;
}
.drag-handle-cell { padding-left: 4px !important; padding-right: 0 !important; }
.drag-handle { cursor: grab; }
.drag-handle:active { cursor: grabbing; }
.drag-ghost { opacity: 0.35; background: rgba(25, 118, 210, 0.08); }
</style>
