<template>
    <v-container>
        <h1 class="text-h4 mb-6">My Profile</h1>

        <Spinner v-model="loading" />

        <v-row v-if="!loading && !isApex && newsletterStatus">
            <v-col cols="12">
                <v-card class="mb-4" variant="tonal">
                    <v-card-text>
                        <div class="d-flex align-center">
                            <div class="flex-grow-1">
                                <div class="text-subtitle-1">{{ branding.displayName }} newsletter</div>
                                <div class="text-caption text-medium-emphasis">
                                    Event updates and announcements for {{ newsletterStatus.email }}.
                                </div>
                            </div>
                            <v-switch v-model="newsletterSubscribed" color="primary" hide-details density="compact"
                                :loading="newsletterSaving" @update:model-value="toggleNewsletter"></v-switch>
                        </div>
                    </v-card-text>
                </v-card>
            </v-col>
        </v-row>

        <v-row v-if="!loading">
            <v-col cols="12">
                <v-card class="mb-4">
                    <v-card-title>Mobile phone</v-card-title>
                    <v-card-text>
                        <p class="text-caption text-medium-emphasis mb-3">
                            Used for waitlist promotions and event-day alerts.
                        </p>
                        <v-row>
                            <v-col cols="12" sm="6">
                                <PhoneField v-model="phone" label="Phone" density="compact" />
                            </v-col>
                        </v-row>
                        <v-btn color="primary" :loading="savingPhone" :disabled="!canSavePhone" class="mt-2"
                            @click="savePhone">Save phone</v-btn>
                    </v-card-text>
                </v-card>
                <v-card class="mb-4">
                    <v-card-title>Emergency contact</v-card-title>
                    <v-card-text>
                        <p class="text-caption text-medium-emphasis mb-3">
                            Who to call if there's a problem at the track. Some tracks won't let you
                            purchase passes until this is on file.
                        </p>
                        <v-row>
                            <v-col cols="12" sm="6">
                                <v-text-field v-model="emergencyContact.name" label="Name" density="compact"></v-text-field>
                            </v-col>
                            <v-col cols="12" sm="6">
                                <PhoneField v-model="emergencyContact.phone" label="Phone" density="compact" />
                            </v-col>
                        </v-row>
                        <v-btn color="primary" :loading="savingEmergency" :disabled="!canSaveEmergency" class="mt-2"
                            @click="saveEmergencyContact">Save emergency contact</v-btn>
                    </v-card-text>
                </v-card>
            </v-col>

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
                            <v-btn type="submit" color="primary" class="mt-2" :loading="saving">Save</v-btn>
                        </v-form>
                    </v-card-text>
                </v-card>
            </v-col>
        </v-row>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { UserService } from '@/services/UserService'
import { NewsletterService } from '@/services/NewsletterService'
import Spinner from '@/components/Spinner.vue'
import PhoneField from '@/components/PhoneField.vue'
import { branding } from '@/stores/branding'
import tenantHelper from '@/helpers/TenantHelper'

const userService = new UserService()
const newsletterService = new NewsletterService()
const isApex = computed(() => !tenantHelper.getSubdomain())

const newsletterStatus = ref<{ subscribed: boolean; email: string } | null>(null)
const newsletterSubscribed = ref(false)
const newsletterSaving = ref(false)

const profile = ref<any>({
    firstName: '', lastName: '', email: '', phone: '', aboutMe: '', imageUrl: ''
})
const profileImage = ref<File[] | null>(null)
const loading = ref(false)
const saving = ref(false)
const phone = ref('')
const savingPhone = ref(false)
const canSavePhone = computed(() => phone.value.replace(/\D/g, '').length >= 7)
const emergencyContact = ref({ name: '', phone: '' })
const savingEmergency = ref(false)
const canSaveEmergency = computed(() =>
    emergencyContact.value.name.trim().length > 0
    && emergencyContact.value.phone.replace(/\D/g, '').length >= 7)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('success')

onMounted(async () => {
    try {
        loading.value = true
        const response = await userService.getProfile()
        profile.value = response.data
        const data = (response.data as any).data ?? response.data
        phone.value = data?.phone ?? ''
        emergencyContact.value = {
            name: data?.emergencyContactName ?? '',
            phone: data?.emergencyContactPhone ?? '',
        }
    } catch {
        snackbarText.value = 'Failed to load profile.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        loading.value = false
    }
    if (!isApex.value) {
        try {
            const r = await newsletterService.getMyStatus()
            newsletterStatus.value = (r.data as any).data
            newsletterSubscribed.value = newsletterStatus.value!.subscribed
        } catch { /* non-critical */ }
    }
})

async function toggleNewsletter(next: boolean | null) {
    const target = !!next
    newsletterSaving.value = true
    try {
        if (target) {
            await newsletterService.subscribeMe()
        } else {
            await newsletterService.unsubscribeMe()
        }
        newsletterSubscribed.value = target
    } catch (err: any) {
        // Revert the switch if the call failed.
        newsletterSubscribed.value = !target
        snackbarText.value = err.response?.data?.error || 'Could not update newsletter preference.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        newsletterSaving.value = false
    }
}

async function savePhone() {
    if (!canSavePhone.value) return
    savingPhone.value = true
    try {
        await userService.updatePhone({ phone: phone.value.trim() })
        snackbarText.value = 'Phone saved.'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Failed to save phone.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        savingPhone.value = false
    }
}

async function saveEmergencyContact() {
    if (!canSaveEmergency.value) return
    savingEmergency.value = true
    try {
        await userService.updateEmergencyContact({
            name: emergencyContact.value.name.trim(),
            phone: emergencyContact.value.phone.trim(),
        })
        snackbarText.value = 'Emergency contact saved.'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Failed to save emergency contact.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        savingEmergency.value = false
    }
}

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
