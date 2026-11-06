<template>
    <v-dialog :model-value="modelValue" max-width="640" scrollable @update:model-value="$emit('update:modelValue', $event)">
        <v-card>
            <v-card-title class="d-flex align-center">
                <span>{{ editMode ? 'Edit listing' : 'List for sale' }}</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" :disabled="saving" @click="close"></v-btn>
            </v-card-title>

            <v-card-text>
                <!-- What's being listed: read-only identity so the cashier can see they picked the right unit. -->
                <div class="ld-unit mb-4">
                    <div class="text-caption text-muted">Unit</div>
                    <div class="text-body-2">
                        <strong>{{ unitLabel }}</strong>
                        <span v-if="serial" class="text-muted"> · {{ serial }}</span>
                    </div>
                    <div v-if="current" class="d-flex align-center ga-2 mt-1 flex-wrap">
                        <span class="rp-pill" :class="statePillClass(current.state)">{{ current.state }}</span>
                        <span v-if="current.state === 'sold' && current.soldOrderNumber != null" class="text-caption text-muted">
                            Order #{{ current.soldOrderNumber }}
                        </span>
                    </div>
                </div>

                <v-alert v-if="readOnly" type="info" variant="tonal" density="compact" class="mb-4">
                    {{ current?.state === 'sold'
                        ? 'This unit has been sold. The listing is kept for the record.'
                        : `This listing was unlisted and the unit went ${unlistDestination(current?.priorStatus)}.` }}
                </v-alert>

                <!-- Opened from the Units dialog with just a listing id: wait for GET ShopListing/{id}
                     rather than showing a create form that would try to list the unit twice. -->
                <div v-if="waitingForListing" class="text-center py-6">
                    <v-progress-circular v-if="loadingListing" indeterminate size="28" color="primary"></v-progress-circular>
                    <div v-else class="text-error text-body-2">{{ error || 'Could not load the listing.' }}</div>
                </div>
                <template v-else>
                <v-text-field v-model="form.title" label="Title" density="compact" :disabled="readOnly"
                    hint="What the buyer sees. Leave as is for the default." persistent-hint class="mb-3"></v-text-field>

                <v-row dense>
                    <v-col cols="12" sm="6">
                        <v-text-field v-model.number="form.askingDollars" label="Asking price" type="number" prefix="$"
                            min="0" step="0.01" inputmode="decimal" autocomplete="off" density="compact"
                            :disabled="readOnly" :error-messages="priceError" :hint="costHint" persistent-hint></v-text-field>
                    </v-col>
                    <v-col cols="12" sm="6">
                        <v-select v-model="form.conditionGrade" :items="CONDITION_GRADES" item-title="title" item-value="value"
                            label="Condition" density="compact" :disabled="readOnly" :hint="gradeHint" persistent-hint></v-select>
                    </v-col>
                </v-row>

                <v-textarea v-model="form.conditionNotes" label="Condition notes" rows="2" auto-grow density="compact"
                    :disabled="readOnly" class="mt-3"
                    hint="Scratches, replaced parts, service history. Shown to the buyer." persistent-hint></v-textarea>

                <v-switch v-model="form.published" color="primary" density="compact" hide-details :disabled="readOnly"
                    class="mt-2" :label="form.published ? 'Show online' : 'Counter only (not shown online)'"></v-switch>

                <div v-if="error" class="text-error text-body-2 mt-2">{{ error }}</div>

                <!-- ── Photos: only once the listing exists (the upload needs its id) ──────── -->
                <template v-if="current">
                    <v-divider class="my-4"></v-divider>
                    <div class="d-flex align-center ga-2 mb-2 flex-wrap">
                        <div class="text-subtitle-2">Photos</div>
                        <v-chip v-if="photos.length" size="x-small" variant="tonal">{{ photos.length }}</v-chip>
                        <v-spacer></v-spacer>
                        <v-btn size="small" variant="tonal" prepend-icon="mdi-camera-plus" :disabled="readOnly || uploading"
                            @click="pickFiles">Add photos</v-btn>
                    </div>
                    <p class="text-caption text-muted mb-2">
                        The first photo is the cover. Drag to reorder, or use the arrows.
                    </p>
                    <v-text-field v-if="!readOnly" v-model="captionForNew" label="Caption for the next photos (optional)"
                        density="compact" hide-details class="mb-3"></v-text-field>
                    <input ref="fileInput" type="file" accept="image/png,image/jpeg,image/webp" class="d-none" multiple
                        @change="onFilesPicked" />

                    <div v-if="uploads.length" class="mb-3">
                        <div v-for="u in uploads" :key="u.key" class="mb-2">
                            <div class="d-flex align-center ga-2 text-caption">
                                <span class="text-truncate" style="min-width: 0">{{ u.name }}</span>
                                <v-spacer></v-spacer>
                                <span v-if="u.error" class="text-error">{{ u.error }}</span>
                                <span v-else>{{ u.pct }}%</span>
                            </div>
                            <v-progress-linear :model-value="u.pct" :color="u.error ? 'error' : 'primary'" height="4" rounded></v-progress-linear>
                        </div>
                    </div>

                    <div v-if="photos.length === 0 && uploads.length === 0" class="text-caption text-muted py-2">
                        No photos yet. The product photo is used as the cover until you add one.
                    </div>
                    <div v-else class="ld-grid">
                        <div v-for="(p, i) in photos" :key="p.id" class="ld-tile"
                            :class="{ 'ld-tile--over': dragOver === i, 'ld-tile--dragging': dragging === i }"
                            :draggable="!readOnly && !reordering" @dragstart="onDragStart(i, $event)" @dragover.prevent="dragOver = i"
                            @dragleave="dragOver = dragOver === i ? null : dragOver" @drop.prevent="onDrop(i)" @dragend="onDragEnd">
                            <v-img :src="absoluteUrl(p.url) ?? ''" :alt="p.caption || 'Listing photo'" aspect-ratio="1" cover class="rounded"></v-img>
                            <span v-if="i === 0" class="rp-pill rp-pill--solid ld-cover">Cover</span>
                            <div v-if="!readOnly" class="ld-tools">
                                <v-btn icon="mdi-chevron-left" size="x-small" variant="flat" :disabled="i === 0 || reordering"
                                    aria-label="Move earlier" @click.stop="move(i, -1)"></v-btn>
                                <v-btn icon="mdi-chevron-right" size="x-small" variant="flat" :disabled="i === photos.length - 1 || reordering"
                                    aria-label="Move later" @click.stop="move(i, 1)"></v-btn>
                                <v-btn icon="mdi-delete-outline" size="x-small" variant="flat" color="error" :loading="deletingId === p.id"
                                    aria-label="Delete photo" @click.stop="removePhoto(p)"></v-btn>
                            </div>
                            <div v-if="p.caption" class="text-caption text-truncate mt-1">{{ p.caption }}</div>
                        </div>
                    </div>
                    <div v-if="photoError" class="text-error text-caption mt-2">{{ photoError }}</div>
                </template>
                </template>
            </v-card-text>

            <v-card-actions class="flex-wrap">
                <v-tooltip v-if="current && canUnlist" :text="`Unlist: ${unlistDestination(current.priorStatus)}`" location="top">
                    <template #activator="{ props: tp }">
                        <v-btn v-bind="tp" variant="text" color="error" :loading="unlisting" :disabled="saving"
                            @click="unlist">Unlist</v-btn>
                    </template>
                </v-tooltip>
                <v-spacer></v-spacer>
                <v-btn variant="text" :disabled="saving" @click="close">{{ current ? 'Done' : 'Cancel' }}</v-btn>
                <v-btn v-if="!readOnly" color="primary" :loading="saving" :disabled="waitingForListing" @click="save">
                    {{ editMode ? 'Save changes' : 'List for sale' }}
                </v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, reactive, computed, watch } from 'vue'
import type { ShopItem, ShopVariant } from '@/services/BikeShopService'
import {
    ShopListingService, CONDITION_GRADES, unlistDestination,
    type ShopListing, type ListingPhoto, type ListingConditionGrade, type ListingState,
} from '@/services/ShopListingService'
import { absoluteUrl } from '@/helpers/ImageUrl'
import { useConfirm } from '@/composables/useConfirm'

// Create mode: `item` (+ `variant`, `productName`), no `listing` / `listingId`. Edit mode: either
// `listing` (a row already on screen, refreshed from GET ShopListing/{id} on open) or `listingId`
// alone (loaded from GET ShopListing/{id}). After a successful create the dialog flips into edit
// mode on the returned listing so photos can be added without reopening (the upload needs the id).
const props = defineProps<{
    modelValue: boolean
    item?: ShopItem | null
    variant?: ShopVariant | null
    productName?: string | null
    listing?: ShopListing | null
    listingId?: string | null
}>()
const emit = defineEmits<{
    (e: 'update:modelValue', v: boolean): void
    (e: 'saved', listing: ShopListing): void
    (e: 'unlisted', listing: ShopListing): void
    /** The open-time GET returned a fresher copy than the caller passed in (no write happened). */
    (e: 'refreshed', listing: ShopListing): void
    (e: 'flash', text: string, color?: 'success' | 'error'): void
}>()

const service = new ShopListingService()
const confirm = useConfirm()

const current = ref<ShopListing | null>(null)
const saving = ref(false)
const unlisting = ref(false)
const error = ref('')
const form = ref<{
    title: string
    askingDollars: number | null | ''
    conditionGrade: ListingConditionGrade
    conditionNotes: string
    published: boolean
}>({ title: '', askingDollars: null, conditionGrade: 'good', conditionNotes: '', published: true })

const loadingListing = ref(false)
const editMode = computed(() => current.value != null || !!props.listing || !!props.listingId)
/** Edit mode with nothing loaded yet (still loading, or the load failed). */
const waitingForListing = computed(() => editMode.value && current.value == null)
const readOnly = computed(() => current.value != null && (current.value.state === 'sold' || current.value.state === 'unlisted'))
const canUnlist = computed(() => current.value != null && (current.value.state === 'active' || current.value.state === 'reserved'))

const variantLabel = computed(() => {
    if (current.value) return current.value.variantLabel ?? ''
    const v = props.variant
    return v ? [v.size, v.color, v.gender].filter(Boolean).join(' / ') : ''
})
const unitLabel = computed(() => {
    if (current.value) return [current.value.productName, current.value.variantLabel, '·', current.value.unitLabel].filter(Boolean).join(' ')
    return [props.productName, variantLabel.value, props.item ? `· ${props.item.label}` : ''].filter(Boolean).join(' ')
})
const serial = computed(() => current.value?.serial ?? props.item?.serial ?? null)
const acquiredCostCents = computed(() => current.value?.acquiredCostCents ?? props.item?.acquiredCostCents ?? null)

// Dollars in, cents stored. A cleared number field reads '' (not 0), so guard with hasAmount
// rather than coercing: a blank price must block Save, not list the bike for $0.
function hasAmount(d: unknown): d is number { return typeof d === 'number' && Number.isFinite(d) }
const askingCents = computed(() => hasAmount(form.value.askingDollars) ? Math.round(form.value.askingDollars * 100) : 0)
const priceTouched = ref(false)
const priceError = computed(() => priceTouched.value && askingCents.value <= 0 ? 'Enter an asking price.' : '')
const costHint = computed(() => {
    const cost = acquiredCostCents.value
    if (cost == null || cost <= 0) return 'What you paid is not on file; price it by eye.'
    const margin = askingCents.value - cost
    return `Paid ${money(cost)}${askingCents.value > 0 ? ` · margin ${margin < 0 ? '-' : ''}${money(Math.abs(margin))}` : ''}`
})
const gradeHint = computed(() => CONDITION_GRADES.find(g => g.value === form.value.conditionGrade)?.hint ?? '')

function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
function statePillClass(s: ListingState): string {
    return s === 'active' ? 'rp-pill--green' : s === 'reserved' ? 'rp-pill--blue' : s === 'sold' ? 'rp-pill--solid' : ''
}

// Prefill only, to save typing: the server's ListingRules is authoritative. It writes
// DefaultTitle when the title comes back blank and runs ValidatePrice on every save, so these
// two helpers never decide anything. The price starts at the acquired cost rounded UP to the
// nearest $5 (a sane floor for an ex-rental), and stays blank when the cost is unknown so nobody
// lists at an accidental $0.
function defaultDollars(): number | null {
    const cost = props.item?.acquiredCostCents
    if (cost == null || cost <= 0) return null
    return Math.ceil(cost / 500) * 5
}
function defaultTitle(): string {
    return [props.productName, variantLabel.value].filter(Boolean).join(' ') + (props.item ? ` · ${props.item.label}` : '')
}

function sortPhotos(list: ListingPhoto[] | null | undefined): ListingPhoto[] {
    return [...(list ?? [])].sort((a, b) => a.sortOrder - b.sortOrder)
}
// The form as last seeded from a listing: a refresh only reseeds fields the user hasn't edited.
let seededForm = ''
function seedForm(l: ShopListing) {
    form.value = {
        title: l.title,
        askingDollars: l.askingPriceCents / 100,
        conditionGrade: l.conditionGrade,
        conditionNotes: l.conditionNotes ?? '',
        published: l.published,
    }
    seededForm = JSON.stringify(form.value)
}

// Bumped on every open and close, so a GET that lands after the dialog moved on is dropped.
let openSeq = 0

function reset() {
    const seq = ++openSeq
    error.value = ''
    photoError.value = ''
    priceTouched.value = false
    uploads.value = []
    captionForNew.value = ''
    current.value = props.listing ?? null
    if (current.value) {
        seedForm(current.value)
        photos.value = sortPhotos(current.value.photos)
    } else {
        form.value = props.listingId
            ? { title: '', askingDollars: null, conditionGrade: 'good', conditionNotes: '', published: true }
            : { title: defaultTitle(), askingDollars: defaultDollars(), conditionGrade: 'good', conditionNotes: '', published: true }
        seededForm = ''
        photos.value = []
    }
    const id = props.listing?.id ?? props.listingId ?? null
    if (id) void refresh(id, seq)
}
watch(() => props.modelValue, open => { if (open) reset(); else openSeq++ })

/**
 * Load or refresh one listing from GET ShopListing/{id}. On open it loads a listing given only by
 * id (from the Units dialog) or brings a table row up to date (state, priorStatus, photos); after a
 * thin upload reply it re-syncs the photos. Fields the user already edited are left alone.
 */
async function refresh(id: string, seq = openSeq) {
    const waiting = current.value == null
    if (waiting) loadingListing.value = true
    try {
        const fresh = await service.get(id)
        if (seq !== openSeq) return
        const untouched = JSON.stringify(form.value) === seededForm
        current.value = fresh
        photos.value = sortPhotos(fresh.photos)
        if (waiting || untouched) seedForm(fresh)
        emit('refreshed', fresh)
    } catch (e: any) {
        if (seq !== openSeq) return
        // Refreshing a listing already on screen fails quietly (keep what we have); a load by id
        // that fails leaves nothing to edit, so say why.
        if (waiting) {
            error.value = e.response?.status === 404
                ? 'That listing no longer exists. Close this and refresh.'
                : (e.response?.data?.error || 'Could not load the listing. Close this and try again.')
        }
    } finally { if (seq === openSeq) loadingListing.value = false }
}

function close() { emit('update:modelValue', false) }

async function save() {
    // Edit mode before the listing has loaded: never fall through to a create (that would try to
    // list the unit a second time).
    if (waitingForListing.value) return
    error.value = ''
    priceTouched.value = true
    if (askingCents.value <= 0) return
    saving.value = true
    try {
        const body = {
            askingPriceCents: askingCents.value,
            conditionGrade: form.value.conditionGrade,
            conditionNotes: form.value.conditionNotes.trim() || null,
            title: form.value.title.trim() || null,
            published: form.value.published,
        }
        if (current.value) {
            const saved = await service.update(current.value.id, body)
            current.value = { ...current.value, ...saved, photos: saved?.photos ?? current.value.photos }
            emit('saved', current.value)
            emit('flash', 'Listing saved.')
            close()
        } else {
            if (!props.item) { error.value = 'Pick a unit first.'; return }
            const created = await service.create({ itemId: props.item.id, ...body })
            current.value = created
            seedForm(created)
            photos.value = sortPhotos(created.photos)
            emit('saved', created)
            emit('flash', created.published ? 'Listed for sale. It is live online now; add photos below.' : 'Listed at the counter. Add photos below.')
        }
    } catch (e: any) {
        const status = e.response?.status
        error.value = e.response?.data?.error
            || (status === 409 ? 'This unit already has an active listing.'
                : status === 404 ? 'Used sales are not available on this server yet.'
                : 'Could not save the listing. Please try again.')
    } finally { saving.value = false }
}

async function unlist() {
    if (!current.value) return
    // Where it goes is where it was listed from (priorStatus): the fleet, the bench, or retirement.
    const ok = await confirm({
        title: 'Unlist this bike?',
        message: `It comes off the sales floor and the store, and the unit goes ${unlistDestination(current.value.priorStatus)}.`,
        confirmText: 'Unlist',
        confirmColor: 'error',
    })
    if (!ok) return
    unlisting.value = true
    error.value = ''
    try {
        const r = await service.unlist(current.value.id)
        const after: ShopListing = { ...current.value, ...(r ?? {}), state: 'unlisted' }
        emit('unlisted', after)
        emit('flash', `Unlisted: ${unlistDestination(after.priorStatus)}.`)
        close()
    } catch (e: any) {
        error.value = e.response?.data?.error
            || (e.response?.status === 409 ? 'This unit is sold or someone is checking it out right now; it cannot be unlisted.'
                : 'Could not unlist. Please try again.')
    } finally { unlisting.value = false }
}

// ── Photos ─────────────────────────────────────────────────────────────────
const photos = ref<ListingPhoto[]>([])
const photoError = ref('')
const fileInput = ref<HTMLInputElement | null>(null)
const captionForNew = ref('')
const uploads = ref<{ key: number; name: string; pct: number; error: string }[]>([])
const uploading = ref(false)
const deletingId = ref<string | null>(null)
const reordering = ref(false)
let uploadKey = 0

function pickFiles() { fileInput.value?.click() }

async function onFilesPicked(ev: Event) {
    const input = ev.target as HTMLInputElement
    const files = Array.from(input.files ?? [])
    input.value = ''
    if (files.length === 0 || !current.value) return
    uploading.value = true
    photoError.value = ''
    const caption = captionForNew.value.trim() || null
    let needReload = false
    let landed = 0
    for (const f of files) {
        // reactive() so the progress callback's writes repaint; a plain object pushed into the
        // array would be proxied inside it, but `row` here would still be the raw one.
        const row = reactive({ key: ++uploadKey, name: f.name, pct: 0, error: '' })
        uploads.value.push(row)
        try {
            const r = await service.uploadPhoto(current.value.id, f, caption, pct => { row.pct = pct })
            row.pct = 100
            const photo = (r.data as any)?.data ?? r.data
            if (photo && typeof photo === 'object' && photo.id && photo.url) {
                photos.value.push({ id: photo.id, url: photo.url, caption: photo.caption ?? caption, sortOrder: photo.sortOrder ?? photos.value.length })
            } else needReload = true
            landed++
        } catch (e: any) {
            row.error = e.response?.data?.error || 'Upload failed'
            photoError.value = e.response?.data?.error || 'That photo could not be uploaded. Try again.'
        }
    }
    // A reply without the photo DTO: re-sync this one listing from GET ShopListing/{id}.
    if (needReload && current.value) await refresh(current.value.id)
    if (landed > 0) {
        captionForNew.value = ''
        if (current.value) emit('saved', { ...current.value, photos: photos.value, coverUrl: photos.value[0]?.url ?? current.value.coverUrl })
    }
    // Finished rows linger briefly so the user sees them land, then clear.
    setTimeout(() => { uploads.value = uploads.value.filter(u => u.error) }, 1200)
    uploading.value = false
}

async function removePhoto(p: ListingPhoto) {
    if (!current.value) return
    const ok = await confirm({ title: 'Delete this photo?', message: 'It is removed from the listing permanently.', confirmText: 'Delete', confirmColor: 'error' })
    if (!ok) return
    deletingId.value = p.id
    photoError.value = ''
    try {
        await service.deletePhoto(current.value.id, p.id)
        photos.value = photos.value.filter(x => x.id !== p.id)
        emit('saved', { ...current.value, photos: photos.value, coverUrl: photos.value[0]?.url ?? null })
    } catch (e: any) {
        photoError.value = e.response?.data?.error || 'Could not delete that photo. Try again.'
    } finally { deletingId.value = null }
}

// Reorder: HTML5 drag between tiles, plus arrows for touch screens (no drag events there).
const dragging = ref<number | null>(null)
const dragOver = ref<number | null>(null)
function onDragStart(i: number, ev: DragEvent) {
    dragging.value = i
    ev.dataTransfer?.setData('text/plain', String(i))
    if (ev.dataTransfer) ev.dataTransfer.effectAllowed = 'move'
}
function onDragEnd() { dragging.value = null; dragOver.value = null }
async function onDrop(target: number) {
    const from = dragging.value
    onDragEnd()
    if (from == null || from === target) return
    await applyOrder(from, target)
}
async function move(i: number, delta: number) {
    const target = i + delta
    if (target < 0 || target >= photos.value.length) return
    await applyOrder(i, target)
}
async function applyOrder(from: number, to: number) {
    if (!current.value || reordering.value) return
    const before = photos.value
    const next = [...before]
    const [moved] = next.splice(from, 1)
    next.splice(to, 0, moved)
    photos.value = next.map((p, i) => ({ ...p, sortOrder: i }))
    reordering.value = true
    photoError.value = ''
    try {
        await service.reorderPhotos(current.value.id, photos.value.map(p => p.id))
        emit('saved', { ...current.value, photos: photos.value, coverUrl: photos.value[0]?.url ?? current.value.coverUrl })
    } catch (e: any) {
        photos.value = before
        photoError.value = e.response?.data?.error || 'Could not reorder the photos. Try again.'
    } finally { reordering.value = false }
}
</script>

<style scoped>
.ld-unit {
    padding: 10px 12px;
    border: 1px solid var(--rp-line);
    border-radius: var(--rp-radius-sm);
    background: var(--rp-card2);
}
.ld-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(110px, 1fr));
    gap: 8px;
}
.ld-tile {
    position: relative;
    border: 1px solid var(--rp-line);
    border-radius: var(--rp-radius-sm);
    padding: 4px;
    background: var(--rp-card);
    cursor: grab;
    transition: border-color 120ms ease, opacity 120ms ease;
}
.ld-tile--over { border-color: var(--rp-line3); }
.ld-tile--dragging { opacity: 0.5; }
.ld-cover {
    position: absolute;
    top: 8px;
    left: 8px;
}
.ld-tools {
    display: flex;
    justify-content: center;
    gap: 2px;
    margin-top: 4px;
}
</style>
