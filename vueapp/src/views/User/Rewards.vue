<template>
    <v-container style="max-width: 720px">
        <h1 class="text-h4 mb-4">Rewards</h1>

        <!-- Store credit: balance + how it moved. Spendable at any register or online checkout. -->
        <v-card v-if="creditLoaded && (creditBalance > 0 || creditEntries.length > 0)" class="mb-4 pa-4">
            <v-card-title class="d-flex align-center">
                Store credit
                <v-spacer></v-spacer>
                <v-chip color="primary" variant="tonal">{{ money(creditBalance) }}</v-chip>
            </v-card-title>
            <v-card-text>
                <p class="text-caption text-medium-emphasis mb-3">
                    Use it at the counter (give them the email or phone on your account) or apply it
                    at checkout when buying online.
                </p>
                <v-table v-if="creditEntries.length" density="compact">
                    <thead><tr><th>When</th><th>What</th><th class="text-right">Amount</th></tr></thead>
                    <tbody>
                        <tr v-for="(e, i) in creditEntries" :key="i">
                            <td class="text-caption">{{ formatDate(e.createdAt) }}</td>
                            <td class="text-caption">{{ creditKindLabel(e.kind) }}</td>
                            <td class="text-right" :class="e.deltaCents < 0 ? 'text-error' : 'text-success'">
                                {{ e.deltaCents < 0 ? '-' : '+' }}{{ money(Math.abs(e.deltaCents)) }}</td>
                        </tr>
                    </tbody>
                </v-table>
            </v-card-text>
        </v-card>
        <v-alert v-if="creditError" type="warning" variant="tonal" density="compact" class="mb-4">{{ creditError }}</v-alert>

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
        <v-alert v-else-if="loadError" type="error" variant="tonal" class="mb-3">{{ loadError }}</v-alert>
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
                <v-chip v-if="p.rewardKind === 'credit_rate'" size="small" variant="tonal" color="primary">
                    Earn {{ ((p.creditRateBps ?? 0) / 100) }}% back in store credit
                </v-chip>
                <v-chip v-else size="small" variant="tonal">
                    Buy {{ p.requirementCount }} {{ kindLabel(p.requirementKind) }}{{ p.requirementCount === 1 ? '' : 's' }} →
                    {{ p.rewardPercentOff === 100 ? 'Free' : `${p.rewardPercentOff}% off` }}
                </v-chip>
            </div>

            <template v-if="p.rewardKind === 'credit_rate'">
                <p class="text-caption text-medium-emphasis mb-2">
                    {{ creditProgramBlurb(p) }}
                </p>
                <v-btn v-if="p.enrollmentMode === 'opt_in' && !p.isEnrolled" color="primary" size="small"
                    :loading="busyId === p.programId" @click="enroll(p.programId)">Join program</v-btn>
                <v-btn v-else-if="p.enrollmentMode === 'opt_in' && p.isEnrolled" variant="text" size="small"
                    :loading="busyId === p.programId" @click="unenroll(p.programId)">Leave program</v-btn>
            </template>
            <template v-else-if="p.isEnrolled">
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
import { CreditService, type CreditEntry } from '@/services/CreditService'
import { branding } from '@/stores/branding'
import { useConfirm } from '@/composables/useConfirm'

const service = new RewardService()
const confirm = useConfirm()
const programs = ref<RiderRewardProgram[]>([])
const redemptions = ref<RiderRewardRedemption[]>([])
const loading = ref(true)
const loadError = ref('')
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

// ── Store credit ───────────────────────────────────────────────────────────
const creditBalance = ref(0)
const creditEntries = ref<Pick<CreditEntry, 'deltaCents' | 'kind' | 'note' | 'createdAt'>[]>([])
const creditLoaded = ref(false)
const creditError = ref('')
function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
function creditProgramBlurb(p: RiderRewardProgram): string {
    const what = p.creditQualifyingKind === 'event_ticket' ? 'event and gate purchases'
        : p.creditQualifyingKind === 'concession' ? 'food & beverage orders'
        : p.creditQualifyingKind === 'shop_sale' ? 'bike shop purchases'
        : 'every purchase'
    return p.enrollmentMode === 'auto' || p.isEnrolled
        ? `Credit from ${what} lands on your balance above automatically.`
        : `Join to start earning credit on ${what}.`
}

function creditKindLabel(kind: CreditEntry['kind']): string {
    switch (kind) {
        case 'deposit_excess': return 'Deposit overage kept as credit'
        case 'refund_to_credit': return 'Refund issued as credit'
        case 'loyalty_award': return 'Loyalty reward'
        case 'manual_adjust': return 'Adjustment by the track'
        case 'redeem': return 'Spent'
        case 'redeem_reversal': return 'Returned to your balance'
        default: return kind
    }
}
async function loadCredit() {
    try {
        const r = await new CreditService().mine()
        creditBalance.value = r.data.data.balanceCents
        creditEntries.value = r.data.data.entries
        creditLoaded.value = true
    } catch (err: any) {
        creditError.value = err.response?.data?.error || 'Could not load your store credit balance. Refresh to try again.'
    }
}

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        const [p, r] = await Promise.all([service.listMyPrograms(), service.listMyRedemptions()])
        programs.value = (p.data as any).data
        redemptions.value = (r.data as any).data
        await loadCredit()
    } catch (err: any) {
        loadError.value = err.response?.data?.error || 'Could not load rewards. Refresh to try again.'
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
    if (!await confirm({ message: `Leave this program? Your progress and unredeemed vouchers will be removed.`, confirmText: 'Leave', confirmColor: 'error' })) return
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
