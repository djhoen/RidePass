<template>
    <v-container style="max-width: 920px">
        <div class="d-flex align-center mb-4 ga-2">
            <v-btn variant="text" prepend-icon="mdi-arrow-left" to="/Admin/Blog">Back</v-btn>
            <v-spacer></v-spacer>
            <v-chip v-if="!isNew" size="small" :color="form.status === 'published' ? 'success' : 'grey'" variant="tonal">
                {{ form.status === 'published' ? 'Published' : 'Draft' }}
            </v-chip>
        </div>

        <h1 class="text-h4 mb-6">{{ isNew ? 'New post' : 'Edit post' }}</h1>

        <v-card class="mb-6">
            <v-card-text>
                <v-text-field v-model="form.title" label="Title" :error-messages="titleError ? [titleError] : []"
                    counter="200" maxlength="200" density="compact"></v-text-field>

                <v-text-field v-model="form.slug" label="URL slug" class="mt-4" density="compact"
                    :placeholder="derivedSlug" persistent-placeholder
                    prefix="/Blog/"
                    hint="Leave blank to generate it from the title." persistent-hint></v-text-field>

                <v-textarea v-model="form.excerpt" label="Excerpt" class="mt-4" density="compact"
                    rows="2" auto-grow counter="500" maxlength="500"
                    hint="Short summary shown on the blog list and the home-page feature." persistent-hint></v-textarea>

                <v-select v-model="form.status" label="Status" class="mt-4" density="compact"
                    :items="[{ title: 'Draft (hidden)', value: 'draft' }, { title: 'Published', value: 'published' }]"></v-select>
            </v-card-text>
        </v-card>

        <!-- Main image -->
        <v-card class="mb-6">
            <v-card-title class="text-subtitle-1">Main image</v-card-title>
            <v-card-text>
                <div class="d-flex align-center ga-4 flex-wrap">
                    <v-img v-if="form.mainImageUrl" :src="absoluteUrl(form.mainImageUrl)" width="220" aspect-ratio="16/9"
                        cover class="rounded border"></v-img>
                    <div v-else class="main-image-placeholder rounded d-flex align-center justify-center">
                        <v-icon color="grey" size="40">mdi-image-outline</v-icon>
                    </div>
                    <div class="d-flex flex-column ga-2">
                        <v-btn variant="tonal" prepend-icon="mdi-upload" :loading="uploadingMain"
                            @click="mainFileInput?.click()">
                            {{ form.mainImageUrl ? 'Replace image' : 'Upload image' }}
                        </v-btn>
                        <v-btn v-if="form.mainImageUrl" variant="text" color="error" size="small"
                            @click="form.mainImageUrl = null">Remove</v-btn>
                        <span class="text-caption text-medium-emphasis">PNG, JPG, or WebP. Max 5 MB.</span>
                    </div>
                    <input ref="mainFileInput" type="file" accept="image/png,image/jpeg,image/webp"
                        class="d-none" @change="onMainFileChange" />
                </div>
            </v-card-text>
        </v-card>

        <!-- Body -->
        <v-card class="mb-6">
            <v-card-title class="text-subtitle-1">Body</v-card-title>
            <v-card-text>
                <RichTextEditor v-model="bodyHtml" />
            </v-card-text>
        </v-card>

        <!-- Additional images (gallery) -->
        <v-card class="mb-6">
            <v-card-title class="d-flex align-center text-subtitle-1">
                More photos
                <v-spacer></v-spacer>
                <v-btn v-if="!isNew" variant="tonal" size="small" prepend-icon="mdi-plus"
                    :loading="uploadingGallery" @click="galleryFileInput?.click()">Add photos</v-btn>
                <input ref="galleryFileInput" type="file" accept="image/png,image/jpeg,image/webp" multiple
                    class="d-none" @change="onGalleryFilesChange" />
            </v-card-title>
            <v-card-text>
                <p v-if="isNew" class="text-body-2 text-medium-emphasis">
                    Save the post first, then you can add more photos.
                </p>
                <p v-else-if="images.length === 0" class="text-body-2 text-medium-emphasis">
                    No extra photos yet. These appear as a gallery on the post page.
                </p>
                <draggable v-else v-model="visibleRows" item-key="id" handle=".drag-handle"
                    :animation="180" ghost-class="drag-ghost" class="gallery-grid" @end="onReorderEnd">
                    <template #item="{ element: img }">
                        <div class="gallery-card rounded border">
                            <v-icon class="drag-handle" color="grey">mdi-drag</v-icon>
                            <v-img :src="absoluteUrl(img.imageUrl)" aspect-ratio="1" cover class="rounded-t"></v-img>
                            <div class="pa-2">
                                <v-text-field :model-value="img.caption ?? ''" label="Caption" density="compact"
                                    hide-details variant="plain"
                                    @update:model-value="(v: string) => (img.caption = v)"
                                    @blur="saveCaption(img)"></v-text-field>
                                <div class="d-flex justify-end mt-1">
                                    <v-btn icon variant="text" size="x-small" color="error" aria-label="Delete photo"
                                        @click="removeImage(img)">
                                        <v-icon>mdi-delete</v-icon>
                                    </v-btn>
                                </div>
                            </div>
                        </div>
                    </template>
                </draggable>
            </v-card-text>
        </v-card>

        <div class="d-flex ga-3">
            <v-btn color="primary" size="large" :loading="saving" :disabled="!!titleError" @click="save">
                {{ isNew ? 'Create post' : 'Save changes' }}
            </v-btn>
            <v-btn variant="text" to="/Admin/Blog">Cancel</v-btn>
        </div>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import draggable from 'vuedraggable'
import { BlogService, type BlogPostImageDto } from '@/services/BlogService'
import { useConfirm } from '@/composables/useConfirm'
import { useDragReorder } from '@/composables/useDragReorder'
import RichTextEditor from '@/components/RichTextEditor.vue'

const route = useRoute()
const router = useRouter()
const blogService = new BlogService()
const confirm = useConfirm()

// /Admin/Blog/New has no :id param → create mode. /Admin/Blog/:id → edit mode.
const postId = ref<string | null>((route.params.id as string) ?? null)
const isNew = computed(() => !postId.value)

const form = ref<{
    title: string
    slug: string
    excerpt: string
    status: 'draft' | 'published'
    mainImageUrl: string | null
}>({ title: '', slug: '', excerpt: '', status: 'draft', mainImageUrl: null })
const bodyHtml = ref<string>('')
const images = ref<BlogPostImageDto[]>([])

const saving = ref(false)
const uploadingMain = ref(false)
const uploadingGallery = ref(false)
const mainFileInput = ref<HTMLInputElement | null>(null)
const galleryFileInput = ref<HTMLInputElement | null>(null)
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

const titleError = computed(() => (form.value.title.trim() ? '' : 'Title is required.'))

// Live preview of the slug that would be generated when the field is left blank.
const derivedSlug = computed(() => slugify(form.value.title) || 'post')
function slugify(input: string): string {
    return (input || '').trim().toLowerCase()
        .replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '')
}

// Drag-drop reorder of the gallery (same composable every admin sort list uses).
const { visibleRows, onReorderEnd } = useDragReorder<BlogPostImageDto>({
    rows: images,
    save: (items) => blogService.reorderImages(postId.value!, items),
    onError: async () => { await reloadImages() },
})

async function loadForEdit() {
    if (!postId.value) return
    try {
        const resp = await blogService.getAdmin(postId.value)
        const d = resp.data.data
        form.value = {
            title: d.title,
            slug: d.slug,
            excerpt: d.excerpt ?? '',
            status: d.status,
            mainImageUrl: d.mainImageUrl,
        }
        bodyHtml.value = d.bodyHtml ?? ''
        images.value = d.images
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load post.', 'error')
    }
}

async function reloadImages() {
    if (!postId.value) return
    const resp = await blogService.getAdmin(postId.value)
    images.value = resp.data.data.images
}

function requestBody() {
    return {
        title: form.value.title.trim(),
        slug: form.value.slug.trim() || null,
        excerpt: form.value.excerpt.trim() || null,
        bodyHtml: bodyHtml.value || null,
        mainImageUrl: form.value.mainImageUrl,
        status: form.value.status,
    }
}

async function save() {
    if (titleError.value) return
    saving.value = true
    try {
        if (isNew.value) {
            const resp = await blogService.create(requestBody())
            postId.value = resp.data.data.id
            flash('Post created. You can now add photos.', 'success')
            // Move to the edit URL so a refresh lands back on this post and the
            // gallery section unlocks.
            router.replace(`/Admin/Blog/${postId.value}`)
            await loadForEdit()
        } else {
            await blogService.update(postId.value!, requestBody())
            flash('Saved.', 'success')
            await loadForEdit()
        }
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function onMainFileChange(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0]
    if (!file) return
    uploadingMain.value = true
    try {
        const resp = await blogService.uploadMainImage(file)
        form.value.mainImageUrl = resp.data.data.imageUrl
    } catch (err: any) {
        flash(err.response?.data?.error || 'Upload failed.', 'error')
    } finally {
        uploadingMain.value = false
        if (mainFileInput.value) mainFileInput.value.value = ''
    }
}

async function onGalleryFilesChange(e: Event) {
    const files = Array.from((e.target as HTMLInputElement).files ?? [])
    if (files.length === 0 || !postId.value) return
    uploadingGallery.value = true
    try {
        // Append after the current max sort_order, spaced by 10 (the reorder convention).
        let nextSort = (images.value.reduce((m, i) => Math.max(m, i.sortOrder), 0)) + 10
        const added: BlogPostImageDto[] = []
        for (const file of files) {
            const resp = await blogService.addImage(postId.value, file, null, nextSort)
            added.push(resp.data.data)
            nextSort += 10
        }
        // Reassign (not push): useDragReorder watches the `images` ref without `deep`,
        // so an in-place push wouldn't resync the draggable list and the new photos
        // wouldn't render until reload.
        images.value = [...images.value, ...added]
        flash(`Added ${files.length} photo${files.length > 1 ? 's' : ''}.`, 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Upload failed.', 'error')
    } finally {
        uploadingGallery.value = false
        if (galleryFileInput.value) galleryFileInput.value.value = ''
    }
}

async function saveCaption(img: BlogPostImageDto) {
    try {
        await blogService.updateImage(img.id, img.caption?.trim() || null)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to save caption.', 'error')
    }
}

async function removeImage(img: BlogPostImageDto) {
    const ok = await confirm({
        title: 'Remove photo?',
        message: 'This photo will be deleted from the post.',
        confirmText: 'Remove',
        confirmColor: 'error',
    })
    if (!ok) return
    try {
        await blogService.deleteImage(img.id)
        images.value = images.value.filter(i => i.id !== img.id)
        flash('Photo removed.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
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
.main-image-placeholder {
    width: 220px;
    aspect-ratio: 16 / 9;
    background: rgba(0, 0, 0, 0.04);
    border: 1px dashed rgba(0, 0, 0, 0.2);
}
.gallery-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
    gap: 12px;
}
.gallery-card {
    position: relative;
    background: rgb(var(--v-theme-surface));
}
.drag-handle {
    position: absolute;
    top: 6px;
    left: 6px;
    z-index: 2;
    cursor: grab;
    background: rgba(255, 255, 255, 0.85);
    border-radius: 4px;
}
.drag-handle:active { cursor: grabbing; }
.drag-ghost { opacity: 0.5; }
</style>
