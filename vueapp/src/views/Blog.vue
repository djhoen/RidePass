<template>
    <v-container class="py-8">
        <h1 class="text-h3 font-display mb-6">Blog</h1>

        <div v-if="loading" class="d-flex justify-center py-12">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <v-alert v-else-if="unavailable" type="info" variant="tonal">
            There's no blog here yet. Check back soon.
        </v-alert>

        <p v-else-if="posts.length === 0" class="text-body-1 text-medium-emphasis">
            No posts yet. Check back soon.
        </p>

        <v-row v-else>
            <v-col v-for="p in posts" :key="p.slug" cols="12" sm="6" md="4">
                <v-card class="h-100 d-flex flex-column" :to="`/Blog/${p.slug}`" hover>
                    <v-img v-if="p.mainImageUrl" :src="absoluteUrl(p.mainImageUrl)" aspect-ratio="16/9" cover></v-img>
                    <div v-else class="blog-card-placeholder d-flex align-center justify-center">
                        <v-icon color="grey" size="48">mdi-post-outline</v-icon>
                    </div>
                    <v-card-item>
                        <v-card-title class="text-wrap">{{ p.title }}</v-card-title>
                        <v-card-subtitle v-if="p.publishedAtUtc">{{ formatDate(p.publishedAtUtc) }}</v-card-subtitle>
                    </v-card-item>
                    <v-card-text v-if="p.excerpt" class="flex-grow-1">{{ p.excerpt }}</v-card-text>
                    <v-card-actions>
                        <v-btn variant="text" color="primary" append-icon="mdi-arrow-right">Read more</v-btn>
                    </v-card-actions>
                </v-card>
            </v-col>
        </v-row>
    </v-container>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import { BlogService, type PublicBlogListItem } from '@/services/BlogService'

const blogService = new BlogService()
const posts = ref<PublicBlogListItem[]>([])
const loading = ref(true)
const unavailable = ref(false)

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
    return dayjs(iso).format('MMMM D, YYYY')
}

onMounted(async () => {
    try {
        const resp = await blogService.listPublic()
        posts.value = resp.data.data
    } catch (err: any) {
        // 404 = the tenant has the blog turned off; show a soft "nothing here" state.
        if (err.response?.status === 404) unavailable.value = true
        else console.error('Failed to load blog', err)
    } finally {
        loading.value = false
    }
})
</script>

<style scoped>
.blog-card-placeholder {
    aspect-ratio: 16 / 9;
    background: rgba(0, 0, 0, 0.04);
}
</style>
