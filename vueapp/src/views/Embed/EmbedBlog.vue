<template>
    <!-- Chromeless news feed: latest published blog posts as cards. Clicking a post
         opens the full article on the tenant's hosted site in a new tab (reading a
         long post inside a widget iframe is a bad time). -->
    <div class="embed-blog pa-3">
        <div v-if="loading" class="text-center py-8">
            <v-progress-circular indeterminate color="primary" />
        </div>
        <template v-else>
            <div v-if="loadError" class="text-center text-error py-8">{{ loadError }}</div>
            <div v-else-if="posts.length === 0" class="text-center text-medium-emphasis py-8">
                No news yet.
            </div>
            <div v-else class="blog-grid">
                <a v-for="p in posts" :key="p.slug" class="blog-card" :href="`/Blog/${p.slug}`" target="_blank"
                    rel="noopener">
                    <div v-if="p.mainImageUrl" class="blog-card__img">
                        <v-img :src="p.mainImageUrl" cover height="140" />
                    </div>
                    <div class="pa-3">
                        <div class="text-subtitle-2">{{ p.title }}</div>
                        <div v-if="p.publishedAtUtc" class="text-caption text-medium-emphasis mb-1">
                            {{ formatDay(p.publishedAtUtc) }}
                        </div>
                        <div v-if="p.excerpt" class="text-body-2 blog-card__excerpt">{{ p.excerpt }}</div>
                    </div>
                </a>
            </div>
        </template>
    </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import { BlogService, type PublicBlogListItem } from '@/services/BlogService'

const route = useRoute()
const posts = ref<PublicBlogListItem[]>([])
const loading = ref(true)
const loadError = ref('')

const limit = (() => {
    const n = parseInt(String(route.query.limit ?? ''), 10)
    return Number.isFinite(n) && n > 0 ? n : 6
})()

function formatDay(utc: string): string {
    return dayjs.utc(utc).local().format('MMM D, YYYY')
}

onMounted(async () => {
    try {
        const r = await new BlogService().listPublic()
        posts.value = (r.data.data ?? []).slice(0, limit)
    } catch (err: any) {
        // A 404 means the blog feature is off for this tenant; that renders as empty
        // rather than an error banner on someone else's website.
        if (err.response?.status === 404) posts.value = []
        else loadError.value = err.response?.data?.error || 'Could not load news. Refresh the page to try again.'
    } finally {
        loading.value = false
    }
})
</script>

<style scoped>
.blog-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
    gap: 12px;
}
.blog-card {
    display: block;
    border: 1px solid rgba(var(--v-theme-on-surface), 0.12);
    border-radius: 8px;
    overflow: hidden;
    text-decoration: none;
    color: inherit;
    transition: box-shadow 0.15s ease;
}
.blog-card:hover { box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15); }
.blog-card__excerpt {
    display: -webkit-box;
    -webkit-line-clamp: 3;
    -webkit-box-orient: vertical;
    overflow: hidden;
}
</style>
