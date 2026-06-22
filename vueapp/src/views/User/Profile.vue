<template>
    <v-container>
        <div class="profile-header">
            <h1 class="text-h4">My Profile</h1>
            <v-spacer></v-spacer>
            <v-btn color="primary" :loading="saving" prepend-icon="mdi-content-save"
                @click="saveProfile">Save profile</v-btn>
        </div>

        <Spinner v-model="loading" />

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
            <v-col cols="12" md="3" class="text-center">
                <v-avatar size="120" color="grey-lighten-2" class="mb-4">
                    <v-img v-if="avatarSrc" :src="avatarSrc"></v-img>
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
                                        readonly hint="Contact support to change your email." persistent-hint></v-text-field>
                                </v-col>
                                <v-col cols="12" sm="6">
                                    <PhoneField v-model="profile.phone" label="Mobile Phone" />
                                </v-col>
                            </v-row>
                        </v-form>
                    </v-card-text>
                </v-card>
            </v-col>

            <v-col cols="12">
                <v-card class="mt-4">
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
                    </v-card-text>
                </v-card>
            </v-col>


            <v-col cols="12" v-if="!isApex && branding.allowEventSubscriptions">
                <v-card class="mt-4">
                    <v-card-title>New events</v-card-title>
                    <v-card-text>
                        <p class="text-caption text-medium-emphasis mb-3">
                            Get notified when {{ branding.displayName }} posts a new event.
                        </p>
                        <v-alert v-if="eventSubError" type="warning" variant="tonal" density="compact" class="mb-2">
                            {{ eventSubError }}
                        </v-alert>
                        <v-switch v-model="eventSub.notifyEmail" color="primary" hide-details
                            :disabled="eventSubLoading || eventSubSaving"
                            label="Email me about new events"
                            @update:model-value="saveEventSub"></v-switch>
                        <v-switch v-model="eventSub.notifySms" color="primary" hide-details class="mt-1"
                            :disabled="eventSubLoading || eventSubSaving || !profile.phone"
                            :label="profile.phone ? 'Text me about new events' : 'Text me about new events (add a mobile phone above)'"
                            @update:model-value="saveEventSub"></v-switch>
                    </v-card-text>
                </v-card>
            </v-col>

            <v-col cols="12" v-if="!isApex && (newsletterStatus || newsletterError)">
                <v-card class="mt-4">
                    <v-card-text>
                        <v-alert v-if="newsletterError" type="warning" variant="tonal" density="compact">
                            {{ newsletterError }}
                        </v-alert>
                        <v-switch v-else v-model="newsletterSubscribed" color="primary" hide-details
                            :loading="newsletterSaving" @update:model-value="toggleNewsletter"
                            :label="`Subscribe to the ${branding.displayName} newsletter`"></v-switch>
                    </v-card-text>
                </v-card>
            </v-col>
        </v-row>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { UserService } from '@/services/UserService'
import { NewsletterService } from '@/services/NewsletterService'
import Spinner from '@/components/Spinner.vue'
import PhoneField from '@/components/PhoneField.vue'
import { branding } from '@/stores/branding'
import tenantHelper from '@/helpers/TenantHelper'
import { LoampassLinkService, type LoampassStatus } from '@/services/LoampassLinkService'
import { EventSubscriptionService } from '@/services/EventSubscriptionService'
import { useConfirm } from '@/composables/useConfirm'

const userService = new UserService()
const newsletterService = new NewsletterService()
const loampassService = new LoampassLinkService()
const eventSubService = new EventSubscriptionService()
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
const newsletterError = ref('')

const eventSub = ref({ notifyEmail: false, notifySms: false })
const eventSubLoading = ref(false)
const eventSubSaving = ref(false)
const eventSubError = ref('')

const profile = ref<any>({
    firstName: '', lastName: '', email: '', phone: '', imageUrl: ''
})
// v-file-input returns a single File (non-multiple) or a File[] depending on the Vuetify
// build, so always normalize before use.
const profileImage = ref<any>(null)
function pickFile(val: any): File | undefined {
    if (!val) return undefined
    return Array.isArray(val) ? val[0] : val
}
const loading = ref(false)
const saving = ref(false)
const emergencyContact = ref({ name: '', phone: '' })

// Resolve a stored image URL for display: Spaces URLs are absolute; local-disk URLs are
// app-relative and need the API origin (the SPA runs on a different port in dev).
const apiOrigin = (() => {
    try { return new URL((import.meta as any).env?.VITE_API_ENDPOINT ?? '', window.location.origin).origin }
    catch { return '' }
})()
const displayImageUrl = computed(() => {
    const u = profile.value.imageUrl
    if (!u) return ''
    return /^https?:\/\//i.test(u) ? u : `${apiOrigin}${u}`
})

// Live preview of a freshly selected file before it's uploaded; falls back to the saved photo.
const localPreview = ref('')
watch(profileImage, (val) => {
    if (localPreview.value) URL.revokeObjectURL(localPreview.value)
    const f = pickFile(val)
    localPreview.value = f ? URL.createObjectURL(f) : ''
})
const avatarSrc = computed(() => localPreview.value || displayImageUrl.value)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('success')

onMounted(async () => {
    try {
        loading.value = true
        const response = await userService.getProfile()
        // The endpoint wraps the user in { data: {...} }; bind the UNWRAPPED object, or every
        // form field reads off the wrapper and renders blank.
        const data = (response.data as any).data ?? response.data
        profile.value = {
            firstName: data?.firstName ?? '',
            lastName: data?.lastName ?? '',
            email: data?.email ?? '',
            phone: data?.phone ?? '',
            imageUrl: data?.imageUrl ?? '',
        }
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
        } catch (err: any) {
            newsletterError.value = err.response?.data?.error || 'Could not load your newsletter preference. Refresh to try again.'
        }
        if (branding.loampassMxEnabled) await loadLoampassStatus()
        if (branding.allowEventSubscriptions) await loadEventSub()
    }
})

async function loadEventSub() {
    eventSubLoading.value = true
    try {
        const r = await eventSubService.mine()
        const d = (r.data as any).data
        eventSub.value = { notifyEmail: !!d.notifyEmail, notifySms: !!d.notifySms }
    } catch (err: any) {
        eventSubError.value = err.response?.data?.error
            || 'Could not load your event notifications. Refresh to try again.'
    } finally {
        eventSubLoading.value = false
    }
}

async function saveEventSub() {
    eventSubError.value = ''
    eventSubSaving.value = true
    try {
        await eventSubService.updateMine({
            notifyEmail: eventSub.value.notifyEmail,
            notifySms: eventSub.value.notifySms,
        })
    } catch (err: any) {
        eventSubError.value = err.response?.data?.error || 'Could not update your event notifications.'
        // Revert the optimistic toggle to the server's actual state.
        await loadEventSub()
    } finally {
        eventSubSaving.value = false
    }
}

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

async function saveProfile() {
    try {
        saving.value = true
        // Upload a newly chosen photo first, then persist its URL alongside the rest of the form.
        const file = pickFile(profileImage.value)
        if (file) {
            const up = await userService.uploadProfilePhoto(file)
            profile.value.imageUrl = (up.data as any).data?.imageUrl ?? profile.value.imageUrl
            profileImage.value = null
        }
        await userService.updateProfile({
            firstName: profile.value.firstName,
            lastName: profile.value.lastName,
            phone: profile.value.phone,
            emergencyContactName: emergencyContact.value.name.trim() || null,
            emergencyContactPhone: emergencyContact.value.phone.trim() || null,
            imageUrl: profile.value.imageUrl || null,
        })
        snackbarText.value = 'Profile saved.'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch (error: any) {
        snackbarText.value = error.response?.data?.error || error.response?.data?.message || 'Failed to save profile.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        saving.value = false
    }
}
</script>

<style scoped>
/* Title + Save stay pinned just below the app bar so the Save button is always reachable. */
.profile-header {
    position: sticky;
    top: 64px;
    z-index: 5;
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 12px 0;
    margin-bottom: 16px;
    background: rgb(var(--v-theme-background));
    border-bottom: 1px solid rgba(0, 0, 0, 0.08);
}
</style>
