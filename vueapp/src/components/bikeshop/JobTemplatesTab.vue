<template>
    <div>
        <div class="d-flex align-center mb-3 ga-3 flex-wrap">
            <p class="text-body-2 text-medium-emphasis mb-0 flex-grow-1">
                Jobs you do over and over. Save the labor and parts once, then drop the whole job
                onto a work order instead of retyping it.
            </p>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">New job</v-btn>
        </div>

        <div v-if="loading" class="text-center py-6">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>
        <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>
        <v-card v-else-if="templates.length === 0" variant="outlined" class="pa-6 text-center text-medium-emphasis">
            No saved jobs yet. 
        </v-card>

        <v-table v-else density="compact">
            <thead>
                <tr>
                    <th>Job</th>
                    <th>Fits</th>
                    <th style="width: 160px">Contents</th>
                    <th style="width: 110px">Status</th>
                    <th style="width: 120px" class="text-right"></th>
                </tr>
            </thead>
            <tbody>
                <tr v-for="t in templates" :key="t.id">
                    <td>
                        <div class="font-weight-medium">{{ t.name }}</div>
                        <div v-if="t.notes" class="text-caption text-medium-emphasis text-truncate"
                            style="max-width: 320px">{{ t.notes }}</div>
                    </td>
                    <td class="text-caption text-medium-emphasis">{{ t.fitsNote || '—' }}</td>
                    <td class="text-caption text-medium-emphasis">{{ contentsLabel(t) }}</td>
                    <td>
                        <v-chip size="x-small" :color="t.isActive ? 'success' : 'grey'" variant="tonal">
                            {{ t.isActive ? 'Active' : 'Inactive' }}
                        </v-chip>
                    </td>
                    <td class="text-right" style="white-space: nowrap">
                        <v-btn size="x-small" variant="text" icon="mdi-pencil" @click="openEdit(t)"></v-btn>
                        <v-btn size="x-small" variant="text" icon="mdi-delete" @click="remove(t)"></v-btn>
                    </td>
                </tr>
            </tbody>
        </v-table>

        <!-- Editor -->
        <v-dialog v-model="dialog" max-width="720" persistent scrollable>
            <v-card class="d-flex flex-column" style="max-height: 90vh">
                <v-card-title class="d-flex align-center" style="flex: 0 0 auto">
                    <span class="text-body-1">{{ editing ? 'Edit job' : 'New job' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="saving"
                        @click="dialog = false"></v-btn>
                </v-card-title>
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <v-text-field v-model="form.name" label="Job name" density="compact" hide-details
                        placeholder="e.g. Fork seal service" :disabled="saving"></v-text-field>
                    <v-text-field v-model="form.fitsNote" label="Fits (optional)" density="compact"
                        class="mt-4" hide-details placeholder="e.g. 250F four-strokes"
                        :disabled="saving"></v-text-field>
                    <v-textarea v-model="form.notes" label="Notes added to the work order (optional)"
                        rows="2" auto-grow density="compact" class="mt-4" hide-details
                        :disabled="saving"></v-textarea>

                    <div class="d-flex align-center mt-5 mb-2">
                        <div class="text-subtitle-2">Lines</div>
                        <v-spacer></v-spacer>
                        <v-btn size="small" variant="tonal" prepend-icon="mdi-wrench"
                            :disabled="saving" @click="addLabor">Add labor</v-btn>
                        <v-btn size="small" variant="tonal" prepend-icon="mdi-package-variant" class="ml-2"
                            :disabled="saving" @click="addPart">Add part</v-btn>
                    </div>

                    <div v-if="form.lines.length === 0" class="text-caption text-medium-emphasis py-2">
                        No lines yet. Add the labor and any parts this job always uses.
                    </div>

                    <div v-for="(l, i) in form.lines" :key="i" class="d-flex align-center ga-2 py-1">
                        <v-chip size="x-small" variant="tonal" class="flex-shrink-0">
                            {{ l.lineKind === 'labor' ? 'Labor' : 'Part' }}
                        </v-chip>
                        <v-text-field v-if="l.lineKind === 'labor'" v-model="l.description"
                            label="Description" density="compact" hide-details :disabled="saving"></v-text-field>
                        <v-select v-else v-model="l.variantId" :items="partOptions" item-title="title"
                            item-value="id" label="Part" density="compact" hide-details
                            :disabled="saving"></v-select>
                        <v-text-field v-model.number="l.quantity" type="number" min="1"
                            label="Qty" density="compact" hide-details style="max-width: 84px"
                            :disabled="saving"></v-text-field>
                        <v-text-field v-model.number="l.priceDollars" type="number" min="0" step="0.5"
                            prefix="$" :label="l.lineKind === 'labor' ? 'Rate' : 'Price'"
                            density="compact" hide-details style="max-width: 120px"
                            :placeholder="l.lineKind === 'part' ? 'current' : ''"
                            :disabled="saving"></v-text-field>
                        <v-text-field v-if="l.lineKind === 'labor'" v-model.number="l.estMin" type="number" min="0"
                            label="Est. min" suffix="m" density="compact" hide-details style="max-width: 96px"
                            :disabled="saving"></v-text-field>
                        <v-btn icon="mdi-close" size="x-small" variant="text" :disabled="saving"
                            @click="form.lines.splice(i, 1)"></v-btn>
                    </div>

                    <p class="text-caption text-medium-emphasis mt-2">
                        Leave a part's price blank to always use that product's current price when
                        the job is applied.
                    </p>

                    <v-switch v-model="form.isActive" label="Active" color="primary" density="compact"
                        hide-details class="mt-2" :disabled="saving"></v-switch>
                    <div v-if="error" class="text-error text-caption mt-2">{{ error }}</div>
                </v-card-text>
                <v-card-actions style="flex: 0 0 auto">
                    <v-spacer></v-spacer>
                    <v-btn :disabled="saving" @click="dialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" :disabled="!canSave" @click="save">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackColor" :timeout="3000">{{ snackText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { BikeShopService, type ShopJobTemplate, type ShopProduct } from '@/services/BikeShopService'
import { useConfirm } from '@/composables/useConfirm'

const service = new BikeShopService()
const confirm = useConfirm()

interface EditLine {
    lineKind: 'labor' | 'part'
    description: string
    variantId: string | null
    quantity: number
    // Dollars in the form; converted to cents on save. Null/empty for a part means "use the
    // product's price at apply time".
    priceDollars: number | null
    // Labor standard time (minutes); auto-fills the estimate when the job is applied.
    estMin: number | null
}

const templates = ref<ShopJobTemplate[]>([])
const products = ref<ShopProduct[]>([])
const loading = ref(false)
const loadError = ref('')
const dialog = ref(false)
const saving = ref(false)
const error = ref('')
const editing = ref<ShopJobTemplate | null>(null)
const form = ref<{ name: string; fitsNote: string; notes: string; isActive: boolean; lines: EditLine[] }>(
    { name: '', fitsNote: '', notes: '', isActive: true, lines: [] })

const snackbar = ref(false)
const snackText = ref('')
const snackColor = ref<'success' | 'error'>('success')
function flash(t: string, c: 'success' | 'error' = 'success') {
    snackText.value = t; snackColor.value = c; snackbar.value = true
}

const partOptions = computed(() =>
    products.value.filter(p => p.isActive && p.isSellable).flatMap(p =>
        p.variants.filter(v => v.isActive).map(v => ({
            id: v.id,
            title: `${p.name}${[v.size, v.color].filter(Boolean).length ? ' (' + [v.size, v.color].filter(Boolean).join('/') + ')' : ''}`,
        }))))

const canSave = computed(() =>
    !!form.value.name.trim()
    && form.value.lines.every(l =>
        l.lineKind === 'labor' ? !!l.description.trim() : !!l.variantId))

function contentsLabel(t: ShopJobTemplate): string {
    const labor = t.lines.filter(l => l.lineKind === 'labor').length
    const parts = t.lines.filter(l => l.lineKind === 'part').length
    if (labor === 0 && parts === 0) return 'empty'
    return [labor ? `${labor} labor` : '', parts ? `${parts} part${parts === 1 ? '' : 's'}` : '']
        .filter(Boolean).join(', ')
}

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        const [t, p] = await Promise.all([service.listJobTemplates(false), service.listProducts(false)])
        templates.value = (t.data as any).data
        products.value = (p.data as any).data
    } catch (e: any) {
        loadError.value = e.response?.data?.error || 'Could not load saved jobs. Refresh to try again.'
    } finally {
        loading.value = false
    }
}

function openCreate() {
    editing.value = null
    form.value = { name: '', fitsNote: '', notes: '', isActive: true, lines: [] }
    error.value = ''
    dialog.value = true
}

function openEdit(t: ShopJobTemplate) {
    editing.value = t
    form.value = {
        name: t.name,
        fitsNote: t.fitsNote ?? '',
        notes: t.notes ?? '',
        isActive: t.isActive,
        lines: t.lines.map(l => ({
            lineKind: l.lineKind,
            description: l.description ?? '',
            variantId: l.variantId,
            quantity: l.quantity,
            priceDollars: l.unitPriceCents == null ? null : l.unitPriceCents / 100,
            estMin: l.estimatedMinutes ?? null,
        })),
    }
    error.value = ''
    dialog.value = true
}

function addLabor() {
    form.value.lines.push({ lineKind: 'labor', description: '', variantId: null, quantity: 1, priceDollars: 0, estMin: null })
}
function addPart() {
    form.value.lines.push({ lineKind: 'part', description: '', variantId: null, quantity: 1, priceDollars: null, estMin: null })
}

async function save() {
    if (!canSave.value) return
    saving.value = true
    error.value = ''
    try {
        await service.saveJobTemplate({
            id: editing.value?.id ?? null,
            name: form.value.name.trim(),
            fitsNote: form.value.fitsNote.trim() || null,
            notes: form.value.notes.trim() || null,
            isActive: form.value.isActive,
            sortOrder: 100,
            lines: form.value.lines.map(l => ({
                lineKind: l.lineKind,
                description: l.lineKind === 'labor' ? l.description.trim() : null,
                variantId: l.lineKind === 'part' ? l.variantId : null,
                quantity: l.quantity || 1,
                unitPriceCents: l.priceDollars == null || isNaN(l.priceDollars)
                    ? null : Math.round(l.priceDollars * 100),
                estimatedMinutes: l.lineKind === 'labor' && l.estMin != null && !isNaN(l.estMin)
                    ? Math.max(0, Math.round(l.estMin)) : null,
            })),
        })
        dialog.value = false
        await load()
        flash('Job saved.')
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not save this job. Try again.'
    } finally {
        saving.value = false
    }
}

async function remove(t: ShopJobTemplate) {
    const ok = await confirm({
        title: `Delete "${t.name}"?`,
        message: 'Work orders already using it keep their lines; only the saved job goes away.',
        confirmText: 'Delete',
        confirmColor: 'error',
    })
    if (!ok) return
    try {
        await service.deleteJobTemplate(t.id)
        await load()
        flash('Job deleted.')
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not delete that job.', 'error')
    }
}

onMounted(load)
</script>
