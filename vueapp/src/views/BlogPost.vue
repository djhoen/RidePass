<template>
    <v-container>
        <Spinner v-model="loading" />

        <template v-if="!loading && post">
            <v-btn variant="text" prepend-icon="mdi-arrow-left" to="/BlogFeed" class="mb-4">Back to Blog</v-btn>

            <h1 class="text-h3 mb-2">{{ post.title }}</h1>
            <p class="text-subtitle-1 text-medium-emphasis mb-6">{{ filters.date(post.publishedDate) }}</p>

            <v-img v-if="post.imageUrl" :src="post.imageUrl" max-height="400" class="mb-6 rounded"></v-img>

            <p class="text-body-1 mb-8">{{ post.summary }}</p>

            <div v-for="section in sections" :key="section.id" class="mb-8">
                <h2 v-if="section.title" class="text-h5 mb-3">{{ section.title }}</h2>
                <p v-if="section.text" class="text-body-1 mb-4">{{ section.text }}</p>
                <v-img v-if="section.mediaUrl" :src="section.mediaUrl" max-height="400"
                    class="rounded mb-4"></v-img>
            </div>
        </template>

        <v-snackbar v-model="snackbar" color="error" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { BlogService } from '@/services/BlogService'
import filters from '@/helpers/Filters'
import Spinner from '@/components/Spinner.vue'

const route = useRoute()
const blogService = new BlogService()

const post = ref<any>(null)
const sections = ref<any[]>([])
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')

onMounted(async () => {
    try {
        loading.value = true
        const url = route.params.url as string
        const postResponse = await blogService.getBlogPostByUrl(url)
        post.value = postResponse.data
        const sectionsResponse = await blogService.getBlogPostSections(post.value.id)
        sections.value = sectionsResponse.data
    } catch {
        snackbarText.value = 'Failed to load blog post.'
        snackbar.value = true
    } finally {
        loading.value = false
    }
})
</script>
