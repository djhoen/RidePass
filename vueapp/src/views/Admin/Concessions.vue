<template>
    <v-container>
        <div class="d-flex align-center mb-2 flex-wrap ga-3">
            <h1 class="text-h4">Concessions</h1>
            <v-chip v-if="rows.length" size="small" color="grey" variant="tonal">{{ rows.length }}</v-chip>
            <v-spacer></v-spacer>
            <v-btn variant="text" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">Add item</v-btn>
        </div>

        <v-alert v-if="!branding.concessionsEnabled" type="info" variant="tonal" density="compact" class="mb-4">
            Concessions is turned off, so the cashier app won't show these items. You can still set up the catalog now,
            then flip it on in <router-link to="/Admin/Settings/Features">Settings &rarr; Features</router-link>.
        </v-alert>

        <p class="text-caption text-medium-emphasis mb-4" style="max-width: 720px;">
            Food, drink, and swag a cashier rings up in the mobile tap-to-pay app, separate from events. Drag the
            handle to reorder how items appear. Add sizes/colors as variants on a product (e.g. a shirt's S/M/L/XL).
        </p>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 36px"></th>
                        <th>Item</th>
                        <th style="width: 110px">Category</th>
                        <th style="width: 100px">Price</th>
                        <th style="width: 140px">Variants</th>
                        <th style="width: 90px">Status</th>
                        <th style="width: 150px" class="text-right"></th>
                    </tr>
                </thead>
                <draggable tag="tbody" :list="rows" item-key="id" handle=".drag-handle"
                    :animation="180" ghost-class="drag-ghost" @end="onReorderEnd">
                    <template #item="{ element: p }">
                        <tr>
                            <td class="drag-handle-cell">
                                <v-icon class="drag-handle" color="grey">mdi-drag-vertical</v-icon>
                            </td>
                            <td>
                                <div class="d-flex align-center ga-3">
                                    <v-avatar v-if="p.imageUrl" size="36" rounded="lg">
                                        <v-img :src="absoluteUrl(p.imageUrl)"></v-img>
                                    </v-avatar>
                                    <v-icon v-else :icon="categoryIcon(p.category)" color="grey"></v-icon>
                                    <div>{{ p.name }}</div>
                                </div>
                            </td>
                            <td><v-chip size="x-small" variant="tonal">{{ categoryLabel(p.category) }}</v-chip></td>
                            <td>${{ (p.priceCents / 100).toFixed(2) }}</td>
                            <td>
                                <v-btn variant="text" size="small" @click="openVariants(p)">
                                    {{ p.variants.length ? `${p.variants.length} variant${p.variants.length === 1 ? '' : 's'}` : 'Add sizes' }}
                                </v-btn>
                            </td>
                            <td>
                                <v-chip size="x-small" :color="p.isActive ? 'success' : 'grey'" variant="tonal">
                                    {{ p.isActive ? 'Active' : 'Hidden' }}
                                </v-chip>
                            </td>
                            <td class="text-right">
                                <v-btn variant="text" size="small" @click="openEdit(p)">Edit</v-btn>
                                <v-btn variant="text" size="small" color="error" @click="remove(p)">Delete</v-btn>
                            </td>
                        </tr>
                    </template>
                </draggable>
            </v-table>
            <div v-if="!loading && rows.length === 0" class="text-center text-medium-emphasis py-8">
                No items yet. Add your first concession.
            </div>
        </v-card>

        <!-- Add / edit product -->
        <v-dialog v-model="productDialog" max-width="560">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ editing ? 'Edit item' : 'Add item' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="productDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.name" label="Name" density="compact"></v-text-field>
                    <v-row class="mt-0">
                        <v-col cols="7">
                            <v-select v-model="form.category" :items="categoryItems" label="Category"
                                density="compact" class="mt-4"></v-select>
                        </v-col>
                        <v-col cols="5">
                            <v-text-field v-model.number="form.priceDollars" type="number" min="0" step="0.01"
                                prefix="$" label="Price" density="compact" class="mt-4"
                                hint="Base price; variants can override" persistent-hint></v-text-field>
                        </v-col>
                    </v-row>
                    <v-textarea v-model="form.description" label="Description (optional)" rows="2"
                        density="compact" class="mt-4"></v-textarea>

                    <div class="d-flex align-center ga-3 mt-4">
                        <v-avatar v-if="form.imageUrl" size="48" rounded="lg">
                            <v-img :src="absoluteUrl(form.imageUrl)"></v-img>
                        </v-avatar>
                        <v-file-input :model-value="null" label="Image (optional)" accept="image/*"
                            density="compact" prepend-icon="mdi-camera" hide-details :loading="uploading"
                            style="flex: 1" @update:model-value="onImageSelected"></v-file-input>
                        <v-btn v-if="form.imageUrl" icon="mdi-delete" variant="text" size="small"
                            @click="form.imageUrl = null"></v-btn>
                    </div>

                    <v-switch v-model="form.isActive" label="Active" color="primary"
                        density="compact" hide-details class="mt-2"></v-switch>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="saving" @click="productDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="saveProduct">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Variant manager -->
        <v-dialog v-model="variantDialog" max-width="720">
            <v-card v-if="variantProduct">
                <v-card-title class="d-flex align-center">
                    <span>Variants &mdash; {{ variantProduct.name }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="variantDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-caption text-medium-emphasis mb-3">
                        Add a row per size/color. Leave Price blank to use the item's base price, and Stock blank for unlimited.
                    </p>
                    <v-table density="compact">
                        <thead>
                            <tr>
                                <th>Size</th>
                                <th>Color</th>
                                <th style="width: 110px">Price</th>
                                <th style="width: 110px">Stock</th>
                                <th style="width: 80px">Active</th>
                                <th style="width: 120px" class="text-right"></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="(v, i) in variantRows" :key="v.id ?? `new-${i}`">
                                <td><v-text-field v-model="v.size" density="compact" hide-details placeholder="M"></v-text-field></td>
                                <td><v-text-field v-model="v.color" density="compact" hide-details placeholder="Red"></v-text-field></td>
                                <td><v-text-field v-model.number="v.priceDollars" type="number" min="0" step="0.01"
                                    prefix="$" density="compact" hide-details placeholder="base"></v-text-field></td>
                                <td><v-text-field v-model.number="v.inventory" type="number" min="0"
                                    density="compact" hide-details placeholder="∞"></v-text-field></td>
                                <td><v-switch v-model="v.isActive" color="primary" density="compact" hide-details></v-switch></td>
                                <td class="text-right">
                                    <v-btn variant="text" size="small" color="primary"
                                        :loading="savingVariantKey === (v.id ?? `new-${i}`)" @click="saveVariant(v, i)">Save</v-btn>
                                    <v-btn icon="mdi-delete" variant="text" size="small" color="error"
                                        @click="removeVariant(v, i)"></v-btn>
                                </td>
                            </tr>
                            <tr v-if="variantRows.length === 0">
                                <td colspan="6" class="text-center text-medium-emphasis py-4">No variants. Flat item sold at the base price.</td>
                            </tr>
                        </tbody>
                    </v-table>
                    <v-btn variant="tonal" size="small" prepend-icon="mdi-plus" class="mt-2" @click="addVariantRow">Add variant</v-btn>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="variantDialog = false">Done</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import draggable from 'vuedraggable'
import {
    ConcessionService, CONCESSION_CATEGORIES, categoryLabel, categoryIcon,
    type ConcessionProduct, type ConcessionVariant,
} from '@/services/ConcessionService'
import { branding, loadBranding } from '@/stores/branding'
import { useConfirm } from '@/composables/useConfirm'

const service = new ConcessionService()
const confirm = useConfirm()

const rows = ref<ConcessionProduct[]>([])
const loading = ref(false)
const categoryItems = CONCESSION_CATEGORIES.map(c => ({ title: c.label, value: c.value }))

const productDialog = ref(false)
const editing = ref<ConcessionProduct | null>(null)
const saving = ref(false)
const uploading = ref(false)
const form = ref({
    name: '', category: 'food', priceDollars: 0,
    description: '' as string | null, imageUrl: null as string | null, isActive: true,
})

// Variant editor rows carry a dollar field for the price; id null = not yet created.
interface VariantRow {
    id: string | null
    size: string | null
    color: string | null
    priceDollars: number | null
    inventory: number | null
    isActive: boolean
}
const variantDialog = ref(false)
const variantProduct = ref<ConcessionProduct | null>(null)
const variantRows = ref<VariantRow[]>([])
const savingVariantKey = ref<string | null>(null)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(async () => {
    if (!branding.loaded) await loadBranding()
    await load()
})

function absoluteUrl(u: string): string {
    return u.startsWith('http') ? u : `${import.meta.env.VITE_API_ENDPOINT?.replace(/\/api$/, '') ?? ''}${u}`
}

async function load() {
    loading.value = true
    try {
        const r = await service.listForAdmin()
        rows.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load items.', 'error')
    } finally {
        loading.value = false
    }
}

function openCreate() {
    editing.value = null
    form.value = { name: '', category: 'food', priceDollars: 0, description: '', imageUrl: null, isActive: true }
    productDialog.value = true
}

function openEdit(p: ConcessionProduct) {
    editing.value = p
    form.value = {
        name: p.name, category: p.category, priceDollars: p.priceCents / 100,
        description: p.description, imageUrl: p.imageUrl, isActive: p.isActive,
    }
    productDialog.value = true
}

async function onImageSelected(v: File | File[] | null) {
    const file = Array.isArray(v) ? (v[0] ?? null) : v
    if (!file) return
    uploading.value = true
    try {
        const r = await service.uploadImage(file)
        form.value.imageUrl = (r.data as any).data.imageUrl
    } catch (err: any) {
        flash(err.response?.data?.error || 'Image upload failed.', 'error')
    } finally {
        uploading.value = false
    }
}

async function saveProduct() {
    const name = form.value.name.trim()
    if (!name) { flash('Name is required.', 'error'); return }
    saving.value = true
    try {
        const payload = {
            name,
            category: form.value.category,
            priceCents: Math.round((form.value.priceDollars || 0) * 100),
            description: form.value.description?.trim() || null,
            imageUrl: form.value.imageUrl,
            isActive: form.value.isActive,
            sortOrder: editing.value?.sortOrder ?? rows.value.length * 10 + 10,
        }
        if (editing.value) await service.update(editing.value.id, payload)
        else await service.create(payload)
        productDialog.value = false
        flash('Item saved.', 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function remove(p: ConcessionProduct) {
    if (!await confirm({
        title: 'Delete item?',
        message: `Delete "${p.name}"? If it has sales on file, set it inactive instead.`,
        confirmText: 'Delete', confirmColor: 'error',
    })) return
    try {
        await service.remove(p.id)
        flash('Item deleted.', 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    }
}

async function onReorderEnd(evt: { oldIndex?: number; newIndex?: number }) {
    if (evt.oldIndex === evt.newIndex) return
    rows.value.forEach((r, i) => { r.sortOrder = (i + 1) * 10 })
    try {
        await service.reorder(rows.value.map(r => ({ id: r.id, sortOrder: r.sortOrder })))
        flash('Order saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to save order — refreshing.', 'error')
        await load()
    }
}

// ── Variants ──────────────────────────────────────────────────────────────
function openVariants(p: ConcessionProduct) {
    variantProduct.value = p
    variantRows.value = p.variants.map(toVariantRow)
    variantDialog.value = true
}

function toVariantRow(v: ConcessionVariant): VariantRow {
    return {
        id: v.id, size: v.size, color: v.color,
        priceDollars: v.priceCents != null ? v.priceCents / 100 : null,
        inventory: v.inventory, isActive: v.isActive,
    }
}

function addVariantRow() {
    variantRows.value.push({ id: null, size: '', color: '', priceDollars: null, inventory: null, isActive: true })
}

async function saveVariant(v: VariantRow, i: number) {
    if (!variantProduct.value) return
    const key = v.id ?? `new-${i}`
    savingVariantKey.value = key
    try {
        const payload = {
            size: (v.size ?? '').toString().trim() || null,
            color: (v.color ?? '').toString().trim() || null,
            priceCents: v.priceDollars != null && v.priceDollars !== ('' as any) ? Math.round(v.priceDollars * 100) : null,
            imageUrl: null,
            inventory: v.inventory != null && v.inventory !== ('' as any) ? Math.trunc(v.inventory) : null,
            isActive: v.isActive,
            sortOrder: i * 10,
        }
        if (v.id) await service.updateVariant(variantProduct.value.id, v.id, payload)
        else {
            const r = await service.createVariant(variantProduct.value.id, payload)
            v.id = (r.data as any).data.id
        }
        flash('Variant saved.', 'success')
        await load()
        syncVariantProduct()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Variant save failed.', 'error')
    } finally {
        savingVariantKey.value = null
    }
}

async function removeVariant(v: VariantRow, i: number) {
    if (!variantProduct.value) return
    if (!v.id) { variantRows.value.splice(i, 1); return }   // unsaved row
    if (!await confirm({
        title: 'Delete variant?', message: 'Delete this variant? If it has sales, set it inactive instead.',
        confirmText: 'Delete', confirmColor: 'error',
    })) return
    try {
        await service.removeVariant(variantProduct.value.id, v.id)
        variantRows.value.splice(i, 1)
        flash('Variant deleted.', 'success')
        await load()
        syncVariantProduct()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Variant delete failed.', 'error')
    }
}

// Keep the open variant dialog pointed at the freshly-loaded product row.
function syncVariantProduct() {
    if (!variantProduct.value) return
    const fresh = rows.value.find(r => r.id === variantProduct.value!.id)
    if (fresh) variantProduct.value = fresh
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
