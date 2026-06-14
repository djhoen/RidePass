<template>
    <v-container>
        <!-- Sticky header: title, alert, save button, and tabs stay pinned
             while the form content scrolls underneath so the Save action is
             always one click away. -->
        <div class="homepage-sticky-header">
            <div class="d-flex align-center ga-3 pt-2">
                <h1 class="text-h4">Home page</h1>
                <v-spacer></v-spacer>
                <v-btn v-if="branding" color="primary" size="large" :loading="saving" @click="save">
                    Save changes
                </v-btn>
            </div>

            <v-alert v-if="loadError" type="error" variant="tonal" class="mt-3">{{ loadError }}</v-alert>

            <v-tabs v-if="branding" v-model="tab" density="compact" class="mt-3">
                <v-tab value="general">General</v-tab>
                <v-tab value="benefits">Benefits</v-tab>
                <v-tab value="testimonials">Testimonials</v-tab>
                <v-tab value="navbar">Nav bar</v-tab>
            </v-tabs>
        </div>

        <div v-if="loading" class="text-center my-12">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <template v-else-if="branding">
            <v-window v-model="tab" class="mt-4">

                <!-- ── GENERAL: hero + stats + section titles ───────────────── -->
                <v-window-item value="general">
                    <v-card class="mb-4">
                        <v-card-title>Hero</v-card-title>
                        <v-card-text>
                            <div class="mb-3">
                                <div class="text-caption text-medium-emphasis mb-1">Hero image (background)</div>
                                <div class="d-flex ga-3 align-center">
                                    <div class="hero-preview" :style="heroPreviewStyle"></div>
                                    <div class="d-flex flex-column ga-2">
                                        <v-btn variant="tonal" size="small" prepend-icon="mdi-upload"
                                            :loading="uploadingHero" @click="pickHero">Upload</v-btn>
                                        <v-btn v-if="branding.heroImageUrl" variant="text" size="small" color="error"
                                            prepend-icon="mdi-delete" @click="removeHero">Remove</v-btn>
                                        <input ref="heroInput" type="file" accept="image/*" class="d-none"
                                            @change="onHeroPicked">
                                    </div>
                                </div>
                            </div>

                            <v-text-field v-model="form.heroHeadline" label="Hero headline" class="mt-4"
                                placeholder="Find your track. Ride this weekend." density="compact"
                                hint="Big display text at the top. Split on periods renders each clause on its own line; the second clause picks up the brand accent color."
                                persistent-hint></v-text-field>
                            <v-textarea v-model="form.heroSubhead" label="Hero subheadline" rows="2" class="mt-4"
                                auto-grow density="compact"
                                hint="One-line description directly under the headline."
                                persistent-hint></v-textarea>

                            <v-row dense>
                                <v-col cols="12" md="6">
                                    <v-text-field v-model="form.heroCtaPrimaryLabel" label="Primary CTA label" class="mt-4"
                                        placeholder="Browse tracks" density="compact"></v-text-field>
                                </v-col>
                                <v-col cols="12" md="6">
                                    <v-text-field v-model="form.heroCtaPrimaryUrl" label="Primary CTA URL" class="mt-4"
                                        placeholder="/Discover" density="compact"></v-text-field>
                                </v-col>
                                <v-col cols="12" md="6">
                                    <v-text-field v-model="form.heroCtaSecondaryLabel" label="Secondary CTA label" class="mt-4"
                                        placeholder="Upcoming events" density="compact"></v-text-field>
                                </v-col>
                                <v-col cols="12" md="6">
                                    <v-text-field v-model="form.heroCtaSecondaryUrl" label="Secondary CTA URL" class="mt-4"
                                        placeholder="/Events" density="compact"></v-text-field>
                                </v-col>
                            </v-row>
                        </v-card-text>
                    </v-card>

                    <v-card class="mb-4">
                        <v-card-title>Stats badge</v-card-title>
                        <v-card-text>
                            <p class="text-caption text-medium-emphasis mb-3">
                                Track count and event-day count are computed automatically from real data.
                            </p>
                            <v-switch v-model="form.statsShowTracks" label="Show track count"
                                density="compact" hide-details color="primary" inset></v-switch>
                            <v-switch v-model="form.statsShowEventDays" label="Show event-day count"
                                density="compact" hide-details color="primary" inset></v-switch>
                        </v-card-text>
                    </v-card>

                    <v-card class="mb-4">
                        <v-card-title>Section titles</v-card-title>
                        <v-card-text>
                            <v-text-field v-model="form.sectionTracksTitle" label="Tracks section" class="mt-4"
                                placeholder="Ride the best tracks" density="compact"></v-text-field>
                            <v-text-field v-model="form.sectionEventsTitle" label="Events section" class="mt-4"
                                placeholder="Upcoming events" density="compact"></v-text-field>
                            <v-text-field v-model="form.sectionBenefitsTitle" label="Benefits section" class="mt-4"
                                placeholder="Why ride with RidePass" density="compact"></v-text-field>
                            <v-text-field v-model="form.sectionTestimonialsTitle" label="Testimonials section" class="mt-4"
                                placeholder="What riders are saying" density="compact"></v-text-field>
                            <v-text-field v-model="form.sectionTracksNearYouTitle" label="Tracks-near-you section" class="mt-4"
                                placeholder="Tracks near you" density="compact"></v-text-field>
                        </v-card-text>
                    </v-card>

                    <v-card class="mb-4">
                        <v-card-title>Featured tracks</v-card-title>
                        <v-card-text>
                            <p class="text-caption text-medium-emphasis mb-3">
                                Pick the tracks shown in the "Ride the best tracks" section. Selection order is
                                display order. Leave empty to auto-pick by upcoming-event count.
                            </p>
                            <v-autocomplete
                                v-model="form.featuredTrackIds"
                                :items="trackOptions"
                                :loading="loadingTracks"
                                item-title="label"
                                item-value="tenantId"
                                label="Featured tracks"
                                multiple chips closable-chips
                                density="compact"
                                clearable></v-autocomplete>
                            <p v-if="form.featuredTrackIds && form.featuredTrackIds.length > 3"
                                class="text-caption text-medium-emphasis mb-0">
                                The landing page renders the first 3 picks. Extras are kept as backup ordering.
                            </p>
                        </v-card-text>
                    </v-card>

                </v-window-item>

                <!-- ── BENEFITS: HTML block + photo ─────────────────────────── -->
                <v-window-item value="benefits">
                    <v-card class="mb-4">
                        <v-card-title>Benefits photo</v-card-title>
                        <v-card-text>
                            <div class="d-flex ga-3 align-center">
                                <div class="benefits-preview" :style="benefitsPreviewStyle"></div>
                                <div class="d-flex flex-column ga-2">
                                    <v-btn variant="tonal" size="small" prepend-icon="mdi-upload"
                                        :loading="uploadingBenefits" @click="pickBenefits">Upload</v-btn>
                                    <v-btn v-if="branding.benefitsImageUrl" variant="text" size="small" color="error"
                                        prepend-icon="mdi-delete" @click="removeBenefits">Remove</v-btn>
                                    <input ref="benefitsInput" type="file" accept="image/*" class="d-none"
                                        @change="onBenefitsPicked">
                                </div>
                            </div>
                        </v-card-text>
                    </v-card>

                    <v-card>
                        <v-card-title>Benefits list</v-card-title>
                        <v-card-text>
                            <p class="text-caption text-medium-emphasis mb-3">
                                A bullet list of rider-facing reasons to ride RidePass tracks. Edited as HTML.
                            </p>
                            <RichTextEditor v-model="benefitsHtml"></RichTextEditor>
                        </v-card-text>
                    </v-card>
                </v-window-item>

                <!-- ── TESTIMONIALS: list CRUD ──────────────────────────────── -->
                <v-window-item value="testimonials">
                    <v-card>
                        <v-card-title class="d-flex align-center">
                            <span>Testimonials</span>
                            <v-spacer></v-spacer>
                            <v-btn color="primary" prepend-icon="mdi-plus" @click="openTestimonialNew">Add</v-btn>
                        </v-card-title>
                        <v-card-text>
                            <p v-if="testimonials.length === 0" class="text-medium-emphasis">
                                No testimonials yet.
                            </p>
                            <v-list v-else lines="three" density="compact">
                                <draggable v-model="testimonials" item-key="id" handle=".drag-handle"
                                    @end="onTestimonialDragEnd">
                                    <template #item="{ element: t }">
                                        <v-list-item>
                                            <template #prepend>
                                                <v-icon class="drag-handle mr-2" style="cursor: grab">mdi-drag</v-icon>
                                                <v-avatar size="40">
                                                    <v-img v-if="t.riderPhotoUrl" :src="absoluteUrl(t.riderPhotoUrl)"></v-img>
                                                    <v-icon v-else>mdi-account</v-icon>
                                                </v-avatar>
                                            </template>
                                            <v-list-item-title class="d-flex align-center ga-2">
                                                {{ t.riderName }}
                                                <v-chip size="x-small" v-if="!t.isActive" color="warning" variant="tonal">
                                                    inactive
                                                </v-chip>
                                                <span class="text-caption">{{ '★'.repeat(t.rating) }}</span>
                                            </v-list-item-title>
                                            <v-list-item-subtitle>{{ t.quote }}</v-list-item-subtitle>
                                            <template #append>
                                                <v-btn icon variant="text" size="small" @click="openTestimonialEdit(t)">
                                                    <v-icon>mdi-pencil</v-icon>
                                                </v-btn>
                                                <v-btn icon variant="text" size="small" color="error"
                                                    @click="removeTestimonial(t)">
                                                    <v-icon>mdi-delete</v-icon>
                                                </v-btn>
                                            </template>
                                        </v-list-item>
                                    </template>
                                </draggable>
                            </v-list>
                        </v-card-text>
                    </v-card>
                </v-window-item>

                <!-- ── NAV BAR: separate color for home page + rest of site ─ -->
                <v-window-item value="navbar">
                    <v-card>
                        <v-card-title>Nav bar</v-card-title>
                        <v-card-text>
                            <p class="text-caption text-medium-emphasis mb-4">
                                Set the background color for the top app bar. The home/landing
                                page can use a different color from the rest of the site, e.g.
                                a dark accent on the apex hero and an orange brand color elsewhere.
                                Leave a field blank to use the theme primary (rest of site) or
                                inherit (home page).
                            </p>
                            <v-row>
                                <v-col cols="12" md="6">
                                    <div class="text-subtitle-2 mb-2">Rest of site</div>
                                    <v-text-field v-model="form.navBarColor" label="Background color"
                                        placeholder="#1A1F2B" persistent-hint
                                        hint="Hex like #1A1F2B. Leave blank to use the theme primary."
                                        density="compact"></v-text-field>
                                    <v-color-picker v-model="form.navBarColor"
                                        class="mt-2" mode="hex" hide-inputs hide-canvas-actions
                                        show-swatches swatches-max-height="100" :modes="['hex']"></v-color-picker>
                                    <v-text-field v-model="form.navBarTextColor" label="Text + icon color"
                                        placeholder="#FFFFFF" persistent-hint
                                        hint="Hex like #FFFFFF. Leave blank for white."
                                        density="compact" class="mt-4"></v-text-field>
                                    <v-color-picker v-model="form.navBarTextColor"
                                        class="mt-2" mode="hex" hide-inputs hide-canvas-actions
                                        show-swatches swatches-max-height="100" :modes="['hex']"></v-color-picker>
                                    <NavBarPreview :color="form.navBarColor"
                                        :text-color="form.navBarTextColor" class="mt-3" />
                                </v-col>
                                <v-col cols="12" md="6">
                                    <div class="text-subtitle-2 mb-2">Home page</div>
                                    <v-text-field v-model="form.navBarHomeColor"
                                        label="Background color"
                                        placeholder="#1A1F2B" persistent-hint
                                        hint="Hex like #1A1F2B. Leave blank to inherit the rest-of-site color."
                                        density="compact"></v-text-field>
                                    <v-color-picker v-model="form.navBarHomeColor"
                                        class="mt-2" mode="hex" hide-inputs hide-canvas-actions
                                        show-swatches swatches-max-height="100" :modes="['hex']"></v-color-picker>
                                    <v-text-field v-model="form.navBarHomeTextColor"
                                        label="Text + icon color"
                                        placeholder="#FFFFFF" persistent-hint
                                        hint="Hex like #FFFFFF. Leave blank to inherit the rest-of-site text color."
                                        density="compact" class="mt-4"></v-text-field>
                                    <v-color-picker v-model="form.navBarHomeTextColor"
                                        class="mt-2" mode="hex" hide-inputs hide-canvas-actions
                                        show-swatches swatches-max-height="100" :modes="['hex']"></v-color-picker>
                                    <NavBarPreview :color="form.navBarHomeColor || form.navBarColor"
                                        :text-color="form.navBarHomeTextColor || form.navBarTextColor"
                                        class="mt-3" />
                                </v-col>
                            </v-row>
                        </v-card-text>
                    </v-card>
                </v-window-item>

            </v-window>
        </template>

        <!-- Testimonial editor dialog -->
        <v-dialog v-model="testimonialDialog" max-width="560" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ testimonialDraftId ? 'Edit testimonial' : 'New testimonial' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="testimonialDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="testimonialDraft.riderName" label="Rider name" density="compact"></v-text-field>
                    <v-textarea v-model="testimonialDraft.quote" label="Quote" rows="3" auto-grow
                        density="compact" class="mt-4"></v-textarea>
                    <v-rating v-model="testimonialDraft.rating" length="5" color="amber" active-color="amber"></v-rating>
                    <v-switch v-model="testimonialDraft.isActive" label="Active (shown on landing page)"
                        density="compact" hide-details color="primary" inset></v-switch>

                    <div v-if="testimonialDraftId" class="mt-4">
                        <div class="text-caption text-medium-emphasis mb-1">Photo</div>
                        <div class="d-flex ga-3 align-center">
                            <v-avatar size="60">
                                <v-img v-if="testimonialDraftPhotoUrl"
                                    :src="absoluteUrl(testimonialDraftPhotoUrl)"></v-img>
                                <v-icon v-else>mdi-account</v-icon>
                            </v-avatar>
                            <v-btn variant="tonal" size="small" prepend-icon="mdi-upload"
                                :loading="uploadingTestimonialPhoto" @click="pickTestimonialPhoto">
                                {{ testimonialDraftPhotoUrl ? 'Replace' : 'Upload' }}
                            </v-btn>
                            <input ref="testimonialPhotoInput" type="file" accept="image/*" class="d-none"
                                @change="onTestimonialPhotoPicked">
                        </div>
                    </div>
                </v-card-text>
                <v-card-actions class="px-4 pb-3">
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="testimonialDialog = false">Cancel</v-btn>
                    <v-btn color="primary" variant="flat" :loading="savingTestimonial"
                        @click="saveTestimonial">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import draggable from 'vuedraggable'
import RichTextEditor from '@/components/RichTextEditor.vue'
import {
    PlatformBrandingService,
    type PlatformBranding,
    type PlatformTestimonial,
    type SavePlatformBranding,
} from '@/services/PlatformBrandingService'
import { DiscoverService, type TrackDiscoverItem } from '@/services/DiscoverService'
import { useConfirm } from '@/composables/useConfirm'
import NavBarPreview from '@/components/NavBarPreview.vue'
import { loadPlatformBranding } from '@/stores/platformBranding'

const service = new PlatformBrandingService()
const discoverService = new DiscoverService()
const confirm = useConfirm()

const apiUrl: string = (import.meta as any).env?.VITE_API_ENDPOINT ?? ''
function apiOrigin(): string {
    try { return new URL(apiUrl, window.location.origin).origin } catch { return '' }
}
function absoluteUrl(url: string | null | undefined): string {
    if (!url) return ''
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}

const tab = ref<'general' | 'benefits' | 'testimonials' | 'navbar'>('general')

const branding = ref<PlatformBranding | null>(null)
const testimonials = ref<PlatformTestimonial[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const saving = ref(false)

// Form state mirrors the editable fields. Images and testimonials have their
// own endpoints, so they're not in `form`.
const form = ref<SavePlatformBranding>(emptyForm())
const benefitsHtml = ref('')


const uploadingHero = ref(false)
const uploadingBenefits = ref(false)
const uploadingTestimonialPhoto = ref(false)

// Available tracks for the featured-tracks picker. Loaded once; small enough
// that we don't need server-side search even at platform scale.
interface TrackOption { tenantId: string; label: string }
const allTracks = ref<TrackDiscoverItem[]>([])
const loadingTracks = ref(false)
const trackOptions = computed<TrackOption[]>(() =>
    allTracks.value.map(t => ({
        tenantId: t.tenantId,
        label: t.city || t.region
            ? `${t.displayName} (${[t.city, t.region].filter(Boolean).join(', ')})`
            : t.displayName,
    })))
const heroInput = ref<HTMLInputElement | null>(null)
const benefitsInput = ref<HTMLInputElement | null>(null)
const testimonialPhotoInput = ref<HTMLInputElement | null>(null)

// Testimonial editor state
const testimonialDialog = ref(false)
const testimonialDraftId = ref<string | null>(null)
const testimonialDraftPhotoUrl = ref<string | null>(null)
const testimonialDraft = ref({ riderName: '', quote: '', rating: 5, isActive: true })
const savingTestimonial = ref(false)

const heroPreviewStyle = computed(() => {
    const url = branding.value?.heroImageUrl
    return url ? { backgroundImage: `url(${absoluteUrl(url)})` } : {}
})
const benefitsPreviewStyle = computed(() => {
    const url = branding.value?.benefitsImageUrl
    return url ? { backgroundImage: `url(${absoluteUrl(url)})` } : {}
})

// Snackbar
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')
function flash(text: string, color: 'success' | 'error' = 'success') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onMounted(async () => {
    await load()
    await loadTracks()
})

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const r = await service.get()
        const data = (r.data as any).data as PlatformBranding
        branding.value = data
        testimonials.value = data.testimonials
        form.value = brandingToForm(data)
        benefitsHtml.value = data.benefitsHtml ?? ''
    } catch (err: any) {
        loadError.value = err.response?.data?.error || 'Failed to load landing-page settings.'
    } finally {
        loading.value = false
    }
}

async function loadTracks() {
    loadingTracks.value = true
    try {
        const r = await discoverService.searchTracks({})
        allTracks.value = (r.data as any).data
    } catch {
        // Picker stays empty on failure. Not fatal for editing the rest of the page.
    } finally {
        loadingTracks.value = false
    }
}

// v-color-picker can emit #RRGGBBAA (8-char). The backend regex is strict
// #RRGGBB, so strip the alpha and uppercase before posting. Empty / invalid
// inputs collapse to null so leaving a field blank clears the override.
function normalizeHex(hex: string | null | undefined): string | null {
    if (!hex) return null
    const cleaned = hex.trim()
    if (/^#[0-9A-Fa-f]{6}$/.test(cleaned)) return cleaned.toUpperCase()
    if (/^#[0-9A-Fa-f]{8}$/.test(cleaned)) return cleaned.substring(0, 7).toUpperCase()
    return null
}

async function save() {
    if (saving.value) return
    saving.value = true
    try {
        const payload: SavePlatformBranding = {
            ...form.value,
            benefitsHtml: benefitsHtml.value || null,
            navBarColor: normalizeHex(form.value.navBarColor),
            navBarTextColor: normalizeHex(form.value.navBarTextColor),
            navBarHomeColor: normalizeHex(form.value.navBarHomeColor),
            navBarHomeTextColor: normalizeHex(form.value.navBarHomeTextColor),
        }
        const r = await service.save(payload)
        const data = (r.data as any).data as PlatformBranding
        branding.value = data
        testimonials.value = data.testimonials
        form.value = brandingToForm(data)
        // Push the new platform branding into the global store so the NavBar
        // at the top of THIS page (and elsewhere on the apex domain) re-renders
        // with the saved colors. Without this the change only takes effect on
        // a hard reload.
        await loadPlatformBranding()
        flash('Saved.')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

// ── Image upload helpers ────────────────────────────────────────────────────
function pickHero() { heroInput.value?.click() }
async function onHeroPicked(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0]
    if (!file) return
    uploadingHero.value = true
    try {
        await service.uploadImage('hero', file)
        await load()
        flash('Hero image uploaded.')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Upload failed.', 'error')
    } finally {
        uploadingHero.value = false
        if (heroInput.value) heroInput.value.value = ''
    }
}
async function removeHero() {
    const ok = await confirm({
        title: 'Remove hero image?',
        message: 'The landing page will fall back to a plain background.',
        confirmText: 'Remove', confirmColor: 'error',
    })
    if (!ok) return
    try {
        await service.deleteImage('hero')
        await load()
        flash('Hero image removed.')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Remove failed.', 'error')
    }
}

function pickBenefits() { benefitsInput.value?.click() }
async function onBenefitsPicked(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0]
    if (!file) return
    uploadingBenefits.value = true
    try {
        await service.uploadImage('benefits', file)
        await load()
        flash('Benefits photo uploaded.')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Upload failed.', 'error')
    } finally {
        uploadingBenefits.value = false
        if (benefitsInput.value) benefitsInput.value.value = ''
    }
}
async function removeBenefits() {
    const ok = await confirm({
        title: 'Remove benefits photo?',
        message: 'The section will render without a photo.',
        confirmText: 'Remove', confirmColor: 'error',
    })
    if (!ok) return
    try {
        await service.deleteImage('benefits')
        await load()
        flash('Photo removed.')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Remove failed.', 'error')
    }
}

// ── Testimonials ────────────────────────────────────────────────────────────
function openTestimonialNew() {
    testimonialDraftId.value = null
    testimonialDraftPhotoUrl.value = null
    testimonialDraft.value = { riderName: '', quote: '', rating: 5, isActive: true }
    testimonialDialog.value = true
}
function openTestimonialEdit(t: PlatformTestimonial) {
    testimonialDraftId.value = t.id
    testimonialDraftPhotoUrl.value = t.riderPhotoUrl
    testimonialDraft.value = {
        riderName: t.riderName,
        quote: t.quote,
        rating: t.rating,
        isActive: t.isActive,
    }
    testimonialDialog.value = true
}
async function saveTestimonial() {
    if (!testimonialDraft.value.riderName.trim() || !testimonialDraft.value.quote.trim()) {
        flash('Name and quote are required.', 'error')
        return
    }
    savingTestimonial.value = true
    try {
        if (testimonialDraftId.value) {
            await service.updateTestimonial(testimonialDraftId.value, testimonialDraft.value)
        } else {
            const r = await service.createTestimonial(testimonialDraft.value)
            const created = (r.data as any).data as PlatformTestimonial
            testimonialDraftId.value = created.id
        }
        await reloadTestimonials()
        testimonialDialog.value = false
        flash('Testimonial saved.')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        savingTestimonial.value = false
    }
}
async function removeTestimonial(t: PlatformTestimonial) {
    const ok = await confirm({
        title: 'Delete testimonial?',
        message: `Remove the testimonial by "${t.riderName}".`,
        confirmText: 'Delete', confirmColor: 'error',
    })
    if (!ok) return
    try {
        await service.deleteTestimonial(t.id)
        await reloadTestimonials()
        flash('Testimonial deleted.')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    }
}
async function onTestimonialDragEnd() {
    try {
        await service.reorderTestimonials(testimonials.value.map(t => t.id))
    } catch (err: any) {
        flash(err.response?.data?.error || 'Reorder failed.', 'error')
        await reloadTestimonials()
    }
}
function pickTestimonialPhoto() { testimonialPhotoInput.value?.click() }
async function onTestimonialPhotoPicked(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0]
    if (!file || !testimonialDraftId.value) return
    uploadingTestimonialPhoto.value = true
    try {
        const r = await service.uploadTestimonialPhoto(testimonialDraftId.value, file)
        const updated = (r.data as any).data as PlatformTestimonial
        testimonialDraftPhotoUrl.value = updated.riderPhotoUrl
        await reloadTestimonials()
        flash('Photo uploaded.')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Upload failed.', 'error')
    } finally {
        uploadingTestimonialPhoto.value = false
        if (testimonialPhotoInput.value) testimonialPhotoInput.value.value = ''
    }
}
async function reloadTestimonials() {
    const r = await service.listTestimonials()
    testimonials.value = (r.data as any).data
}

// ── Helpers ─────────────────────────────────────────────────────────────────
function emptyForm(): SavePlatformBranding {
    return {
        heroHeadline: null, heroSubhead: null,
        heroCtaPrimaryLabel: null, heroCtaPrimaryUrl: null,
        heroCtaSecondaryLabel: null, heroCtaSecondaryUrl: null,
        statsShowTracks: true, statsShowEventDays: true, statsPriceLabel: null,
        sectionTracksTitle: null, sectionEventsTitle: null,
        sectionBenefitsTitle: null, sectionTestimonialsTitle: null, sectionTracksNearYouTitle: null,
        benefitsHtml: null,
        ctaBannerHeadline: null, ctaBannerSubhead: null, ctaBannerPriceLabel: null,
        ctaBannerCtaLabel: null, ctaBannerCtaUrl: null,
        featuredTrackIds: null,
        navBarColor: null, navBarTextColor: null,
        navBarHomeColor: null, navBarHomeTextColor: null,
    }
}
function brandingToForm(b: PlatformBranding): SavePlatformBranding {
    return {
        heroHeadline: b.heroHeadline,
        heroSubhead: b.heroSubhead,
        heroCtaPrimaryLabel: b.heroCtaPrimaryLabel,
        heroCtaPrimaryUrl: b.heroCtaPrimaryUrl,
        heroCtaSecondaryLabel: b.heroCtaSecondaryLabel,
        heroCtaSecondaryUrl: b.heroCtaSecondaryUrl,
        statsShowTracks: b.statsShowTracks,
        statsShowEventDays: b.statsShowEventDays,
        statsPriceLabel: b.statsPriceLabel,
        sectionTracksTitle: b.sectionTracksTitle,
        sectionEventsTitle: b.sectionEventsTitle,
        sectionBenefitsTitle: b.sectionBenefitsTitle,
        sectionTestimonialsTitle: b.sectionTestimonialsTitle,
        sectionTracksNearYouTitle: b.sectionTracksNearYouTitle,
        benefitsHtml: b.benefitsHtml,
        ctaBannerHeadline: b.ctaBannerHeadline,
        ctaBannerSubhead: b.ctaBannerSubhead,
        ctaBannerPriceLabel: b.ctaBannerPriceLabel,
        ctaBannerCtaLabel: b.ctaBannerCtaLabel,
        ctaBannerCtaUrl: b.ctaBannerCtaUrl,
        featuredTrackIds: b.featuredTrackIds,
        navBarColor: b.navBarColor,
        navBarTextColor: b.navBarTextColor,
        navBarHomeColor: b.navBarHomeColor,
        navBarHomeTextColor: b.navBarHomeTextColor,
    }
}
</script>

<style scoped>
/* Sticky header keeps the title, Save button, alert, and tabs pinned as
   one block while the form scrolls underneath. The top offset uses
   Vuetify's layout variable so the header sits flush against whatever
   v-app-bar height is in play (default 64px). Solid background prevents
   form content bleeding through; box-shadow gives a soft divider when
   content scrolls behind it. */
.homepage-sticky-header {
    position: sticky;
    top: var(--v-layout-top, 0px);
    z-index: 5;
    background: rgb(var(--v-theme-background));
    padding-top: 12px;
    padding-bottom: 12px;
    margin-top: -12px;
    box-shadow: 0 6px 8px -8px rgba(0, 0, 0, 0.15);
}

.hero-preview {
    width: 240px;
    height: 120px;
    border-radius: 8px;
    background-color: rgba(0, 0, 0, 0.04);
    background-size: cover;
    background-position: center;
    border: 1px solid rgba(0, 0, 0, 0.1);
}
.benefits-preview {
    width: 180px;
    height: 120px;
    border-radius: 8px;
    background-color: rgba(0, 0, 0, 0.04);
    background-size: cover;
    background-position: center;
    border: 1px solid rgba(0, 0, 0, 0.1);
}
</style>
