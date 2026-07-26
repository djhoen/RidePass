<template>
    <v-container fluid>
        <div class="d-flex align-center mb-4 flex-wrap ga-2">
            <h1 class="text-h5">Buddy Pass Usage</h1>
            <v-spacer />
            <v-btn variant="text" prepend-icon="mdi-refresh" :loading="loading" @click="load">Refresh</v-btn>
        </div>

        <v-alert v-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <div class="d-flex ga-3 mb-4 flex-wrap">
            <v-card variant="tonal" class="stat-tile">
                <v-card-text>
                    <div class="text-h5">{{ initialLoading ? '-' : liveCount }}</div>
                    <div class="text-caption text-medium-emphasis">Buddies admitted</div>
                </v-card-text>
            </v-card>
            <v-card variant="tonal" class="stat-tile">
                <v-card-text>
                    <div class="text-h5">{{ initialLoading ? '-' : returnedCount }}</div>
                    <div class="text-caption text-medium-emphasis">Credits returned</div>
                </v-card-text>
            </v-card>
        </div>

        <v-card variant="outlined" :loading="loading">
            <v-table density="compact">
                <thead>
                    <tr>
                        <th>Pass holder</th>
                        <th>Buddy</th>
                        <th>Event</th>
                        <th>Redeemed</th>
                        <th>Credit</th>
                        <th class="text-right" style="width: 150px"></th>
                    </tr>
                </thead>
                <tbody>
                    <template v-if="initialLoading">
                        <tr v-for="n in 4" :key="'sk' + n">
                            <td v-for="c in 6" :key="'skc' + c"><v-skeleton-loader type="text" /></td>
                        </tr>
                    </template>
                    <!-- Returned credits stay listed and flagged. Filtering them out would make the
                         free admissions they explain look like they came from nowhere. -->
                    <tr v-for="r in rows" :key="r.id" :class="{ 'row-returned': r.creditReturned }">
                        <td>{{ r.holderName || '-' }}</td>
                        <td>
                            {{ r.buddyName || '-' }}
                            <div v-if="r.buddyEmail" class="text-caption text-medium-emphasis">{{ r.buddyEmail }}</div>
                        </td>
                        <td>{{ r.eventTitle || '-' }}</td>
                        <td class="text-no-wrap">
                            <div class="text-caption">{{ formatDate(r.redeemedAtUtc) }}</div>
                            <div v-if="r.redeemedByName" class="text-caption text-medium-emphasis">
                                by {{ r.redeemedByName }}
                            </div>
                        </td>
                        <td class="text-no-wrap">
                            <template v-if="r.creditReturned">
                                <v-chip size="x-small" color="warning" variant="tonal">Returned</v-chip>
                                <div class="text-caption text-medium-emphasis">
                                    {{ r.creditReturnReason }}
                                    <span v-if="r.creditReturnedByName"> · {{ r.creditReturnedByName }}</span>
                                </div>
                            </template>
                            <v-chip v-else size="x-small" color="success" variant="tonal">Used</v-chip>
                        </td>
                        <td class="text-right">
                            <v-btn v-if="!r.creditReturned" variant="text" size="small" @click="openReturn(r)">
                                Return credit
                            </v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && !loadError && rows.length === 0">
                        <td colspan="6" class="text-center text-medium-emphasis py-6">
                            No buddy passes have been used yet.
                        </td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- ── Return a credit ─────────────────────────────────────────────── -->
        <v-dialog v-model="returnOpen" max-width="520">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Return this buddy credit</span>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="returnOpen = false" />
                </v-card-title>
                <v-divider />
                <v-card-text>
                    <v-alert v-if="actionError" type="error" variant="tonal" density="compact" class="mb-4">
                        {{ actionError }}
                    </v-alert>
                    <p class="text-body-2 mb-2">
                        Gives one buddy pass back to <strong>{{ target?.holderName || 'the holder' }}</strong>.
                    </p>
                    <p class="text-caption text-medium-emphasis mb-2">
                        No money moves, and <strong>{{ target?.buddyName || 'the guest' }}</strong>'s
                        admission is not cancelled. To undo the admission itself, cancel their ticket.
                    </p>
                    <v-text-field v-model="reason" label="Reason" density="compact" class="mt-4"
                        :error-messages="reason.trim() ? [] : ['A reason is required.']" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="returnOpen = false">Cancel</v-btn>
                    <v-btn color="warning" :loading="saving" :disabled="!reason.trim()" @click="confirmReturn">
                        Return credit
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="toast" :timeout="4000" color="success" location="top">{{ toastText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import { SeasonPassService, type BuddyRedemptionItem } from '@/services/SeasonPassService'

const service = new SeasonPassService()

const rows = ref<BuddyRedemptionItem[]>([])
const loading = ref(false)
const loaded = ref(false)
const loadError = ref<string | null>(null)
const initialLoading = computed(() => loading.value && !loaded.value)

const returnOpen = ref(false)
const target = ref<BuddyRedemptionItem | null>(null)
const reason = ref('')
const saving = ref(false)
const actionError = ref<string | null>(null)
const toast = ref(false)
const toastText = ref('')

const liveCount = computed(() => rows.value.filter(r => !r.creditReturned).length)
const returnedCount = computed(() => rows.value.filter(r => r.creditReturned).length)

function formatDate(utc: string) {
    return dayjs(utc).format('MMM D, YYYY h:mm a')
}

async function load() {
    loading.value = true
    loadError.value = null
    try {
        const { data } = await service.buddyUsage()
        rows.value = data.data
        loaded.value = true
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            ?? 'Could not load buddy pass usage. Use Refresh to try again.'
    } finally {
        loading.value = false
    }
}

function openReturn(r: BuddyRedemptionItem) {
    target.value = r
    reason.value = ''
    actionError.value = null
    returnOpen.value = true
}

async function confirmReturn() {
    if (!target.value || !reason.value.trim()) return
    saving.value = true
    actionError.value = null
    try {
        await service.returnBuddyCredit(target.value.id, reason.value.trim())
        returnOpen.value = false
        toastText.value = 'Credit returned to the holder.'
        toast.value = true
        await load()
    } catch (err: any) {
        actionError.value = err.response?.data?.error
            ?? 'Could not return the credit. Nothing was changed; try again.'
    } finally {
        saving.value = false
    }
}

onMounted(load)
</script>

<style scoped>
.stat-tile { min-width: 150px; }
/* A returned row is history, not current usage: dim it without hiding it. */
.row-returned { opacity: 0.72; }
</style>
