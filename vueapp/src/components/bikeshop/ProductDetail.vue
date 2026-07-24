<template>
    <!-- Product detail CONTENT, deliberately not a dialog: the hosted storefront wraps this
         in a v-dialog, while the embedded widget renders it inline (a fixed-position dialog
         inside a content-sized iframe centers against the iframe's full height, which lands
         it off the visitor's screen). Keeping presentation out of here makes that a branch
         at the call site instead of a fork. -->
    <div class="pd-root">
        <div class="d-flex align-start pa-4 pb-0">
            <div class="flex-grow-1" style="min-width: 0">
                <div v-if="product.brand" class="text-caption text-medium-emphasis">{{ product.brand }}</div>
                <h2 class="text-h6 font-weight-bold">{{ product.name }}</h2>
            </div>
            <v-btn icon="mdi-close" variant="text" size="small" aria-label="Close" @click="$emit('close')"></v-btn>
        </div>

        <v-row class="pa-4">
            <!-- Gallery -->
            <v-col cols="12" md="6">
                <v-img v-if="currentImage" :src="currentImage" :aspect-ratio="4 / 3" cover rounded
                    class="pd-hero" tabindex="0" @keydown.left.prevent="step(-1)" @keydown.right.prevent="step(1)"></v-img>
                <div v-else class="pd-noimage d-flex align-center justify-center rounded">
                    <v-icon size="48" class="text-medium-emphasis">mdi-image-off</v-icon>
                </div>

                <div v-if="gallery.length > 1" class="d-flex ga-2 mt-2 pd-thumbs">
                    <v-img v-for="(src, i) in gallery" :key="i" :src="src" :aspect-ratio="1" cover rounded
                        class="pd-thumb" :class="{ 'pd-thumb--active': i === index }"
                        role="button" :aria-label="`Photo ${i + 1}`" @click="index = i"></v-img>
                </div>
            </v-col>

            <!-- Detail + buy -->
            <v-col cols="12" md="6">
                <div class="text-h6 text-primary font-weight-bold">{{ priceLabel }}</div>

                <p v-if="product.description" class="text-body-2 mt-3" style="white-space: pre-line">{{ product.description }}</p>

                <template v-if="serialized">
                    <v-alert type="info" variant="tonal" density="compact" class="mt-4">
                        Available in store: ask our staff and we will set you up with the right unit.
                    </v-alert>
                </template>

                <template v-else>
                    <v-select v-if="product.variants.length > 1" v-model="variantId" :items="variantItems"
                        item-title="title" item-value="id" label="Option" density="compact"
                        hide-details class="mt-4"></v-select>

                    <div class="text-caption mt-4" :class="availabilityClass">{{ availabilityLabel }}</div>

                    <div v-if="available > 0" class="d-flex align-center ga-2 mt-2">
                        <span class="text-body-2">Quantity</span>
                        <v-btn icon="mdi-minus" size="x-small" variant="tonal" aria-label="One fewer"
                            :disabled="qty <= 1" @click="qty--"></v-btn>
                        <span class="text-body-1 font-weight-medium" style="min-width: 24px; text-align: center">{{ qty }}</span>
                        <v-btn icon="mdi-plus" size="x-small" variant="tonal" aria-label="One more"
                            :disabled="qty >= available" @click="qty++"></v-btn>
                    </div>

                    <v-btn color="primary" block class="mt-4" prepend-icon="mdi-cart-plus"
                        :disabled="!canAdd" @click="add">
                        {{ available > 0 ? 'Add to cart' : 'Out of stock' }}
                    </v-btn>
                    <v-btn variant="text" block class="mt-1" @click="$emit('close')">Keep shopping</v-btn>
                </template>
            </v-col>
        </v-row>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import type { StoreCatalogProduct, StoreCatalogVariant } from '@/services/BikeShopService'
import { absoluteUrl } from '@/helpers/ImageUrl'

const props = defineProps<{ product: StoreCatalogProduct }>()
const emit = defineEmits<{
    (e: 'add', payload: { variantId: string; qty: number }): void
    (e: 'close'): void
}>()

// Cover first, then the gallery, de-duplicated: a tenant who also uploaded the cover into
// the gallery should not see it twice.
const gallery = computed(() => {
    const urls = [props.product.imageUrl, ...(props.product.images ?? []).map(i => i.url)]
    const seen = new Set<string>()
    const out: string[] = []
    for (const u of urls) {
        const abs = absoluteUrl(u)
        if (abs && !seen.has(abs)) { seen.add(abs); out.push(abs) }
    }
    return out
})

const index = ref(0)
const currentImage = computed(() => gallery.value[index.value] ?? null)
function step(delta: number) {
    if (gallery.value.length === 0) return
    index.value = (index.value + delta + gallery.value.length) % gallery.value.length
}

const serialized = computed(() => props.product.variants.every(v => v.trackingKind === 'serialized'))

const sellable = computed(() => props.product.variants.filter(v => v.trackingKind !== 'serialized'))
const variantId = ref<string | null>(sellable.value[0]?.id ?? null)
const variant = computed<StoreCatalogVariant | null>(
    () => sellable.value.find(v => v.id === variantId.value) ?? null)

const variantItems = computed(() => props.product.variants.map(v => ({
    id: v.id,
    title: [v.size, v.color].filter(Boolean).join(' / ') + (v.available > 0 ? '' : ' (out)'),
})))

const available = computed(() => variant.value?.available ?? 0)
const qty = ref(1)
// Switching option resets the quantity rather than silently carrying an amount the new
// option may not have in stock.
watch(variantId, () => { qty.value = 1 })
watch(available, a => { if (qty.value > a) qty.value = Math.max(1, a) })

const canAdd = computed(() => variant.value !== null && available.value > 0)

const availabilityLabel = computed(() => {
    if (!variant.value) return ''
    if (available.value <= 0) return 'Out of stock'
    if (available.value <= 5) return `Only ${available.value} left`
    return 'In stock'
})
const availabilityClass = computed(() =>
    available.value <= 0 ? 'text-error' : available.value <= 5 ? 'text-warning' : 'text-medium-emphasis')

const priceLabel = computed(() => {
    const prices = props.product.variants.map(v => v.salePriceCents)
    if (prices.length === 0) return ''
    const min = Math.min(...prices), max = Math.max(...prices)
    const money = (c: number) => `$${(c / 100).toFixed(2)}`
    return min === max ? money(min) : `${money(min)} to ${money(max)}`
})

function add() {
    if (!variant.value) return
    emit('add', { variantId: variant.value.id, qty: qty.value })
}
</script>

<style scoped>
.pd-hero { background: rgba(var(--v-theme-on-surface), 0.04); }
.pd-noimage {
    aspect-ratio: 4 / 3;
    background: rgba(var(--v-theme-on-surface), 0.04);
}
.pd-thumbs { overflow-x: auto; }
.pd-thumb {
    width: 64px;
    flex: 0 0 64px;
    cursor: pointer;
    opacity: 0.6;
    border: 2px solid transparent;
}
.pd-thumb--active {
    opacity: 1;
    border-color: rgb(var(--v-theme-primary));
}
</style>
