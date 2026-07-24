<template>
    <v-dialog :model-value="modelValue" max-width="720" @update:model-value="$emit('update:modelValue', $event)">
        <!-- Explicit flex column with a bounded height: the Photos tab is a grid that can grow
             tall, and without this it would push the tabs bar (and the active-tab underline)
             out of the card. Never put a min-height on the scrolling body. -->
        <v-card class="d-flex flex-column" style="max-height: 90vh">
            <v-card-title class="d-flex align-center" style="flex: 0 0 auto">
                <span>{{ product ? 'Edit product' : 'New product' }}</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" @click="close"></v-btn>
            </v-card-title>
            <v-tabs v-model="tab" color="primary" style="flex: 0 0 auto">
                <v-tab value="details">Details</v-tab>
                <v-tab value="photos">
                    Photos
                    <v-chip v-if="images.length" size="x-small" class="ml-2">{{ images.length }}</v-chip>
                </v-tab>
            </v-tabs>
            <v-divider></v-divider>
            <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                <v-window v-model="tab">
                    <v-window-item value="details">
                        <v-text-field v-model="form.name" label="Name" density="compact" hide-details></v-text-field>
                        <v-textarea v-model="form.description" label="Description" density="compact" rows="2" class="mt-4" hide-details></v-textarea>
                        <v-row dense class="mt-2">
                            <v-col cols="6"><v-text-field v-model="form.brand" label="Brand" density="compact" hide-details></v-text-field></v-col>
                            <v-col cols="6">
                                <div class="d-flex align-center ga-3">
                                    <v-avatar v-if="form.imageUrl" size="40" rounded="lg">
                                        <v-img :src="absoluteUrl(form.imageUrl)!"></v-img>
                                    </v-avatar>
                                    <v-file-input :model-value="null" label="Cover image" accept="image/png,image/jpeg,image/webp"
                                        density="compact" prepend-icon="mdi-camera" hide-details :loading="uploading"
                                        style="flex: 1" @update:model-value="onImageSelected"></v-file-input>
                                    <v-btn v-if="form.imageUrl" icon="mdi-delete" variant="text" size="small"
                                        @click="form.imageUrl = null"></v-btn>
                                </div>
                            </v-col>
                        </v-row>
                        <v-select v-model="form.categoryId" :items="categories" item-title="name" item-value="id"
                            label="Category" density="compact" clearable class="mt-4" hide-details></v-select>
                        <v-select v-model="form.supplierId" :items="suppliers" item-title="name" item-value="id"
                            label="Supplier" density="compact" clearable class="mt-4" hide-details></v-select>
                        <div class="d-flex ga-4 mt-4 flex-wrap">
                            <v-switch v-model="form.isSellable" label="Sellable" color="primary" hide-details density="compact"></v-switch>
                            <v-switch v-model="form.isRentable" label="Rentable" color="primary" hide-details density="compact"></v-switch>
                            <v-switch v-model="form.isActive" label="Active" color="primary" hide-details density="compact"></v-switch>
                        </div>
                        <!-- Sellable is "can ring it up at the counter"; publish is "list it in the online store". -->
                        <v-switch v-model="form.isPublished" color="primary" hide-details density="compact" class="mt-2"
                            :disabled="!form.isSellable"
                            :label="form.isSellable ? 'List in online store' : 'List in online store (needs Sellable)'"></v-switch>
                        <div v-if="error" class="text-error text-body-2 mt-2">{{ error }}</div>
                    </v-window-item>

                    <!-- ── Photos: the extra shots the storefront detail view shows ────── -->
                    <v-window-item value="photos">
                        <p class="text-body-2 text-medium-emphasis">
                            These show as a gallery when a shopper opens the product. The cover image
                            on the Details tab is what the product card shows.
                        </p>

                        <v-alert v-if="!product" type="info" variant="tonal" density="compact" class="mt-3">
                            Save the product first, then you can add photos.
                        </v-alert>

                        <template v-else>
                            <div class="d-flex align-center ga-2 mt-3">
                                <v-btn size="small" variant="tonal" prepend-icon="mdi-image-plus"
                                    :loading="photoBusy" :disabled="images.length >= maxPhotos"
                                    @click="pickPhotos">Add photos</v-btn>
                                <span class="text-caption text-medium-emphasis">{{ images.length }} of {{ maxPhotos }}</span>
                                <input ref="photoInput" type="file" multiple class="d-none"
                                    accept="image/png,image/jpeg,image/webp" @change="onPhotosPicked" />
                            </div>
                            <div v-if="photoError" class="text-error text-body-2 mt-2">{{ photoError }}</div>

                            <v-progress-linear v-if="photosLoading" indeterminate class="mt-3"></v-progress-linear>
                            <p v-else-if="images.length === 0" class="text-caption text-medium-emphasis mt-4">
                                No extra photos yet.
                            </p>

                            <draggable v-else v-model="visibleRows" item-key="id" handle=".drag-handle"
                                :animation="180" ghost-class="drag-ghost" class="photo-grid mt-3" @end="onReorderEnd">
                                <template #item="{ element: img }">
                                    <div class="photo-tile">
                                        <v-img :src="absoluteUrl(img.imageUrl)!" :aspect-ratio="1" cover rounded></v-img>
                                        <div class="d-flex align-center">
                                            <v-icon class="drag-handle" size="18" color="grey">mdi-drag</v-icon>
                                            <v-spacer></v-spacer>
                                            <v-tooltip text="Use as the cover image" location="top">
                                                <template #activator="{ props: tp }">
                                                    <v-btn v-bind="tp" icon="mdi-star-outline" variant="text" size="x-small"
                                                        :color="form.imageUrl === img.imageUrl ? 'primary' : undefined"
                                                        aria-label="Use as cover" @click="makeCover(img)"></v-btn>
                                                </template>
                                            </v-tooltip>
                                            <v-btn icon="mdi-delete" variant="text" size="x-small" color="error"
                                                aria-label="Delete photo" @click="removePhoto(img)"></v-btn>
                                        </div>
                                        <v-text-field :model-value="img.caption ?? ''" label="Caption" density="compact"
                                            hide-details variant="plain"
                                            @blur="saveCaption(img, ($event.target as HTMLInputElement).value)"></v-text-field>
                                    </div>
                                </template>
                            </draggable>
                        </template>
                    </v-window-item>
                </v-window>
            </v-card-text>
            <v-card-actions style="flex: 0 0 auto">
                <v-spacer></v-spacer>
                <v-btn :disabled="saving" @click="close">Cancel</v-btn>
                <v-btn color="primary" :loading="saving" @click="save">Save</v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import draggable from 'vuedraggable'
import { BikeShopService, type ShopProduct, type ShopCategory, type ShopSupplier,
    type UpsertShopProduct, type ShopProductImage } from '@/services/BikeShopService'
import { useDragReorder } from '@/composables/useDragReorder'
import { useConfirm } from '@/composables/useConfirm'
import { absoluteUrl } from '@/helpers/ImageUrl'

const props = defineProps<{ modelValue: boolean; product: ShopProduct | null; categories: ShopCategory[]; suppliers: ShopSupplier[] }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void; (e: 'saved'): void; (e: 'flash', text: string, color?: 'success' | 'error'): void }>()

const service = new BikeShopService()
const confirm = useConfirm()
const saving = ref(false)
const uploading = ref(false)
const error = ref('')
const tab = ref<'details' | 'photos'>('details')
const form = ref<UpsertShopProduct>(blank())

async function onImageSelected(v: File | File[] | null) {
    const file = Array.isArray(v) ? (v[0] ?? null) : v
    if (!file) return
    uploading.value = true
    try {
        const r = await service.uploadImage(file)
        form.value.imageUrl = (r.data as any).data.imageUrl
    } catch (err: any) {
        error.value = err.response?.data?.error || 'Could not upload the image. Check that it is a PNG, JPEG, or WEBP under 5 MB and try again.'
    } finally {
        uploading.value = false
    }
}

// ── Gallery ────────────────────────────────────────────────────────────────
const maxPhotos = 12
const images = ref<ShopProductImage[]>([])
const photosLoading = ref(false)
const photoBusy = ref(false)
const photoError = ref('')
const photoInput = ref<HTMLInputElement | null>(null)

const { visibleRows, onReorderEnd } = useDragReorder<ShopProductImage>({
    rows: images,
    save: items => service.reorderProductImages(props.product!.id, items),
    onSuccess: () => emit('flash', 'Photo order saved.'),
    onError: async err => {
        photoError.value = (err as any)?.response?.data?.error
            || 'Could not save the new photo order. Reloading the gallery.'
        await loadImages()
    },
})

async function loadImages() {
    if (!props.product) { images.value = []; return }
    photosLoading.value = true
    photoError.value = ''
    try {
        const r = await service.listProductImages(props.product.id)
        // Reassign, never push: useDragReorder watches this ref without `deep`, so an
        // in-place mutation would leave it renumbering a stale list on the next drag.
        images.value = [...(r.data as any).data]
    } catch (err: any) {
        photoError.value = err.response?.data?.error
            || 'Could not load this product\'s photos. Check your connection and reopen the dialog.'
    } finally {
        photosLoading.value = false
    }
}

function pickPhotos() { photoInput.value?.click() }

async function onPhotosPicked(e: Event) {
    const input = e.target as HTMLInputElement
    const files = Array.from(input.files ?? [])
    input.value = ''
    if (files.length === 0 || !props.product) return
    photoBusy.value = true
    photoError.value = ''
    try {
        // Sequentially, so the server's max(sort_order)+10 assigns positions in the order
        // the admin picked them rather than in whatever order the uploads happen to land.
        for (const f of files) {
            if (images.value.length >= maxPhotos) {
                photoError.value = `Only the first ${maxPhotos} photos were kept; remove one to add more.`
                break
            }
            const r = await service.addProductImage(props.product.id, f)
            images.value = [...images.value, (r.data as any).data]
        }
    } catch (err: any) {
        photoError.value = err.response?.data?.error
            || 'Could not upload that photo. Check that it is a PNG, JPEG, or WEBP under 5 MB and try again.'
    } finally {
        photoBusy.value = false
    }
}

async function removePhoto(img: ShopProductImage) {
    if (!await confirm({ message: 'Delete this photo?', confirmText: 'Delete', confirmColor: 'error' })) return
    try {
        await service.deleteProductImage(img.id)
        images.value = images.value.filter(i => i.id !== img.id)
    } catch (err: any) {
        photoError.value = err.response?.data?.error || 'Could not delete the photo. Please try again.'
    }
}

async function saveCaption(img: ShopProductImage, caption: string) {
    const next = caption.trim() || null
    if (next === (img.caption ?? null)) return
    try {
        await service.updateProductImage(img.id, next)
        img.caption = next
    } catch (err: any) {
        photoError.value = err.response?.data?.error || 'Could not save the caption. Please try again.'
    }
}

// Copies the url onto the product; the gallery row stays, so the same photo can be both
// the cover and a gallery shot. Takes effect when the dialog is saved.
function makeCover(img: ShopProductImage) {
    form.value.imageUrl = img.imageUrl
    emit('flash', 'Cover set. Save the product to apply it.')
}

function blank(): UpsertShopProduct {
    return { name: '', description: null, brand: null, imageUrl: null, categoryId: null, supplierId: null,
        isSellable: true, isPublished: true, isRentable: false, isActive: true, sortOrder: 100 }
}

watch(() => props.modelValue, (open) => {
    if (!open) return
    error.value = ''
    photoError.value = ''
    tab.value = 'details'
    const p = props.product
    form.value = p
        ? { name: p.name, description: p.description, brand: p.brand, imageUrl: p.imageUrl, categoryId: p.categoryId,
            supplierId: p.supplierId, isSellable: p.isSellable, isPublished: p.isPublished, isRentable: p.isRentable, isActive: p.isActive, sortOrder: p.sortOrder }
        : blank()
    loadImages()
})

function close() { emit('update:modelValue', false) }

async function save() {
    error.value = ''
    if (!form.value.name.trim()) { error.value = 'Name is required.'; return }
    if (!form.value.isSellable && !form.value.isRentable) { error.value = 'A product must be sellable, rentable, or both.'; return }
    saving.value = true
    try {
        const body: UpsertShopProduct = {
            ...form.value,
            name: form.value.name.trim(),
            description: form.value.description?.trim() || null,
            brand: form.value.brand?.trim() || null,
            imageUrl: form.value.imageUrl?.trim() || null,
        }
        if (props.product) await service.updateProduct(props.product.id, body)
        else await service.createProduct(body)
        emit('flash', 'Product saved.')
        emit('saved')
        close()
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not save the product. Please try again.'
    } finally { saving.value = false }
}
</script>

<style scoped>
.photo-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
    gap: 12px;
}
.photo-tile {
    border: 1px solid rgba(var(--v-theme-on-surface), 0.12);
    border-radius: 8px;
    padding: 8px;
}
.drag-handle { cursor: grab; }
.drag-handle:active { cursor: grabbing; }
.drag-ghost { opacity: 0.35; }
</style>
