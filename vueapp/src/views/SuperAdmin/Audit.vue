<template>
    <v-container>
        <h1 class="text-h4 mb-4">Audit log</h1>

        <div class="d-flex align-center mb-3 ga-2">
            <v-text-field v-model="auditFilterAction" label="Action filter" placeholder="e.g. payout.create"
                density="compact" hide-details clearable style="max-width: 280px"></v-text-field>
            <v-btn @click="loadAuditLog">Apply</v-btn>
            <v-spacer></v-spacer>
            <v-btn variant="text" @click="loadAuditLog">Refresh</v-btn>
        </div>

        <v-card>
            <v-table density="compact">
                <thead>
                    <tr>
                        <th style="width: 180px">When (UTC)</th>
                        <th style="width: 180px">Actor</th>
                        <th style="width: 200px">Action</th>
                        <th style="width: 140px">Target</th>
                        <th>Summary</th>
                        <th style="width: 130px">IP</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="a in auditLog" :key="a.id">
                        <td>{{ formatDate(a.createdAt) }}</td>
                        <td>
                            <div>{{ a.actorEmail || '—' }}</div>
                            <div class="text-caption text-medium-emphasis">{{ a.actorRole }}</div>
                        </td>
                        <td><code>{{ a.action }}</code></td>
                        <td>
                            <span v-if="a.targetKind">{{ a.targetKind }}</span>
                            <span v-else class="text-medium-emphasis">—</span>
                        </td>
                        <td>{{ a.summary }}</td>
                        <td>{{ a.ipAddress || '—' }}</td>
                    </tr>
                    <tr v-if="!loadingAudit && auditLog.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-8">No audit entries yet.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { SuperAdminService, type AuditLogEntry } from '@/services/SuperAdminService'

const service = new SuperAdminService()

const auditLog = ref<AuditLogEntry[]>([])
const loadingAudit = ref(false)
const auditFilterAction = ref('')

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(loadAuditLog)

async function loadAuditLog() {
    loadingAudit.value = true
    try {
        const r = await service.listAuditLog({
            action: auditFilterAction.value || undefined,
            take: 200,
        })
        auditLog.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load audit log.', 'error')
    } finally {
        loadingAudit.value = false
    }
}

function formatDate(utc: string): string {
    return dayjs.utc(utc).format('YYYY-MM-DD HH:mm')
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
