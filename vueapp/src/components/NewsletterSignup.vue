<template>
    <v-card variant="tonal" class="pa-4">
        <div class="text-subtitle-1 font-weight-medium mb-1">{{ title }}</div>
        <div class="text-caption newsletter-subtitle mb-3">{{ subtitle }}</div>
        <v-row dense>
            <v-col cols="12" sm="6">
                <v-text-field v-model="email" type="email" label="Email" density="compact" hide-details></v-text-field>
            </v-col>
            <v-col cols="12" sm="6">
                <v-text-field v-model="name" label="Name (optional)" density="compact" hide-details></v-text-field>
            </v-col>
        </v-row>
        <v-btn block color="primary" class="mt-3" :loading="submitting" :disabled="subscribed"
            @click="submit">{{ subscribed ? 'Subscribed ✓' : 'Subscribe' }}</v-btn>
        <div v-if="error" class="text-caption text-error mt-2">{{ error }}</div>
    </v-card>
</template>


<script setup lang="ts">
import { ref } from 'vue'
import { NewsletterService } from '@/services/NewsletterService'

defineProps<{
    title?: string
    subtitle?: string
}>()

const service = new NewsletterService()
const email = ref('')
const name = ref('')
const submitting = ref(false)
const subscribed = ref(false)
const error = ref('')

async function submit() {
    if (!email.value.trim() || !email.value.includes('@')) {
        error.value = 'Enter a valid email.'
        return
    }
    error.value = ''
    submitting.value = true
    try {
        await service.subscribe(email.value.trim(), name.value.trim() || null)
        subscribed.value = true
    } catch (err: any) {
        error.value = err.response?.data?.error || 'Could not subscribe. Try again later.'
    } finally {
        submitting.value = false
    }
}
</script>

<style scoped>
/* The signup lives in the dark footer, so keep the subtitle legible there
   instead of the default low-opacity dark medium-emphasis color. */
.newsletter-subtitle {
    color: rgba(255, 255, 255, 0.82);
}
</style>
