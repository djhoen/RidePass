<template>
    <div>
        <div v-for="e in extras" :key="e.productId"
            class="d-flex align-start py-3 ga-3 extra-row text-left"
            style="border-bottom: 1px solid rgba(0,0,0,0.06)">
            <!-- Image / placeholder. Clickable to enlarge when present. -->
            <v-img v-if="primaryImage(e)" :src="absoluteUrl(primaryImage(e)!)"
                width="80" height="80" cover class="rounded extra-thumb"
                style="flex: 0 0 auto" @click="openImagePreview(primaryImage(e)!)"></v-img>
            <div v-else class="d-flex align-center justify-center rounded"
                style="width: 80px; height: 80px; background: rgba(0,0,0,0.05); flex: 0 0 auto; border: 1px solid rgba(0,0,0,0.05)">
                <v-icon color="grey">mdi-image-off-outline</v-icon>
            </div>

            <!-- Info column. Left-aligned by default; flex-grow takes remaining width. -->
            <div class="flex-grow-1" style="min-width: 0">
                <div class="text-body-1">
                    <strong>{{ e.name }}</strong>
                    <span class="text-medium-emphasis"> (${{ (e.priceCents / 100).toFixed(2) }})</span>
                </div>
                <div class="text-caption text-medium-emphasis">
                    <span v-if="!hasVariants(e)">
                        <span v-if="e.remaining < 0">Unlimited</span>
                        <span v-else>{{ e.remaining }} left</span>
                    </span>
                    <span v-else>{{ e.variants.length }} option{{ e.variants.length === 1 ? '' : 's' }} available</span>
                    <span v-if="e.requiresWaiver"> · waiver required</span>
                </div>
                <!-- Selection summary for variant products: "M Red × 2, L Blue × 1". -->
                <div v-if="hasVariants(e) && totalQty(e) > 0" class="text-caption text-success mt-1">
                    {{ summaryFor(e) }}
                </div>
            </div>

            <!-- Action column. Inline qty +/- for non-variant products, "Choose Options"
                 button for variant products (opens the variant chooser dialog). -->
            <div v-if="!hasVariants(e)" class="d-flex align-center ga-1" style="flex: 0 0 auto">
                <v-btn size="small" icon variant="outlined"
                    :disabled="qtyOf(e, null) <= 0" @click="setQty(e, null, qtyOf(e, null) - 1)">
                    <v-icon>mdi-minus</v-icon>
                </v-btn>
                <div style="min-width: 32px; text-align: center"><strong>{{ qtyOf(e, null) }}</strong></div>
                <v-btn size="small" icon variant="outlined"
                    :disabled="!canIncrement(e, null)" @click="setQty(e, null, qtyOf(e, null) + 1)">
                    <v-icon>mdi-plus</v-icon>
                </v-btn>
            </div>
            <div v-else style="flex: 0 0 auto">
                <v-btn size="small"
                    :variant="totalQty(e) > 0 ? 'flat' : 'tonal'"
                    :color="totalQty(e) > 0 ? 'primary' : undefined"
                    prepend-icon="mdi-tune-variant"
                    @click="openOptions(e)">
                    {{ totalQty(e) > 0 ? `Edit Options (${totalQty(e)})` : 'Choose Options' }}
                </v-btn>
            </div>
        </div>

        <!-- Variant chooser: per-variant qty +/-, supports multiple variants per product. -->
        <v-dialog v-model="optionsDialog" max-width="640" scrollable>
            <v-card v-if="optionsProduct">
                <v-card-title>{{ optionsProduct.name }} — Options</v-card-title>
                <v-card-subtitle>Pick a quantity for each option you want.</v-card-subtitle>
                <v-card-text>
                    <div v-for="v in optionsProduct.variants" :key="v.id"
                        class="d-flex align-center py-2 ga-3 text-left"
                        style="border-bottom: 1px solid rgba(0,0,0,0.06)">
                        <v-img v-if="effectiveImage(optionsProduct, v)"
                            :src="absoluteUrl(effectiveImage(optionsProduct, v)!)"
                            width="56" height="56" cover class="rounded extra-thumb"
                            style="flex: 0 0 auto"
                            @click="openImagePreview(effectiveImage(optionsProduct, v)!)"></v-img>
                        <div v-else class="d-flex align-center justify-center rounded"
                            style="width: 56px; height: 56px; background: rgba(0,0,0,0.05); flex: 0 0 auto">
                            <v-icon size="small" color="grey">mdi-image-off-outline</v-icon>
                        </div>
                        <div class="flex-grow-1" style="min-width: 0">
                            <div class="text-body-2"><strong>{{ variantLabel(v) }}</strong></div>
                            <div class="text-caption text-medium-emphasis">
                                ${{ (v.priceCents / 100).toFixed(2) }}
                                <span v-if="v.remaining < 0"> · unlimited</span>
                                <span v-else-if="v.remaining === 0" class="text-error"> · sold out</span>
                                <span v-else> · {{ v.remaining }} left</span>
                            </div>
                        </div>
                        <div class="d-flex align-center ga-1" style="flex: 0 0 auto">
                            <v-btn size="small" icon variant="outlined"
                                :disabled="qtyOf(optionsProduct, v.id) <= 0"
                                @click="setQty(optionsProduct, v.id, qtyOf(optionsProduct, v.id) - 1)">
                                <v-icon>mdi-minus</v-icon>
                            </v-btn>
                            <div style="min-width: 32px; text-align: center">
                                <strong>{{ qtyOf(optionsProduct, v.id) }}</strong>
                            </div>
                            <v-btn size="small" icon variant="outlined"
                                :disabled="!canIncrement(optionsProduct, v.id)"
                                @click="setQty(optionsProduct, v.id, qtyOf(optionsProduct, v.id) + 1)">
                                <v-icon>mdi-plus</v-icon>
                            </v-btn>
                        </div>
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn color="primary" @click="optionsDialog = false">Done</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Larger image preview, opened by clicking any thumbnail. -->
        <v-dialog v-model="imagePreviewOpen" max-width="720">
            <v-card>
                <v-img :src="absoluteUrl(imagePreviewUrl)" max-height="80vh" contain></v-img>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="imagePreviewOpen = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import type { EligibleExtra, EligibleExtraVariant } from '@/services/EventService'

// One row per (productId, variantId|null) selection. The parent reads/writes
// `selections` via v-model so it can collect what to send to the server.
// Variant products contribute multiple rows (one per chosen variant), all sharing
// the same productId — the backend dedupes by (productId, variantId).
export interface ExtraSelection {
    productId: string
    variantId: string | null
    quantity: number
}

const props = defineProps<{
    extras: EligibleExtra[]
    modelValue: ExtraSelection[]
}>()

const emit = defineEmits<{ (e: 'update:modelValue', v: ExtraSelection[]): void }>()

// Image storage returns paths like /uploads/<tenant>/extra-<id>.png. Same Vite-vs-API
// origin pattern other admin pages use — relative paths get absolute-prefixed for
// dev, http(s) URLs pass through untouched.
const apiUrl: string = import.meta.env.VITE_API_ENDPOINT ?? ''
function apiOrigin(): string {
    try { return new URL(apiUrl, window.location.origin).origin } catch { return '' }
}
function absoluteUrl(url: string | null | undefined): string {
    if (!url) return ''
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}

function hasVariants(e: EligibleExtra): boolean {
    return (e.variants?.length ?? 0) > 0
}

// Read-only lookups against the parent's selections. Mutations go through setQty
// which emits a fresh array — this keeps the parent as the single source of truth.
function qtyOf(e: EligibleExtra, variantId: string | null): number {
    const match = props.modelValue.find(s =>
        s.productId === e.productId && (s.variantId ?? null) === variantId)
    return match?.quantity ?? 0
}

function totalQty(e: EligibleExtra): number {
    return props.modelValue
        .filter(s => s.productId === e.productId)
        .reduce((sum, s) => sum + s.quantity, 0)
}

function canIncrement(e: EligibleExtra, variantId: string | null): boolean {
    if (variantId !== null) {
        const v = e.variants.find(x => x.id === variantId)
        if (!v) return false
        if (v.remaining === 0) return false
        if (v.remaining < 0) return qtyOf(e, variantId) < 50
        return qtyOf(e, variantId) < Math.min(50, v.remaining)
    }
    if (e.remaining === 0) return false
    if (e.remaining < 0) return qtyOf(e, null) < 50
    return qtyOf(e, null) < Math.min(50, e.remaining)
}

function setQty(e: EligibleExtra, variantId: string | null, qty: number) {
    qty = Math.max(0, qty)
    const out = props.modelValue.filter(s =>
        !(s.productId === e.productId && (s.variantId ?? null) === variantId))
    if (qty > 0) {
        out.push({ productId: e.productId, variantId, quantity: qty })
    }
    emit('update:modelValue', out)
}

function variantLabel(v: EligibleExtraVariant): string {
    const parts = [v.size, v.color, v.gender].filter(s => !!s)
    return parts.length > 0 ? parts.join(' / ') : 'Default'
}

function summaryFor(e: EligibleExtra): string {
    const sels = props.modelValue.filter(s => s.productId === e.productId && s.quantity > 0)
    if (sels.length === 0) return ''
    return sels.map(s => {
        if (!s.variantId) return `${s.quantity}× default`
        const v = e.variants.find(x => x.id === s.variantId)
        return `${s.quantity}× ${v ? variantLabel(v) : 'option'}`
    }).join(', ')
}

// Image fallbacks — variant image wins, else product image, else placeholder.
function primaryImage(e: EligibleExtra): string | null {
    return e.imageUrl ?? (e.variants.find(v => v.imageUrl)?.imageUrl ?? null)
}
function effectiveImage(e: EligibleExtra, v: EligibleExtraVariant): string | null {
    return v.imageUrl ?? e.imageUrl ?? null
}

// Variant chooser dialog
const optionsDialog = ref(false)
const optionsProduct = ref<EligibleExtra | null>(null)
function openOptions(e: EligibleExtra) {
    optionsProduct.value = e
    optionsDialog.value = true
}

// Larger-image preview dialog
const imagePreviewOpen = ref(false)
const imagePreviewUrl = ref('')
function openImagePreview(url: string) {
    imagePreviewUrl.value = url
    imagePreviewOpen.value = true
}
</script>

<style scoped>
.extra-thumb {
    cursor: pointer;
    border: 1px solid rgba(0, 0, 0, 0.08);
    transition: opacity 0.15s ease-in-out;
}
.extra-thumb:hover {
    opacity: 0.85;
}
</style>
