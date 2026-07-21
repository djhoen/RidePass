<template>
    <v-dialog :model-value="modelValue" @update:model-value="$emit('update:modelValue', $event)"
        max-width="900" scrollable>
        <v-card v-if="inspection" class="d-flex flex-column" style="max-height: 92vh">
            <v-card-title class="d-flex align-center" style="flex: 0 0 auto">
                <span>Inspection</span>
                <v-chip size="small" class="ml-2"
                    :color="inspection.status === 'complete' ? 'success' : 'grey'">
                    {{ inspection.status === 'complete' ? 'Complete' : 'Draft' }}
                </v-chip>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" @click="close"></v-btn>
            </v-card-title>

            <v-tabs v-model="view" :height="40" class="mb-2 sub-tabs"
                hide-slider selected-class="sub-tab-active" style="flex: 0 0 auto">
                <v-tab value="grade" class="sub-tab">Grade</v-tab>
                <v-tab value="customer" class="sub-tab">Customer view</v-tab>
            </v-tabs>

            <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                <!-- ── Mechanic grading ─────────────────────────────────────── -->
                <template v-if="view === 'grade'">
                    <div class="d-flex ga-2 align-center mb-3 flex-wrap">
                        <v-btn size="small" variant="tonal" prepend-icon="mdi-check-all"
                            @click="markRemainingGood">Mark remaining good</v-btn>
                        <span class="text-caption text-medium-emphasis">
                            {{ gradedCount }} of {{ inspection.results.length }} graded
                        </span>
                        <v-spacer></v-spacer>
                        <v-chip size="small" color="error" variant="tonal">{{ counts.attention }} needs work</v-chip>
                        <v-chip size="small" color="warning" variant="tonal">{{ counts.monitor }} monitor</v-chip>
                    </div>

                    <div v-for="g in groups" :key="g.name" class="mb-4">
                        <div class="text-subtitle-2 mb-1">{{ g.name }}</div>
                        <div v-for="r in g.items" :key="r.id"
                            class="d-flex align-center ga-2 py-1 flex-wrap insp-row">
                            <div style="min-width: 200px; flex: 1">{{ r.label }}</div>
                            <!-- Colour scale every shop already speaks. Monitor is the one that
                                 earns money later: a documented "we told you in March". -->
                            <v-btn-toggle :model-value="r.rating" density="compact" divided
                                @update:model-value="v => setRating(r, v)">
                                <v-btn value="good" size="small" color="success">Good</v-btn>
                                <v-btn value="monitor" size="small" color="warning">Monitor</v-btn>
                                <v-btn value="attention" size="small" color="error">Needs work</v-btn>
                                <v-btn value="na" size="small">N/A</v-btn>
                            </v-btn-toggle>
                            <v-text-field v-model="r.notes" density="compact" hide-details
                                placeholder="Note (optional)" style="min-width: 200px; flex: 1"></v-text-field>
                        </div>
                    </div>

                    <v-divider class="my-3"></v-divider>
                    <v-textarea v-model="summaryNotes" label="Summary for the customer" rows="2"
                        auto-grow density="compact" hide-details></v-textarea>
                    <v-text-field v-model="nextServiceDate" type="date" label="Next service due"
                        density="compact" class="mt-4" style="max-width: 220px"
                        hint="Defaults to six months out." persistent-hint></v-text-field>
                </template>

                <!-- ── Customer view ────────────────────────────────────────── -->
                <template v-else>
                    <div class="d-flex ga-3 mb-4 flex-wrap">
                        <v-card variant="tonal" color="error" class="pa-3" style="min-width: 150px">
                            <div class="text-h5">{{ counts.attention }}</div>
                            <div class="text-caption">Needs attention now</div>
                        </v-card>
                        <v-card variant="tonal" color="warning" class="pa-3" style="min-width: 150px">
                            <div class="text-h5">{{ counts.monitor }}</div>
                            <div class="text-caption">Wearing, keep an eye on</div>
                        </v-card>
                        <v-card variant="tonal" color="success" class="pa-3" style="min-width: 150px">
                            <div class="text-h5">{{ counts.good }}</div>
                            <div class="text-caption">Checked and good</div>
                        </v-card>
                    </div>

                    <v-alert v-if="summaryNotes" type="info" variant="tonal" density="compact" class="mb-3">
                        {{ summaryNotes }}
                    </v-alert>

                    <!-- Attention and monitor first: what the customer is actually deciding about. -->
                    <template v-for="bucket in customerBuckets" :key="bucket.rating">
                        <div v-if="bucket.items.length" class="mb-4">
                            <div class="d-flex align-center ga-2 mb-1">
                                <v-icon :color="ratingColor(bucket.rating)" size="18">{{ bucket.icon }}</v-icon>
                                <strong>{{ bucket.title }}</strong>
                            </div>
                            <div v-for="r in bucket.items" :key="r.id" class="ml-6 py-1">
                                <div class="text-body-2">
                                    {{ r.label }}
                                    <span class="text-caption text-medium-emphasis">· {{ r.groupLabel }}</span>
                                </div>
                                <div v-if="r.notes" class="text-caption text-medium-emphasis">{{ r.notes }}</div>
                            </div>
                        </div>
                    </template>

                    <div v-if="nextServiceDate" class="text-body-2 mt-2">
                        <strong>Next service due:</strong> {{ formatDate(nextServiceDate) }}
                    </div>
                </template>
            </v-card-text>

            <v-card-actions style="flex: 0 0 auto">
                <v-btn v-if="view === 'customer'" variant="text" prepend-icon="mdi-printer"
                    @click="printCustomerView">Print</v-btn>
                <v-spacer></v-spacer>
                <v-btn :disabled="saving" @click="close">Close</v-btn>
                <v-btn variant="tonal" :loading="saving && pendingStatus === 'draft'"
                    @click="save('draft')">Save draft</v-btn>
                <v-btn color="primary" :loading="saving && pendingStatus === 'complete'"
                    @click="save('complete')">
                    {{ inspection.status === 'complete' ? 'Save' : 'Mark complete' }}
                </v-btn>
            </v-card-actions>
        </v-card>

        <v-card v-else class="pa-8 text-center">
            <v-progress-circular indeterminate></v-progress-circular>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import dayjs from 'dayjs'
import { BikeShopService, type ShopInspection, type ShopInspectionResult, type ShopInspectionRating } from '@/services/BikeShopService'

const props = defineProps<{
    modelValue: boolean
    inspectionId: string | null
    bikeName?: string | null
}>()
const emit = defineEmits<{
    (e: 'update:modelValue', v: boolean): void
    (e: 'saved'): void
    (e: 'flash', text: string, color?: 'success' | 'error'): void
}>()

const service = new BikeShopService()
const inspection = ref<ShopInspection | null>(null)
const view = ref<'grade' | 'customer'>('grade')
const saving = ref(false)
const pendingStatus = ref<'draft' | 'complete' | null>(null)
const summaryNotes = ref('')
const nextServiceDate = ref<string | null>(null)

const groups = computed(() => {
    const out: { name: string; items: ShopInspectionResult[] }[] = []
    for (const r of [...(inspection.value?.results ?? [])].sort((a, b) => a.sortOrder - b.sortOrder)) {
        let g = out.find(x => x.name === r.groupLabel)
        if (!g) { g = { name: r.groupLabel, items: [] }; out.push(g) }
        g.items.push(r)
    }
    return out
})

const counts = computed(() => {
    const rs = inspection.value?.results ?? []
    return {
        good: rs.filter(r => r.rating === 'good').length,
        monitor: rs.filter(r => r.rating === 'monitor').length,
        attention: rs.filter(r => r.rating === 'attention').length,
    }
})
// "Graded" excludes N/A: an untouched row and a deliberate N/A both read as na, so this is a
// progress hint rather than a guarantee everything was looked at.
const gradedCount = computed(() =>
    (inspection.value?.results ?? []).filter(r => r.rating !== 'na').length)

// The customer cares about problems first; the all-clear list is reassurance, not news.
const customerBuckets = computed(() => {
    const rs = inspection.value?.results ?? []
    return [
        { rating: 'attention', title: 'Needs attention now', icon: 'mdi-alert-circle', items: rs.filter(r => r.rating === 'attention') },
        { rating: 'monitor', title: 'Wearing — keep an eye on', icon: 'mdi-alert', items: rs.filter(r => r.rating === 'monitor') },
        { rating: 'good', title: 'Checked and good', icon: 'mdi-check-circle', items: rs.filter(r => r.rating === 'good') },
    ]
})

function ratingColor(r: string) {
    return r === 'attention' ? 'error' : r === 'monitor' ? 'warning' : r === 'good' ? 'success' : 'grey'
}
function formatDate(d: string) { return dayjs(d).format('MMM D, YYYY') }

function setRating(r: ShopInspectionResult, v: ShopInspectionRating | undefined) {
    // v-btn-toggle clears on re-click; treat that as N/A rather than an invalid empty rating.
    r.rating = v ?? 'na'
}

function markRemainingGood() {
    for (const r of inspection.value?.results ?? []) {
        if (r.rating === 'na') r.rating = 'good'
    }
}

async function load(id: string) {
    inspection.value = null
    try {
        const r = await service.getInspection(id)
        inspection.value = r.data.data
        summaryNotes.value = r.data.data.summaryNotes ?? ''
        nextServiceDate.value = r.data.data.nextServiceDate
            ? dayjs(r.data.data.nextServiceDate).format('YYYY-MM-DD') : null
        view.value = r.data.data.status === 'complete' ? 'customer' : 'grade'
    } catch (e: any) {
        emit('flash', e.response?.data?.error || 'Could not load that inspection.', 'error')
        emit('update:modelValue', false)
    }
}

async function save(status: 'draft' | 'complete') {
    if (!inspection.value) return
    saving.value = true
    pendingStatus.value = status
    try {
        const r = await service.saveInspection(inspection.value.id, {
            status,
            nextServiceDate: nextServiceDate.value || null,
            summaryNotes: summaryNotes.value?.trim() || null,
            results: inspection.value.results.map(x => ({ id: x.id, rating: x.rating, notes: x.notes })),
        })
        inspection.value = r.data.data
        emit('saved')
        emit('flash', status === 'complete' ? 'Inspection marked complete.' : 'Inspection saved.')
        if (status === 'complete') view.value = 'customer'
    } catch (e: any) {
        emit('flash', e.response?.data?.error || 'Could not save the inspection.', 'error')
    } finally {
        saving.value = false
        pendingStatus.value = null
    }
}

function close() { emit('update:modelValue', false) }

// Printed from a plain window rather than by styling the dialog: fighting print CSS inside a
// scrollable modal reliably produces a clipped page.
function printCustomerView() {
    const esc = (t: string) => t.replace(/[&<>]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c] as string))
    const section = (title: string, colour: string, items: ShopInspectionResult[]) => items.length === 0 ? '' : `
        <h2 style="color:${colour};font-size:15px;margin:18px 0 6px">${esc(title)}</h2>
        ${items.map(r => `
            <div style="padding:4px 0;border-bottom:1px solid #eee">
                <div>${esc(r.label)} <span style="color:#888;font-size:12px">· ${esc(r.groupLabel)}</span></div>
                ${r.notes ? `<div style="color:#666;font-size:12px">${esc(r.notes)}</div>` : ''}
            </div>`).join('')}`

    const b = customerBuckets.value
    const html = `<!doctype html><html><head><title>Inspection</title></head>
        <body style="font-family:system-ui,sans-serif;max-width:720px;margin:24px auto;color:#222">
        <h1 style="font-size:20px;margin:0 0 4px">Inspection report</h1>
        ${props.bikeName ? `<div style="color:#666;margin-bottom:12px">${esc(props.bikeName)}</div>` : ''}
        <div style="margin:12px 0;color:#666;font-size:13px">
            ${counts.value.attention} need attention &middot; ${counts.value.monitor} wearing &middot; ${counts.value.good} good
        </div>
        ${summaryNotes.value ? `<p style="background:#f5f5f5;padding:10px;border-radius:4px">${esc(summaryNotes.value)}</p>` : ''}
        ${section(b[0].title, '#c62828', b[0].items)}
        ${section(b[1].title, '#ef6c00', b[1].items)}
        ${section(b[2].title, '#2e7d32', b[2].items)}
        ${nextServiceDate.value ? `<p style="margin-top:20px"><strong>Next service due:</strong> ${esc(formatDate(nextServiceDate.value))}</p>` : ''}
        </body></html>`

    const w = window.open('', '_blank')
    if (!w) { emit('flash', 'Allow pop-ups to print the inspection.', 'error'); return }
    w.document.write(html)
    w.document.close()
    w.focus()
    w.print()
}

watch(() => [props.modelValue, props.inspectionId], ([open, id]) => {
    if (open && typeof id === 'string') load(id)
}, { immediate: true })
</script>

<style scoped>
.insp-row:hover { background: rgba(var(--v-theme-on-surface), 0.03); }
.sub-tabs {
    background: rgba(var(--v-theme-on-surface), 0.04);
    border-radius: 4px;
    padding: 4px;
    display: inline-flex;
    flex: 0 0 auto;
    margin-left: 16px;
}
.sub-tabs :deep(.v-slide-group__content) { gap: 4px; align-items: center; }
.sub-tabs :deep(.v-tab) {
    border-radius: 4px; height: 32px; min-height: 32px; min-width: 0;
    padding: 0 18px; font-size: 13px; text-transform: none; opacity: 0.75;
}
.sub-tabs :deep(.v-tab.sub-tab-active), .sub-tabs :deep(.v-tab--selected) {
    background: rgba(var(--v-theme-primary), 0.14);
    color: rgb(var(--v-theme-primary));
    opacity: 1; font-weight: 600;
}
</style>
