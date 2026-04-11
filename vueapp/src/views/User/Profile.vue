<template>
    <v-container>
        <h1 class="text-h4 mb-6">My Profile</h1>

        <Spinner v-model="loading" />

        <v-row v-if="!loading">
            <v-col cols="12" md="3" class="text-center">
                <v-avatar size="120" color="grey-lighten-2" class="mb-4">
                    <v-img v-if="profile.imageUrl" :src="profile.imageUrl"></v-img>
                    <v-icon v-else size="64">mdi-account</v-icon>
                </v-avatar>
                <v-file-input v-model="profileImage" label="Upload Photo" prepend-icon="mdi-camera"
                    accept="image/*" density="compact" hide-details class="mb-4"></v-file-input>
            </v-col>

            <v-col cols="12" md="9">
                <v-card>
                    <v-card-text>
                        <v-form @submit.prevent="saveProfile">
                            <v-row>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="profile.firstName" label="First Name"
                                        required></v-text-field>
                                </v-col>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="profile.lastName" label="Last Name"
                                        required></v-text-field>
                                </v-col>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="profile.email" label="Email" type="email"
                                        required></v-text-field>
                                </v-col>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="profile.phone" label="Phone"></v-text-field>
                                </v-col>
                                <v-col cols="12">
                                    <v-textarea v-model="profile.aboutMe" label="About Me" rows="3"></v-textarea>
                                </v-col>
                            </v-row>
                            <v-btn type="submit" color="primary" :loading="saving">Save</v-btn>
                        </v-form>
                    </v-card-text>
                </v-card>
            </v-col>
        </v-row>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { UserService } from '@/services/UserService'
import Spinner from '@/components/Spinner.vue'

const userService = new UserService()

const profile = ref<any>({
    firstName: '', lastName: '', email: '', phone: '', aboutMe: '', imageUrl: ''
})
const profileImage = ref<File[] | null>(null)
const loading = ref(false)
const saving = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('success')

onMounted(async () => {
    try {
        loading.value = true
        const response = await userService.getProfile()
        profile.value = response.data
    } catch {
        snackbarText.value = 'Failed to load profile.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        loading.value = false
    }
})

async function saveProfile() {
    try {
        saving.value = true
        await userService.updateProfile(profile.value)
        snackbarText.value = 'Profile updated successfully!'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch (error: any) {
        snackbarText.value = error.response?.data?.message || 'Failed to update profile.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        saving.value = false
    }
}
</script>
