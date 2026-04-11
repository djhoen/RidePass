<template>
    <v-container>
        <h1 class="text-h4 mb-6">FAQ Management</h1>

        <Spinner v-model="loading" />

        <v-card class="mb-4">
            <v-card-title>{{ editingFaq ? 'Edit FAQ' : 'Add New FAQ' }}</v-card-title>
            <v-card-text>
                <v-form @submit.prevent="saveFaq">
                    <v-text-field v-model="form.question" label="Question" required class="mb-2"></v-text-field>
                    <v-textarea v-model="form.answer" label="Answer" rows="3" required class="mb-2"></v-textarea>
                    <v-btn type="submit" color="primary" :loading="saving">
                        {{ editingFaq ? 'Update' : 'Add' }}
                    </v-btn>
                    <v-btn v-if="editingFaq" variant="text" class="ml-2" @click="cancelEdit">Cancel</v-btn>
                </v-form>
            </v-card-text>
        </v-card>

        <v-card v-if="!loading">
            <v-table>
                <thead>
                    <tr>
                        <th>Question</th>
                        <th>Answer</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="faq in faqs" :key="faq.id">
                        <td>{{ faq.question }}</td>
                        <td>{{ filters.truncate(faq.answer, 80) }}</td>
                        <td class="text-right" style="white-space: nowrap;">
                            <v-btn variant="text" size="small" @click="editFaq(faq)">Edit</v-btn>
                            <v-btn variant="text" size="small" color="error"
                                @click="deleteFaq(faq.id)">Delete</v-btn>
                        </td>
                    </tr>
                </tbody>
            </v-table>
            <v-card-text v-if="faqs.length === 0">No FAQs found.</v-card-text>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { FaqService } from '@/services/FaqService'
import filters from '@/helpers/Filters'
import Spinner from '@/components/Spinner.vue'

const faqService = new FaqService()

const faqs = ref<any[]>([])
const form = ref({ question: '', answer: '' } as any)
const editingFaq = ref(false)
const loading = ref(false)
const saving = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('success')

onMounted(async () => {
    await loadFaqs()
})

async function loadFaqs() {
    try {
        loading.value = true
        const response = await faqService.getFaqs()
        faqs.value = response.data
    } catch {
        snackbarText.value = 'Failed to load FAQs.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        loading.value = false
    }
}

function editFaq(faq: any) {
    form.value = { ...faq }
    editingFaq.value = true
}

function cancelEdit() {
    form.value = { question: '', answer: '' }
    editingFaq.value = false
}

async function saveFaq() {
    try {
        saving.value = true
        if (editingFaq.value) {
            await faqService.updateFaq(form.value)
        } else {
            await faqService.createFaq(form.value)
        }
        form.value = { question: '', answer: '' }
        editingFaq.value = false
        await loadFaqs()
        snackbarText.value = 'FAQ saved!'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch {
        snackbarText.value = 'Failed to save FAQ.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        saving.value = false
    }
}

async function deleteFaq(id: number) {
    try {
        await faqService.deleteFaq(id)
        await loadFaqs()
        snackbarText.value = 'FAQ deleted.'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch {
        snackbarText.value = 'Failed to delete FAQ.'
        snackbarColor.value = 'error'
        snackbar.value = true
    }
}
</script>
