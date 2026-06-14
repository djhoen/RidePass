<template>
    <v-container>
        <div class="d-flex align-center mb-6">
            <h1 class="text-h4">Passes</h1>
            <v-spacer></v-spacer>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">Add Pass</v-btn>
        </div>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 36px"></th>
                        <th>Name</th>
                        <th>Description</th>
                        <th style="width: 110px">Price</th>
                        <th style="width: 90px">Active</th>
                        <th style="width: 110px">Waiver</th>
                        <th style="width: 160px" class="text-right"></th>
                    </tr>
                </thead>
                <draggable tag="tbody" :list="visibleRows" item-key="id" handle=".drag-handle"
                    :animation="180" ghost-class="drag-ghost" @end="onReorderEnd">
                    <template #item="{ element: p }">
                        <tr>
                            <td class="drag-handle-cell">
                                <v-icon class="drag-handle" color="grey">mdi-drag-vertical</v-icon>
                            </td>
                            <td>{{ p.name }}</td>
                            <td class="text-medium-emphasis">{{ p.description || '—' }}</td>
                            <td>${{ (p.priceCents / 100).toFixed(2) }}</td>
                            <td>
                                <v-icon v-if="p.isActive" color="success">mdi-check</v-icon>
                                <v-icon v-else color="grey">mdi-close</v-icon>
                            </td>
                            <td>
                                <v-chip v-if="p.requiresWaiver" size="x-small" color="warning">Required</v-chip>
                                <span v-else class="text-caption text-medium-emphasis">Not required</span>
                            </td>
                            <td class="text-right">
                                <v-btn variant="text" size="small" @click="openEdit(p)">Edit</v-btn>
                                <v-btn variant="text" size="small" color="error" @click="remove(p)">Delete</v-btn>
                            </td>
                        </tr>
                    </template>
                </draggable>
                <tbody v-if="!loading && products.length === 0">
                    <tr>
                        <td colspan="7" class="text-center text-medium-emphasis py-8">No products yet.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="dialog" max-width="560">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ editing ? 'Edit Pass' : 'Add Pass' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="dialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.name" label="Name" density="compact"></v-text-field>
                    <v-textarea v-model="form.description" label="Description" rows="2" class="mt-6" density="compact"></v-textarea>
                    <v-row class="mt-2">
                        <v-col cols="12" md="8">
                            <v-text-field v-model.number="form.priceDollars" type="number" step="0.01" min="0.5"
                                label="Price (USD)" density="compact" prefix="$"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="4">
                            <v-switch v-model="form.isActive" label="Active" hide-details></v-switch>
                        </v-col>
                    </v-row>
                    <v-switch v-model="form.requiresWaiver" label="Requires waiver signing" hide-details
                        density="compact" color="primary"></v-switch>
                    <v-text-field v-model.number="form.riderPaidServiceChargePct" type="number" step="1" min="0" max="100"
                        label="Rider pays this % of the service charge" suffix="%" density="compact" class="mt-3"
                        hint="100% = added to rider's bill as a separate line item. 0% = absorbed by you."
                        persistent-hint></v-text-field>
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
import { PassService, type PassProduct } from '@/services/PassService'

const service = new PassService()

const products = ref<PassProduct[]>([])
const { visibleRows, onReorderEnd } = useDragReorder<PassProduct>({
    rows: products,
    save: items => service.reorderProducts(items),
    onSuccess: () => flash('Order saved.', 'success'),
    onError: async err => {
        flash((err as any)?.response?.data?.error || 'Failed to save order — refreshing.', 'error')
        await load()
    },
})
const loading = ref(false)
const dialog = ref(false)
const editing = ref<PassProduct | null>(null)
const saving = ref(false)

const form = ref({
    name: '',
    description: '' as string | null,
    priceDollars: 30,
    isActive: true,
    sortOrder: 100,
    requiresWaiver: true,
    riderPaidServiceChargePct: 100,
})

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(load)

async function load() {
    loading.value = true
    try {
        const r = await service.listForAdmin()
        products.value = (r.data as any).data
    } finally {
        loading.value = false
    }
}

function openCreate() {
    editing.value = null
    form.value = { name: '', description: '', priceDollars: 30, isActive: true, sortOrder: 100, requiresWaiver: true, riderPaidServiceChargePct: 100 }
    dialog.value = true
}

function openEdit(p: PassProduct) {
    editing.value = p
    form.value = {
        name: p.name,
        description: p.description ?? '',
        priceDollars: p.priceCents / 100,
        isActive: p.isActive,
        sortOrder: p.sortOrder,
        requiresWaiver: p.requiresWaiver,
        riderPaidServiceChargePct: p.riderPaidServiceChargeBps / 100,
    }
    dialog.value = true
}

async function save() {
    try {
        saving.value = true
        const body = {
            name: form.value.name.trim(),
            description: form.value.description && form.value.description.trim().length > 0 ? form.value.description : null,
            priceCents: Math.round(form.value.priceDollars * 100),
            isActive: form.value.isActive,
            sortOrder: form.value.sortOrder,
            requiresWaiver: form.value.requiresWaiver,
            riderPaidServiceChargeBps: Math.round((form.value.riderPaidServiceChargePct || 0) * 100),
        }
        if (editing.value) {
            await service.updateProduct(editing.value.id, body)
        } else {
            await service.createProduct(body)
        }
        dialog.value = false
        await load()
        flash('Product saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function remove(p: PassProduct) {
    if (!confirm(`Delete "${p.name}"?`)) return
    try {
        await service.deleteProduct(p.id)
        await load()
        flash('Product deleted.', 'success')
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
.drag-handle-cell { padding-left: 4px !important; padding-right: 0 !important; }
.drag-handle { cursor: grab; }
.drag-handle:active { cursor: grabbing; }
.drag-ghost { opacity: 0.35; background: rgba(25, 118, 210, 0.08); }
</style>
