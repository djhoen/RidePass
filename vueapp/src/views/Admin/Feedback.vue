<template>
    <v-container>
        <div class="d-flex align-center mb-4 flex-wrap ga-3">
            <h1 class="text-h4">Feedback</h1>
            <v-spacer></v-spacer>
            <v-btn-toggle v-model="statusFilter" color="primary" density="compact" mandatory variant="outlined">
                <v-btn value="new">New</v-btn>
                <v-btn value="addressed">Addressed</v-btn>
                <v-btn value="dismissed">Dismissed</v-btn>
                <v-btn :value="null">All</v-btn>
            </v-btn-toggle>
            <v-btn variant="text" @click="load">Refresh</v-btn>
        </div>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Submitted</th>
                        <th>Name</th>
                        <th>Email</th>
                        <th style="width: 80px">Rating</th>
                        <th>Body</th>
                        <th style="width: 110px">Status</th>
                        <th style="width: 80px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="row in items" :key="row.id">
                        <td class="text-no-wrap">{{ formatRelative(row.createdAtUtc) }}</td>
                        <td><strong>{{ row.name }}</strong></td>
                        <td>{{ row.email }}</td>
                        <td>
                            <span v-if="row.rating">
                                <v-icon size="small" color="amber-darken-2">mdi-star</v-icon>
                                {{ row.rating }}
                            </span>
                            <span v-else class="text-medium-emphasis">—</span>
                        </td>
                        <td style="max-width: 360px; white-space: pre-wrap">{{ row.body }}</td>
                        <td>
                            <v-chip size="small" :color="statusColor(row.status)">{{ row.status }}</v-chip>
                        </td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" @click="openEdit(row)">Open</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && items.length === 0">
                        <td colspan="7" class="text-center text-medium-emphasis py-8">
                            <span v-if="statusFilter">No feedback in "{{ statusFilter }}" status.</span>
                            <span v-else>No feedback yet.</span>
                        </td>
                    </tr>
                </tbody>
            </v-table>

            <v-divider v-if="total > limit"></v-divider>
            <div v-if="total > limit" class="d-flex align-center pa-3 ga-2">
                <span class="text-caption text-medium-emphasis">
                    {{ offset + 1 }}–{{ Math.min(offset + limit, total) }} of {{ total }}
                </span>
                <v-spacer></v-spacer>
                <v-btn size="small" variant="text" :disabled="offset === 0" @click="prevPage">Prev</v-btn>
                <v-btn size="small" variant="text" :disabled="offset + limit >= total" @click="nextPage">Next</v-btn>
            </div>
        </v-card>

        <v-dialog v-model="editOpen" max-width="640" scrollable>
            <v-card v-if="editing">
                <v-card-title class="d-flex align-center">
                    <span>Feedback from {{ editing.name }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="editOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div class="text-caption text-medium-emphasis mb-2">
                        {{ editing.email }} · {{ formatLong(editing.createdAtUtc) }}
                        <span v-if="editing.rating"> · ⭐ {{ editing.rating }}/5</span>
                    </div>
                    <v-card variant="outlined" class="pa-3 mb-3" style="white-space: pre-wrap">
                        {{ editing.body }}
                    </v-card>
                    <v-textarea v-model="adminNotes" label="Admin notes (private)"
                        rows="3" auto-grow maxlength="2000" counter></v-textarea>

                    <div class="text-subtitle-2 mt-3 mb-1">Status</div>
                    <v-btn-toggle v-model="editStatus" color="primary" density="compact" mandatory variant="outlined">
                        <v-btn value="new">New</v-btn>
                        <v-btn value="addressed">Addressed</v-btn>
                        <v-btn value="dismissed">Dismissed</v-btn>
                    </v-btn-toggle>

                    <div v-if="editing.actionedAtUtc" class="text-caption text-medium-emphasis mt-3">
                        Last actioned {{ formatLong(editing.actionedAtUtc) }}
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="editOpen = false">Close</v-btn>
                    <v-btn color="primary" :loading="saving" @click="save">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import dayjs from 'dayjs'
import { FeedbackService, type FeedbackDto } from '@/services/FeedbackService'
import { branding } from '@/stores/branding'

const service = new FeedbackService()

// Default filter is "new" so admins land on what needs attention.
const statusFilter = ref<'new' | 'addressed' | 'dismissed' | null>('new')
const items = ref<FeedbackDto[]>([])
const total = ref(0)
const loading = ref(false)

const limit = 25
const offset = ref(0)

const editOpen = ref(false)
const editing = ref<FeedbackDto | null>(null)
const editStatus = ref<'new' | 'addressed' | 'dismissed'>('new')
const adminNotes = ref('')
const saving = ref(false)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(load)
watch(statusFilter, () => { offset.value = 0; load() })

async function load() {
    loading.value = true
    try {
        const r = await service.listAdmin({
            status: statusFilter.value,
            limit,
            offset: offset.value,
        })
        const data = (r.data as any).data
        items.value = data.items
        total.value = data.total
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load feedback.', 'error')
    } finally {
        loading.value = false
    }
}

function nextPage() { offset.value += limit; load() }
function prevPage() { offset.value = Math.max(0, offset.value - limit); load() }

function openEdit(row: FeedbackDto) {
    editing.value = row
    editStatus.value = row.status
    adminNotes.value = row.adminNotes ?? ''
    editOpen.value = true
}

async function save() {
    if (!editing.value) return
    saving.value = true
    try {
        await service.updateStatus(editing.value.id, editStatus.value, adminNotes.value.trim() || null)
        await load()
        editOpen.value = false
        flash('Saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

function formatLong(utc: string): string {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('MMM D, YYYY [at] h:mm A')
}
function formatRelative(utc: string): string {
    const d = dayjs.utc(utc)
    const diffH = dayjs().diff(d, 'hour')
    if (diffH < 24) return d.tz(branding.timezone || 'UTC').format('h:mm A')
    return d.tz(branding.timezone || 'UTC').format('MMM D')
}
function statusColor(s: string): string {
    switch (s) {
        case 'new': return 'warning'
        case 'addressed': return 'success'
        case 'dismissed': return 'grey'
        default: return 'default'
    }
}
function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
