<template>
    <!-- Chromeless news feed: latest published posts as cards. Clicking a post opens
         the full article INLINE within the same iframe (list <-> detail), so the
         reader never leaves the host site. The iframe auto-resizes to whichever
         view is showing. -->
    <div class="embed-blog pa-3">
        <div v-if="loading" class="text-center py-8">
            <v-progress-circular indeterminate color="primary" />
        </div>

        <!-- ── Detail: one full post ─────────────────────────────────────── -->
        <template v-else-if="selected">
            <a href="#" class="blog-back" @click.prevent="closePost">
                <v-icon icon="mdi-arrow-left" size="18" /> Back to news
            </a>
            <div v-if="detailError" class="text-center text-error py-8">{{ detailError }}</div>
            <div v-else-if="detailLoading" class="text-center py-8">
                <v-progress-circular indeterminate color="primary" />
            </div>
            <article v-else-if="post" class="blog-article mt-3">
                <h1 class="blog-article__title">{{ post.title }}</h1>
                <div v-if="post.publishedAtUtc" class="text-caption text-medium-emphasis mb-3">
                    {{ formatDay(post.publishedAtUtc) }}
                </div>
                <v-img v-if="post.mainImageUrl" :src="post.mainImageUrl" class="blog-article__hero mb-4"
                    max-height="360" cover />
                <div class="blog-article__body" v-html="post.bodyHtml"></div>
            </article>
        </template>

        <!-- ── List: cards ───────────────────────────────────────────────── -->
        <template v-else>
            <div v-if="loadError" class="text-center text-error py-8">{{ loadError }}</div>
            <div v-else-if="posts.length === 0" class="text-center text-medium-emphasis py-8">
                No news yet.
            </div>
            <div v-else class="blog-grid">
                <button v-for="p in posts" :key="p.slug" type="button" class="blog-card" @click="openPost(p.slug)">
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
                </button>
            </div>
        </template>
    </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import { BlogService, type PublicBlogListItem, type BlogPostDetail } from '@/services/BlogService'

const route = useRoute()
const service = new BlogService()

const posts = ref<PublicBlogListItem[]>([])
const loading = ref(true)
const loadError = ref('')

const selected = ref<string | null>(null)
const post = ref<BlogPostDetail | null>(null)
const detailLoading = ref(false)
const detailError = ref('')

const limit = (() => {
    const n = parseInt(String(route.query.limit ?? ''), 10)
    return Number.isFinite(n) && n > 0 ? n : 6
})()

function formatDay(utc: string): string {
    return dayjs.utc(utc).local().format('MMM D, YYYY')
}

async function openPost(slug: string) {
    selected.value = slug
    post.value = null
    detailError.value = ''
    detailLoading.value = true
    // Jump the reader to the top of the iframe content when the article opens.
    window.scrollTo({ top: 0 })
    try {
        const r = await service.getBySlug(slug)
        post.value = r.data.data
    } catch (err: any) {
        detailError.value = err.response?.data?.error || 'Could not load this article. Go back and try another.'
    } finally {
        detailLoading.value = false
    }
}

function closePost() {
    selected.value = null
    post.value = null
    window.scrollTo({ top: 0 })
}

onMounted(async () => {
    try {
        const r = await service.listPublic()
        posts.value = (r.data.data ?? []).slice(0, limit)
    } catch (err: any) {
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
    text-align: left;
    width: 100%;
    border: 1px solid rgba(var(--v-theme-on-surface), 0.12);
    border-radius: 8px;
    overflow: hidden;
    background: rgb(var(--v-theme-surface));
    color: inherit;
    cursor: pointer;
    transition: box-shadow 0.15s ease;
    font: inherit;
}
.blog-card:hover { box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15); }
.blog-card__excerpt {
    display: -webkit-box;
    -webkit-line-clamp: 3;
    -webkit-box-orient: vertical;
    overflow: hidden;
}
.blog-back {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    color: rgb(var(--v-theme-primary));
    text-decoration: none;
    font-weight: 600;
    font-size: 14px;
}
.blog-back:hover { text-decoration: underline; }
.blog-article { max-width: 760px; margin: 0 auto; }
.blog-article__title { font-size: 1.6rem; font-weight: 700; line-height: 1.2; margin-bottom: 4px; }
.blog-article__hero { border-radius: 8px; }
.blog-article__body { font-size: 15px; line-height: 1.6; }
.blog-article__body :deep(p) { margin: 0 0 1em; }
.blog-article__body :deep(img) { max-width: 100%; height: auto; border-radius: 6px; }
.blog-article__body :deep(h2),
.blog-article__body :deep(h3) { margin: 1.2em 0 0.5em; font-weight: 700; }
.blog-article__body :deep(ul),
.blog-article__body :deep(ol) { margin: 0 0 1em; padding-left: 1.4em; }
</style>
