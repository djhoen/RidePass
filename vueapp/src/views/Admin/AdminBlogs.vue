<template>
    <v-container>
        <div class="d-flex align-center mb-6">
            <h1 class="text-h4">Blog Management</h1>
            <v-spacer></v-spacer>
            <v-btn color="primary" to="/Admin/Blogs/Post/0">New Post</v-btn>
        </div>

        <Spinner v-model="loading" />

        <v-card v-if="!loading" class="mb-6">
            <v-card-title>Blog Feeds</v-card-title>
            <v-list>
                <v-list-item v-for="feed in feeds" :key="feed.id" :title="feed.name"
                    :subtitle="feed.url"></v-list-item>
            </v-list>
            <v-card-text v-if="feeds.length === 0">No feeds found.</v-card-text>
        </v-card>

        <v-card v-if="!loading">
            <v-card-title>Blog Posts</v-card-title>
            <v-table>
                <thead>
                    <tr>
                        <th>Title</th>
                        <th>URL</th>
                        <th>Published</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="post in posts" :key="post.id">
                        <td>{{ post.title }}</td>
                        <td>{{ post.url }}</td>
                        <td>{{ filters.date(post.publishedDate) }}</td>
                        <td class="text-right">
                            <v-btn variant="text" size="small"
                                :to="`/Admin/Blogs/Post/${post.id}`">Edit</v-btn>
                        </td>
                    </tr>
                </tbody>
            </v-table>
            <v-card-text v-if="posts.length === 0">No posts found.</v-card-text>
        </v-card>

        <v-snackbar v-model="snackbar" color="error" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { BlogService } from '@/services/BlogService'
import filters from '@/helpers/Filters'
import Spinner from '@/components/Spinner.vue'

const blogService = new BlogService()

const feeds = ref<any[]>([])
const posts = ref<any[]>([])
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')

onMounted(async () => {
    try {
        loading.value = true
        const [feedsRes, postsRes] = await Promise.all([
            blogService.getBlogFeeds(),
            blogService.getBlogPosts(0)
        ])
        feeds.value = feedsRes.data
        posts.value = postsRes.data
    } catch {
        snackbarText.value = 'Failed to load blog data.'
        snackbar.value = true
    } finally {
        loading.value = false
    }
})
</script>
