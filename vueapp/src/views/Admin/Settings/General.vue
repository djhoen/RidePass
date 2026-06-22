<template>
    <v-container>
        <h1 class="text-h4 mb-2">General</h1>
        <p class="text-body-2 text-medium-emphasis mb-6">
            Display name, mailing address, and timezone for your tenant. Used on the RidePass
            Discover page so riders can find you by area, and on packages we ship you.
            Feature toggles live in
            <router-link to="/Admin/Settings/Features">Settings → Features</router-link>.
        </p>

        <v-card class="pa-4">
            <v-card-text>
                <v-row>
                    <v-col cols="12" md="6">
                        <v-text-field :model-value="branding.displayName" label="Display Name" readonly density="compact"
                            hint="Change requires super-admin assistance for now." persistent-hint></v-text-field>
                    </v-col>
                    <v-col cols="12" md="6">
                        <v-text-field v-model="form.shippingName" label="Shipping Name" density="compact"
                            hint="Recipient name for packages — e.g. 'Acme MX – Office'." persistent-hint
                            :hide-details="false"></v-text-field>
                    </v-col>
                </v-row>
                <v-row>
                    <v-col cols="12" md="6">
                        <div class="text-caption text-medium-emphasis mb-1">Tenant type</div>
                        <v-chip size="small" color="primary" variant="tonal">
                            <v-icon start size="small">
                                {{ branding.tenantType === 'mountain_bike' ? 'mdi-bike' : 'mdi-motorbike' }}
                            </v-icon>
                            {{ branding.tenantType === 'mountain_bike' ? 'Mountain Bike (MTB)' : 'Motocross (MX)' }}
                        </v-chip>
                        <div class="text-caption text-medium-emphasis mt-1">
                            Locked at creation. Contact a super admin to change.
                        </div>
                    </v-col>
                </v-row>

                <v-row>
                    <v-col cols="12">
                        <v-text-field v-model="form.addressLine" label="Address" density="compact"></v-text-field>
                    </v-col>
                </v-row>
                <v-row>
                    <v-col cols="12" md="4">
                        <v-text-field v-model="form.postalCode" label="Postal code" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="4">
                        <v-text-field v-model="form.city" label="City" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="4">
                        <v-text-field v-model="form.region" label="State / Region" density="compact"></v-text-field>
                    </v-col>
                </v-row>
                <v-row align="center">
                    <v-col cols="12" md="4">
                        <v-autocomplete v-model="form.timezone" :items="timezoneOptions" label="Timezone"
                            density="compact" :loading="saving" @update:model-value="tzTouched = true"></v-autocomplete>
                    </v-col>
                    <v-col cols="12" md="4">
                        <v-text-field v-model.number="form.latitude" type="number" step="0.00001"
                            label="Latitude (-90 to 90)" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="4">
                        <v-text-field v-model.number="form.longitude" type="number" step="0.00001"
                            label="Longitude (-180 to 180)" density="compact"></v-text-field>
                    </v-col>
                </v-row>
                <div v-if="geocodeResult" class="text-caption text-medium-emphasis mt-2">
                    Found: {{ geocodeResult }}
                </div>

                <v-divider class="my-6"></v-divider>
                <div class="text-subtitle-1 mb-2">Contact</div>
                <p class="text-caption text-medium-emphasis mb-3">
                    Shown in the home-page footer for riders to reach out.
                </p>
                <v-row>
                    <v-col cols="12" md="6">
                        <v-text-field v-model="form.contactEmail" type="email"
                            label="Contact email" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="6">
                        <PhoneField v-model="form.phone"
                            label="Phone" density="compact" />
                    </v-col>
                </v-row>

                <v-divider class="my-6"></v-divider>
                <div class="text-subtitle-1 mb-2">Social links</div>
                <p class="text-caption text-medium-emphasis mb-3">
                    Each link's icon shows in the footer when the URL is set.
                    Leave any field blank to hide that platform.
                </p>
                <v-row>
                    <v-col cols="12" md="6">
                        <v-text-field v-model="form.socialFacebookUrl"
                            label="Facebook URL" density="compact"
                            placeholder="https://facebook.com/your-track"
                            prepend-inner-icon="mdi-facebook"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="6">
                        <v-text-field v-model="form.socialInstagramUrl"
                            label="Instagram URL" density="compact"
                            placeholder="https://instagram.com/your-track"
                            prepend-inner-icon="mdi-instagram"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="6">
                        <v-text-field v-model="form.socialTiktokUrl"
                            label="TikTok URL" density="compact"
                            placeholder="https://tiktok.com/@your-track"
                            prepend-inner-icon="mdi-music-note"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="6">
                        <v-text-field v-model="form.socialYoutubeUrl"
                            label="YouTube URL" density="compact"
                            placeholder="https://youtube.com/@your-track"
                            prepend-inner-icon="mdi-youtube"></v-text-field>
                    </v-col>
                </v-row>

                <v-divider class="my-6"></v-divider>
                <div class="text-subtitle-1 mb-2">Refund Policy</div>
                <p class="text-caption text-medium-emphasis mb-3">
                    Linked from the footer. Riders can read it before purchasing.
                </p>
                <RichTextEditor v-model="form.refundPolicyHtml" />

                <v-btn color="primary" class="mt-4" :loading="saving" @click="save">Save</v-btn>
            </v-card-text>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import { TenantService } from '@/services/TenantService'
import { branding, loadBranding } from '@/stores/branding'
import { geocode } from '@/helpers/Geocode'
import RichTextEditor from '@/components/RichTextEditor.vue'
import PhoneField from '@/components/PhoneField.vue'
// @ts-expect-error tz-lookup ships no types — single function (lat, lng) => string IANA name.
import tzlookup from 'tz-lookup'

const tenantService = new TenantService()

const form = ref({
    shippingName: '' as string | null,
    timezone: 'America/New_York',
    addressLine: '' as string | null,
    city: '' as string | null,
    region: '' as string | null,
    postalCode: '' as string | null,
    latitude: null as number | null,
    longitude: null as number | null,
    contactEmail: '' as string | null,
    phone: '' as string | null,
    socialFacebookUrl: '' as string | null,
    socialInstagramUrl: '' as string | null,
    socialTiktokUrl: '' as string | null,
    socialYoutubeUrl: '' as string | null,
    refundPolicyHtml: '',
})

const saving = ref(false)
const geocoding = ref(false)
// Set once the admin picks a timezone themselves, so an address geocode won't silently
// overwrite their choice.
const tzTouched = ref(false)
const geocodeResult = ref('')
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

// North American IANA zones the user is likely to pick — US, Canada, Mexico, plus
// Hawaii. Keeping it scoped avoids the 400-zone global list.
const timezoneOptions = [
    'America/New_York',
    'America/Detroit',
    'America/Indiana/Indianapolis',
    'America/Chicago',
    'America/Denver',
    'America/Boise',
    'America/Phoenix',
    'America/Los_Angeles',
    'America/Anchorage',
    'America/Adak',
    'Pacific/Honolulu',
    'America/Toronto',
    'America/Halifax',
    'America/St_Johns',
    'America/Winnipeg',
    'America/Regina',
    'America/Edmonton',
    'America/Vancouver',
    'America/Whitehorse',
    'America/Yellowknife',
    'America/Mexico_City',
    'America/Cancun',
    'America/Monterrey',
    'America/Chihuahua',
    'America/Hermosillo',
    'America/Mazatlan',
    'America/Tijuana',
]

function populateForm() {
    form.value.shippingName = branding.shippingName ?? ''
    form.value.timezone = branding.timezone || 'America/New_York'
    form.value.addressLine = branding.addressLine ?? ''
    form.value.city = branding.city ?? ''
    form.value.region = branding.region ?? ''
    form.value.postalCode = branding.postalCode ?? ''
    form.value.latitude = branding.latitude
    form.value.longitude = branding.longitude
    form.value.contactEmail = branding.contactEmail ?? ''
    form.value.phone = branding.phone ?? ''
    form.value.socialFacebookUrl = branding.socialFacebookUrl ?? ''
    form.value.socialInstagramUrl = branding.socialInstagramUrl ?? ''
    form.value.socialTiktokUrl = branding.socialTiktokUrl ?? ''
    form.value.socialYoutubeUrl = branding.socialYoutubeUrl ?? ''
    form.value.refundPolicyHtml = branding.refundPolicyHtml ?? ''
}

function normalizeString(s: string | null): string | null {
    if (s === null || s === undefined) return null
    const t = s.trim()
    return t.length === 0 ? null : t
}

// Auto-geocode when there's enough address info to actually find a location: address line +
// (city OR postal code). Debounce so we don't spam the geocoder while the user is typing.
// Only triggers when lat/lng are still empty — once the user has a fix, manual edits don't
// re-trigger lookups.
let autoLookupTimer: ReturnType<typeof setTimeout> | null = null

function scheduleAutoLookup() {
    if (autoLookupTimer) clearTimeout(autoLookupTimer)
    if (form.value.latitude && form.value.longitude) return  // already located
    const hasAddr = !!normalizeString(form.value.addressLine)
    const hasArea = !!normalizeString(form.value.city) || !!normalizeString(form.value.postalCode)
    if (!hasAddr || !hasArea) return
    autoLookupTimer = setTimeout(() => {
        if (!geocoding.value) geocodeAddress()
    }, 1500)
}

watch(() => [form.value.addressLine, form.value.city, form.value.region, form.value.postalCode], () => {
    scheduleAutoLookup()
}, { deep: false })

async function geocodeAddress() {
    const parts = [form.value.addressLine, form.value.city, form.value.region, form.value.postalCode, 'USA']
        .filter(p => p && (p as string).trim().length > 0)
    if (parts.length === 0) {
        snackbarText.value = 'Enter address details first.'
        snackbarColor.value = 'error'
        snackbar.value = true
        return
    }
    geocoding.value = true
    try {
        const result = await geocode(parts.join(', '))
        if (!result) {
            snackbarText.value = 'Could not locate that address.'
            snackbarColor.value = 'error'
            snackbar.value = true
            return
        }
        form.value.latitude = Number(result.lat.toFixed(6))
        form.value.longitude = Number(result.lng.toFixed(6))
        geocodeResult.value = result.displayName

        // Derive the IANA timezone from the resolved coordinates so the admin doesn't
        // have to pick it manually. Offline lookup (tz-lookup) — no API call.
        try {
            const tz = tzlookup(result.lat, result.lng)
            if (tz && !tzTouched.value) form.value.timezone = tz
        } catch { /* tz-lookup throws on out-of-range coords; leave timezone untouched */ }
    } catch {
        snackbarText.value = 'Couldn’t look up that address. Enter coordinates manually or try again.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        geocoding.value = false
    }
}

async function save() {
    try {
        saving.value = true
        // Two backend endpoints: timezone lives on the settings update, the rest on location.
        // Send settings first — if its validation (IANA timezone check) fails we abort before
        // writing partial location data. The other three flags are owned by the Features page;
        // pass through whatever's currently in branding so we don't trample them.
        if (form.value.timezone !== branding.timezone) {
            await tenantService.updateSettings({
                timezone: form.value.timezone,
                requireReservationForPasses: branding.requireReservationForPasses,
                requireEmergencyContact: branding.requireEmergencyContact,
                allowEventSubscriptions: branding.allowEventSubscriptions,
                requireIdAtCheckin: branding.requireIdAtCheckin,
            })
        }
        await tenantService.updateLocation({
            shippingName: normalizeString(form.value.shippingName),
            addressLine: normalizeString(form.value.addressLine),
            city: normalizeString(form.value.city),
            region: normalizeString(form.value.region),
            postalCode: normalizeString(form.value.postalCode),
            country: 'USA',  // assumed for now; surface as a field again if you expand outside US.
            latitude: form.value.latitude,
            longitude: form.value.longitude,
        })
        // Footer endpoint owns contactEmail / phone / social URLs / refund policy.
        await tenantService.updateFooter({
            contactEmail: normalizeString(form.value.contactEmail),
            phone: normalizeString(form.value.phone),
            socialFacebookUrl: normalizeString(form.value.socialFacebookUrl),
            socialInstagramUrl: normalizeString(form.value.socialInstagramUrl),
            socialTiktokUrl: normalizeString(form.value.socialTiktokUrl),
            socialYoutubeUrl: normalizeString(form.value.socialYoutubeUrl),
            refundPolicyHtml: normalizeString(form.value.refundPolicyHtml),
        })
        await loadBranding()
        snackbarText.value = 'Saved.'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Save failed.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        saving.value = false
    }
}

onMounted(async () => {
    if (!branding.loaded) await loadBranding()
    populateForm()
})

watch(() => branding.loaded, (loaded) => {
    if (loaded) populateForm()
})
</script>
