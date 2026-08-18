<template>
    <v-dialog :model-value="modelValue" max-width="640" @update:model-value="$emit('update:modelValue', $event)">
        <v-card class="d-flex flex-column" style="max-height: 90vh">
            <v-card-title class="d-flex align-center" style="flex: 0 0 auto">
                <span>Import gift cards</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" :disabled="importing" @click="close"></v-btn>
            </v-card-title>
            <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                <p class="text-body-2 mb-2">
                    Bring outstanding gift card balances over from a previous system. Each card
                    keeps its printed code, so customers redeem the same physical card at any
                    register or online. Imported cards don't email anyone and don't appear in
                    Purchases (they weren't sold through RidePass).
                </p>
                <p class="text-caption text-medium-emphasis mb-2">
                    CSV columns: <code>code,balance</code> with optional
                    <code>recipient_name,recipient_email</code>. Balance is in dollars
                    (e.g. <code>25.00</code>) — the card's remaining balance, not its original value.
                    Skip fully-spent cards.
                </p>
                <v-btn size="small" variant="text" prepend-icon="mdi-download" class="mb-3" @click="downloadTemplate">
                    Download template
                </v-btn>
                <v-file-input v-model="file" label="CSV file" accept=".csv,text/csv" density="compact"
                    prepend-icon="mdi-file-delimited" @update:model-value="report = null"></v-file-input>
                <v-text-field v-model="source" label="Where are these cards from?" density="compact"
                    class="mt-4" hide-details placeholder="e.g. Card Dog / old POS"></v-text-field>

                <template v-if="report">
                    <v-alert :type="report.errors.length === 0 ? 'success' : 'warning'" variant="tonal"
                        density="compact" class="mt-4">
                        {{ report.imported }} card{{ report.imported === 1 ? '' : 's' }}
                        {{ report.dryRun ? 'ready to import' : 'imported' }}
                        ({{ money(report.totalBalanceCents) }} total balance)<template v-if="report.errors.length">,
                        {{ report.errors.length }} row{{ report.errors.length === 1 ? '' : 's' }} skipped</template>.
                    </v-alert>
                    <v-table v-if="report.errors.length" density="compact" class="mt-2">
                        <tbody>
                            <tr v-for="(e, i) in report.errors" :key="i">
                                <td class="text-caption text-error">
                                    Line {{ e.line }}<template v-if="e.code"> ({{ e.code }})</template>: {{ e.reason }}
                                </td>
                            </tr>
                        </tbody>
                    </v-table>
                </template>
                <div v-if="error" class="text-error text-body-2 mt-2">{{ error }}</div>
            </v-card-text>
            <v-card-actions style="flex: 0 0 auto">
                <v-spacer></v-spacer>
                <v-btn :disabled="importing" @click="close">{{ report && !report.dryRun ? 'Done' : 'Cancel' }}</v-btn>
                <v-btn v-if="!report || !report.dryRun" color="primary" :loading="checking"
                    :disabled="!file" @click="run(true)">Check file</v-btn>
                <v-btn v-else color="primary" :loading="importing" :disabled="report.imported === 0"
                    @click="run(false)">
                    Import {{ report.imported }} card{{ report.imported === 1 ? '' : 's' }}
                </v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { GiftCardService, type GiftCardImportReport } from '@/services/GiftCardService'

defineProps<{ modelValue: boolean }>()
const emit = defineEmits<{
    (e: 'update:modelValue', v: boolean): void
    (e: 'imported'): void
    (e: 'flash', text: string, color?: 'success' | 'error'): void
}>()

const service = new GiftCardService()
const file = ref<File | null>(null)
const source = ref('')
const report = ref<GiftCardImportReport | null>(null)
const checking = ref(false)
const importing = ref(false)
const error = ref('')

function money(cents: number) { return `$${(cents / 100).toFixed(2)}` }

function close() {
    file.value = null
    report.value = null
    error.value = ''
    emit('update:modelValue', false)
}

function downloadTemplate() {
    const csv = [
        'code,balance,recipient_name,recipient_email',
        'GC-100234,25.00,,',
        'GC-100235,12.50,Jamie Rider,jamie@example.com',
    ].join('\n')
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }))
    const a = document.createElement('a')
    a.href = url
    a.download = 'gift-card-import-template.csv'
    a.click()
    URL.revokeObjectURL(url)
}

async function run(dryRun: boolean) {
    error.value = ''
    if (!file.value) return
    let text: string
    try { text = await file.value.text() }
    catch { error.value = 'Could not read that file. Try re-selecting it.'; return }
    const busy = dryRun ? checking : importing
    busy.value = true
    try {
        const r = (await service.adminImport(text, dryRun, source.value.trim() || null)).data.data
        report.value = r
        if (!dryRun) {
            emit('flash', `Imported ${r.imported} gift card${r.imported === 1 ? '' : 's'} (${money(r.totalBalanceCents)} in balances).`, 'success')
            emit('imported')
        }
    } catch (e: any) {
        error.value = e.response?.data?.error
            || (dryRun ? 'Could not check the file. Make sure it is a CSV and try again.'
                : 'Import failed. Check the file and try again — already-imported codes are skipped, not duplicated.')
    } finally { busy.value = false }
}
</script>
