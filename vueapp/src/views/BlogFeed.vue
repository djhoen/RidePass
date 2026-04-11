<template>
    <v-container>
        <h1 class="text-h4 mb-6">Blog</h1>

        <Spinner v-model="loading" />

        <v-row v-if="!loading">
            <v-col v-for="post in posts" :key="post.id" cols="12" md="4">
                <v-card :to="`/Blog/${post.url}`" hover height="100%">
                    <v-img v-if="post.imageUrl" :src="post.imageUrl" height="200" cover></v-img>
                    <v-card-title>{{ post.title }}</v-card-title>
                    <v-card-subtitle>{{ filters.date(post.publishedDate) }}</v-card-subtitle>
                    <v-card-text>{{ post.summary }}</v-card-text>
                </v-card>
            </v-col>
        </v-row>

        <v-alert v-if="!loading && posts.length === 0" type="info" variant="tonal">
            No blog posts found.
        </v-alert>

        <v-snackbar v-model="snackbar" color="error" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { BlogService } from '@/services/BlogService'
import filters from '@/helpers/Filters'
import Spinner from '@/components/Spinner.vue'

const blogService = new BlogService()

const posts = ref<any[]>([])
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')

onMounted(async () => {
    try {
        loading.value = true
        const response = await blogService.getBlogPosts(0)
        posts.value = response.data
    } catch {
        snackbarText.value = 'Failed to load blog posts.'
        snackbar.value = true
    } finally {
        loading.value = false
    }
})
</script>
