<template>
    <div>
        <p class="text-body-2 text-medium-emphasis mb-4">
            Terms customers sign at the counter. The rental agreement is required before gear can be
            checked out; the repair authorization is signed when a bike is dropped off.
        </p>

        <v-tabs v-model="kind" :height="40" class="mb-4 sub-tabs" hide-slider selected-class="sub-tab-active">
            <v-tab value="work_order_terms" class="sub-tab">Repair authorization</v-tab>
            <v-tab value="rental_agreement" class="sub-tab">Rental agreement</v-tab>
        </v-tabs>

        <div v-if="loading" class="text-center py-6">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <template v-else>
            <v-alert v-if="!current" type="info" variant="tonal" density="compact" class="mb-4">
                {{ kind === 'rental_agreement'
                    ? 'No rental agreement published yet. Until you publish one, rentals check out without it.'
                    : 'No repair authorization published yet.' }}
            </v-alert>
            <div v-else class="d-flex align-center ga-2 mb-3">
                <v-chip size="small" variant="tonal">Version {{ current.version }}</v-chip>
                <span class="text-caption text-medium-emphasis">Currently in use</span>
            </div>

            <v-text-field v-model="form.title" label="Title" density="compact" hide-details
                :disabled="saving"></v-text-field>
            <v-textarea v-model="form.body" label="Agreement text" rows="14" auto-grow
                density="compact" class="mt-4" hide-details :disabled="saving"></v-textarea>

            <!-- Publishing supersedes rather than edits: existing signatures have to keep
                 proving what that customer actually agreed to. -->
            <v-alert v-if="isDirty" type="warning" variant="tonal" density="compact" class="mt-4">
                Publishing saves this as a new version. Anyone who already signed the old terms
                will be asked to sign again at their next checkout.
            </v-alert>
            <div v-if="error" class="text-error text-caption mt-2">{{ error }}</div>

            <div class="d-flex ga-2 mt-4">
                <v-btn color="primary" :loading="saving" :disabled="!canPublish" @click="publish">
                    {{ current ? 'Publish new version' : 'Publish' }}
                </v-btn>
                <v-btn variant="text" :disabled="saving || !isDirty" @click="reset">Discard changes</v-btn>
            </div>
        </template>

        <v-snackbar v-model="snackbar" :color="snackColor" :timeout="3000">{{ snackText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { BikeShopService, type ShopAgreement } from '@/services/BikeShopService'

const service = new BikeShopService()

const kind = ref<'rental_agreement' | 'work_order_terms'>('work_order_terms')
const loading = ref(false)
const saving = ref(false)
const error = ref('')
const current = ref<ShopAgreement | null>(null)
const form = ref({ title: '', body: '' })

const snackbar = ref(false)
const snackText = ref('')
const snackColor = ref<'success' | 'error'>('success')
function flash(t: string, c: 'success' | 'error' = 'success') {
    snackText.value = t; snackColor.value = c; snackbar.value = true
}

const defaultTitle = computed(() =>
    kind.value === 'rental_agreement' ? 'Rental Agreement' : 'Repair Authorization')

const isDirty = computed(() =>
    form.value.title !== (current.value?.title ?? defaultTitle.value)
    || form.value.body !== (current.value?.body ?? ''))

const canPublish = computed(() =>
    !!form.value.title.trim() && !!form.value.body.trim() && isDirty.value)

async function load() {
    loading.value = true
    error.value = ''
    try {
        const r = await service.getAgreement(kind.value)
        current.value = (r.data as any).data ?? null
        reset()
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not load the agreement. Refresh to try again.'
    } finally {
        loading.value = false
    }
}

function reset() {
    form.value = {
        title: current.value?.title ?? defaultTitle.value,
        body: current.value?.body ?? '',
    }
}

async function publish() {
    if (!canPublish.value) return
    saving.value = true
    error.value = ''
    try {
        await service.publishAgreement(kind.value, {
            title: form.value.title.trim(),
            body: form.value.body.trim(),
        })
        await load()
        flash('Published. Customers will sign these terms from now on.')
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not publish. Try again.'
    } finally {
        saving.value = false
    }
}

watch(kind, load, { immediate: true })
</script>
