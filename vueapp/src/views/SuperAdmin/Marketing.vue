<template>
    <v-container>
        <div class="d-flex align-center mb-6">
            <h1 class="text-h4">Marketing — Captured Emails</h1>
            <v-spacer></v-spacer>
            <v-btn variant="text" prepend-icon="mdi-download" @click="exportCsv" :disabled="rows.length === 0">
                Export CSV
            </v-btn>
        </div>

        <p class="text-body-2 text-medium-emphasis mb-6">
            Every time a rider sends a coupon code to a friend from My Passes, the recipient's
            email is captured here. These are warm leads — people who heard about a track from
            someone they know. Use sparingly and respect unsubscribe preferences.
        </p>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Recipient</th>
                        <th>Email</th>
                        <th>Tenant</th>
                        <th>Sent</th>
                        <th>Redeemed?</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="(r, i) in rows" :key="i">
                        <td>{{ r.recipientName ?? '—' }}</td>
                        <td><code>{{ r.recipientEmail }}</code></td>
                        <td>{{ r.tenantDisplayName }} <span class="text-caption text-medium-emphasis">({{ r.tenantSubdomain }})</span></td>
                        <td class="text-caption">{{ formatDate(r.sentAtUtc) }}</td>
                        <td>
                            <v-chip v-if="r.redeemedAtUtc" size="small" color="success">
                                {{ formatDate(r.redeemedAtUtc) }}
                            </v-chip>
                            <span v-else class="text-medium-emphasis">—</span>
                        </td>
                    </tr>
                    <tr v-if="!loading && rows.length === 0">
                        <td colspan="5" class="text-center text-medium-emphasis py-8">
                            No coupon shares yet.
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { SuperAdminService, type CouponShareRow } from '@/services/SuperAdminService'

const service = new SuperAdminService()

const rows = ref<CouponShareRow[]>([])
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

function formatDate(iso: string): string {
    return dayjs(iso).format('MMM D, YYYY h:mm A')
}

async function load() {
    loading.value = true
    try {
        const r = await service.listCouponShares()
        rows.value = r.data.data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load shares.', 'error')
    } finally {
        loading.value = false
    }
}

function exportCsv() {
    // Quote any field that might contain commas or quotes per RFC4180.
    const escape = (v: string | null) => v === null
        ? ''
        : (/[",\n]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v)
    const header = ['Recipient name', 'Email', 'Tenant', 'Subdomain', 'Sent at UTC', 'Redeemed at UTC'].join(',')
    const body = rows.value.map(r => [
        escape(r.recipientName), escape(r.recipientEmail),
        escape(r.tenantDisplayName), escape(r.tenantSubdomain),
        r.sentAtUtc, r.redeemedAtUtc ?? '',
    ].join(',')).join('\n')
    const blob = new Blob([header + '\n' + body], { type: 'text/csv' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `coupon-shares-${dayjs().format('YYYY-MM-DD')}.csv`
    a.click()
    URL.revokeObjectURL(url)
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onMounted(load)
</script>
