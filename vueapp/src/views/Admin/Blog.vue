<template>
    <v-container>
        <div class="d-flex align-center mb-2 ga-2 flex-wrap">
            <h1 class="text-h4">Blog</h1>
            <v-spacer></v-spacer>
            <v-btn color="primary" prepend-icon="mdi-plus" to="/Admin/Blog/New">New post</v-btn>
        </div>
        <p class="text-body-2 text-medium-emphasis mb-6">
            Write posts with photos, publish them, and feature one on your home page.
            <template v-if="!branding.blogEnabled">
                The blog is currently <strong>off</strong> — turn it on under
                <router-link to="/Admin/Settings/Features">Settings &rarr; Features</router-link>
                to show it on your public site.
            </template>
        </p>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 64px"></th>
                        <th>Title</th>
                        <th style="width: 120px">Status</th>
                        <th style="width: 110px" class="text-center">Photos</th>
                        <th style="width: 160px">Published</th>
                        <th style="width: 200px" class="text-right">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-if="!loading && posts.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-8">
                            No posts yet. Click "New post" to write your first.
                        </td>
                    </tr>
                    <tr v-for="p in posts" :key="p.id">
                        <td>
                            <v-avatar v-if="p.mainImageUrl" rounded size="44">
                                <v-img :src="absoluteUrl(p.mainImageUrl)" cover></v-img>
                            </v-avatar>
                            <v-avatar v-else rounded size="44" color="grey-lighten-2">
                                <v-icon color="grey">mdi-image-outline</v-icon>
                            </v-avatar>
                        </td>
                        <td>
                            <router-link :to="`/Admin/Blog/${p.id}`" class="font-weight-medium text-primary">
                                {{ p.title }}
                            </router-link>
                            <div class="text-caption text-medium-emphasis">/{{ p.slug }}</div>
                        </td>
                        <td>
                            <v-chip size="small" :color="p.status === 'published' ? 'success' : 'grey'" variant="tonal">
                                {{ p.status === 'published' ? 'Published' : 'Draft' }}
                            </v-chip>
                        </td>
                        <td class="text-center">{{ p.imageCount }}</td>
                        <td class="text-caption">{{ p.publishedAtUtc ? formatDate(p.publishedAtUtc) : '—' }}</td>
                        <td class="text-right">
                            <v-tooltip :text="featureTooltip(p)" location="top">
                                <template #activator="{ props }">
                                    <span v-bind="props">
                                        <v-btn icon variant="text" size="small"
                                            :color="p.isFeatured ? 'amber-darken-2' : undefined"
                                            :disabled="p.status !== 'published' || busyId === p.id"
                                            :loading="busyId === p.id"
                                            @click="toggleFeatured(p)">
                                            <v-icon>{{ p.isFeatured ? 'mdi-star' : 'mdi-star-outline' }}</v-icon>
                                        </v-btn>
                                    </span>
                                </template>
                            </v-tooltip>
                            <v-btn icon variant="text" size="small" :to="`/Admin/Blog/${p.id}`" aria-label="Edit">
                                <v-icon>mdi-pencil</v-icon>
                            </v-btn>
                            <v-btn icon variant="text" size="small" color="error" aria-label="Delete"
                                :disabled="busyId === p.id" @click="remove(p)">
                                <v-icon>mdi-delete</v-icon>
                            </v-btn>
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import { BlogService, type BlogPostListItem } from '@/services/BlogService'
import { branding, loadBranding } from '@/stores/branding'
import { useConfirm } from '@/composables/useConfirm'

const blogService = new BlogService()
const confirm = useConfirm()

const posts = ref<BlogPostListItem[]>([])
const loading = ref(true)
const busyId = ref<string | null>(null)
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

function formatDate(iso: string): string {
    return dayjs(iso).format('MMM D, YYYY')
}

function featureTooltip(p: BlogPostListItem): string {
    if (p.status !== 'published') return 'Publish the post to feature it'
    return p.isFeatured ? 'Featured on home page' : 'Feature on home page'
}

async function load() {
    loading.value = true
    try {
        const resp = await blogService.listAdmin()
        posts.value = resp.data.data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load posts.', 'error')
    } finally {
        loading.value = false
    }
}

async function toggleFeatured(p: BlogPostListItem) {
    busyId.value = p.id
    try {
        await blogService.setFeatured(p.id, !p.isFeatured)
        await load()
        flash(p.isFeatured ? 'Removed from home page.' : 'Featured on home page.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Update failed.', 'error')
    } finally {
        busyId.value = null
    }
}

async function remove(p: BlogPostListItem) {
    const ok = await confirm({
        title: 'Delete post?',
        message: `"${p.title}" and its photos will be permanently deleted.`,
        confirmText: 'Delete',
        confirmColor: 'error',
    })
    if (!ok) return
    busyId.value = p.id
    try {
        await blogService.delete(p.id)
        await load()
        flash('Post deleted.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    } finally {
        busyId.value = null
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onMounted(async () => {
    if (!branding.loaded) await loadBranding()
    await load()
})
</script>
