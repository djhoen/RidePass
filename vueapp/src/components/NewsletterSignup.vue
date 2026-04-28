<template>
    <v-card variant="tonal" class="pa-4">
        <div class="text-subtitle-1 mb-1">{{ title }}</div>
        <div class="text-caption text-medium-emphasis mb-3">{{ subtitle }}</div>
        <v-row dense>
            <v-col cols="12" sm="5">
                <v-text-field v-model="email" type="email" label="Email" density="compact" hide-details></v-text-field>
            </v-col>
            <v-col cols="12" sm="4">
                <v-text-field v-model="name" label="Name (optional)" density="compact" hide-details></v-text-field>
            </v-col>
            <v-col cols="12" sm="3">
                <v-btn block color="primary" :loading="submitting" :disabled="subscribed"
                    @click="submit">{{ subscribed ? 'Subscribed ✓' : 'Subscribe' }}</v-btn>
            </v-col>
        </v-row>
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
