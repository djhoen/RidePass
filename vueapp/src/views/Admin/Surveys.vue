<template>
    <v-container>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h1 class="text-h4">Surveys</h1>
            <v-spacer></v-spacer>
            <v-btn variant="text" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">New Survey</v-btn>
        </div>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Name</th>
                        <th style="width: 110px">Status</th>
                        <th style="width: 110px">Questions</th>
                        <th style="width: 110px">Responses</th>
                        <th style="width: 160px">Closes</th>
                        <th style="width: 320px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="s in items" :key="s.id">
                        <td>
                            <strong>{{ s.name }}</strong>
                            <div class="text-caption text-medium-emphasis">{{ s.title }}</div>
                        </td>
                        <td><v-chip size="small" :color="statusColor(s.status)">{{ s.status }}</v-chip></td>
                        <td>{{ s.questionCount }}</td>
                        <td>{{ s.responseCount }}</td>
                        <td>{{ s.closesAtUtc ? formatDate(s.closesAtUtc) : '—' }}</td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" @click="goEdit(s.id)">Edit</v-btn>
                            <v-btn variant="text" size="small" @click="goResults(s.id)">Results</v-btn>
                            <v-btn v-if="s.status === 'published'" variant="text" size="small" color="primary"
                                @click="copyShareLink(s)">
                                Share link
                            </v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && items.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-8">
                            No surveys yet. Create one to get started.
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="createOpen" max-width="640" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>New Survey</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="createOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.name" label="Internal name" hint="Only admins see this" persistent-hint
                        density="compact"></v-text-field>
                    <v-text-field v-model="form.title" label="Title" hint="Shown to respondents" persistent-hint class="mt-6"
                        density="compact"></v-text-field>
                    <v-textarea v-model="form.description" label="Description (optional)" rows="2" auto-grow class="mt-6"
                        density="compact"></v-textarea>
                    <v-text-field v-model="form.closesAtUtc" type="datetime-local" class="mt-6"
                        label="Closes (optional)" density="compact"></v-text-field>
                    <v-checkbox v-model="form.requireEmail" label="Require respondent email + name" class="mt-4"
                        density="compact"></v-checkbox>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="saving" @click="createOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="create">Create</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { SurveyService, type SurveyListItem } from '@/services/SurveyService'
import { branding } from '@/stores/branding'

const router = useRouter()
const service = new SurveyService()

const items = ref<SurveyListItem[]>([])
const loading = ref(false)
const createOpen = ref(false)
const saving = ref(false)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const form = ref({
    name: '',
    title: '',
    description: '',
    closesAtUtc: '',
    requireEmail: false,
})

onMounted(load)

async function load() {
    loading.value = true
    try {
        const r = await service.listAdmin()
        items.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load surveys.', 'error')
    } finally {
        loading.value = false
    }
}

function openCreate() {
    form.value = { name: '', title: '', description: '', closesAtUtc: '', requireEmail: false }
    createOpen.value = true
}

async function create() {
    if (!form.value.name.trim() || !form.value.title.trim()) {
        flash('Name and title are required.', 'error')
        return
    }
    saving.value = true
    try {
        const r = await service.create({
            name: form.value.name.trim(),
            title: form.value.title.trim(),
            description: form.value.description.trim() || null,
            closesAtUtc: form.value.closesAtUtc
                ? dayjs(form.value.closesAtUtc).utc().toISOString()
                : null,
            requireEmail: form.value.requireEmail,
        })
        const created = (r.data as any).data
        createOpen.value = false
        router.push(`/Admin/Surveys/${created.id}`)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Create failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function copyShareLink(s: SurveyListItem) {
    const origin = window.location.origin
    const url = `${origin}/Survey/${s.publicToken}`
    try {
        await navigator.clipboard.writeText(url)
        flash('Share link copied.', 'success')
    } catch {
        prompt('Share link:', url)
    }
}

function goEdit(id: string) { router.push(`/Admin/Surveys/${id}`) }
function goResults(id: string) { router.push(`/Admin/Surveys/${id}/Results`) }

function formatDate(utc: string) {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('MMM D, YYYY h:mm A')
}
function statusColor(s: string) {
    return s === 'published' ? 'success' : s === 'closed' ? 'grey' : 'warning'
}
function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
