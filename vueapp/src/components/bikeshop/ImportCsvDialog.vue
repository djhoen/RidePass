<template>
    <v-dialog :model-value="modelValue" max-width="640" @update:model-value="$emit('update:modelValue', $event)">
        <v-card class="d-flex flex-column" style="max-height: 90vh">
            <v-card-title class="d-flex align-center" style="flex: 0 0 auto">
                <span>Import products from CSV</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" :disabled="importing" @click="close"></v-btn>
            </v-card-title>
            <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                <p class="text-body-2 mb-2">
                    One row per variant; rows sharing a Product name become one product. Categories
                    and suppliers are created automatically. Serialized products import with zero
                    stock (add each unit with its serial afterward).
                </p>
                <v-btn size="small" variant="text" prepend-icon="mdi-download" class="mb-3" @click="downloadTemplate">
                    Download template
                </v-btn>
                <v-file-input v-model="file" label="CSV file" accept=".csv,text/csv" density="compact"
                    prepend-icon="mdi-file-delimited" @update:model-value="preview = null"></v-file-input>

                <template v-if="preview">
                    <v-alert v-if="preview.errors.length === 0" type="success" variant="tonal" density="compact" class="mt-2">
                        Ready: {{ preview.products }} product{{ preview.products === 1 ? '' : 's' }},
                        {{ preview.variants }} variant{{ preview.variants === 1 ? '' : 's' }}{{ newThingsNote }}.
                    </v-alert>
                    <template v-else>
                        <v-alert type="error" variant="tonal" density="compact" class="mt-2 mb-2">
                            {{ preview.errors.length }} problem{{ preview.errors.length === 1 ? '' : 's' }} to fix before importing.
                        </v-alert>
                        <v-table density="compact">
                            <tbody>
                                <tr v-for="(e, i) in preview.errors" :key="i"><td class="text-caption text-error">{{ e }}</td></tr>
                            </tbody>
                        </v-table>
                    </template>
                </template>
                <div v-if="error" class="text-error text-body-2 mt-2">{{ error }}</div>
            </v-card-text>
            <v-card-actions style="flex: 0 0 auto">
                <v-spacer></v-spacer>
                <v-btn :disabled="importing" @click="close">Cancel</v-btn>
                <v-btn v-if="!preview || preview.errors.length > 0" color="primary" :loading="checking"
                    :disabled="!file" @click="runPreview">Check file</v-btn>
                <v-btn v-else color="primary" :loading="importing" @click="runImport">
                    Import {{ preview.variants }} variant{{ preview.variants === 1 ? '' : 's' }}
                </v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { BikeShopService, type ShopImportPreview } from '@/services/BikeShopService'

defineProps<{ modelValue: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void; (e: 'imported'): void; (e: 'flash', text: string, color?: 'success' | 'error'): void }>()

const service = new BikeShopService()
const file = ref<File | null>(null)
const preview = ref<ShopImportPreview | null>(null)
const checking = ref(false)
const importing = ref(false)
const error = ref('')

const newThingsNote = computed(() => {
    if (!preview.value) return ''
    const cats = Array.isArray(preview.value.newCategories) ? preview.value.newCategories.length : preview.value.newCategories
    const sups = Array.isArray(preview.value.newSuppliers) ? preview.value.newSuppliers.length : preview.value.newSuppliers
    const bits: string[] = []
    if (cats > 0) bits.push(`${cats} new categor${cats === 1 ? 'y' : 'ies'}`)
    if (sups > 0) bits.push(`${sups} new supplier${sups === 1 ? '' : 's'}`)
    return bits.length ? ` (${bits.join(', ')})` : ''
})

function close() {
    file.value = null
    preview.value = null
    error.value = ''
    emit('update:modelValue', false)
}

function downloadTemplate() {
    const csv = [
        'Product,Description,Brand,Category,Supplier,SKU,Barcode,Size,Color,Price,Cost,DailyRate,Deposit,Tracking,Stock,LowStockAt',
        'Team Jersey,Track team jersey,Fly Racing,Apparel,MX Distribution,JRS-M,,M,,39.99,16.00,,,pool,8,2',
        'Team Jersey,,,Apparel,MX Distribution,JRS-L,,L,,39.99,16.00,,,pool,8,2',
        'Trail Bike 250F,Race-ready 250F,Yamaha,Bikes,MX Distribution,BIKE-250F,,,,5499.00,4200.00,80.00,300.00,serialized,0,',
    ].join('\n')
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }))
    const a = document.createElement('a')
    a.href = url
    a.download = 'bike-shop-import-template.csv'
    a.click()
    URL.revokeObjectURL(url)
}

async function readFileText(): Promise<string | null> {
    if (!file.value) return null
    try { return await file.value.text() }
    catch { error.value = 'Could not read that file. Try re-selecting it.'; return null }
}

async function runPreview() {
    error.value = ''
    const text = await readFileText()
    if (text == null) return
    checking.value = true
    try {
        preview.value = (await service.importCsv(text, true)).data.data
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not check the file. Make sure it is a CSV and try again.'
    } finally { checking.value = false }
}

async function runImport() {
    error.value = ''
    const text = await readFileText()
    if (text == null) return
    importing.value = true
    try {
        const r = (await service.importCsv(text, false)).data.data
        emit('flash', `Imported ${r.products} products (${r.variants} variants).`, 'success')
        emit('imported')
        close()
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Import failed. Nothing was created; check the file and try again.'
    } finally { importing.value = false }
}
</script>
