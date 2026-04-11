<template>
    <v-container>
        <h1 class="text-h4 mb-6">Site Content</h1>

        <Spinner v-model="loading" />

        <template v-if="!loading">
            <v-card class="mb-6">
                <v-card-title>Banners</v-card-title>
                <v-card-text>
                    <v-table v-if="banners.length > 0">
                        <thead>
                            <tr>
                                <th>Title</th>
                                <th>Active</th>
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="banner in banners" :key="banner.id">
                                <td>{{ banner.title }}</td>
                                <td>
                                    <v-chip :color="banner.isActive ? 'success' : 'grey'" size="small">
                                        {{ banner.isActive ? 'Active' : 'Inactive' }}
                                    </v-chip>
                                </td>
                                <td class="text-right">
                                    <v-btn variant="text" size="small" @click="editBanner(banner)">Edit</v-btn>
                                </td>
                            </tr>
                        </tbody>
                    </v-table>
                    <div v-else>No banners found.</div>
                    <v-btn color="primary" size="small" class="mt-4" @click="addBanner">Add Banner</v-btn>
                </v-card-text>
            </v-card>

            <v-card>
                <v-card-title>Site Settings</v-card-title>
                <v-card-text>
                    <v-form @submit.prevent="saveSetting">
                        <v-row>
                            <v-col cols="12" sm="4">
                                <v-text-field v-model="setting.name" label="Setting Name"
                                    density="compact"></v-text-field>
                            </v-col>
                            <v-col cols="12" sm="6">
                                <v-text-field v-model="setting.value" label="Value" density="compact"></v-text-field>
                            </v-col>
                            <v-col cols="12" sm="2">
                                <v-btn type="submit" color="primary" :loading="savingSettings">Save</v-btn>
                            </v-col>
                        </v-row>
                    </v-form>
                </v-card-text>
            </v-card>
        </template>

        <v-dialog v-model="bannerDialog" max-width="600">
            <v-card>
                <v-card-title>{{ bannerForm.id ? 'Edit Banner' : 'New Banner' }}</v-card-title>
                <v-card-text>
                    <v-text-field v-model="bannerForm.title" label="Title" class="mb-2"></v-text-field>
                    <v-textarea v-model="bannerForm.text" label="Text" rows="2" class="mb-2"></v-textarea>
                    <v-text-field v-model="bannerForm.imageUrl" label="Image URL" class="mb-2"></v-text-field>
                    <v-switch v-model="bannerForm.isActive" label="Active" color="primary"></v-switch>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="bannerDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="savingBanner" @click="saveBanner">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { SiteService } from '@/services/SiteService'
import Spinner from '@/components/Spinner.vue'

const siteService = new SiteService()

const banners = ref<any[]>([])
const bannerDialog = ref(false)
const bannerForm = ref<any>({ title: '', text: '', imageUrl: '', isActive: true })
const setting = ref({ name: '', value: '' })
const loading = ref(false)
const savingBanner = ref(false)
const savingSettings = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('success')

onMounted(async () => {
    try {
        loading.value = true
        const response = await siteService.getBanners()
        banners.value = response.data
    } catch {
        snackbarText.value = 'Failed to load site content.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        loading.value = false
    }
})

function addBanner() {
    bannerForm.value = { title: '', text: '', imageUrl: '', isActive: true }
    bannerDialog.value = true
}

function editBanner(banner: any) {
    bannerForm.value = { ...banner }
    bannerDialog.value = true
}

async function saveBanner() {
    try {
        savingBanner.value = true
        await siteService.saveBanner(bannerForm.value)
        bannerDialog.value = false
        const response = await siteService.getBanners()
        banners.value = response.data
        snackbarText.value = 'Banner saved!'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch {
        snackbarText.value = 'Failed to save banner.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        savingBanner.value = false
    }
}

async function saveSetting() {
    try {
        savingSettings.value = true
        await siteService.saveSetting(setting.value)
        snackbarText.value = 'Setting saved!'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch {
        snackbarText.value = 'Failed to save setting.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        savingSettings.value = false
    }
}
</script>
