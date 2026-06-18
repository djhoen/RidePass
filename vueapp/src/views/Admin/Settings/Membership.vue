<template>
    <v-container>
        <h1 class="text-h4 mb-2">Membership</h1>
        <p class="text-body-2 text-medium-emphasis mb-6">
            Configure the membership product riders can buy and where it's required.
            Turn the feature on or off in
            <router-link to="/Admin/Settings/Features">Settings → Features</router-link>.
        </p>

        <v-alert v-if="!branding.membershipEnabled" type="info" variant="tonal" class="mb-4">
            Memberships are turned off — the settings below won't apply until you enable the feature.
        </v-alert>

        <v-card class="pa-4 mb-4">
            <v-card-text>
                <v-text-field v-model="form.name" density="compact"
                    label="Membership name" :hide-details="false"
                    hint="Shown to riders. e.g. 'Track Membership', 'BMX Club Card'."
                    persistent-hint maxlength="120"></v-text-field>

                <v-row class="mt-2">
                    <v-col cols="12" sm="6">
                        <v-text-field v-model.number="form.priceDollars" type="number" min="0" step="1"
                            density="compact" prefix="$"
                            label="Price" :hide-details="false"
                            hint="Whole dollars. Cents go through Stripe like any other charge."
                            persistent-hint></v-text-field>
                    </v-col>
                    <v-col cols="12" sm="6">
                        <v-select v-model="form.durationKind"
                            :items="[
                                { value: 'yearly', title: 'Yearly (365 days)' },
                                { value: 'one_time', title: 'One-time (lifetime)' },
                            ]"
                            density="compact"
                            label="Duration" :hide-details="false"
                            hint="Existing memberships keep their original duration when this changes."
                            persistent-hint></v-select>
                    </v-col>
                </v-row>

                <p class="text-caption text-medium-emphasis mt-6 mb-2">
                    Memberships aren't required to buy entry. If you need riders to be "members"
                    for liability, fold a membership into a (waiver-backed) gate fee — name a
                    required gate fee something like "Gate Fee &amp; Membership" and require the
                    waiver on the event.
                </p>

                <v-btn color="primary" class="mt-2" :loading="saving" :disabled="!canSave" @click="save">
                    Save
                </v-btn>
            </v-card-text>
        </v-card>

        <v-card class="pa-4">
            <v-card-title>Preview</v-card-title>
            <v-card-text>
                <p class="text-body-2 text-medium-emphasis mb-2">
                    Riders will see this on the <code>/Membership</code> page:
                </p>
                <v-card variant="tonal" class="pa-3">
                    <div class="d-flex align-center">
                        <div>
                            <div class="text-h6">{{ form.name || '—' }}</div>
                            <div class="text-caption text-medium-emphasis">
                                {{ form.durationKind === 'yearly' ? 'Yearly · valid 365 days' : 'One-time · valid forever' }}
                            </div>
                        </div>
                        <v-spacer></v-spacer>
                        <div class="text-h5">${{ Number(form.priceDollars || 0).toFixed(2) }}</div>
                    </div>
                </v-card>
            </v-card-text>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { MembershipService } from '@/services/MembershipService'
import { branding, loadBranding } from '@/stores/branding'

const service = new MembershipService()

const form = ref({
    name: 'Track Membership',
    priceDollars: 0,
    durationKind: 'yearly' as 'one_time' | 'yearly',
    requiredForRiders: true,
    requiredForSpectators: false,
})

const saving = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const canSave = computed(() => {
    if (!form.value.name.trim()) return false
    if (form.value.priceDollars < 0) return false
    return true
})

function populateForm() {
    form.value.name = branding.membershipName
    form.value.priceDollars = Math.round(branding.membershipPriceCents / 100)
    form.value.durationKind = branding.membershipDurationKind
    form.value.requiredForRiders = branding.membershipRequiredForRiders
    form.value.requiredForSpectators = branding.membershipRequiredForSpectators
}

async function save() {
    if (!canSave.value) return
    saving.value = true
    try {
        // Preserve the current enabled state — Features owns that toggle now.
        await service.updateSettings({
            enabled: branding.membershipEnabled,
            name: form.value.name.trim(),
            priceCents: Math.round(form.value.priceDollars * 100),
            durationKind: form.value.durationKind,
            requiredForRiders: form.value.requiredForRiders,
            requiredForSpectators: form.value.requiredForSpectators,
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

watch(() => branding.loaded, (loaded) => { if (loaded) populateForm() })
</script>
