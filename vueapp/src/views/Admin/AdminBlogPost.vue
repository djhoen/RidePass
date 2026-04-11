<template>
    <v-container>
        <v-btn variant="text" prepend-icon="mdi-arrow-left" to="/Admin/Blogs" class="mb-4">Back to Blogs</v-btn>

        <h1 class="text-h4 mb-6">{{ isNew ? 'New Blog Post' : 'Edit Blog Post' }}</h1>

        <Spinner v-model="loading" />

        <template v-if="!loading">
            <v-card class="mb-4">
                <v-card-text>
                    <v-form @submit.prevent="savePost">
                        <v-text-field v-model="post.title" label="Title" required class="mb-2"></v-text-field>
                        <v-text-field v-model="post.url" label="URL Slug" required class="mb-2"></v-text-field>
                        <v-textarea v-model="post.summary" label="Summary" rows="3" class="mb-2"></v-textarea>
                        <v-switch v-model="post.isPublished" label="Published" color="primary"
                            class="mb-2"></v-switch>
                        <v-btn type="submit" color="primary" :loading="saving">Save Post</v-btn>
                    </v-form>
                </v-card-text>
            </v-card>

            <template v-if="!isNew">
                <div class="d-flex align-center mb-4">
                    <h2 class="text-h5">Sections</h2>
                    <v-spacer></v-spacer>
                    <v-btn color="primary" size="small" @click="addSection">Add Section</v-btn>
                </div>

                <v-card v-for="(section, index) in sections" :key="section.id || index" class="mb-3">
                    <v-card-text>
                        <v-text-field v-model="section.title" label="Section Title" density="compact"
                            class="mb-2"></v-text-field>
                        <v-textarea v-model="section.text" label="Text" rows="3" density="compact"
                            class="mb-2"></v-textarea>
                        <v-text-field v-model="section.mediaUrl" label="Media URL" density="compact"
                            class="mb-2"></v-text-field>
                        <div class="d-flex">
                            <v-btn size="small" :disabled="index === 0" icon="mdi-arrow-up" variant="text"
                                @click="moveSection(index, -1)"></v-btn>
                            <v-btn size="small" :disabled="index === sections.length - 1" icon="mdi-arrow-down"
                                variant="text" @click="moveSection(index, 1)"></v-btn>
                            <v-spacer></v-spacer>
                            <v-btn size="small" color="primary" variant="text"
                                @click="saveSection(section)">Save</v-btn>
                            <v-btn size="small" color="error" variant="text"
                                @click="deleteSection(section, index)">Delete</v-btn>
                        </div>
                    </v-card-text>
                </v-card>
            </template>
        </template>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { BlogService } from '@/services/BlogService'
import Spinner from '@/components/Spinner.vue'

const route = useRoute()
const router = useRouter()
const blogService = new BlogService()

const postId = computed(() => Number(route.params.id))
const isNew = computed(() => postId.value === 0)

const post = ref<any>({ title: '', url: '', summary: '', isPublished: false })
const sections = ref<any[]>([])
const loading = ref(false)
const saving = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('success')

onMounted(async () => {
    if (!isNew.value) {
        try {
            loading.value = true
            const [postRes, sectionsRes] = await Promise.all([
                blogService.getBlogPost(postId.value),
                blogService.getBlogPostSections(postId.value)
            ])
            post.value = postRes.data
            sections.value = sectionsRes.data
        } catch {
            snackbarText.value = 'Failed to load blog post.'
            snackbarColor.value = 'error'
            snackbar.value = true
        } finally {
            loading.value = false
        }
    }
})

async function savePost() {
    try {
        saving.value = true
        if (isNew.value) {
            const response = await blogService.createBlogPost(post.value)
            router.push(`/Admin/Blogs/Post/${response.data.id}`)
        } else {
            await blogService.updateBlogPost(post.value)
        }
        snackbarText.value = 'Post saved!'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch {
        snackbarText.value = 'Failed to save post.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        saving.value = false
    }
}

function addSection() {
    sections.value.push({ title: '', text: '', mediaUrl: '', blogPostId: postId.value })
}

async function saveSection(section: any) {
    try {
        if (section.id) {
            await blogService.updateBlogPostSection(section)
        } else {
            const response = await blogService.createBlogPostSection(section)
            section.id = response.data.id
        }
        snackbarText.value = 'Section saved!'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch {
        snackbarText.value = 'Failed to save section.'
        snackbarColor.value = 'error'
        snackbar.value = true
    }
}

async function deleteSection(section: any, index: number) {
    try {
        if (section.id) {
            await blogService.deleteBlogPostSection(section.id)
        }
        sections.value.splice(index, 1)
        snackbarText.value = 'Section deleted.'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch {
        snackbarText.value = 'Failed to delete section.'
        snackbarColor.value = 'error'
        snackbar.value = true
    }
}

function moveSection(index: number, direction: number) {
    const newIndex = index + direction
    const temp = sections.value[index]
    sections.value[index] = sections.value[newIndex]
    sections.value[newIndex] = temp
}
</script>
