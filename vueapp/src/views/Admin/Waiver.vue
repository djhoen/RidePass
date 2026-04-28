<template>
    <v-container>
        <h1 class="text-h4 mb-6">Waiver</h1>

        <v-card class="mb-4 pa-4">
            <v-card-title>Current Version: v{{ form.version }}</v-card-title>
            <v-card-text>
                <p class="text-body-2 text-medium-emphasis mb-4">
                    Saving publishes a new version. All riders will need to re-sign before their next purchase.
                </p>
                <v-text-field v-model="form.title" label="Title" density="compact" class="mb-3"></v-text-field>
                <label class="text-subtitle-2 d-block mb-1">Body</label>
                <RichTextEditor v-model="form.body" />
                <v-btn color="primary" class="mt-4" :loading="saving" @click="save">Publish New Version</v-btn>
            </v-card-text>
        </v-card>

        <v-card class="pa-4" variant="outlined">
            <v-card-title>Preview</v-card-title>
            <v-card-text>
                <h3 class="text-h6 mb-3">{{ form.title }}</h3>
                <RichTextView :html="form.body" />
            </v-card-text>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { DayPassService } from '@/services/DayPassService'
import RichTextEditor from '@/components/RichTextEditor.vue'
import RichTextView from '@/components/RichTextView.vue'

const service = new DayPassService()

const form = ref({ version: 0, title: '', body: '' })
const saving = ref(false)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(load)

async function load() {
    try {
        const r = await service.getWaiver()
        const w = (r.data as any).data
        form.value.version = w.version
        form.value.title = w.title
        form.value.body = w.body
    } catch (err) {
        console.error('Failed to load waiver', err)
    }
}

async function save() {
    if (!confirm('Publish a new waiver version? Riders will need to re-sign.')) return
    try {
        saving.value = true
        await service.publishWaiver({ title: form.value.title.trim(), body: form.value.body })
        await load()
        flash('New waiver version published.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
