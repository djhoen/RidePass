<template>
    <v-container>
        <div class="d-flex align-center mb-6 flex-wrap ga-3">
            <h1 class="text-h4">Newsletter Subscribers</h1>
            <v-chip v-if="activeCount !== null" size="small" color="success" variant="tonal">
                {{ activeCount }} active
            </v-chip>
            <v-spacer></v-spacer>
            <v-btn variant="text" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-account-plus" @click="addDialog = true">Add</v-btn>
            <v-btn variant="tonal" prepend-icon="mdi-upload" @click="importDialog = true">Import</v-btn>
        </div>

        <v-tabs v-model="tab" density="compact" class="mb-3">
            <v-tab value="active">Active</v-tab>
            <v-tab value="all">All (incl. unsubscribed)</v-tab>
        </v-tabs>

        <v-text-field v-model="search" prepend-inner-icon="mdi-magnify" label="Search" density="compact"
            hide-details class="mb-3"></v-text-field>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Email</th>
                        <th>Name</th>
                        <th style="width: 110px">Source</th>
                        <th style="width: 160px">Subscribed</th>
                        <th style="width: 160px">Unsubscribed</th>
                        <th style="width: 100px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="s in filtered" :key="s.id">
                        <td>{{ s.email }}</td>
                        <td>{{ s.name || '—' }}</td>
                        <td><v-chip size="x-small">{{ s.source }}</v-chip></td>
                        <td>{{ formatDate(s.subscribedAtUtc) }}</td>
                        <td>
                            <span v-if="s.unsubscribedAtUtc" class="text-grey">{{ formatDate(s.unsubscribedAtUtc) }}</span>
                            <span v-else class="text-success">—</span>
                        </td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" color="error" @click="deleteSubscriber(s)">Delete</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && filtered.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-8">No subscribers match.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="addDialog" max-width="500">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Add subscriber</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="addDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="addForm.email" type="email" label="Email" density="compact"></v-text-field>
                    <v-text-field v-model="addForm.name" label="Name (optional)" density="compact" class="mt-4"></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="adding" @click="addDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="adding" @click="submitAdd">Add</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="importDialog" max-width="640">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Import subscribers</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="importDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-caption text-medium-emphasis mb-2">
                        One per line. Email first, optional name after a comma. Example:
                        <br><code>alice@example.com, Alice Smith</code>
                    </p>
                    <v-textarea v-model="importRaw" label="Emails" rows="10" density="compact"></v-textarea>
                    <v-checkbox v-model="importConsent" density="compact" class="mt-2"
                        label="I confirm these recipients opted in to receive email from this track."></v-checkbox>
                    <p class="text-caption text-medium-emphasis">
                        Addresses that previously bounced, complained, or unsubscribed are skipped automatically.
                    </p>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="importing" @click="importDialog = false">Close</v-btn>
                    <v-btn color="primary" :loading="importing" :disabled="!importConsent" @click="submitImport">Import</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3500">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import dayjs from 'dayjs'
import { NewsletterService, type SubscriberListItem } from '@/services/NewsletterService'

const service = new NewsletterService()

const tab = ref<'active' | 'all'>('active')
const search = ref('')
const rows = ref<SubscriberListItem[]>([])
const activeCount = ref<number | null>(null)
const loading = ref(false)

const addDialog = ref(false)
const adding = ref(false)
const addForm = ref({ email: '', name: '' })

const importDialog = ref(false)
const importing = ref(false)
const importRaw = ref('')
const importConsent = ref(false)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(load)
watch(tab, load)

async function load() {
    loading.value = true
    try {
        const [list, count] = await Promise.all([
            service.listSubscribers(tab.value === 'all'),
            service.getActiveCount(),
        ])
        rows.value = (list.data as any).data
        activeCount.value = (count.data as any).data.count
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load subscribers.', 'error')
    } finally {
        loading.value = false
    }
}

const filtered = computed(() => {
    const q = search.value.trim().toLowerCase()
    if (!q) return rows.value
    return rows.value.filter(r =>
        r.email.toLowerCase().includes(q) || (r.name ?? '').toLowerCase().includes(q))
})

async function submitAdd() {
    const email = addForm.value.email.trim()
    if (!email) return
    adding.value = true
    try {
        await service.addSubscriber(email, addForm.value.name.trim() || null)
        addDialog.value = false
        addForm.value = { email: '', name: '' }
        flash('Subscriber added.', 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to add subscriber.', 'error')
    } finally {
        adding.value = false
    }
}

async function submitImport() {
    if (!importRaw.value.trim() || !importConsent.value) return
    importing.value = true
    try {
        const r = await service.importSubscribers(importRaw.value, importConsent.value)
        const data = (r.data as any).data
        flash(`Imported: ${data.added} new, ${data.skipped} skipped, ${data.suppressed} suppressed.`, 'success')
        importRaw.value = ''
        importConsent.value = false
        importDialog.value = false
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Import failed.', 'error')
    } finally {
        importing.value = false
    }
}

async function deleteSubscriber(s: SubscriberListItem) {
    if (!confirm(`Delete ${s.email}? This removes them from the list entirely — for CAN-SPAM compliance consider unsubscribing instead.`)) return
    try {
        await service.deleteSubscriber(s.id)
        flash('Subscriber deleted.', 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    }
}

function formatDate(utc: string): string {
    return dayjs.utc(utc).local().format('YYYY-MM-DD HH:mm')
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
