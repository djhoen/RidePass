<template>
    <v-container>
        <div class="d-flex align-center ga-3 mb-4">
            <h1 class="text-h4">For Tracks page</h1>
            <v-spacer></v-spacer>
            <v-btn v-if="loaded" color="primary" size="large" :loading="saving" @click="save">
                Save changes
            </v-btn>
        </div>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <div v-if="loading" class="text-center my-12">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <template v-else-if="loaded">
            <v-alert type="info" variant="tonal" density="comfortable" class="mb-4">
                Edits the hero and the "Why Tracks love RidePass" section on the public
                <a href="/ForTracks" target="_blank" class="text-primary">For Tracks page</a>.
                The feature lists, pricing, and steps on that page are fixed for now.
            </v-alert>

            <v-card class="mb-4">
                <v-card-title>Hero</v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.heroEyebrow" label="Eyebrow (small label above the headline)"
                        placeholder="RidePass for track operators" density="compact"></v-text-field>
                    <v-text-field v-model="form.heroHeadline" label="Headline" class="mt-4"
                        placeholder="Run your track on one platform" density="compact"></v-text-field>
                    <v-textarea v-model="form.heroSubhead" label="Subheadline" rows="3" auto-grow class="mt-4"
                        placeholder="From the front gate to the finish line..." density="compact"></v-textarea>
                </v-card-text>
            </v-card>

            <v-card class="mb-4">
                <v-card-title>Why Tracks love RidePass</v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.benefitsTitle" label="Section title"
                        placeholder="Why Tracks love RidePass" density="compact"></v-text-field>

                    <div class="text-caption text-medium-emphasis mb-1 mt-4">Photo (optional)</div>
                    <div class="d-flex ga-3 align-center mb-2">
                        <div class="ft-benefits-preview" :style="benefitsPreviewStyle"></div>
                        <div class="d-flex flex-column ga-2">
                            <v-btn variant="tonal" size="small" prepend-icon="mdi-upload"
                                :loading="uploadingBenefits" @click="pickBenefits">Upload</v-btn>
                            <v-btn v-if="branding?.benefitsImageUrl" variant="text" size="small" color="error"
                                prepend-icon="mdi-delete" @click="removeBenefits">Remove</v-btn>
                            <input ref="benefitsInput" type="file" accept="image/*" class="d-none"
                                @change="onBenefitsPicked">
                        </div>
                    </div>

                    <div class="text-caption text-medium-emphasis mb-1 mt-4">
                        Reasons tracks choose RidePass. Renders as a checkmark list.
                    </div>
                    <RichTextEditor :model-value="form.benefitsHtml ?? ''"
                        @update:model-value="form.benefitsHtml = $event || null"></RichTextEditor>
                </v-card-text>
            </v-card>
        </template>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import RichTextEditor from '@/components/RichTextEditor.vue'
import {
    PlatformBrandingService,
    type PlatformBranding,
    type SaveForTracks,
} from '@/services/PlatformBrandingService'
import { useConfirm } from '@/composables/useConfirm'
import { loadPlatformBranding } from '@/stores/platformBranding'

const service = new PlatformBrandingService()
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

const loading = ref(true)
const loaded = ref(false)
const saving = ref(false)
const loadError = ref<string | null>(null)
const uploadingBenefits = ref(false)
const benefitsInput = ref<HTMLInputElement | null>(null)
const branding = ref<PlatformBranding | null>(null)

const form = ref<SaveForTracks>({
    heroEyebrow: null,
    heroHeadline: null,
    heroSubhead: null,
    benefitsTitle: null,
    benefitsHtml: null,
})

const benefitsPreviewStyle = computed(() => {
    const url = branding.value?.benefitsImageUrl
    return url ? { backgroundImage: `url(${absoluteUrl(url)})` } : {}
})

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')
function flash(text: string, color: 'success' | 'error' = 'success') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onMounted(load)

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const r = await service.get()
        const data = (r.data as any).data as PlatformBranding
        branding.value = data
        form.value = {
            heroEyebrow: data.forTracksHeroEyebrow,
            heroHeadline: data.forTracksHeroHeadline,
            heroSubhead: data.forTracksHeroSubhead,
            // The benefits block reuses the platform benefits content (section title + html).
            benefitsTitle: data.sectionBenefitsTitle,
            benefitsHtml: data.benefitsHtml,
        }
        loaded.value = true
    } catch (err: any) {
        loadError.value = err.response?.data?.error || 'Failed to load For Tracks content.'
    } finally {
        loading.value = false
    }
}

async function save() {
    if (saving.value) return
    saving.value = true
    try {
        const r = await service.saveForTracks(form.value)
        branding.value = (r.data as any).data as PlatformBranding
        // Refresh the global store so the public For Tracks page (apex) reflects edits.
        await loadPlatformBranding()
        flash('Saved.')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
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
        await loadPlatformBranding()
        flash('Photo uploaded.')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Upload failed.', 'error')
    } finally {
        uploadingBenefits.value = false
        if (benefitsInput.value) benefitsInput.value.value = ''
    }
}
async function removeBenefits() {
    const ok = await confirm({
        title: 'Remove photo?',
        message: 'The section will render without a photo.',
        confirmText: 'Remove', confirmColor: 'error',
    })
    if (!ok) return
    try {
        await service.deleteImage('benefits')
        await load()
        await loadPlatformBranding()
        flash('Photo removed.')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Remove failed.', 'error')
    }
}
</script>

<style scoped>
.ft-benefits-preview {
    width: 180px;
    height: 110px;
    border-radius: 8px;
    background-color: rgba(0, 0, 0, 0.04);
    background-size: cover;
    background-position: center;
    border: 1px solid rgba(0, 0, 0, 0.1);
}
</style>
