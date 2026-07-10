<template>
    <v-container style="max-width: 920px">
        <div class="d-flex align-center mb-4 ga-2">
            <v-btn variant="text" prepend-icon="mdi-arrow-left" to="/Admin/Pages">Back</v-btn>
            <v-spacer></v-spacer>
            <v-btn variant="tonal" prepend-icon="mdi-eye" @click="showPreview = true">Preview</v-btn>
            <v-chip v-if="!isNew" size="small" :color="form.status === 'published' ? 'success' : 'grey'" variant="tonal">
                {{ form.status === 'published' ? 'Published' : 'Draft' }}
            </v-chip>
        </div>

        <h1 class="text-h4 mb-6">{{ isNew ? 'New page' : 'Edit page' }}</h1>

        <v-card class="mb-6">
            <v-card-text>
                <v-text-field v-model="form.title" label="Title" :error-messages="titleError ? [titleError] : []"
                    counter="200" maxlength="200" density="compact"></v-text-field>

                <v-text-field v-model="form.slug" label="URL slug" class="mt-4" density="compact"
                    :placeholder="derivedSlug" persistent-placeholder
                    :prefix="slugPrefix"
                    hint="Leave blank to generate it from the title." persistent-hint></v-text-field>

                <v-select v-model="form.status" label="Status" class="mt-4" density="compact"
                    :items="[{ title: 'Draft (hidden)', value: 'draft' }, { title: 'Published', value: 'published' }]"></v-select>

                <v-switch v-model="form.showInNav" class="mt-4" density="compact" color="primary" hide-details
                    label="Show in navigation"></v-switch>

                <v-text-field v-if="form.showInNav" v-model="form.navLabel" label="Nav label" class="mt-4"
                    density="compact" counter="100" maxlength="100"
                    :placeholder="form.title || 'Page'" persistent-placeholder
                    hint="Leave blank to use the page title." persistent-hint></v-text-field>
            </v-card-text>
        </v-card>

        <!-- Hero image -->
        <v-card class="mb-6">
            <v-card-title class="text-subtitle-1">Hero image</v-card-title>
            <v-card-text>
                <div class="d-flex align-center ga-4 flex-wrap">
                    <v-img v-if="form.heroImageUrl" :src="absoluteUrl(form.heroImageUrl)" width="220" aspect-ratio="16/9"
                        cover class="rounded border"></v-img>
                    <div v-else class="hero-image-placeholder rounded d-flex align-center justify-center">
                        <v-icon color="grey" size="40">mdi-image-outline</v-icon>
                    </div>
                    <div class="d-flex flex-column ga-2">
                        <v-btn variant="tonal" prepend-icon="mdi-upload" :loading="uploadingHero"
                            @click="heroFileInput?.click()">
                            {{ form.heroImageUrl ? 'Replace image' : 'Upload image' }}
                        </v-btn>
                        <v-btn v-if="form.heroImageUrl" variant="text" color="error" size="small"
                            @click="form.heroImageUrl = null">Remove</v-btn>
                        <span class="text-caption text-medium-emphasis">PNG, JPG, or WebP. Max 5 MB.</span>
                    </div>
                    <input ref="heroFileInput" type="file" accept="image/png,image/jpeg,image/webp"
                        class="d-none" @change="onHeroFileChange" />
                </div>
            </v-card-text>
        </v-card>

        <!-- Body -->
        <v-card class="mb-6">
            <v-card-title class="text-subtitle-1">Body</v-card-title>
            <v-card-text>
                <RichTextEditor v-model="bodyHtml" :upload-image="uploadInlineImage" />
            </v-card-text>
        </v-card>

        <div class="d-flex ga-3">
            <v-btn color="primary" size="large" :loading="saving" :disabled="!!titleError" @click="save">
                {{ isNew ? 'Create page' : 'Save changes' }}
            </v-btn>
            <v-btn variant="text" to="/Admin/Pages">Cancel</v-btn>
        </div>

        <!-- Live preview of the current (unsaved) content, rendered exactly as the public
             page (views/Page.vue) will show it. Lets an admin check a draft before publishing. -->
        <v-dialog v-model="showPreview" max-width="900" scrollable>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Preview</span>
                    <v-chip size="x-small" class="ml-2" variant="tonal"
                        :color="form.status === 'published' ? 'success' : 'grey'">
                        {{ form.status === 'published' ? 'Published' : 'Draft' }}
                    </v-chip>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="showPreview = false"></v-btn>
                </v-card-title>
                <v-divider></v-divider>
                <v-card-text>
                    <v-alert v-if="form.status !== 'published'" type="info" variant="tonal" density="compact" class="mb-4">
                        This is a preview of unsaved content. Drafts are not visible to the public until published.
                    </v-alert>
                    <!-- Mirrors views/Page.vue markup so the preview matches the live page. -->
                    <div class="mx-auto" style="max-width: 860px">
                        <article>
                            <h1 class="text-h3 font-display mb-6">{{ form.title.trim() || 'Untitled page' }}</h1>
                            <v-img v-if="form.heroImageUrl" :src="absoluteUrl(form.heroImageUrl)" max-height="460"
                                cover class="rounded mb-6"></v-img>
                            <RichTextView v-if="bodyHtml" :html="bodyHtml" />
                            <p v-else class="text-medium-emphasis">No content yet.</p>
                        </article>
                    </div>
                </v-card-text>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { PageService } from '@/services/PageService'
import { branding } from '@/stores/branding'
import RichTextEditor from '@/components/RichTextEditor.vue'
import RichTextView from '@/components/RichTextView.vue'

const route = useRoute()
const router = useRouter()
const pageService = new PageService()

// /Admin/Pages/New has no :id param -> create mode. /Admin/Pages/:id -> edit mode.
const pageId = ref<string | null>((route.params.id as string) ?? null)
const isNew = computed(() => !pageId.value)

const form = ref<{
    title: string
    slug: string
    status: 'draft' | 'published'
    showInNav: boolean
    navLabel: string
    heroImageUrl: string | null
    sortOrder: number
}>({ title: '', slug: '', status: 'draft', showInNav: false, navLabel: '', heroImageUrl: null, sortOrder: 0 })
const bodyHtml = ref<string>('')

const saving = ref(false)
const showPreview = ref(false)
const uploadingHero = ref(false)
const heroFileInput = ref<HTMLInputElement | null>(null)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const apiUrl: string = import.meta.env.VITE_API_ENDPOINT ?? ''
function apiOrigin(): string {
    try { return new URL(apiUrl, window.location.origin).origin } catch { return '' }
}
function absoluteUrl(url: string | null | undefined): string {
    if (!url) return ''
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}

const slugPrefix = computed(() => `${branding.subdomain ? branding.subdomain + '.' : ''}ridepass.io/`)

const titleError = computed(() => (form.value.title.trim() ? '' : 'Title is required.'))

// Live preview of the slug that would be generated when the field is left blank.
const derivedSlug = computed(() => slugify(form.value.title) || 'page')
function slugify(input: string): string {
    return (input || '').trim().toLowerCase()
        .replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '')
}

async function uploadInlineImage(file: File): Promise<string> {
    const resp = await pageService.uploadImage(file)
    return resp.data.data.imageUrl
}

async function loadForEdit() {
    if (!pageId.value) return
    try {
        const resp = await pageService.getAdmin(pageId.value)
        const d = resp.data.data
        form.value = {
            title: d.title,
            slug: d.slug,
            status: d.status,
            showInNav: d.showInNav,
            navLabel: d.navLabel ?? '',
            heroImageUrl: d.heroImageUrl,
            sortOrder: d.sortOrder,
        }
        bodyHtml.value = d.bodyHtml ?? ''
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load page.', 'error')
    }
}

function requestBody() {
    return {
        title: form.value.title.trim(),
        slug: form.value.slug.trim() || null,
        bodyHtml: bodyHtml.value || null,
        heroImageUrl: form.value.heroImageUrl,
        status: form.value.status,
        showInNav: form.value.showInNav,
        navLabel: form.value.navLabel.trim() || null,
        sortOrder: form.value.sortOrder,
    }
}

async function save() {
    if (titleError.value) return
    saving.value = true
    try {
        if (isNew.value) {
            const resp = await pageService.create(requestBody())
            pageId.value = resp.data.data.id
            flash('Page created.', 'success')
            // Move to the edit URL so a refresh lands back on this page.
            router.replace(`/Admin/Pages/${pageId.value}`)
            await loadForEdit()
        } else {
            await pageService.update(pageId.value!, requestBody())
            flash('Saved.', 'success')
            await loadForEdit()
        }
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function onHeroFileChange(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0]
    if (!file) return
    uploadingHero.value = true
    try {
        const resp = await pageService.uploadImage(file)
        form.value.heroImageUrl = resp.data.data.imageUrl
    } catch (err: any) {
        flash(err.response?.data?.error || 'Upload failed.', 'error')
    } finally {
        uploadingHero.value = false
        if (heroFileInput.value) heroFileInput.value.value = ''
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onMounted(loadForEdit)
</script>

<style scoped>
.hero-image-placeholder {
    width: 220px;
    aspect-ratio: 16 / 9;
    background: rgba(0, 0, 0, 0.04);
    border: 1px dashed rgba(0, 0, 0, 0.2);
}
</style>
