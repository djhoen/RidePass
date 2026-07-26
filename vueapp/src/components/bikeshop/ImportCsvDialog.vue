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
                <p class="text-caption text-medium-emphasis mb-2">
                    <strong>Product</strong> is your own name for the row and stays private to your
                    shop. The optional <strong>ManufacturerName</strong> column is the name on the
                    box: supply it and a scan of that barcode can identify the part for you later.
                </p>
                <v-btn size="small" variant="text" prepend-icon="mdi-download" class="mb-3" @click="downloadTemplate">
                    Download template
                </v-btn>
                <v-file-input v-model="file" label="CSV file" accept=".csv,text/csv" density="compact"
                    prepend-icon="mdi-file-delimited" @update:model-value="preview = null"></v-file-input>

                <!-- The difference between a first load and a refresh. Off by default because a
                     silent rewrite of a live catalog is worse than an error telling you it exists. -->
                <v-checkbox v-model="updateExisting" density="compact" hide-details class="mt-1"
                    @update:model-value="preview = null">
                    <template #label>
                        <span class="text-body-2">Update products that already exist</span>
                    </template>
                </v-checkbox>
                <p class="text-caption text-medium-emphasis mb-2">
                    <template v-if="updateExisting">
                        Rows are matched to your catalog by barcode, then MPN, then SKU, and only the
                        columns in the file are written. Stock is never changed by an import.
                    </template>
                    <template v-else>
                        Anything already in your catalog will be reported as an error rather than changed.
                    </template>
                </p>

                <template v-if="preview">
                    <v-alert v-if="preview.errors.length === 0" type="success" variant="tonal" density="compact" class="mt-2">
                        Ready:
                        <template v-if="preview.variants > 0">
                            {{ preview.products }} new product{{ preview.products === 1 ? '' : 's' }},
                            {{ preview.variants }} new variant{{ preview.variants === 1 ? '' : 's' }}
                        </template>
                        <template v-if="preview.variants > 0 && preview.variantsUpdated > 0">, </template>
                        <template v-if="preview.variantsUpdated > 0">
                            {{ preview.variantsUpdated }} existing variant{{ preview.variantsUpdated === 1 ? '' : 's' }} refreshed
                        </template>
                        <template v-if="preview.variants === 0 && preview.variantsUpdated === 0">
                            nothing to do
                        </template>{{ newThingsNote }}.
                        <div v-if="preview.variantsUpdated > 0 && preview.columns?.length" class="text-caption mt-1">
                            Updating: {{ preview.columns.join(', ') }}. Everything else is left alone.
                        </div>
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
                <v-btn v-else color="primary" :loading="importing"
                    :disabled="preview.variants === 0 && preview.variantsUpdated === 0" @click="runImport">
                    {{ preview.variantsUpdated > 0 && preview.variants === 0
                        ? `Refresh ${preview.variantsUpdated} variant${preview.variantsUpdated === 1 ? '' : 's'}`
                        : `Import ${preview.variants + preview.variantsUpdated} variant${preview.variants + preview.variantsUpdated === 1 ? '' : 's'}` }}
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
const updateExisting = ref(false)
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
        // MPN and ManufacturerName are here because a distributor export carries both and they are
        // what make a barcode scan resolve later. "Product" is YOUR name for the row and stays
        // private; "ManufacturerName" is the name on the box and is the one shared field.
        'Product,Description,Brand,Category,Supplier,SKU,Barcode,MPN,ManufacturerName,Size,Color,Price,Cost,DailyRate,Deposit,Tracking,Stock,LowStockAt',
        'Team Jersey,Track team jersey,Fly Racing,Apparel,MX Distribution,JRS-M,,FR-JRS-24,Fly Racing Kinetic Jersey,M,,39.99,16.00,,,pool,8,2',
        'Team Jersey,,,Apparel,MX Distribution,JRS-L,,FR-JRS-24,Fly Racing Kinetic Jersey,L,,39.99,16.00,,,pool,8,2',
        'Trail Bike 250F,Race-ready 250F,Yamaha,Bikes,MX Distribution,BIKE-250F,,,,,,5499.00,4200.00,80.00,300.00,serialized,0,',
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
        preview.value = (await service.importCsv(text, true, updateExisting.value)).data.data
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
        const r = (await service.importCsv(text, false, updateExisting.value)).data.data
        const bits: string[] = []
        if (r.variants > 0) bits.push(`${r.variants} variant${r.variants === 1 ? '' : 's'} added`)
        if (r.variantsUpdated > 0) bits.push(`${r.variantsUpdated} refreshed`)
        emit('flash', bits.length ? `Import complete: ${bits.join(', ')}.` : 'Import complete: nothing changed.', 'success')
        emit('imported')
        close()
    } catch (e: any) {
        error.value = e.response?.data?.error
            || 'Import failed. Nothing was changed; check the file and try again.'
    } finally { importing.value = false }
}
</script>
