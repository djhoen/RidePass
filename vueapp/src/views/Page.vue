<template>
    <v-container class="py-8" style="max-width: 860px">
        <div v-if="loading" class="d-flex justify-center py-12">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <v-alert v-else-if="notFound" type="info" variant="tonal">
            This page isn't available.
        </v-alert>

        <v-alert v-else-if="loadError" type="error" variant="tonal">{{ loadError }}</v-alert>

        <article v-else-if="page">
            <h1 class="text-h3 font-display mb-6">{{ page.title }}</h1>

            <v-img v-if="page.heroImageUrl" :src="absoluteUrl(page.heroImageUrl)" max-height="460"
                cover class="rounded mb-6"></v-img>

            <RichTextView v-if="page.bodyHtml" :html="page.bodyHtml" />
        </article>
    </v-container>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { PageService, type PublicPageResponse } from '@/services/PageService'
import RichTextView from '@/components/RichTextView.vue'

const route = useRoute()
const pageService = new PageService()

const page = ref<PublicPageResponse | null>(null)
const loading = ref(true)
const notFound = ref(false)
const loadError = ref('')

const apiUrl: string = import.meta.env.VITE_API_ENDPOINT ?? ''
function apiOrigin(): string {
    try { return new URL(apiUrl, window.location.origin).origin } catch { return '' }
}
function absoluteUrl(url: string | null | undefined): string {
    if (!url) return ''
    if (/^https?:\/\//i.test(url)) return url
    return `${apiOrigin()}${url}`
}

async function load(slug: string) {
    loading.value = true
    notFound.value = false
    loadError.value = ''
    page.value = null
    try {
        const resp = await pageService.getBySlug(slug)
        page.value = resp.data.data
    } catch (err: any) {
        // 404 = no such page for this tenant, or it isn't published; don't leak which.
        if (err.response?.status === 404) notFound.value = true
        else loadError.value = err.response?.data?.error
            || 'Could not load this page. Refresh to try again, or check your connection.'
    } finally {
        loading.value = false
    }
}

onMounted(() => load(route.params.slug as string))
// Support client-side navigation between custom pages without a full remount.
watch(() => route.params.slug, (slug) => { if (slug) load(slug as string) })
</script>
