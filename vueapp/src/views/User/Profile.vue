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

        <v-row v-if="!loading && branding.loampassMxEnabled && loampass.trackParticipates">
            <v-col cols="12">
                <v-card class="mb-4">
                    <v-card-title class="d-flex align-center">
                        <span>Loam Pass</span>
                        <v-spacer></v-spacer>
                        <v-chip v-if="loampass.linked" size="small" color="success" variant="tonal">Connected</v-chip>
                    </v-card-title>
                    <v-card-text>
                        <template v-if="loampass.accounts.length > 0">
                            <p v-if="loampass.creditsAvailable !== null" class="text-body-2 text-medium-emphasis mb-2">
                                You have <strong>{{ loampass.creditsAvailable }}</strong>
                                Loam Pass credit{{ loampass.creditsAvailable === 1 ? '' : 's' }} available here.
                                Use one to pay for entry at checkout.
                            </p>
                            <v-list density="compact" class="py-0 mb-2">
                                <v-list-item v-for="a in loampass.accounts" :key="a.loampassAccountId" class="px-0">
                                    <template #prepend>
                                        <v-icon size="small" class="mr-2">mdi-ticket-account</v-icon>
                                    </template>
                                    <v-list-item-title>{{ a.loampassEmail }}</v-list-item-title>
                                    <template #append>
                                        <v-btn variant="text" color="error" size="small" :loading="loampassBusy"
                                            @click="disconnectLoampass(a.loampassAccountId)">Disconnect</v-btn>
                                    </template>
                                </v-list-item>
                            </v-list>
                            <v-divider class="mb-3"></v-divider>
                        </template>

                        <p class="text-body-2 text-medium-emphasis mb-3">
                            {{ loampass.accounts.length > 0
                                ? 'Connect another Loam Pass account.'
                                : 'Connect your Loam Pass account to redeem credits for entry at this track.' }}
                        </p>
                        <template v-if="loampassStep === 'email'">
                            <v-row>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="loampassEmail" type="email" label="Loam Pass email"
                                        density="compact" hide-details></v-text-field>
                                </v-col>
                            </v-row>
                            <v-btn color="primary" class="mt-2" :loading="loampassBusy"
                                :disabled="!loampassEmail.trim()" @click="sendLoampassCode">Email me a code</v-btn>
                        </template>
                        <template v-else>
                            <p class="text-body-2 mb-2">We sent a 6-digit code to <strong>{{ loampassEmail }}</strong>.</p>
                            <v-row>
                                <v-col cols="12" sm="4">
                                    <v-text-field v-model="loampassCode" label="Code" density="compact"
                                        hide-details maxlength="6"></v-text-field>
                                </v-col>
                            </v-row>
                            <div class="d-flex ga-2 mt-2">
                                <v-btn color="primary" :loading="loampassBusy" :disabled="!loampassCode.trim()"
                                    @click="confirmLoampassCode">Connect</v-btn>
                                <v-btn variant="text" :disabled="loampassBusy"
                                    @click="loampassStep = 'email'">Use a different email</v-btn>
                            </div>
                        </template>
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
import { LoampassLinkService, type LoampassStatus } from '@/services/LoampassLinkService'
import { useConfirm } from '@/composables/useConfirm'

const userService = new UserService()
const newsletterService = new NewsletterService()
const loampassService = new LoampassLinkService()
const confirm = useConfirm()
const isApex = computed(() => !tenantHelper.getSubdomain())

const loampass = ref<LoampassStatus>({ trackParticipates: false, linked: false, accounts: [], creditsAvailable: null })
const loampassStep = ref<'email' | 'code'>('email')
const loampassEmail = ref('')
const loampassCode = ref('')
const loampassBusy = ref(false)

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
        if (branding.loampassMxEnabled) await loadLoampassStatus()
    }
})

async function loadLoampassStatus() {
    try {
        const r = await loampassService.status()
        loampass.value = (r.data as any).data
    } catch { /* non-critical */ }
}

async function sendLoampassCode() {
    if (!loampassEmail.value.trim()) return
    loampassBusy.value = true
    try {
        await loampassService.linkStart(loampassEmail.value.trim())
        loampassStep.value = 'code'
        flash('Code sent. Check your Loam Pass email.', 'success')
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not send a code.', 'error')
    } finally {
        loampassBusy.value = false
    }
}

async function confirmLoampassCode() {
    if (!loampassCode.value.trim()) return
    loampassBusy.value = true
    try {
        await loampassService.linkConfirm(loampassEmail.value.trim(), loampassCode.value.trim())
        loampassCode.value = ''
        loampassEmail.value = ''
        loampassStep.value = 'email'
        await loadLoampassStatus()
        flash('Loam Pass connected.', 'success')
    } catch (e: any) {
        flash(e.response?.data?.error || 'That code was invalid or expired.', 'error')
    } finally {
        loampassBusy.value = false
    }
}

async function disconnectLoampass(accountId: string) {
    const ok = await confirm({
        title: 'Disconnect this Loam Pass?',
        message: 'You can reconnect it any time.',
        confirmText: 'Disconnect',
        confirmColor: 'error',
    })
    if (!ok) return
    loampassBusy.value = true
    try {
        await loampassService.unlink(accountId)
        await loadLoampassStatus()
        flash('Loam Pass disconnected.', 'success')
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not disconnect.', 'error')
    } finally {
        loampassBusy.value = false
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

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
