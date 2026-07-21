<template>
    <v-dialog :model-value="modelValue" max-width="480"
        @update:model-value="$emit('update:modelValue', $event)">
        <v-card>
            <v-card-title class="d-flex align-center">
                <span>Print barcode labels</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" @click="close"></v-btn>
            </v-card-title>
            <v-card-text>
                <div class="text-subtitle-2">{{ productName }}</div>
                <div class="text-caption text-medium-emphasis mb-3">{{ variantLabel || '(default variant)' }}</div>

                <v-alert v-if="!barcodeValue" type="warning" variant="tonal" density="compact">
                    This variant has no barcode or SKU to encode. Add one on the variant first.
                </v-alert>
                <template v-else>
                    <div class="preview d-flex justify-center pa-3 mb-1" v-html="previewSvg"></div>
                    <div class="text-caption text-center text-medium-emphasis mb-3">
                        Encoding <strong>{{ barcodeValue }}</strong> (Code 128)
                    </div>

                    <v-row dense align="center">
                        <v-col cols="12" sm="5">
                            <v-text-field v-model.number="quantity" type="number" min="1" max="200"
                                label="How many labels" density="compact" hide-details></v-text-field>
                        </v-col>
                        <v-col cols="12" sm="7" class="d-flex flex-column">
                            <v-switch v-model="includeName" label="Product name" color="primary"
                                density="compact" hide-details></v-switch>
                            <v-switch v-model="includePrice" label="Price" color="primary" density="compact"
                                hide-details :disabled="variant?.salePriceCents == null"></v-switch>
                        </v-col>
                    </v-row>
                    <div v-if="printError" class="text-error text-caption mt-2">{{ printError }}</div>
                </template>
            </v-card-text>
            <v-card-actions>
                <v-spacer></v-spacer>
                <v-btn @click="close">Cancel</v-btn>
                <v-btn color="primary" :disabled="!barcodeValue || quantity < 1" @click="printLabels">
                    Print {{ quantity > 0 ? quantity : '' }}
                </v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import JsBarcode from 'jsbarcode'
import type { ShopVariant } from '@/services/BikeShopService'

const props = defineProps<{ modelValue: boolean; variant: ShopVariant | null; productName: string }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void }>()

const quantity = ref(1)
const includeName = ref(true)
const includePrice = ref(true)
const printError = ref('')

// The barcode's own number if it has one, else the SKU. Code 128 encodes either as free text.
const barcodeValue = computed(() => (props.variant?.barcode || props.variant?.sku || '').trim())
const variantLabel = computed(() => {
    const v = props.variant
    return v ? [v.size, v.color].filter(Boolean).join(' / ') : ''
})

// Render a barcode to a standalone SVG string (usable both in the preview and the print window).
function svgString(value: string, forPrint: boolean): string {
    const el = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
    try {
        JsBarcode(el, value, {
            format: 'CODE128', width: forPrint ? 1.6 : 1.4, height: forPrint ? 44 : 40,
            fontSize: 12, margin: 4, displayValue: true,
        })
    } catch {
        return ''
    }
    return new XMLSerializer().serializeToString(el)
}

const previewSvg = computed(() => (barcodeValue.value ? svgString(barcodeValue.value, false) : ''))

function money(c: number): string { return `$${(c / 100).toFixed(2)}` }
function esc(s: string): string {
    return s.replace(/[&<>"]/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[ch] as string))
}
function close() { emit('update:modelValue', false) }

watch(() => props.modelValue, (open) => { if (open) { quantity.value = 1; printError.value = '' } })

function printLabels() {
    printError.value = ''
    const value = barcodeValue.value
    if (!value) return
    const svg = svgString(value, true)
    if (!svg) { printError.value = 'Could not generate a barcode for that value.'; return }

    const v = props.variant!
    const nameLine = includeName.value ? `<div class="nm">${esc(props.productName)}</div>` : ''
    const meta: string[] = []
    if (variantLabel.value) meta.push(esc(variantLabel.value))
    if (includePrice.value && v.salePriceCents != null) meta.push(money(v.salePriceCents))
    const metaLine = meta.length ? `<div class="meta">${meta.join(' &middot; ')}</div>` : ''

    const one = `<div class="label">${svg}${nameLine}${metaLine}</div>`
    const n = Math.max(1, Math.min(200, Math.round(quantity.value || 1)))
    const all = Array(n).fill(one).join('')

    const w = window.open('', '_blank', 'width=800,height=600')
    if (!w) { printError.value = 'The print window was blocked. Allow pop-ups and try again.'; return }
    w.document.write(`<!DOCTYPE html><html><head><title>Barcode labels</title><style>
        * { box-sizing: border-box; }
        body { font-family: Arial, Helvetica, sans-serif; margin: 8px; }
        .label { display: inline-flex; flex-direction: column; align-items: center; justify-content: center;
            width: 2.5in; height: 1in; border: 1px dashed #ccc; margin: 2px; padding: 4px;
            overflow: hidden; page-break-inside: avoid; }
        .label svg { max-width: 100%; height: auto; }
        .nm { font-size: 11px; font-weight: bold; text-align: center; white-space: nowrap;
            overflow: hidden; text-overflow: ellipsis; max-width: 100%; }
        .meta { font-size: 10px; color: #333; }
        @media print { .label { border: none; } @page { margin: 6mm; } }
    </style></head><body onload="window.print()">${all}</body></html>`)
    w.document.close()
    close()
}
</script>
