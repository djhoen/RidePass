<template>
    <v-container>
        <div class="d-flex align-center mb-4 flex-wrap ga-3">
            <h1 class="text-h4">Add-ons</h1>
            <v-spacer></v-spacer>
            <v-switch v-model="showInactive" color="primary" density="compact" hide-details
                label="Show inactive / expired" style="flex: 0 0 auto"></v-switch>
            <v-btn variant="text" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">Add Product</v-btn>
        </div>

        <v-alert v-if="!branding.extrasEnabled" type="info" variant="tonal" class="mb-4">
            Add-ons are turned off for this tenant. Enable them on
            <router-link to="/Admin/Settings/Features">Settings → Features</router-link> before riders can buy.
        </v-alert>

        <p class="text-caption text-medium-emphasis mb-4">
            Define add-on products once here, then on each event check which products are offered (with
            optional per-event inventory). Change the display order by dragging/dropping using the icon on the left.
        </p>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 36px"></th>
                        <th style="width: 72px"></th>
                        <th>Name</th>
                        <th style="width: 130px">Type</th>
                        <th style="width: 110px">Price</th>
                        <th style="width: 130px">Variants</th>
                        <th style="width: 130px">Expires</th>
                        <th style="width: 110px">Waiver</th>
                        <th style="width: 90px">Active</th>
                        <th style="width: 90px" class="text-right"></th>
                    </tr>
                </thead>
                <draggable tag="tbody" :list="visibleRows" item-key="id" handle=".drag-handle"
                    :animation="180" ghost-class="drag-ghost"
                    @end="onReorderEnd">
                    <template #item="{ element: row }">
                        <tr>
                            <td class="drag-handle-cell">
                                <v-icon class="drag-handle" color="grey">mdi-drag-vertical</v-icon>
                            </td>
                            <td>
                                <v-img v-if="row.imageUrl" :src="absoluteUrl(row.imageUrl)" width="48" height="48"
                                    cover class="rounded" style="border: 1px solid rgba(0,0,0,0.1)"></v-img>
                                <div v-else class="d-flex align-center justify-center rounded"
                                    style="width: 48px; height: 48px; background: rgba(0,0,0,0.05)">
                                    <v-icon size="small" color="grey">mdi-image-off-outline</v-icon>
                                </div>
                            </td>
                            <td>
                                <strong>{{ row.name }}</strong>
                                <div v-if="row.description" class="text-caption text-medium-emphasis">{{ row.description }}</div>
                            </td>
                            <td>
                                <v-chip size="small" :prepend-icon="kindIcon(row.kind)">{{ kindLabel(row.kind) }}</v-chip>
                            </td>
                            <td>${{ (row.priceCents / 100).toFixed(2) }}</td>
                            <td>
                                <v-btn size="small" variant="text" prepend-icon="mdi-tshirt-crew"
                                    @click="openVariants(row)">
                                    {{ row.variants.length === 0 ? 'Add' : `${row.variants.length}` }}
                                </v-btn>
                            </td>
                            <td>
                                <span v-if="!row.expiresAt" class="text-medium-emphasis">—</span>
                                <span v-else :class="isExpired(row) ? 'text-error' : 'text-medium-emphasis'">
                                    <v-icon v-if="isExpired(row)" size="small" class="mr-1">mdi-clock-alert-outline</v-icon>
                                    {{ formatExpires(row.expiresAt) }}
                                </span>
                            </td>
                            <td>
                                <v-icon v-if="row.requiresWaiver" color="warning">mdi-file-sign</v-icon>
                                <span v-else class="text-medium-emphasis">—</span>
                            </td>
                            <td>
                                <v-icon v-if="row.isActive" color="success">mdi-check</v-icon>
                                <v-icon v-else color="grey">mdi-close</v-icon>
                            </td>
                            <td class="text-right">
                                <v-btn variant="text" size="small" @click="openEdit(row)">Edit</v-btn>
                            </td>
                        </tr>
                    </template>
                </draggable>
                <tbody v-if="!loading && visibleRows.length === 0">
                    <tr>
                        <td colspan="10" class="text-center text-medium-emphasis py-8">
                            <span v-if="rows.length === 0">No add-on products yet. Click "Add Product" to create one.</span>
                            <span v-else>Nothing currently sellable. Toggle "Show inactive / expired" to see all.</span>
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- ── Variants editor ────────────────────────────────────────────── -->
        <v-dialog v-model="variantsDialog" max-width="1400" width="95vw" scrollable>
            <v-card v-if="variantsProduct">
                <v-card-title>Variants — {{ variantsProduct.name }}</v-card-title>
                <v-card-subtitle>
                    Each row is a buyable SKU. Leave any column blank to skip that attribute.
                    Price overrides the product price; image overrides the product image.
                </v-card-subtitle>
                <v-card-text>
                    <v-table density="compact">
                        <thead>
                            <tr>
                                <th style="width: 80px">Image</th>
                                <th style="width: 100px">Size</th>
                                <th style="width: 110px">Color</th>
                                <th style="width: 110px">Gender</th>
                                <th style="width: 110px">Tier</th>
                                <th style="min-width: 180px">Description</th>
                                <th style="width: 100px">Price</th>
                                <th style="width: 100px">Inventory</th>
                                <th style="width: 100px">SKU</th>
                                <th style="width: 80px">Active</th>
                                <th style="width: 90px"></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="v in editingVariants" :key="v.tempKey" class="variant-row">
                                <td>
                                    <div class="d-flex align-center">
                                        <v-img v-if="v.imageUrl" :src="absoluteUrl(v.imageUrl)" width="48" height="48" cover
                                            class="rounded variant-thumb" @click="triggerVariantImage(v)"></v-img>
                                        <v-btn v-else size="small" variant="tonal" icon="mdi-upload"
                                            :loading="variantImageUploading[v.tempKey]"
                                            @click="triggerVariantImage(v)"></v-btn>
                                    </div>
                                </td>
                                <td><v-text-field v-model="v.size" density="compact" hide-details placeholder="—" maxlength="40"></v-text-field></td>
                                <td><v-text-field v-model="v.color" density="compact" hide-details placeholder="—" maxlength="40"></v-text-field></td>
                                <td>
                                    <v-combobox v-model="v.gender" density="compact" hide-details
                                        :items="['mens','womens','unisex','youth']" placeholder="—"></v-combobox>
                                </td>
                                <td><v-text-field v-model="v.tier" density="compact" hide-details placeholder="—" maxlength="60"></v-text-field></td>
                                <td><v-text-field v-model="v.description" density="compact" hide-details placeholder="—" maxlength="500"></v-text-field></td>
                                <td>
                                    <v-text-field v-model.number="v.priceDollars" density="compact" hide-details
                                        type="number" min="0" step="0.01" prefix="$"
                                        :placeholder="`${(variantsProduct.priceCents / 100).toFixed(2)}`"></v-text-field>
                                </td>
                                <td>
                                    <v-text-field v-model.number="v.inventory" density="compact" hide-details
                                        type="number" min="0" placeholder="∞"></v-text-field>
                                </td>
                                <td><v-text-field v-model="v.sku" density="compact" hide-details placeholder="—" maxlength="80"></v-text-field></td>
                                <td><v-switch v-model="v.isActive" color="primary" density="compact" hide-details></v-switch></td>
                                <td>
                                    <v-btn icon="mdi-delete" variant="text" size="small"
                                        @click="removeVariantRow(v)"></v-btn>
                                </td>
                            </tr>
                            <tr v-if="editingVariants.length === 0">
                                <td colspan="11" class="text-center text-medium-emphasis py-4">
                                    No variants yet. Click "Add Variant" to create one.
                                </td>
                            </tr>
                        </tbody>
                    </v-table>
                    <div class="d-flex align-center mt-3 ga-2">
                        <v-btn variant="text" prepend-icon="mdi-plus" @click="addVariantRow">Add Variant</v-btn>
                        <v-spacer></v-spacer>
                        <span v-if="variantSaveError" class="text-error text-caption">{{ variantSaveError }}</span>
                    </div>
                    <!-- Single hidden file input shared across all variant rows; the row clicked
                         when the picker opens is captured in `variantImageTarget`. -->
                    <input ref="variantImageInput" type="file" accept="image/png,image/jpeg,image/webp"
                        style="display: none" @change="onVariantImageChange" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="variantsDialog = false">Close</v-btn>
                    <v-btn color="primary" :loading="variantSaving" @click="saveVariants">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="dialog" max-width="640" scrollable>
            <v-card>
                <v-card-title>{{ editing ? 'Edit Add-on' : 'Add Product' }}</v-card-title>
                <v-card-text>
                    <v-row>
                        <v-col cols="12" md="8">
                            <v-text-field v-model="form.name" label="Name" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="4">
                            <v-text-field v-model.number="form.sortOrder" type="number"
                                label="Sort order" density="compact"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-textarea v-model="form.description" label="Description (optional)" rows="2"
                        density="compact" class="mt-6"></v-textarea>

                    <div class="text-subtitle-2 mt-4 mb-2">Image</div>
                    <div class="d-flex align-center ga-3 flex-wrap mb-2">
                        <v-img v-if="form.imageUrl" :src="absoluteUrl(form.imageUrl)" width="96" height="96" cover
                            class="rounded" style="flex: 0 0 auto; border: 1px solid rgba(0,0,0,0.1)"></v-img>
                        <div v-else class="d-flex align-center justify-center rounded"
                            style="width: 96px; height: 96px; background: rgba(0,0,0,0.05); flex: 0 0 auto">
                            <v-icon color="grey">mdi-image-off-outline</v-icon>
                        </div>
                        <div class="d-flex align-center ga-2 flex-wrap">
                            <v-btn variant="tonal" prepend-icon="mdi-upload" :loading="imageUploading"
                                @click="productImageInput?.click()">
                                {{ form.imageUrl ? 'Replace' : 'Upload' }}
                            </v-btn>
                            <v-btn v-if="form.imageUrl" variant="text" size="small" @click="form.imageUrl = null">
                                Remove
                            </v-btn>
                            <input ref="productImageInput" type="file" accept="image/png,image/jpeg,image/webp"
                                style="display: none" @change="onProductImageChange" />
                        </div>
                    </div>
                    <div class="text-caption text-medium-emphasis mb-2">
                        PNG, JPEG, or WebP up to 5 MB. Variants without their own image fall back to this one.
                    </div>

                    <div class="text-subtitle-2 mt-4 mb-2">Type</div>
                    <div class="d-flex flex-wrap ga-2 mb-2">
                        <v-chip v-for="k in DEFAULT_EXTRA_KINDS" :key="k.value"
                            :variant="form.kind === k.value ? 'flat' : 'outlined'"
                            :color="form.kind === k.value ? 'primary' : undefined"
                            :prepend-icon="k.icon"
                            @click="setKind(k.value)">
                            {{ k.label }}
                        </v-chip>
                        <v-chip :variant="customMode ? 'flat' : 'outlined'"
                            :color="customMode ? 'primary' : undefined"
                            prepend-icon="mdi-pencil-plus"
                            @click="enableCustom">
                            Custom...
                        </v-chip>
                    </div>
                    <v-text-field v-if="customMode" v-model="form.kind" class="mt-6"
                        label="Custom type"
                        placeholder="Enter type name"
                        density="compact"
                        hint="Lowercase letters, numbers, underscores or hyphens. You can group reports by type later."
                        persistent-hint
                        :error-messages="kindError ? [kindError] : []"></v-text-field>

                    <v-row class="mt-4">
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="priceDollars" type="number" min="0" step="0.01"
                                label="Price ($)" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="serviceChargePercent" type="number" min="0" max="100"
                                label="Rider-paid service charge %" density="compact"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="form.inventory" type="number" min="0"
                                label="Inventory" density="compact"
                                hint="Total units across all events + variants. Leave blank for unlimited."
                                persistent-hint placeholder="∞"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="expiresAtDate" type="date"
                                label="Stops selling on (optional)" density="compact"
                                hint="After this date the add-on hides from buyers. Leave blank to keep selling."
                                persistent-hint clearable></v-text-field>
                        </v-col>
                    </v-row>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-switch v-model="form.requiresWaiver" color="primary" density="compact"
                                label="Requires waiver" hide-details></v-switch>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-switch v-model="form.isActive" color="primary" density="compact"
                                label="Active" hide-details></v-switch>
                        </v-col>
                    </v-row>

                    <!-- Variants live on a saved product, so the link only appears when editing.
                         For new products the admin saves first, then opens variants from the row. -->
                    <div v-if="editing" class="mt-4">
                        <v-btn variant="tonal" prepend-icon="mdi-tshirt-crew" @click="openVariantsFromEdit">
                            {{ editing.variants.length === 0
                                ? 'Add variants (sizes, colors, etc.)'
                                : `Edit ${editing.variants.length} variant${editing.variants.length === 1 ? '' : 's'}` }}
                        </v-btn>
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-btn v-if="editing" variant="text" color="error" @click="remove">Delete</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn @click="dialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" :disabled="!canSave" @click="save">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, watch } from 'vue'
import draggable from 'vuedraggable'
import { ExtraService, type ExtraProduct, type ExtraVariant, DEFAULT_EXTRA_KINDS, kindIcon, kindLabel } from '@/services/ExtraService'
import { branding } from '@/stores/branding'

const service = new ExtraService()

// Uploads return paths relative to the API host (e.g. /uploads/<tenant>/extra-<id>.png).
// On the Vite dev server the relative path points at the wrong host — same fix every
// other admin page uses: prefix the API origin so the <img> resolves correctly.
const apiUrl: string = import.meta.env.VITE_API_ENDPOINT ?? ''
function apiOrigin(): string {
    try { return new URL(apiUrl, window.location.origin).origin } catch { return '' }
}
function absoluteUrl(url: string | null | undefined): string {
    if (!url) return ''
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}

function formatExpires(iso: string | null): string {
    if (!iso) return ''
    const d = new Date(iso)
    if (isNaN(d.getTime())) return ''
    return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
}
function isExpired(row: ExtraProduct): boolean {
    if (!row.expiresAt) return false
    const d = new Date(row.expiresAt).getTime()
    return !isNaN(d) && d <= Date.now()
}

const rows = ref<ExtraProduct[]>([])
const loading = ref(false)
// Default the list to "currently sellable" — active and not past its expiration.
// Products without an expiration date are never considered expired, so they
// always pass the expiration check (only the active flag matters for them).
const showInactive = ref(false)
// `visibleRows` is the mutable array vuedraggable binds to — SortableJS rewrites
// it in place on drop. We sync it from `rows` whenever the source list or the
// filter changes; `onReorderEnd` interleaves the new visible order back into
// `rows`, preserving hidden rows' canonical positions.
const visibleRows = ref<ExtraProduct[]>([])
function syncVisibleRows() {
    visibleRows.value = showInactive.value
        ? [...rows.value]
        : rows.value.filter(r => r.isActive && !isExpired(r))
}
watch([rows, showInactive], syncVisibleRows, { immediate: true })

const dialog = ref(false)
const editing = ref<ExtraProduct | null>(null)
const saving = ref(false)
const customMode = ref(false)

// Image upload state for the product edit dialog. Hidden file input is triggered
// via a ref so the user-facing button can carry our own styling.
const productImageInput = ref<HTMLInputElement | null>(null)
const imageUploading = ref(false)
async function onProductImageChange(e: Event) {
    const input = e.target as HTMLInputElement
    const file = input.files?.[0]
    if (!file) return
    imageUploading.value = true
    try {
        const r = await service.uploadImage(file)
        form.value.imageUrl = (r.data as any).data.imageUrl
    } catch (err: any) {
        flash(err.response?.data?.error || 'Upload failed.', 'error')
    } finally {
        imageUploading.value = false
        // Reset so re-selecting the same file fires change again.
        input.value = ''
    }
}

const form = ref({
    name: '',
    description: '' as string | null,
    imageUrl: null as string | null,
    kind: 'camping',
    priceCents: 0,
    riderPaidServiceChargeBps: 10000,
    requiresWaiver: false,
    isActive: true,
    sortOrder: 100,
    expiresAt: null as string | null,
    inventory: null as number | null,
})

// HTML date input wants 'YYYY-MM-DD'; the server stores a UTC instant. Convert
// at the boundary so the admin only ever picks a date (the cutoff is end-of-day
// in tenant time, sent to the server as UTC midnight of the next day).
const expiresAtDate = computed<string | null>({
    get: () => form.value.expiresAt ? form.value.expiresAt.slice(0, 10) : null,
    set: (v: string | null) => {
        if (!v) { form.value.expiresAt = null; return }
        // End of selected date in UTC (rough cut — good enough for "stop after this day").
        form.value.expiresAt = new Date(`${v}T23:59:59Z`).toISOString()
    },
})

const priceDollars = computed({
    get: () => form.value.priceCents / 100,
    set: (v: number) => { form.value.priceCents = Math.round((v || 0) * 100) },
})
const serviceChargePercent = computed({
    get: () => Math.round(form.value.riderPaidServiceChargeBps / 100),
    set: (v: number) => { form.value.riderPaidServiceChargeBps = Math.max(0, Math.min(10000, Math.round((v || 0) * 100))) },
})

const kindError = computed(() => {
    if (!form.value.kind) return 'Pick or enter a type.'
    if (!/^[a-z0-9_-]+$/.test(form.value.kind)) return 'Lowercase letters, numbers, underscores or hyphens only.'
    return ''
})

const canSave = computed(() =>
    form.value.name.trim().length > 0 && !kindError.value)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(load)

async function load() {
    loading.value = true
    try {
        const r = await service.listForAdmin()
        rows.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load.', 'error')
    } finally {
        loading.value = false
    }
}

function setKind(value: string) {
    form.value.kind = value
    customMode.value = false
}
function enableCustom() {
    customMode.value = true
    if (DEFAULT_EXTRA_KINDS.some(k => k.value === form.value.kind)) form.value.kind = ''
}

function openCreate() {
    editing.value = null
    form.value = {
        name: '',
        description: '',
        imageUrl: null,
        kind: 'camping',
        priceCents: 5000,
        riderPaidServiceChargeBps: 10000,
        requiresWaiver: false,
        isActive: true,
        sortOrder: 100,
        expiresAt: null,
        inventory: null,
    }
    customMode.value = false
    dialog.value = true
}

function openEdit(row: ExtraProduct) {
    editing.value = row
    form.value = {
        ...row,
        description: row.description ?? '',
        expiresAt: row.expiresAt ?? null,
        inventory: row.inventory ?? null,
    }
    customMode.value = !DEFAULT_EXTRA_KINDS.some(k => k.value === row.kind)
    dialog.value = true
}

async function save() {
    if (!canSave.value) return
    saving.value = true
    try {
        const body = {
            name: form.value.name.trim(),
            description: form.value.description?.trim() || null,
            imageUrl: form.value.imageUrl?.trim() || null,
            kind: form.value.kind.trim().toLowerCase(),
            priceCents: form.value.priceCents,
            riderPaidServiceChargeBps: form.value.riderPaidServiceChargeBps,
            requiresWaiver: form.value.requiresWaiver,
            isActive: form.value.isActive,
            sortOrder: form.value.sortOrder,
            expiresAt: form.value.expiresAt,
            inventory: form.value.inventory ?? null,
        }
        if (editing.value) await service.update(editing.value.id, body)
        else await service.create(body)
        await load()
        dialog.value = false
        flash('Saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function remove() {
    if (!editing.value) return
    if (!confirm(`Delete "${editing.value.name}"?`)) return
    try {
        await service.remove(editing.value.id)
        await load()
        dialog.value = false
        flash('Deleted.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    }
}

// Drag-drop reorder. SortableJS mutates `visibleRows` (the filtered subset) in
// place; hidden rows aren't touched. We rebuild the canonical `rows` list by
// walking the original order and slotting visible rows in their new sequence
// while hidden rows hold their position — that way reordering the visible
// subset doesn't shuffle hidden rows around. Then renumber 10/20/30/… across
// the full list and bulk-update server-side.
async function onReorderEnd(evt: { oldIndex?: number; newIndex?: number }) {
    // SortableJS fires @end even when the row is dropped in the same slot
    // (a click without a real drag). Skip the round-trip and toast in that case.
    if (evt.oldIndex === evt.newIndex) return
    const visibleIds = new Set(visibleRows.value.map(r => r.id))
    let visibleIdx = 0
    const rebuilt: ExtraProduct[] = rows.value.map(r => {
        if (visibleIds.has(r.id)) {
            return visibleRows.value[visibleIdx++]
        }
        return r
    })
    rebuilt.forEach((r, i) => { r.sortOrder = (i + 1) * 10 })
    rows.value = rebuilt
    const items = rebuilt.map(r => ({ id: r.id, sortOrder: r.sortOrder }))
    try {
        await service.reorderProducts(items)
        flash('Order saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to save order — refreshing.', 'error')
        await load()
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

// ── Variants editor ──────────────────────────────────────────────────────────
// Editable rows live in a local array — we save them all on Submit by diffing
// against `originalVariants`. New rows have `id: null`, deleted rows have a
// `removed: true` flag so we know to issue a DELETE.
type VariantRow = {
    tempKey: string
    id: string | null
    size: string
    color: string
    gender: string
    sku: string
    tier: string
    description: string
    priceDollars: number | null
    inventory: number | null
    imageUrl: string | null
    sortOrder: number
    isActive: boolean
    removed?: boolean
}

const variantsDialog = ref(false)
const variantsProduct = ref<ExtraProduct | null>(null)
const editingVariants = ref<VariantRow[]>([])
const originalVariants = ref<ExtraVariant[]>([])
const variantSaving = ref(false)
const variantSaveError = ref('')

// Variant image upload — one shared file input, the active row tracked via
// `variantImageTarget` between the click and the change event.
const variantImageInput = ref<HTMLInputElement | null>(null)
const variantImageTarget = ref<VariantRow | null>(null)
const variantImageUploading = reactive<Record<string, boolean>>({})

function triggerVariantImage(v: VariantRow) {
    variantImageTarget.value = v
    variantImageInput.value?.click()
}

async function onVariantImageChange(e: Event) {
    const input = e.target as HTMLInputElement
    const file = input.files?.[0]
    const target = variantImageTarget.value
    if (!file || !target) { input.value = ''; return }
    variantImageUploading[target.tempKey] = true
    try {
        const r = await service.uploadImage(file)
        target.imageUrl = (r.data as any).data.imageUrl
    } catch (err: any) {
        flash(err.response?.data?.error || 'Upload failed.', 'error')
    } finally {
        variantImageUploading[target.tempKey] = false
        variantImageTarget.value = null
        input.value = ''
    }
}

// Triggered from the edit-product dialog's "Edit variants" button. Closes the
// edit dialog so the variants editor isn't stacked on top, then opens variants
// for the row currently being edited.
function openVariantsFromEdit() {
    if (!editing.value) return
    const row = editing.value
    dialog.value = false
    // Defer past the edit dialog's close transition so the new dialog actually paints.
    setTimeout(() => openVariants(row), 200)
}

function openVariants(row: ExtraProduct) {
    variantsProduct.value = row
    originalVariants.value = [...row.variants]
    editingVariants.value = row.variants.map(v => ({
        tempKey: v.id,
        id: v.id,
        size: v.size ?? '',
        color: v.color ?? '',
        gender: v.gender ?? '',
        sku: v.sku ?? '',
        tier: v.tier ?? '',
        description: v.description ?? '',
        priceDollars: v.priceCents !== null ? v.priceCents / 100 : null,
        inventory: v.inventory,
        imageUrl: v.imageUrl,
        sortOrder: v.sortOrder,
        isActive: v.isActive,
    }))
    variantSaveError.value = ''
    variantsDialog.value = true
}

function addVariantRow() {
    editingVariants.value.push({
        tempKey: 'new-' + Math.random().toString(36).slice(2),
        id: null,
        size: '', color: '', gender: '', sku: '', tier: '', description: '',
        priceDollars: null, inventory: null, imageUrl: null,
        sortOrder: 100, isActive: true,
    })
}

function removeVariantRow(v: VariantRow) {
    if (v.id === null) {
        // Brand-new row — drop it without telling the server.
        editingVariants.value = editingVariants.value.filter(x => x.tempKey !== v.tempKey)
    } else {
        // Existing row — mark for delete; submit will fire the DELETE call.
        v.removed = true
    }
}

async function saveVariants() {
    if (!variantsProduct.value) return
    variantSaveError.value = ''
    variantSaving.value = true
    try {
        const productId = variantsProduct.value.id
        // Submit deletions first, then upserts. A failure mid-way leaves the
        // tenant a partially-applied diff, but we re-load() at the end so the
        // UI reflects whatever the server actually has.
        for (const v of editingVariants.value.filter(x => x.removed && x.id)) {
            await service.removeVariant(productId, v.id!)
        }
        for (const v of editingVariants.value.filter(x => !x.removed)) {
            const body = {
                size: v.size.trim() || null,
                color: v.color.trim() || null,
                gender: v.gender.trim() || null,
                sku: v.sku.trim() || null,
                tier: v.tier.trim() || null,
                description: v.description.trim() || null,
                priceCents: v.priceDollars === null || isNaN(v.priceDollars)
                    ? null
                    : Math.round(v.priceDollars * 100),
                inventory: v.inventory === null || isNaN(v.inventory) ? null : v.inventory,
                imageUrl: v.imageUrl,
                sortOrder: v.sortOrder ?? 100,
                isActive: v.isActive,
            }
            if (v.id) await service.updateVariant(productId, v.id, body)
            else await service.createVariant(productId, body)
        }
        await load()
        variantsDialog.value = false
        flash('Variants saved.', 'success')
    } catch (err: any) {
        variantSaveError.value = err.response?.data?.error || 'Save failed.'
    } finally {
        variantSaving.value = false
    }
}
</script>

<style scoped>
/* Vuetify's dense v-table jams the inputs together; give each variant cell
   real breathing room so the rows don't read as a wall of fields. */
.variant-row > td {
    padding-top: 10px !important;
    padding-bottom: 10px !important;
    padding-left: 8px !important;
    padding-right: 8px !important;
}
/* Visual hint that the variant thumbnail is clickable to replace. */
.variant-thumb {
    cursor: pointer;
    border: 1px solid rgba(0, 0, 0, 0.1);
}
.variant-thumb:hover {
    opacity: 0.85;
}
.drag-handle-cell {
    padding-left: 4px !important;
    padding-right: 0 !important;
}
.drag-handle {
    cursor: grab;
}
.drag-handle:active {
    cursor: grabbing;
}
/* Ghost row appearance during drag. */
.drag-ghost {
    opacity: 0.35;
    background: rgba(25, 118, 210, 0.08);
}
</style>
