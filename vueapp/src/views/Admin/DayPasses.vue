<template>
    <v-container>
        <div class="d-flex align-center mb-6">
            <h1 class="text-h4">Day Pass Products</h1>
            <v-spacer></v-spacer>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">Add Product</v-btn>
        </div>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Description</th>
                        <th style="width: 110px">Price</th>
                        <th style="width: 90px">Active</th>
                        <th style="width: 160px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="p in products" :key="p.id">
                        <td>{{ p.name }}</td>
                        <td class="text-medium-emphasis">{{ p.description || '—' }}</td>
                        <td>${{ (p.priceCents / 100).toFixed(2) }}</td>
                        <td>
                            <v-icon v-if="p.isActive" color="success">mdi-check</v-icon>
                            <v-icon v-else color="grey">mdi-close</v-icon>
                        </td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" @click="openEdit(p)">Edit</v-btn>
                            <v-btn variant="text" size="small" color="error" @click="remove(p)">Delete</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && products.length === 0">
                        <td colspan="5" class="text-center text-medium-emphasis py-8">No products yet.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="dialog" max-width="560">
            <v-card>
                <v-card-title>{{ editing ? 'Edit Product' : 'Add Product' }}</v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.name" label="Name" density="compact"></v-text-field>
                    <v-textarea v-model="form.description" label="Description" rows="2" density="compact"></v-textarea>
                    <v-row>
                        <v-col cols="12" md="8">
                            <v-text-field v-model.number="form.priceDollars" type="number" step="0.01" min="0.5"
                                label="Price (USD)" density="compact" prefix="$"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="4">
                            <v-switch v-model="form.isActive" label="Active" hide-details></v-switch>
                        </v-col>
                    </v-row>
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
import { DayPassService, type DayPassProduct } from '@/services/DayPassService'

const service = new DayPassService()

const products = ref<DayPassProduct[]>([])
const loading = ref(false)
const dialog = ref(false)
const editing = ref<DayPassProduct | null>(null)
const saving = ref(false)

const form = ref({
    name: '',
    description: '' as string | null,
    priceDollars: 30,
    isActive: true,
    sortOrder: 100,
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
    form.value = { name: '', description: '', priceDollars: 30, isActive: true, sortOrder: 100 }
    dialog.value = true
}

function openEdit(p: DayPassProduct) {
    editing.value = p
    form.value = {
        name: p.name,
        description: p.description ?? '',
        priceDollars: p.priceCents / 100,
        isActive: p.isActive,
        sortOrder: p.sortOrder,
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

async function remove(p: DayPassProduct) {
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
