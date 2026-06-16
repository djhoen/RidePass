<template>
    <v-container>
        <h1 class="text-h4 mb-2">Home Page</h1>
        <p class="text-body-2 text-medium-emphasis mb-6">
            Configure what visitors to your public site see. Hero images, the About section,
            today's open/closed status, and operating hours.
        </p>

        <!-- ── Daily Status ──────────────────────────────────────────────────── -->
        <v-card class="mb-6 pa-4">
            <v-card-title>Today's Status</v-card-title>
            <v-card-subtitle>
                Quick toggle for whether you're open right now. Visible at the top of the public home page.
                Auto-clears after 24 hours. Blackout days are always shown as closed automatically — no need to set status on those.
            </v-card-subtitle>
            <v-card-text>
                <div class="d-flex flex-wrap ga-2 mb-3">
                    <v-btn :color="status.open === true ? 'success' : undefined"
                        :variant="status.open === true ? 'flat' : 'tonal'"
                        prepend-icon="mdi-check-circle"
                        @click="setStatus(true)">Open today</v-btn>
                    <v-btn :color="status.open === false ? 'error' : undefined"
                        :variant="status.open === false ? 'flat' : 'tonal'"
                        prepend-icon="mdi-close-circle"
                        @click="setStatus(false)">Closed today</v-btn>
                    <v-btn variant="text" prepend-icon="mdi-eraser" @click="setStatus(null)"
                        :disabled="status.open === null">Clear status</v-btn>
                </div>
                <v-text-field v-model="status.message" label="Conditions / note (optional)"
                    placeholder="Muddy after rain — bring boots"
                    hint="Short free-text shown alongside the status badge. Leave empty for none."
                    persistent-hint :hide-details="false" :disabled="status.open === null"
                    @update:model-value="scheduleStatusSave"></v-text-field>
                <div v-if="branding.dailyStatusUpdatedAt" class="text-caption text-medium-emphasis mt-2">
                    Last updated: {{ formatLocal(branding.dailyStatusUpdatedAt) }}
                </div>
                <v-btn color="primary" class="mt-4" :loading="savingStatus" @click="saveStatus">Save Status</v-btn>
            </v-card-text>
        </v-card>

        <!-- ── Next Up section (events row) ──────────────────────────────────── -->
        <v-card class="mb-6 pa-4">
            <v-card-title>Next Up section</v-card-title>
            <v-card-subtitle>The events row at the top of the public home page.</v-card-subtitle>
            <v-card-text>
                <v-text-field v-model="content.nextUpTitle" label="Heading"
                    placeholder="Next Up" :hide-details="false"
                    hint="Defaults to 'Next Up' when blank." persistent-hint></v-text-field>
                <v-autocomplete v-model="content.nextUpEventTypeIds" :items="eventTypeOptions"
                    item-title="name" item-value="id" multiple chips closable-chips
                    label="Show only these event types"
                    :hide-details="false"
                    hint="Leave empty to include every event type."
                    persistent-hint class="mt-3"></v-autocomplete>
                <v-btn color="primary" class="mt-4" :loading="savingContent" @click="saveContent">Save Next Up</v-btn>
            </v-card-text>
        </v-card>

        <!-- ── About + Hours (saved together, both live in tenant.about_html / hours_json) ── -->
        <v-card class="mb-6 pa-4">
            <v-card-title>About</v-card-title>
            <v-card-subtitle>Shown on the home page below the hero. Tell riders what your track is like.</v-card-subtitle>
            <v-card-text>
                <RichTextEditor v-model="content.aboutHtml" />
                <v-btn color="primary" class="mt-4" :loading="savingContent" @click="saveContent">Save About</v-btn>
            </v-card-text>
        </v-card>

        <!-- ── Benefits ("why ride here" band) ──────────────────────────────── -->
        <v-card class="mb-6 pa-4">
            <v-card-title>Benefits</v-card-title>
            <v-card-subtitle>A "why ride here" band on the home page. The side image is optional.</v-card-subtitle>
            <v-card-text>
                <RichTextEditor v-model="content.benefitsHtml" />
                <div class="text-subtitle-2 mt-4 mb-2">Benefits image (optional)</div>
                <BrandingImageSlot label="Benefits Image" kind="benefits" :url="branding.benefitsImageUrl"
                    @uploaded="onUploaded" @removed="onRemoved" />
                <v-btn color="primary" class="mt-4" :loading="savingContent" @click="saveContent">Save Benefits</v-btn>
            </v-card-text>
        </v-card>

        <!-- ── Section visibility ───────────────────────────────────────────── -->
        <v-card class="mb-6 pa-4">
            <v-card-title>Page Sections</v-card-title>
            <v-card-subtitle>Turn home page sections on or off. The hero is always shown.</v-card-subtitle>
            <v-card-text>
                <v-switch v-for="s in SECTION_DEFS" :key="s.key" v-model="sections[s.key]"
                    :label="s.label" color="primary" density="compact" hide-details inset
                    :disabled="s.key === 'benefits' && !benefitsHasContent"></v-switch>
                <p v-if="!benefitsHasContent" class="text-caption text-medium-emphasis mt-1">
                    Add Benefits content (text or an image) above to enable that section.
                </p>
                <v-btn color="primary" class="mt-4" :loading="savingContent" @click="saveContent">Save Sections</v-btn>
            </v-card-text>
        </v-card>

        <v-card class="mb-6 pa-4">
            <v-card-title>Hours of Operation</v-card-title>
            <v-card-subtitle>When riding is allowed. The public page can warn riders before they buy a pass for a day you're closed.</v-card-subtitle>
            <v-card-text>
                <v-row v-for="day in days" :key="day.key" align="center" class="mb-1">
                    <v-col cols="3" md="2" class="text-subtitle-2">{{ day.label }}</v-col>
                    <v-col cols="3" md="2">
                        <v-checkbox v-model="hours[day.key].closed" label="Closed"
                            density="compact" hide-details></v-checkbox>
                    </v-col>
                    <v-col cols="3" md="3">
                        <v-text-field v-model="hours[day.key].open" type="time" label="Open"
                            :disabled="hours[day.key].closed"></v-text-field>
                    </v-col>
                    <v-col cols="3" md="3">
                        <v-text-field v-model="hours[day.key].close" type="time" label="Close"
                            :disabled="hours[day.key].closed"></v-text-field>
                    </v-col>
                </v-row>
                <v-btn color="primary" class="mt-4" :loading="savingContent" @click="saveContent">Save Hours</v-btn>
            </v-card-text>
        </v-card>

        <!-- ── Heroes (moved from Branding admin page) ──────────────────────── -->
        <v-card class="mt-8 mb-6 pa-4">
            <v-card-title>Hero Images</v-card-title>
            <v-card-subtitle>Large images shown at the top of the public home page.</v-card-subtitle>
            <v-card-text>
                <BrandingImageSlot label="Home Hero" kind="hero" :url="branding.heroImageUrl"
                    @uploaded="onUploaded" @removed="onRemoved" />
                <v-divider class="my-4"></v-divider>
                <BrandingImageSlot label="Secondary Hero" kind="secondaryHero" :url="branding.secondaryHeroUrl"
                    @uploaded="onUploaded" @removed="onRemoved" />
            </v-card-text>
        </v-card>

        <!-- ── Photo Gallery ─────────────────────────────────────────────────── -->
        <v-card class="mb-6 pa-4">
            <v-card-title>Photo Gallery</v-card-title>
            <v-card-subtitle>Photos of the track shown in a grid on the home page. Add a caption to give context.</v-card-subtitle>
            <v-card-text>
                <v-file-input v-model="galleryUploadFile" label="Add a photo" prepend-icon="mdi-camera-plus"
                    accept="image/*" :hide-details="false" :loading="uploadingGallery"
                    @update:model-value="onGalleryFileSelected"></v-file-input>
                <draggable tag="div" :list="visibleGallery" item-key="id"
                    class="gallery-grid mt-4" handle=".drag-handle"
                    :animation="180" ghost-class="drag-ghost"
                    @end="onGalleryReorderEnd">
                    <template #item="{ element: img }">
                        <v-card variant="outlined">
                            <div class="gallery-img-wrap">
                                <v-img :src="absoluteUrl(img.imageUrl)" aspect-ratio="1.5" cover></v-img>
                                <v-icon class="drag-handle gallery-handle" color="white">mdi-drag</v-icon>
                            </div>
                            <v-card-text class="pa-2">
                                <v-text-field :model-value="img.caption ?? ''"
                                    label="Caption" placeholder="Optional"
                                    @blur="updateGalleryCaption(img, ($event.target as HTMLInputElement).value)"></v-text-field>
                                <div class="d-flex justify-end mt-1">
                                    <v-btn size="small" variant="text" color="error" prepend-icon="mdi-delete"
                                        @click="removeGalleryImage(img)">Remove</v-btn>
                                </div>
                            </v-card-text>
                        </v-card>
                    </template>
                </draggable>
                <p v-if="gallery.length === 0" class="text-body-2 text-medium-emphasis mt-4">
                    No photos yet — add your first one above.
                </p>
            </v-card-text>
        </v-card>

        <!-- ── Track Graphics ───────────────────────────────────────────────── -->
        <v-card class="mb-6 pa-4">
            <v-card-title>Track Graphics</v-card-title>
            <v-card-subtitle>
                Layout diagrams or section maps of your track. Each graphic gets a title and description
                shown next to it on the home page (e.g., "Pro Loop — advanced jumps only").
            </v-card-subtitle>
            <v-card-text>
                <v-file-input v-model="trackUploadFile" label="Add a track graphic" prepend-icon="mdi-map-plus"
                    accept="image/*" :hide-details="false" :loading="uploadingTrack"
                    @update:model-value="onTrackFileSelected"></v-file-input>
                <draggable tag="div" :list="visibleTrackGraphics" item-key="id"
                    class="mt-4" handle=".drag-handle"
                    :animation="180" ghost-class="drag-ghost"
                    @end="onTrackReorderEnd">
                    <template #item="{ element: g }">
                        <v-card variant="outlined" class="mb-3">
                            <v-row no-gutters>
                                <v-col cols="auto" class="d-flex align-center justify-center"
                                    style="width: 36px; background: rgba(0,0,0,0.03)">
                                    <v-icon class="drag-handle" color="grey">mdi-drag-vertical</v-icon>
                                </v-col>
                                <v-col cols="12" md="4">
                                    <v-img :src="absoluteUrl(g.imageUrl)" aspect-ratio="1.5" cover></v-img>
                                </v-col>
                                <v-col cols="12" md="8">
                                    <v-card-text>
                                        <v-text-field v-model="g.title" label="Title" placeholder="Pro Loop"
                                            @blur="saveTrackGraphic(g)"></v-text-field>
                                        <v-textarea v-model="g.description" label="Description" rows="3"
                                            placeholder="Tight, technical, advanced riders only"
                                            class="mt-2" @blur="saveTrackGraphic(g)"></v-textarea>
                                        <div class="d-flex justify-end mt-2">
                                            <v-btn size="small" variant="text" color="error" prepend-icon="mdi-delete"
                                                @click="removeTrackGraphic(g)">Remove</v-btn>
                                        </div>
                                    </v-card-text>
                                </v-col>
                            </v-row>
                        </v-card>
                    </template>
                </draggable>
                <p v-if="trackGraphics.length === 0" class="text-body-2 text-medium-emphasis">
                    No track graphics yet — upload your first one above.
                </p>
            </v-card-text>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import draggable from 'vuedraggable'
import { useDragReorder } from '@/composables/useDragReorder'
import { TenantService, type GalleryImage, type TrackGraphic } from '@/services/TenantService'
import { EventTypeService, type EventType } from '@/services/EventTypeService'
import { branding, loadBranding } from '@/stores/branding'
import RichTextEditor from '@/components/RichTextEditor.vue'
import BrandingImageSlot from '@/components/BrandingImageSlot.vue'

const tenantService = new TenantService()
const eventTypeService = new EventTypeService()
const apiUrl: string = (import.meta as any).env?.VITE_API_ENDPOINT ?? ''

function apiOrigin(): string {
    try { return new URL(apiUrl, window.location.origin).origin } catch { return '' }
}

function absoluteUrl(url: string | null | undefined): string {
    if (!url) return ''
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}

const days = [
    { key: 'mon', label: 'Monday' },
    { key: 'tue', label: 'Tuesday' },
    { key: 'wed', label: 'Wednesday' },
    { key: 'thu', label: 'Thursday' },
    { key: 'fri', label: 'Friday' },
    { key: 'sat', label: 'Saturday' },
    { key: 'sun', label: 'Sunday' },
] as const

type DayKey = typeof days[number]['key']
type DayHours = { closed: boolean; open: string; close: string }

function blankHours(): Record<DayKey, DayHours> {
    return {
        mon: { closed: false, open: '09:00', close: '17:00' },
        tue: { closed: false, open: '09:00', close: '17:00' },
        wed: { closed: false, open: '09:00', close: '17:00' },
        thu: { closed: false, open: '09:00', close: '17:00' },
        fri: { closed: false, open: '09:00', close: '17:00' },
        sat: { closed: false, open: '09:00', close: '17:00' },
        sun: { closed: false, open: '09:00', close: '17:00' },
    }
}

const content = ref({
    aboutHtml: '' as string,
    benefitsHtml: '' as string,
    nextUpTitle: '' as string,
    nextUpEventTypeIds: [] as string[],
})

// Toggleable home sections (everything except the always-on hero). A section is
// visible unless its key is explicitly false.
const SECTION_DEFS: { key: string; label: string }[] = [
    { key: 'nextEvents', label: 'Upcoming events' },
    { key: 'passes', label: 'Passes pricing' },
    { key: 'benefits', label: 'Benefits' },
    { key: 'about', label: 'About' },
    { key: 'gallery', label: 'Photo gallery' },
    { key: 'trackLayout', label: 'Track layout' },
    { key: 'hours', label: 'Hours of operation' },
    { key: 'signup', label: 'Sign-up strip' },
]
const sections = ref<Record<string, boolean>>({})

// The Benefits section can't be enabled until it has content (rich text or an image),
// otherwise toggling it on does nothing on the public page.
const benefitsHasContent = computed(() =>
    content.value.benefitsHtml.trim().length > 0 || !!branding.benefitsImageUrl)

// If benefits content is removed, drop the section back off so a content-less
// section can't stay enabled.
watch(benefitsHasContent, (has) => { if (!has) sections.value.benefits = false })
const eventTypeOptions = ref<EventType[]>([])
const hours = ref<Record<DayKey, DayHours>>(blankHours())
const status = ref<{ open: boolean | null; message: string }>({ open: null, message: '' })

const savingContent = ref(false)
const savingStatus = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const gallery = ref<GalleryImage[]>([])
const trackGraphics = ref<TrackGraphic[]>([])
const { visibleRows: visibleGallery, onReorderEnd: onGalleryReorderEnd } = useDragReorder<GalleryImage>({
    rows: gallery,
    save: items => tenantService.reorderGallery(items),
    onSuccess: () => flash('Order saved.', 'success'),
    onError: async err => {
        flash((err as any)?.response?.data?.error || 'Failed to save order — refreshing.', 'error')
        await loadGallery()
    },
})
const { visibleRows: visibleTrackGraphics, onReorderEnd: onTrackReorderEnd } = useDragReorder<TrackGraphic>({
    rows: trackGraphics,
    save: items => tenantService.reorderTrackGraphics(items),
    onSuccess: () => flash('Order saved.', 'success'),
    onError: async err => {
        flash((err as any)?.response?.data?.error || 'Failed to save order — refreshing.', 'error')
        await loadTrackGraphics()
    },
})
const galleryUploadFile = ref<File | File[] | null>(null)
const trackUploadFile = ref<File | File[] | null>(null)
const uploadingGallery = ref(false)
const uploadingTrack = ref(false)

async function loadGallery() {
    try {
        const r = await tenantService.listGallery()
        gallery.value = r.data.data
    } catch (err: any) {
        flash('Failed to load gallery.', 'error')
    }
}

async function loadTrackGraphics() {
    try {
        const r = await tenantService.listTrackGraphics()
        trackGraphics.value = r.data.data
    } catch {
        flash('Failed to load track graphics.', 'error')
    }
}

function fileFrom(v: File | File[] | null): File | null {
    if (!v) return null
    return Array.isArray(v) ? (v[0] ?? null) : v
}

async function onGalleryFileSelected(v: File | File[] | null) {
    const f = fileFrom(v)
    if (!f) return
    try {
        uploadingGallery.value = true
        const nextSort = (gallery.value[gallery.value.length - 1]?.sortOrder ?? 0) + 10
        await tenantService.addGalleryImage(f, null, nextSort)
        galleryUploadFile.value = null
        await loadGallery()
        flash('Photo added.', 'success')
    } catch (err: any) {
        const status = err?.response?.status
        const detail = err?.response?.data?.error ?? err?.response?.data?.title ?? err?.message ?? 'unknown error'
        console.error('Gallery upload failed', { status, detail, response: err?.response?.data })
        flash(`Upload failed (${status ?? '?'}): ${detail}`, 'error')
    } finally {
        uploadingGallery.value = false
    }
}

async function updateGalleryCaption(img: GalleryImage, newCaption: string) {
    const trimmed = (newCaption ?? '').trim()
    const next = trimmed.length > 0 ? trimmed : null
    if (next === (img.caption ?? null)) return
    try {
        await tenantService.updateGalleryImage(img.id, { caption: next, sortOrder: img.sortOrder })
        img.caption = next
        flash('Caption saved.', 'success')
    } catch {
        flash('Failed to update caption.', 'error')
    }
}

async function removeGalleryImage(img: GalleryImage) {
    if (!confirm('Remove this photo?')) return
    try {
        await tenantService.deleteGalleryImage(img.id)
        await loadGallery()
        flash('Photo removed.', 'success')
    } catch {
        flash('Failed to remove photo.', 'error')
    }
}

async function onTrackFileSelected(v: File | File[] | null) {
    const f = fileFrom(v)
    if (!f) return
    try {
        uploadingTrack.value = true
        const nextSort = (trackGraphics.value[trackGraphics.value.length - 1]?.sortOrder ?? 0) + 10
        await tenantService.addTrackGraphic(f, null, null, nextSort)
        trackUploadFile.value = null
        await loadTrackGraphics()
        flash('Track graphic added.', 'success')
    } catch (err: any) {
        const status = err?.response?.status
        const detail = err?.response?.data?.error ?? err?.response?.data?.title ?? err?.message ?? 'unknown error'
        console.error('Track graphic upload failed', { status, detail, response: err?.response?.data })
        flash(`Upload failed (${status ?? '?'}): ${detail}`, 'error')
    } finally {
        uploadingTrack.value = false
    }
}

async function saveTrackGraphic(g: TrackGraphic) {
    try {
        await tenantService.updateTrackGraphic(g.id, {
            title: (g.title ?? '').trim().length > 0 ? (g.title as string).trim() : null,
            description: (g.description ?? '').trim().length > 0 ? (g.description as string).trim() : null,
            sortOrder: g.sortOrder,
        })
        flash('Saved.', 'success')
    } catch {
        flash('Failed to save changes.', 'error')
    }
}

async function removeTrackGraphic(g: TrackGraphic) {
    if (!confirm('Remove this track graphic?')) return
    try {
        await tenantService.deleteTrackGraphic(g.id)
        await loadTrackGraphics()
        flash('Removed.', 'success')
    } catch {
        flash('Failed to remove.', 'error')
    }
}

function populateForm() {
    content.value.aboutHtml = branding.aboutHtml ?? ''
    content.value.benefitsHtml = branding.benefitsHtml ?? ''
    content.value.nextUpTitle = branding.homeNextUpTitle ?? ''
    content.value.nextUpEventTypeIds = branding.homeNextUpEventTypeIds ?? []
    const sec: Record<string, boolean> = {}
    for (const s of SECTION_DEFS) sec[s.key] = branding.homeSections[s.key] !== false
    sections.value = sec
    if (branding.hoursJson) {
        try {
            const parsed = JSON.parse(branding.hoursJson) as Record<string, Partial<DayHours>>
            const next = blankHours()
            for (const day of days) {
                const v = parsed[day.key]
                if (v) {
                    next[day.key] = {
                        closed: !!v.closed,
                        open: v.open ?? '09:00',
                        close: v.close ?? '17:00',
                    }
                }
            }
            hours.value = next
        } catch {
            hours.value = blankHours()
        }
    } else {
        hours.value = blankHours()
    }
    status.value = {
        open: branding.dailyStatusOpen,
        message: branding.dailyStatusMessage ?? '',
    }
}

async function saveContent() {
    try {
        savingContent.value = true
        const hoursJson = JSON.stringify(hours.value)
        const aboutHtml = content.value.aboutHtml.trim().length > 0 ? content.value.aboutHtml : null
        const homeBenefitsHtml = content.value.benefitsHtml.trim().length > 0 ? content.value.benefitsHtml : null
        const homeNextUpTitle = content.value.nextUpTitle.trim().length > 0 ? content.value.nextUpTitle.trim() : null
        const homeNextUpEventTypeIds = content.value.nextUpEventTypeIds.length > 0
            ? content.value.nextUpEventTypeIds : null
        const homeSectionsJson = JSON.stringify(sections.value)
        await tenantService.updateHomeContent({
            aboutHtml, hoursJson, homeNextUpTitle, homeNextUpEventTypeIds, homeBenefitsHtml, homeSectionsJson,
        })
        await loadBranding()
        flash('Saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to save.', 'error')
    } finally {
        savingContent.value = false
    }
}

async function loadEventTypes() {
    try {
        const r = await eventTypeService.list()
        eventTypeOptions.value = (r.data as any).data
    } catch {
        eventTypeOptions.value = []
    }
}

function setStatus(open: boolean | null) {
    status.value.open = open
    if (open === null) status.value.message = ''
}

let statusSaveTimer: ReturnType<typeof setTimeout> | null = null
function scheduleStatusSave() {
    // Auto-save the conditions text after a short pause; the open/closed toggle saves
    // explicitly via the Save Status button so a misclick doesn't immediately broadcast.
    if (statusSaveTimer) clearTimeout(statusSaveTimer)
    statusSaveTimer = setTimeout(() => { /* placeholder — current UX is explicit save */ }, 800)
}

async function saveStatus() {
    try {
        savingStatus.value = true
        const message = status.value.message.trim().length > 0 ? status.value.message : null
        await tenantService.updateDailyStatus({ open: status.value.open, message })
        await loadBranding()
        flash('Status saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to save status.', 'error')
    } finally {
        savingStatus.value = false
    }
}

async function onUploaded() {
    await loadBranding()
    flash('Image updated.', 'success')
}
async function onRemoved() {
    await loadBranding()
    flash('Image removed.', 'success')
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

function formatLocal(iso: string): string {
    try {
        return new Date(iso).toLocaleString()
    } catch {
        return iso
    }
}

onMounted(async () => {
    if (!branding.loaded) await loadBranding()
    populateForm()
    await Promise.all([loadGallery(), loadTrackGraphics(), loadEventTypes()])
})

watch(() => branding.loaded, (loaded) => {
    if (loaded) populateForm()
})
</script>

<style scoped>
.gallery-grid {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 16px;
}
@media (max-width: 960px) {
    .gallery-grid { grid-template-columns: repeat(2, 1fr); }
}
@media (max-width: 600px) {
    .gallery-grid { grid-template-columns: 1fr; }
}
.gallery-img-wrap { position: relative; }
.gallery-handle {
    position: absolute;
    top: 8px;
    left: 8px;
    background: rgba(0, 0, 0, 0.45);
    border-radius: 4px;
    padding: 2px;
    cursor: grab;
}
.gallery-handle:active { cursor: grabbing; }
.drag-handle { cursor: grab; }
.drag-handle:active { cursor: grabbing; }
.drag-ghost { opacity: 0.35; background: rgba(25, 118, 210, 0.08); }
</style>
