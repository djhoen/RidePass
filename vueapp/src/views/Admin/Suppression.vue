<template>
    <v-container>
        <div class="d-flex align-center mb-2 flex-wrap ga-3">
            <h1 class="text-h4">Suppression List</h1>
            <v-chip v-if="rows.length" size="small" color="grey" variant="tonal">{{ rows.length }}</v-chip>
            <v-spacer></v-spacer>
            <v-btn variant="text" @click="load">Refresh</v-btn>
            <v-btn color="primary" prepend-icon="mdi-email-off" @click="addDialog = true">Suppress address</v-btn>
        </div>

        <p class="text-body-2 text-medium-emphasis mb-6">
            Addresses here are skipped on every marketing send (campaigns, reward nudges, rider email blasts).
            Entries come from one-click unsubscribes, spam complaints, hard bounces, or a manual add below.
            Receipts and account emails are unaffected. Platform-wide hard bounces are enforced automatically
            and aren't shown here.
        </p>

        <v-text-field v-model="search" prepend-inner-icon="mdi-magnify" label="Search" density="compact"
            hide-details class="mb-3"></v-text-field>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Email</th>
                        <th style="width: 130px">Reason</th>
                        <th style="width: 120px">Scope</th>
                        <th style="width: 170px">Added</th>
                        <th style="width: 110px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="s in filtered" :key="s.id">
                        <td>{{ s.email }}</td>
                        <td><v-chip size="x-small" :color="reasonColor(s.reason)" variant="tonal">{{ s.reason }}</v-chip></td>
                        <td>
                            <v-chip size="x-small" variant="outlined">
                                {{ s.scope === 'all' ? 'all email' : 'marketing' }}
                            </v-chip>
                        </td>
                        <td>{{ formatDate(s.createdAtUtc) }}</td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" color="primary" @click="reEnable(s)">Re-enable</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && filtered.length === 0">
                        <td colspan="5" class="text-center text-medium-emphasis py-8">No suppressed addresses.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="addDialog" max-width="500">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Suppress an address</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="addDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-caption text-medium-emphasis mb-2">
                        Stops marketing email to this address for your track. They can still receive receipts and
                        account emails.
                    </p>
                    <v-text-field v-model="addForm.email" type="email" label="Email" density="compact"></v-text-field>
                    <v-text-field v-model="addForm.note" label="Note (optional)" density="compact" class="mt-4"></v-text-field>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="adding" @click="addDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="adding" @click="submitAdd">Suppress</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3500">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { SuppressionService, type SuppressionItem } from '@/services/SuppressionService'
import { useConfirm } from '@/composables/useConfirm'

const service = new SuppressionService()
const confirm = useConfirm()

const search = ref('')
const rows = ref<SuppressionItem[]>([])
const loading = ref(false)

const addDialog = ref(false)
const adding = ref(false)
const addForm = ref({ email: '', note: '' })

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(load)

async function load() {
    loading.value = true
    try {
        const r = await service.list()
        rows.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load suppression list.', 'error')
    } finally {
        loading.value = false
    }
}

const filtered = computed(() => {
    const q = search.value.trim().toLowerCase()
    if (!q) return rows.value
    return rows.value.filter(r => r.email.toLowerCase().includes(q))
})

function reasonColor(reason: string): string {
    switch (reason) {
        case 'bounce': return 'error'
        case 'complaint': return 'warning'
        case 'unsubscribe': return 'grey'
        default: return 'primary'
    }
}

async function submitAdd() {
    const email = addForm.value.email.trim()
    if (!email) return
    adding.value = true
    try {
        await service.add(email, addForm.value.note.trim() || null)
        addDialog.value = false
        addForm.value = { email: '', note: '' }
        flash('Address suppressed.', 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to suppress address.', 'error')
    } finally {
        adding.value = false
    }
}

async function reEnable(s: SuppressionItem) {
    if (!await confirm({
        title: 'Re-enable address?',
        message: `Allow marketing email to ${s.email} again? Only do this if you're sure they want to receive it.`,
        confirmText: 'Re-enable',
    })) return
    try {
        await service.remove(s.id)
        flash('Address re-enabled.', 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to re-enable.', 'error')
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
