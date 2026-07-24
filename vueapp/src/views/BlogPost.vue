<template>
    <v-container class="py-8" style="max-width: 860px">
        <v-btn variant="text" prepend-icon="mdi-arrow-left" to="/Blog" class="mb-4">All posts</v-btn>

        <div v-if="loading" class="d-flex justify-center py-12">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <v-alert v-else-if="notFound" type="info" variant="tonal">
            This post isn't available.
        </v-alert>

        <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>

        <article v-else-if="post">
            <h1 class="text-h3 font-display mb-2">{{ post.title }}</h1>
            <p v-if="post.publishedAtUtc" class="text-body-2 text-medium-emphasis mb-6">
                {{ formatDate(post.publishedAtUtc) }}
            </p>

            <v-img v-if="post.mainImageUrl" :src="absoluteUrl(post.mainImageUrl)" max-height="460"
                cover class="rounded mb-6"></v-img>

            <RichTextView v-if="post.bodyHtml" :html="post.bodyHtml" class="mb-8" />

            <!-- Gallery: the "several other images". Click to open a fullscreen viewer. -->
            <section v-if="post.images.length > 0">
                <h2 class="text-h5 mb-3">Photos</h2>
                <v-row>
                    <v-col v-for="(img, idx) in post.images" :key="img.id" cols="6" sm="4">
                        <v-img :src="absoluteUrl(img.imageUrl)" aspect-ratio="1" cover
                            class="rounded gallery-thumb" @click="openViewer(idx)"></v-img>
                    </v-col>
                </v-row>
            </section>
        </article>

        <!-- Fullscreen photo viewer -->
        <v-dialog v-model="viewerOpen" fullscreen :scrim="false" transition="dialog-bottom-transition">
            <v-card color="black">
                <v-toolbar color="black" density="compact">
                    <v-toolbar-title v-if="post && post.images.length > 1" class="text-caption text-grey-lighten-2">
                        {{ viewerIndex + 1 }} / {{ post.images.length }}
                    </v-toolbar-title>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" color="white" @click="viewerOpen = false"></v-btn>
                </v-toolbar>
                <v-carousel v-if="post" v-model="viewerIndex" hide-delimiters
                    :show-arrows="post.images.length > 1" height="calc(100vh - 48px)">
                    <v-carousel-item v-for="img in post.images" :key="img.id">
                        <div class="d-flex flex-column align-center justify-center fill-height">
                            <v-img :src="absoluteUrl(img.imageUrl)" max-height="90vh" contain></v-img>
                            <div v-if="img.caption" class="text-grey-lighten-2 text-body-2 mt-2 px-4 text-center">
                                {{ img.caption }}
                            </div>
                        </div>
                    </v-carousel-item>
                </v-carousel>
            </v-card>
        </v-dialog>
    </v-container>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { formatTenantDate } from '@/helpers/TenantTime'
import { BlogService, type BlogPostDetail } from '@/services/BlogService'
import RichTextView from '@/components/RichTextView.vue'

const route = useRoute()
const blogService = new BlogService()

const post = ref<BlogPostDetail | null>(null)
const loading = ref(true)
const notFound = ref(false)
const loadError = ref('')
const viewerOpen = ref(false)
const viewerIndex = ref(0)

const apiUrl: string = import.meta.env.VITE_API_ENDPOINT ?? ''
function apiOrigin(): string {
    try { return new URL(apiUrl, window.location.origin).origin } catch { return '' }
}
function absoluteUrl(url: string | null | undefined): string {
    if (!url) return ''
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}

function formatDate(iso: string): string {
    return formatTenantDate(iso, 'MMMM D, YYYY')
}

function openViewer(index: number) {
    viewerIndex.value = index
    viewerOpen.value = true
}

async function load(slug: string) {
    loading.value = true
    notFound.value = false
    loadError.value = ''
    post.value = null
    try {
        const resp = await blogService.getBySlug(slug)
        post.value = resp.data.data
    } catch (err: any) {
        if (err.response?.status === 404) notFound.value = true
        else loadError.value = err.response?.data?.error
            || 'Could not load this post. Refresh to try again, or check your connection.'
    } finally {
        loading.value = false
    }
}

onMounted(() => load(route.params.slug as string))
// Support client-side navigation between posts without a full remount.
watch(() => route.params.slug, (slug) => { if (slug) load(slug as string) })
</script>

<style scoped>
.gallery-thumb {
    cursor: pointer;
    transition: transform 0.15s ease;
}
.gallery-thumb:hover { transform: scale(1.02); }
</style>
