<template>
    <v-container style="max-width: 640px">
        <h1 class="text-h4 mb-2">Send {{ branding.displayName }} Feedback</h1>
        <p class="text-body-2 text-medium-emphasis mb-4">
            Got something to share? We read every message. Optional star rating helps us
            see at a glance how things are going.
        </p>

        <v-card v-if="!submitted" class="pa-4">
            <v-row>
                <v-col cols="12" sm="6">
                    <v-text-field v-model="form.name" label="Your name" density="compact" maxlength="120"></v-text-field>
                </v-col>
                <v-col cols="12" sm="6">
                    <v-text-field v-model="form.email" type="email" label="Email" density="compact" maxlength="200"></v-text-field>
                </v-col>
            </v-row>

            <div class="text-subtitle-2 mb-1">Rating (optional)</div>
            <div class="d-flex align-center ga-2 mb-3">
                <v-btn v-for="n in 5" :key="n" size="small" icon variant="text"
                    @click="form.rating = (form.rating === n ? null : n)">
                    <v-icon :color="(form.rating ?? 0) >= n ? 'amber-darken-2' : 'grey-lighten-1'">
                        {{ (form.rating ?? 0) >= n ? 'mdi-star' : 'mdi-star-outline' }}
                    </v-icon>
                </v-btn>
                <span v-if="form.rating" class="text-caption text-medium-emphasis">
                    {{ form.rating }} / 5
                </span>
            </div>

            <v-textarea v-model="form.body" label="Your feedback"
                rows="6" auto-grow counter maxlength="4000"
                hint="Tell us what's working, what isn't, or anything else on your mind."
                persistent-hint></v-textarea>

            <div v-if="errorMessage" class="text-error text-caption mt-2">{{ errorMessage }}</div>

            <div class="d-flex mt-4">
                <v-spacer></v-spacer>
                <v-btn color="primary" :loading="submitting" :disabled="!canSubmit" @click="submit">
                    Send Feedback
                </v-btn>
            </div>
        </v-card>

        <v-card v-else class="pa-6 text-center">
            <v-icon size="48" color="success" class="mb-2">mdi-check-circle</v-icon>
            <h2 class="text-h5 mb-2">Thanks for the feedback!</h2>
            <p class="text-body-2 text-medium-emphasis mb-4">
                We've passed your message along. If we need to follow up, we'll reach you at {{ form.email }}.
            </p>
            <v-btn variant="text" @click="resetForm">Send another</v-btn>
        </v-card>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { branding } from '@/stores/branding'
import { FeedbackService } from '@/services/FeedbackService'
import { UserService } from '@/services/UserService'
import authHelper from '@/helpers/AuthHelper'

const feedbackService = new FeedbackService()
const userService = new UserService()

const form = ref({
    name: '',
    email: '',
    rating: null as number | null,
    body: '',
})
const submitting = ref(false)
const submitted = ref(false)
const errorMessage = ref<string | null>(null)

const canSubmit = computed(() =>
    form.value.name.trim().length > 0
    && /\S+@\S+/.test(form.value.email)
    && form.value.body.trim().length > 0)

onMounted(async () => {
    // Pre-fill name + email when the visitor is signed in.
    if (authHelper.isAuthenticated()) {
        try {
            const r = await userService.getProfile()
            const data = ((r.data as any).data ?? r.data) as any
            form.value.name = `${data?.firstName ?? ''} ${data?.lastName ?? ''}`.trim()
            form.value.email = data?.email ?? ''
        } catch { /* leave blank */ }
    }
})

async function submit() {
    if (!canSubmit.value) return
    submitting.value = true
    errorMessage.value = null
    try {
        await feedbackService.submit({
            name: form.value.name.trim(),
            email: form.value.email.trim(),
            rating: form.value.rating,
            body: form.value.body.trim(),
        })
        submitted.value = true
    } catch (err: any) {
        errorMessage.value = err.response?.data?.error || 'Could not send your feedback.'
    } finally {
        submitting.value = false
    }
}

function resetForm() {
    form.value = { name: form.value.name, email: form.value.email, rating: null, body: '' }
    submitted.value = false
}
</script>
