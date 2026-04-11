<template>
    <v-container>
        <h1 class="text-h4 mb-6">Frequently Asked Questions</h1>

        <Spinner v-model="loading" />

        <v-expansion-panels v-if="!loading" variant="accordion">
            <v-expansion-panel v-for="faq in faqs" :key="faq.id">
                <v-expansion-panel-title>{{ faq.question }}</v-expansion-panel-title>
                <v-expansion-panel-text>{{ faq.answer }}</v-expansion-panel-text>
            </v-expansion-panel>
        </v-expansion-panels>

        <v-alert v-if="!loading && faqs.length === 0" type="info" variant="tonal">
            No FAQs available.
        </v-alert>

        <v-snackbar v-model="snackbar" color="error" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { FaqService } from '@/services/FaqService'
import Spinner from '@/components/Spinner.vue'

const faqService = new FaqService()

const faqs = ref<any[]>([])
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')

onMounted(async () => {
    try {
        loading.value = true
        const response = await faqService.getFaqs()
        faqs.value = response.data
    } catch {
        snackbarText.value = 'Failed to load FAQs.'
        snackbar.value = true
    } finally {
        loading.value = false
    }
})
</script>
