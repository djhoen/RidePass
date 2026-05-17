<template>
    <v-container>
        <h1 class="text-h4 mb-6">Branding</h1>

        <v-row>
            <v-col cols="12" md="6">
                <v-card class="mb-6 pa-4">
                    <v-card-title>Colors & Theme</v-card-title>
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
                        <v-btn color="primary" class="mt-4" :loading="saving" @click="save">Save</v-btn>
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
                        <p class="text-caption text-medium-emphasis mt-4">
                            Home Hero and Secondary Hero are managed from
                            <router-link to="/Admin/Settings/HomePage">Settings → Home Page</router-link>.
                        </p>
                    </v-card-text>
                </v-card>
            </v-col>
        </v-row>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import { TenantService } from '@/services/TenantService'
import { branding, loadBranding } from '@/stores/branding'
import BrandingImageSlot from '@/components/BrandingImageSlot.vue'

const tenantService = new TenantService()

const form = ref({
    primaryColor: '#1976D2',
    secondaryColor: '#424242',
    accentColor: '#82B1FF',
    tagline: '' as string | null,
    themeMode: 'light' as 'light' | 'dark',
})

const saving = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

function populateForm() {
    form.value.primaryColor = branding.primaryColor
    form.value.secondaryColor = branding.secondaryColor
    form.value.accentColor = branding.accentColor
    form.value.tagline = branding.tagline ?? ''
    form.value.themeMode = branding.themeMode
}

// Vuetify v-color-picker may emit #RRGGBBAA; backend accepts only #RRGGBB, so normalize.
function normalizeHex(hex: string): string {
    if (!hex) return '#000000'
    const cleaned = hex.trim()
    if (/^#[0-9A-Fa-f]{6}$/.test(cleaned)) return cleaned.toUpperCase()
    if (/^#[0-9A-Fa-f]{8}$/.test(cleaned)) return cleaned.substring(0, 7).toUpperCase()
    return cleaned
}

async function save() {
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

onMounted(async () => {
    if (!branding.loaded) await loadBranding()
    populateForm()
})

watch(() => branding.loaded, (loaded) => {
    if (loaded) populateForm()
})
</script>
