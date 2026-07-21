<template>
    <v-dialog :model-value="modelValue" max-width="560" @update:model-value="$emit('update:modelValue', $event)">
        <v-card>
            <v-card-title class="d-flex align-center">
                <span>{{ product ? 'Edit product' : 'New product' }}</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" @click="close"></v-btn>
            </v-card-title>
            <v-card-text>
                <v-text-field v-model="form.name" label="Name" density="compact" hide-details></v-text-field>
                <v-textarea v-model="form.description" label="Description" density="compact" rows="2" class="mt-4" hide-details></v-textarea>
                <v-row dense class="mt-2">
                    <v-col cols="6"><v-text-field v-model="form.brand" label="Brand" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="6"><v-text-field v-model="form.imageUrl" label="Image URL" density="compact" hide-details></v-text-field></v-col>
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
import { BikeShopService, type ShopProduct, type ShopCategory, type ShopSupplier, type UpsertShopProduct } from '@/services/BikeShopService'

const props = defineProps<{ modelValue: boolean; product: ShopProduct | null; categories: ShopCategory[]; suppliers: ShopSupplier[] }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void; (e: 'saved'): void; (e: 'flash', text: string, color?: 'success' | 'error'): void }>()

const service = new BikeShopService()
const saving = ref(false)
const error = ref('')
const form = ref<UpsertShopProduct>(blank())

function blank(): UpsertShopProduct {
    return { name: '', description: null, brand: null, imageUrl: null, categoryId: null, supplierId: null,
        isSellable: true, isPublished: true, isRentable: false, isActive: true, sortOrder: 100 }
}

watch(() => props.modelValue, (open) => {
    if (!open) return
    error.value = ''
    const p = props.product
    form.value = p
        ? { name: p.name, description: p.description, brand: p.brand, imageUrl: p.imageUrl, categoryId: p.categoryId,
            supplierId: p.supplierId, isSellable: p.isSellable, isPublished: p.isPublished, isRentable: p.isRentable, isActive: p.isActive, sortOrder: p.sortOrder }
        : blank()
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
