<template>
    <v-container>
        <h1 class="text-h4 mb-6">Branding</h1>

        <v-card class="mb-6 pa-4">
            <v-card-title>Tenant Settings</v-card-title>
            <v-card-text>
                <v-row>
                    <v-col cols="12" md="6">
                        <v-text-field :model-value="branding.displayName" label="Display Name" readonly density="compact"
                            hint="Change requires super-admin assistance for now." persistent-hint></v-text-field>
                    </v-col>
                    <v-col cols="12" md="6">
                        <v-autocomplete v-model="settingsForm.timezone" :items="timezoneOptions" label="Timezone"
                            density="compact" :loading="savingSettings"></v-autocomplete>
                    </v-col>
                </v-row>
                <v-switch v-model="settingsForm.requireReservation" color="primary" hide-details
                    label="Require reservation for day passes"
                    hint="When on, riders must pick a ride-day event (with capacity) before buying a pass. Spots are tracked per event."
                    persistent-hint></v-switch>
                <v-btn color="primary" class="mt-4" :loading="savingSettings" @click="saveSettings">Save Settings</v-btn>
            </v-card-text>
        </v-card>

        <v-card class="mb-6 pa-4">
            <v-card-title>Location</v-card-title>
            <v-card-subtitle>
                Shown on the RidePass Discover page so riders can find your track by area.
            </v-card-subtitle>
            <v-card-text>
                <v-row>
                    <v-col cols="12" md="8">
                        <v-text-field v-model="locationForm.addressLine" label="Address" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="4">
                        <v-text-field v-model="locationForm.postalCode" label="Postal code" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="5">
                        <v-text-field v-model="locationForm.city" label="City" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="4">
                        <v-text-field v-model="locationForm.region" label="State / Region" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="3">
                        <v-text-field v-model="locationForm.country" label="Country" density="compact"></v-text-field>
                    </v-col>
                    <v-col cols="12" md="4">
                        <v-text-field v-model.number="locationForm.latitude" type="number" step="0.00001" label="Latitude"
                            density="compact" hint="-90 to 90" persistent-hint></v-text-field>
                    </v-col>
                    <v-col cols="12" md="4">
                        <v-text-field v-model.number="locationForm.longitude" type="number" step="0.00001" label="Longitude"
                            density="compact" hint="-180 to 180" persistent-hint></v-text-field>
                    </v-col>
                    <v-col cols="12" md="4" class="d-flex align-end">
                        <v-btn variant="tonal" :loading="geocoding" @click="geocodeAddress">
                            Look up from address
                        </v-btn>
                    </v-col>
                </v-row>
                <div v-if="geocodeResult" class="text-caption text-medium-emphasis mt-2">
                    Found: {{ geocodeResult }}
                </div>
                <v-btn color="primary" class="mt-4" :loading="savingLocation" @click="saveLocation">Save Location</v-btn>
            </v-card-text>
        </v-card>

        <v-row>
            <v-col cols="12" md="6">
                <v-card class="mb-6 pa-4">
                    <v-card-title>Colors</v-card-title>
                    <v-card-text>
                        <v-row>
                            <v-col cols="12" md="6">
                                <label class="text-subtitle-2">Primary</label>
                                <v-color-picker v-model="form.primaryColor" mode="hex" hide-inputs hide-canvas-actions
                                    show-swatches swatches-max-height="100" :modes="['hex']"></v-color-picker>
                                <v-text-field v-model="form.primaryColor" density="compact" class="mt-2" hide-details></v-text-field>
                            </v-col>
                            <v-col cols="12" md="6">
                                <label class="text-subtitle-2">Secondary</label>
                                <v-color-picker v-model="form.secondaryColor" mode="hex" hide-inputs hide-canvas-actions
                                    show-swatches swatches-max-height="100" :modes="['hex']"></v-color-picker>
                                <v-text-field v-model="form.secondaryColor" density="compact" class="mt-2" hide-details></v-text-field>
                            </v-col>
                        </v-row>
                        <v-row>
                            <v-col cols="12" md="6">
                                <label class="text-subtitle-2">Accent</label>
                                <v-color-picker v-model="form.accentColor" mode="hex" hide-inputs hide-canvas-actions
                                    show-swatches swatches-max-height="100" :modes="['hex']"></v-color-picker>
                                <v-text-field v-model="form.accentColor" density="compact" class="mt-2" hide-details></v-text-field>
                            </v-col>
                            <v-col cols="12" md="6">
                                <label class="text-subtitle-2">Theme Mode</label>
                                <v-radio-group v-model="form.themeMode" inline>
                                    <v-radio label="Light" value="light"></v-radio>
                                    <v-radio label="Dark" value="dark"></v-radio>
                                </v-radio-group>
                                <label class="text-subtitle-2 mt-4 d-block">Tagline</label>
                                <v-textarea v-model="form.tagline" rows="3" placeholder="Ride hard. Ride safe."
                                    density="compact" hide-details></v-textarea>
                            </v-col>
                        </v-row>
                        <v-btn color="primary" class="mt-4" :loading="saving" @click="saveMetadata">Save</v-btn>
                    </v-card-text>
                </v-card>
            </v-col>

            <v-col cols="12" md="6">
                <v-card class="mb-6 pa-4">
                    <v-card-title>Images</v-card-title>
                    <v-card-text>
                        <BrandingImageSlot label="Logo" kind="logo" :url="branding.logoUrl" @uploaded="onUploaded" @removed="onRemoved" />
                        <v-divider class="my-4"></v-divider>
                        <BrandingImageSlot label="Favicon" kind="favicon" :url="branding.faviconUrl" @uploaded="onUploaded" @removed="onRemoved" />
                        <v-divider class="my-4"></v-divider>
                        <BrandingImageSlot label="Home Hero" kind="hero" :url="branding.heroImageUrl" @uploaded="onUploaded" @removed="onRemoved" />
                        <v-divider class="my-4"></v-divider>
                        <BrandingImageSlot label="Secondary Hero" kind="secondaryHero" :url="branding.secondaryHeroUrl" @uploaded="onUploaded" @removed="onRemoved" />
                    </v-card-text>
                </v-card>
            </v-col>
        </v-row>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import { TenantService } from '@/services/TenantService'
import { branding, loadBranding } from '@/stores/branding'
import BrandingImageSlot from '@/components/BrandingImageSlot.vue'
import { geocode } from '@/helpers/Geocode'

const tenantService = new TenantService()

const form = ref({
    primaryColor: '#1976D2',
    secondaryColor: '#424242',
    accentColor: '#82B1FF',
    tagline: '' as string | null,
    themeMode: 'light' as 'light' | 'dark',
})

const settingsForm = ref({
    timezone: 'UTC',
    requireReservation: false,
})

const locationForm = ref({
    addressLine: '' as string | null,
    city: '' as string | null,
    region: '' as string | null,
    postalCode: '' as string | null,
    country: '' as string | null,
    latitude: null as number | null,
    longitude: null as number | null,
})

const savingLocation = ref(false)
const geocoding = ref(false)
const geocodeResult = ref('')

const timezoneOptions = getTimezoneOptions()

const saving = ref(false)
const savingSettings = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

function populateForm() {
    form.value.primaryColor = branding.primaryColor
    form.value.secondaryColor = branding.secondaryColor
    form.value.accentColor = branding.accentColor
    form.value.tagline = branding.tagline ?? ''
    form.value.themeMode = branding.themeMode
    settingsForm.value.timezone = branding.timezone
    settingsForm.value.requireReservation = branding.requireReservationForPasses
    locationForm.value.addressLine = branding.addressLine ?? ''
    locationForm.value.city = branding.city ?? ''
    locationForm.value.region = branding.region ?? ''
    locationForm.value.postalCode = branding.postalCode ?? ''
    locationForm.value.country = branding.country ?? ''
    locationForm.value.latitude = branding.latitude
    locationForm.value.longitude = branding.longitude
}

function getTimezoneOptions(): string[] {
    // Use native Intl if available; fall back to a short list of common IANA zones.
    try {
        const supported = (Intl as any).supportedValuesOf?.('timeZone') as string[] | undefined
        if (supported && supported.length > 0) return supported
    } catch { /* ignore */ }
    return [
        'UTC',
        'America/New_York', 'America/Chicago', 'America/Denver', 'America/Los_Angeles',
        'America/Phoenix', 'America/Anchorage', 'Pacific/Honolulu',
        'Europe/London', 'Europe/Paris', 'Europe/Berlin',
        'Asia/Tokyo', 'Asia/Shanghai', 'Australia/Sydney',
    ]
}

async function saveSettings() {
    try {
        savingSettings.value = true
        await tenantService.updateSettings({
            timezone: settingsForm.value.timezone,
            requireReservationForPasses: settingsForm.value.requireReservation,
        })
        await loadBranding()
        snackbarText.value = 'Settings saved.'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Failed to save settings.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        savingSettings.value = false
    }
}

onMounted(async () => {
    if (!branding.loaded) await loadBranding()
    populateForm()
})

watch(() => branding.loaded, (loaded) => {
    if (loaded) populateForm()
})

// Vuetify v-color-picker may emit #RRGGBBAA; backend accepts only #RRGGBB, so normalize.
function normalizeHex(hex: string): string {
    if (!hex) return '#000000'
    const cleaned = hex.trim()
    if (/^#[0-9A-Fa-f]{6}$/.test(cleaned)) return cleaned.toUpperCase()
    if (/^#[0-9A-Fa-f]{8}$/.test(cleaned)) return cleaned.substring(0, 7).toUpperCase()
    return cleaned
}

async function saveMetadata() {
    try {
        saving.value = true
        await tenantService.updateBranding({
            primaryColor: normalizeHex(form.value.primaryColor),
            secondaryColor: normalizeHex(form.value.secondaryColor),
            accentColor: normalizeHex(form.value.accentColor),
            tagline: form.value.tagline && form.value.tagline.trim().length > 0 ? form.value.tagline : null,
            themeMode: form.value.themeMode,
        })
        await loadBranding()
        snackbarText.value = 'Branding saved.'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Failed to save branding.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        saving.value = false
    }
}

async function geocodeAddress() {
    const parts = [locationForm.value.addressLine, locationForm.value.city,
                   locationForm.value.region, locationForm.value.postalCode,
                   locationForm.value.country].filter(p => p && p.trim().length > 0)
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
        locationForm.value.latitude = Number(result.lat.toFixed(6))
        locationForm.value.longitude = Number(result.lng.toFixed(6))
        geocodeResult.value = result.displayName
    } finally {
        geocoding.value = false
    }
}

async function saveLocation() {
    try {
        savingLocation.value = true
        await tenantService.updateLocation({
            addressLine: normalizeString(locationForm.value.addressLine),
            city: normalizeString(locationForm.value.city),
            region: normalizeString(locationForm.value.region),
            postalCode: normalizeString(locationForm.value.postalCode),
            country: normalizeString(locationForm.value.country),
            latitude: locationForm.value.latitude,
            longitude: locationForm.value.longitude,
        })
        await loadBranding()
        snackbarText.value = 'Location saved.'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Failed to save location.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        savingLocation.value = false
    }
}

function normalizeString(s: string | null): string | null {
    if (s === null || s === undefined) return null
    const t = s.trim()
    return t.length === 0 ? null : t
}

async function onUploaded() {
    await loadBranding()
    snackbarText.value = 'Image updated.'
    snackbarColor.value = 'success'
    snackbar.value = true
}

async function onRemoved() {
    await loadBranding()
    snackbarText.value = 'Image removed.'
    snackbarColor.value = 'success'
    snackbar.value = true
}
</script>
