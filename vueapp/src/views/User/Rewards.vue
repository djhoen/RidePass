<template>
    <v-container style="max-width: 720px">
        <h1 class="text-h4 mb-4">Rewards</h1>

        <v-card v-if="redemptions.filter(r => !r.redeemedAtUtc).length > 0" class="mb-4 pa-4">
            <v-card-title>Your vouchers</v-card-title>
            <v-card-text>
                <p class="text-caption text-medium-emphasis mb-3">
                    Show these at the gate. The track will honor them on your next purchase.
                </p>
                <v-table density="compact">
                    <thead>
                        <tr><th>Program</th><th>Reward</th><th>Earned</th></tr>
                    </thead>
                    <tbody>
                        <tr v-for="r in unredeemedRedemptions" :key="r.id">
                            <td>{{ r.programName }}</td>
                            <td>
                                <v-chip size="small" color="success">
                                    {{ r.rewardPercentOff === 100 ? 'Free' : `${r.rewardPercentOff}% off` }}
                                </v-chip>
                            </td>
                            <td>{{ formatDate(r.earnedAtUtc) }}</td>
                        </tr>
                    </tbody>
                </v-table>
            </v-card-text>
        </v-card>

        <h2 class="text-h6 mb-2">Programs</h2>
        <div v-if="loading" class="text-center py-6">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>
        <v-card v-else-if="programs.length === 0" class="pa-6 text-center text-medium-emphasis">
            No reward programs at this track yet.
        </v-card>
        <v-card v-for="p in programs" :key="p.programId" class="mb-3 pa-4">
            <div class="d-flex align-center mb-2">
                <div>
                    <strong>{{ p.name }}</strong>
                    <div v-if="p.description" class="text-caption text-medium-emphasis">{{ p.description }}</div>
                </div>
                <v-spacer></v-spacer>
                <v-chip size="small" variant="tonal">
                    Buy {{ p.requirementCount }} {{ kindLabel(p.requirementKind) }}{{ p.requirementCount === 1 ? '' : 's' }} →
                    {{ p.rewardPercentOff === 100 ? 'Free' : `${p.rewardPercentOff}% off` }}
                </v-chip>
            </div>

            <template v-if="p.isEnrolled">
                <v-progress-linear :model-value="(p.progress / p.requirementCount) * 100"
                    height="20" rounded color="primary" class="mb-1">
                    <template #default>
                        <span class="text-caption text-white">{{ p.progress }} / {{ p.requirementCount }}</span>
                    </template>
                </v-progress-linear>
                <div class="d-flex align-center mt-1">
                    <span class="text-caption text-medium-emphasis">
                        {{ p.remainingForReward === 0
                            ? "You've earned a reward — see your vouchers above."
                            : `${p.remainingForReward} more to earn your next reward.` }}
                    </span>
                    <v-spacer></v-spacer>
                    <v-btn v-if="p.enrollmentMode === 'opt_in'" variant="text" size="small"
                        :loading="busyId === p.programId" @click="unenroll(p.programId)">Leave program</v-btn>
                </div>
            </template>
            <template v-else>
                <p class="text-caption text-medium-emphasis mb-2">
                    {{ p.enrollmentMode === 'auto'
                        ? "You'll be enrolled automatically with your next purchase."
                        : "Opt in to start tracking your progress." }}
                </p>
                <v-btn v-if="p.enrollmentMode === 'opt_in'" color="primary" size="small"
                    :loading="busyId === p.programId" @click="enroll(p.programId)">
                    Join program
                </v-btn>
            </template>
        </v-card>

        <v-card v-if="redeemedHistory.length > 0" class="mt-6 pa-4">
            <v-card-title>Redeemed history</v-card-title>
            <v-card-text>
                <v-table density="compact">
                    <thead>
                        <tr><th>Program</th><th>Reward</th><th>Earned</th><th>Redeemed</th></tr>
                    </thead>
                    <tbody>
                        <tr v-for="r in redeemedHistory" :key="r.id">
                            <td>{{ r.programName }}</td>
                            <td>{{ r.rewardPercentOff === 100 ? 'Free' : `${r.rewardPercentOff}% off` }}</td>
                            <td>{{ formatDate(r.earnedAtUtc) }}</td>
                            <td>{{ r.redeemedAtUtc ? formatDate(r.redeemedAtUtc) : '—' }}</td>
                        </tr>
                    </tbody>
                </v-table>
            </v-card-text>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { RewardService, type RiderRewardProgram, type RiderRewardRedemption } from '@/services/RewardService'
import { branding } from '@/stores/branding'

const service = new RewardService()
const programs = ref<RiderRewardProgram[]>([])
const redemptions = ref<RiderRewardRedemption[]>([])
const loading = ref(false)
const busyId = ref<string | null>(null)

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const unredeemedRedemptions = computed(() => redemptions.value.filter(r => !r.redeemedAtUtc))
const redeemedHistory = computed(() => redemptions.value.filter(r => r.redeemedAtUtc))

function kindLabel(k: string): string {
    if (k === 'pass') return 'pass'
    if (k === 'event_ticket') return 'event ticket'
    return 'purchase'
}

function formatDate(iso: string): string {
    return dayjs.utc(iso).tz(branding.timezone || 'UTC').format('MMM D, YYYY')
}

async function load() {
    loading.value = true
    try {
        const [p, r] = await Promise.all([service.listMyPrograms(), service.listMyRedemptions()])
        programs.value = (p.data as any).data
        redemptions.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load rewards.', 'error')
    } finally {
        loading.value = false
    }
}

async function enroll(programId: string) {
    busyId.value = programId
    try {
        await service.enroll(programId)
        await load()
        flash('You\'re in! Your progress will count from your next purchase.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not enroll.', 'error')
    } finally {
        busyId.value = null
    }
}

async function unenroll(programId: string) {
    if (!confirm('Leave this program? Your progress and unredeemed vouchers will be removed.')) return
    busyId.value = programId
    try {
        await service.unenroll(programId)
        await load()
        flash('You\'ve left the program.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not leave.', 'error')
    } finally {
        busyId.value = null
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onMounted(load)
</script>
