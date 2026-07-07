<template>
    <v-container>
        <h1 class="text-h4 mb-6">My Passes</h1>

        <v-progress-circular v-if="loading" indeterminate color="primary"></v-progress-circular>

        <v-alert v-else-if="loadError" type="error" variant="tonal" class="mb-4">{{ loadError }}</v-alert>

        <div v-else-if="purchases.length === 0" class="text-medium-emphasis">
            You haven't bought anything yet.
            <router-link to="/Events">Pick an event</router-link> to reserve a spot.
        </div>

        <v-row v-else>
            <v-col v-for="p in purchases" :key="p.kind + p.id" cols="12" md="6" lg="4">
                <v-card variant="outlined" class="pa-4">
                    <div class="d-flex align-center mb-2">
                        <v-chip size="small" :color="p.kind === 'pass' ? 'primary' : 'secondary'">
                            {{ p.kind === 'pass' ? 'Pass' : 'Admission' }}
                        </v-chip>
                        <v-spacer></v-spacer>
                        <v-chip size="small" :color="statusColor(p.status)">{{ p.status }}</v-chip>
                    </div>
                    <div class="text-subtitle-1 font-weight-bold mb-1">{{ p.itemName }}</div>
                    <div class="text-caption text-medium-emphasis mb-3">
                        <div v-if="p.validOnDate">Valid on {{ p.validOnDate.substring(0,10) }}</div>
                        <div v-if="p.eventStartsAtUtc">Event: {{ formatInTenant(p.eventStartsAtUtc) }}</div>
                        <div>Purchased {{ formatInTenant(p.createdAtUtc) }}</div>
                        <div>${{ (p.amountCents / 100).toFixed(2) }}</div>
                    </div>
                    <v-btn v-if="!expanded[p.id]" variant="text" size="small" @click="expanded[p.id] = true">
                        Show QR
                    </v-btn>
                    <div v-else class="text-center">
                        <QrCode :value="redeemUrl(p.redemptionToken)" :size="220" />
                        <v-btn variant="text" size="small" class="mt-2" @click="expanded[p.id] = false">Hide</v-btn>
                    </div>

                    <v-btn v-if="canCancel(p)" size="small" variant="text" color="error" class="mt-2"
                        prepend-icon="mdi-cancel" @click="openCancelDialog(p)">
                        {{ branding.allowSelfCancel ? 'Cancel' : 'Request cancellation' }}
                    </v-btn>

                    <v-btn v-if="canShareRegistration(p)" size="small" variant="text" color="primary" class="mt-2"
                        prepend-icon="mdi-share-variant" @click="openRegistrationShare(p)">
                        Share my registration
                    </v-btn>

                    <!-- Bundled coupons issued with this race entry. Phase 2 displays them
                         (codes + discount + status); Phase 3 wires the "Send to a friend" action. -->
                    <template v-if="couponsByPurchase[p.id]?.length">
                        <v-divider class="my-3"></v-divider>
                        <div class="text-subtitle-2 mb-2">
                            <v-icon size="small" class="mr-1">mdi-tag-multiple</v-icon>
                            Coupons to share
                        </div>
                        <div v-for="c in couponsByPurchase[p.id]" :key="c.id" class="coupon-row">
                            <div>
                                <code class="coupon-code">{{ c.code }}</code>
                                <div class="text-caption text-medium-emphasis">
                                    {{ formatCouponDiscount(c) }} · {{ couponStatus(c) }}
                                    <span v-if="c.shareCount > 0" class="text-success ml-1">
                                        · sent to {{ c.lastSharedToEmail }}{{ c.shareCount > 1 ? ` (+${c.shareCount - 1})` : '' }}
                                    </span>
                                </div>
                            </div>
                            <v-btn size="x-small" variant="tonal" prepend-icon="mdi-email-outline"
                                :disabled="!c.isActive || (c.maxTotalUses !== null && c.redeemedCount >= c.maxTotalUses)"
                                @click="openShareDialog(c)">
                                {{ c.shareCount > 0 ? 'Resend' : 'Send to a friend' }}
                            </v-btn>
                        </div>
                    </template>
                </v-card>
            </v-col>
        </v-row>

        <template v-if="waitlists.length > 0">
            <h2 class="text-h5 mt-8 mb-3">My Waitlists</h2>
            <v-row>
                <v-col v-for="w in waitlists" :key="w.id" cols="12" md="6" lg="4">
                    <v-card variant="outlined" class="pa-4">
                        <div class="d-flex align-center mb-2">
                            <v-chip size="small" color="amber-darken-2">Waitlist</v-chip>
                            <v-spacer></v-spacer>
                            <v-chip size="small" :color="waitlistStatusColor(w.status)">{{ w.status }}</v-chip>
                        </div>
                        <div class="text-subtitle-1 font-weight-bold mb-1">
                            {{ w.eventTitle }}<span v-if="w.tierName"> — {{ w.tierName }}</span>
                        </div>
                        <div class="text-caption text-medium-emphasis mb-3">
                            <div>{{ formatInTenant(w.eventStartsAtUtc) }}</div>
                            <div v-if="w.status === 'waiting'">
                                Position #{{ w.position }} · {{ w.aheadOfMe }} ahead of you
                            </div>
                            <div v-if="w.isPrepaid" class="text-success">
                                <v-icon size="small" class="mr-1">mdi-check-decagram</v-icon>
                                Pre-paid ${{ (w.prepayAmountCents / 100).toFixed(2) }} · spot guaranteed
                            </div>
                            <div v-if="w.status === 'promoted' && w.confirmDeadlineUtc" class="text-warning">
                                <v-icon size="small" class="mr-1">mdi-clock-alert</v-icon>
                                Confirm by {{ formatInTenant(w.confirmDeadlineUtc) }}
                            </div>
                        </div>
                        <v-btn v-if="w.status === 'promoted' && w.confirmToken" size="small" color="primary"
                            :to="`/Waitlist/Confirm/${w.confirmToken}`">
                            Confirm spot
                        </v-btn>
                        <v-btn v-if="w.status === 'waiting' || w.status === 'promoted'" size="small"
                            variant="text" color="error" class="mt-1"
                            :loading="waitlistCancelling === w.id" @click="cancelWaitlist(w)">
                            Withdraw
                        </v-btn>
                    </v-card>
                </v-col>
            </v-row>
        </template>

        <template v-if="extras.length > 0">
            <h2 class="text-h5 mt-8 mb-3">My Add-ons</h2>
            <v-row>
                <v-col v-for="x in extras" :key="x.id" cols="12" md="6" lg="4">
                    <v-card variant="outlined" class="pa-4">
                        <div class="d-flex align-center mb-2">
                            <v-chip size="small" color="amber-darken-2" :prepend-icon="kindIcon(x.kind)">
                                {{ kindLabel(x.kind) }}
                            </v-chip>
                            <v-spacer></v-spacer>
                            <v-chip size="small" :color="x.status === 'paid' ? 'success' : (x.status === 'redeemed' ? 'primary' : 'grey')">
                                {{ x.status }}
                            </v-chip>
                        </div>
                        <div class="text-subtitle-1 font-weight-bold mb-1">{{ x.productName }}</div>
                        <div class="text-caption text-medium-emphasis mb-3">
                            <div>{{ x.eventTitle }}</div>
                            <div>{{ formatInTenant(x.eventStartsAtUtc) }}</div>
                            <div>${{ (x.amountCents / 100).toFixed(2) }}</div>
                        </div>
                        <v-btn v-if="!extraExpanded[x.id]" variant="text" size="small" @click="extraExpanded[x.id] = true">
                            Show QR
                        </v-btn>
                        <div v-else class="text-center">
                            <QrCode :value="redeemUrl(x.redemptionToken)" :size="220" />
                            <v-btn variant="text" size="small" class="mt-2" @click="extraExpanded[x.id] = false">Hide</v-btn>
                        </div>
                    </v-card>
                </v-col>
            </v-row>
        </template>

        <template v-if="rentals.length > 0">
            <h2 class="text-h5 mt-8 mb-3">My Rentals</h2>
            <v-row>
                <v-col v-for="r in rentals" :key="r.id" cols="12" md="6" lg="4">
                    <v-card variant="outlined" class="pa-4">
                        <div class="d-flex align-center mb-2">
                            <v-chip size="small" color="deep-purple">Rental</v-chip>
                            <v-spacer></v-spacer>
                            <v-chip size="small" :color="rentalStatusColor(r.status)">{{ r.status }}</v-chip>
                        </div>
                        <div class="text-subtitle-1 font-weight-bold mb-1">{{ r.productName }}</div>
                        <div class="text-caption text-medium-emphasis mb-3">
                            <div>{{ formatRentalDate(r.startDate) }} → {{ formatRentalDate(r.endDate) }}</div>
                            <div>{{ r.quantity }} unit{{ r.quantity === 1 ? '' : 's' }} · ${{ (r.amountCents / 100).toFixed(2) }}</div>
                            <div v-if="r.depositCents > 0">
                                Deposit: ${{ (r.depositCents / 100).toFixed(2) }} (refunded at return)
                            </div>
                        </div>
                        <v-btn v-if="!rentalExpanded[r.id]" variant="text" size="small" @click="rentalExpanded[r.id] = true">
                            Show QR
                        </v-btn>
                        <div v-else class="text-center">
                            <QrCode :value="redeemUrl(r.redemptionToken)" :size="220" />
                            <v-btn variant="text" size="small" class="mt-2" @click="rentalExpanded[r.id] = false">Hide</v-btn>
                        </div>
                    </v-card>
                </v-col>
            </v-row>
        </template>

        <v-dialog v-model="shareDialog" max-width="500">
            <v-card v-if="shareTarget">
                <v-card-title class="d-flex align-center">
                    <span>Send coupon to a friend</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="shareDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-3">
                        We'll email <strong><code>{{ shareTarget.code }}</code></strong>
                        ({{ formatCouponDiscount(shareTarget) }}) directly to your friend so they can use it at checkout.
                    </p>
                    <v-text-field v-model="shareForm.recipientName" label="Friend's name (optional)"></v-text-field>
                    <v-text-field v-model="shareForm.recipientEmail" type="email" label="Friend's email" class="mt-2"
                        :error-messages="shareError ? [shareError] : []"></v-text-field>
                    <v-textarea v-model="shareForm.personalNote" label="Personal note (optional)" rows="3"
                        placeholder="Hey! Come watch me race — here's a discount." class="mt-2"></v-textarea>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="shareDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="sharing" :disabled="!validShareEmail" @click="sendShare">Send</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="cancelDialog" max-width="520" persistent>
            <v-card v-if="cancelTarget">
                <v-card-title class="d-flex align-center">
                    <span>{{ branding.allowSelfCancel ? 'Cancel purchase' : 'Request cancellation' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="cancelDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-3">{{ cancelTarget.itemName }}</p>
                    <v-alert v-if="branding.allowSelfCancel" type="info" variant="tonal" density="compact" class="mb-3">
                        You'll be refunded ${{ ((cancelTarget.amountCents) / 100).toFixed(2) }} minus the
                        rider service charge. The refund posts to your original payment method.
                    </v-alert>
                    <v-alert v-else type="info" variant="tonal" density="compact" class="mb-3">
                        Your request will be sent to the track admins. Refunds (when approved) never
                        include the rider service charge.
                    </v-alert>
                    <v-textarea v-model="cancelReason" label="Reason (optional)" rows="2" density="compact"
                        maxlength="500" counter></v-textarea>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="cancelDialog = false">Never mind</v-btn>
                    <v-btn color="error" :loading="cancelling" @click="confirmCancel">
                        {{ branding.allowSelfCancel ? 'Cancel and refund' : 'Submit request' }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="regShareOpen" max-width="560">
            <v-card v-if="regSharing">
                <v-card-title class="d-flex align-center">
                    <span>Share your registration</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="regShareOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div class="text-subtitle-1 mb-1">{{ regSharing.itemName }}</div>
                    <div class="text-caption text-medium-emphasis mb-3">
                        Sharing the public event page so your friends can sign up too.
                    </div>
                    <SocialShare :url="regShareUrl" :title="regShareTitle" :text="regShareText" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="regShareOpen = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { TicketService, type MyPurchase, type MyCoupon } from '@/services/TicketService'
import SocialShare from '@/components/SocialShare.vue'
import { RentalService, type MyRental } from '@/services/RentalService'
import { WaitlistService, type MyWaitlistEntry } from '@/services/WaitlistService'
import { ExtraService, kindIcon, kindLabel, type MyExtra } from '@/services/ExtraService'
import { branding } from '@/stores/branding'
import QrCode from '@/components/QrCode.vue'
import { useConfirm } from '@/composables/useConfirm'

const service = new TicketService()
const confirm = useConfirm()
const rentalService = new RentalService()
const waitlistService = new WaitlistService()
const extraService = new ExtraService()

const purchases = ref<MyPurchase[]>([])
const rentals = ref<MyRental[]>([])
const extras = ref<MyExtra[]>([])
const extraExpanded = reactive<Record<string, boolean>>({})
const waitlists = ref<MyWaitlistEntry[]>([])
const waitlistCancelling = ref<string | null>(null)
const loading = ref(true)
const loadError = ref('')
const expanded = reactive<Record<string, boolean>>({})
const rentalExpanded = reactive<Record<string, boolean>>({})
// Coupons grouped by their issuing purchase id so the per-card render stays simple.
const couponsByPurchase = reactive<Record<string, MyCoupon[]>>({})

onMounted(() => {
    handlePaymentRedirect()
    load()
})

// A season-pass / rental / waitlist checkout using a redirect-based payment method (3DS, wallet)
// lands back here. Surface the outcome so a failed payment isn't silently shown as "nothing new"
// and a succeeded one explains why the item may take a moment to appear (webhook finalizes it).
function handlePaymentRedirect() {
    const params = new URLSearchParams(window.location.search)
    const redirectStatus = params.get('redirect_status')
    if (params.get('payment_intent') && redirectStatus) {
        if (redirectStatus === 'succeeded') {
            flash('Payment received. Your purchase will appear here shortly.', 'success')
        } else {
            flash('Your payment was not completed. Please try again.', 'error')
        }
        history.replaceState(null, '', window.location.pathname)
    }
}

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        const r = await service.getMyPurchases()
        purchases.value = (r.data as any).data
        // Pull all my coupons once, then bucket by source purchase. One round-trip is
        // simpler than per-card lazy-loading and the response is small.
        const cr = await service.getMyCoupons()
        const list: MyCoupon[] = (cr.data as any).data
        for (const k of Object.keys(couponsByPurchase)) delete couponsByPurchase[k]
        for (const c of list) {
            const key = c.issuedFromPurchaseId
            if (!key) continue
            if (!couponsByPurchase[key]) couponsByPurchase[key] = []
            couponsByPurchase[key].push(c)
        }
        // Rentals only when the tenant has them on; otherwise the call would 404 a
        // 'not enabled' message and we'd render an empty section anyway.
        if (branding.rentalsEnabled) {
            try {
                const rr = await rentalService.listMine()
                rentals.value = (rr.data as any).data
            } catch (e: any) {
                rentals.value = []
                // A 404 is the documented "feature not enabled" response and renders an empty
                // section by design; any other failure is real and must not hide silently.
                if (e.response?.status !== 404) flash(e.response?.data?.error || 'Could not load your rentals. Refresh to try again.', 'error')
            }
        }
        if (branding.extrasEnabled) {
            try {
                const er = await extraService.listMine()
                extras.value = ((er.data as any).data as MyExtra[])
                    .filter(x => x.status === 'paid' || x.status === 'redeemed')
            } catch (e: any) {
                extras.value = []
                if (e.response?.status !== 404) flash(e.response?.data?.error || 'Could not load your add-ons. Refresh to try again.', 'error')
            }
        }
        try {
            const wr = await waitlistService.listMine()
            waitlists.value = ((wr.data as any).data as MyWaitlistEntry[])
                .filter(w => w.status === 'waiting' || w.status === 'promoted' || w.status === 'confirmed')
        } catch (e: any) {
            waitlists.value = []
            if (e.response?.status !== 404) flash(e.response?.data?.error || 'Could not load your waitlist entries. Refresh to try again.', 'error')
        }
    } catch (err: any) {
        loadError.value = err.response?.data?.error
            || 'Could not load your passes. Refresh to try again, or check your connection.'
    } finally {
        loading.value = false
    }
}

function waitlistStatusColor(s: string): string {
    if (s === 'waiting') return 'amber-darken-2'
    if (s === 'promoted') return 'warning'
    if (s === 'confirmed') return 'success'
    if (s === 'expired' || s === 'cancelled') return 'grey'
    return 'default'
}

async function cancelWaitlist(w: MyWaitlistEntry) {
    if (!await confirm({ message: `Withdraw from this waitlist?`, confirmText: 'Withdraw', confirmColor: 'error' })) return
    waitlistCancelling.value = w.id
    try {
        await waitlistService.cancel(w.id)
        await load()
        snackbarText.value = 'Withdrew from waitlist.'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Could not withdraw.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        waitlistCancelling.value = null
    }
}

function formatRentalDate(d: string): string { return dayjs(d).format('MMM D, YYYY') }
function rentalStatusColor(s: string): string {
    if (s === 'paid') return 'primary'
    if (s === 'out') return 'warning'
    if (s === 'returned') return 'success'
    if (s === 'damaged') return 'error'
    if (s === 'cancelled' || s === 'failed') return 'grey'
    return 'default'
}

function formatCouponDiscount(c: MyCoupon): string {
    return c.discountKind === 'percent'
        ? `${Math.round(c.discountValue / 100)}% off`
        : `$${(c.discountValue / 100).toFixed(2)} off`
}

function couponStatus(c: MyCoupon): string {
    if (!c.isActive) return 'Inactive'
    if (c.maxTotalUses !== null && c.redeemedCount >= c.maxTotalUses) return 'Used'
    if (c.validToUtc && dayjs.utc(c.validToUtc).isBefore(dayjs())) return 'Expired'
    return 'Available'
}

// ── Send-to-Friend dialog ───────────────────────────────────────────────────
const shareDialog = ref(false)
const shareTarget = ref<MyCoupon | null>(null)
const shareForm = ref({ recipientEmail: '', recipientName: '', personalNote: '' })
const shareError = ref('')
const sharing = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

// ── Cancel / cancel-request dialog ──────────────────────────────────────────
const cancelDialog = ref(false)
const cancelTarget = ref<MyPurchase | null>(null)
const cancelReason = ref('')
const cancelling = ref(false)

function canCancel(p: MyPurchase): boolean {
    return p.status === 'paid'
}

const regShareOpen = ref(false)
const regSharing = ref<MyPurchase | null>(null)
const regShareUrl = computed(() =>
    regSharing.value?.eventId ? `${window.location.origin}/Event/${regSharing.value.eventId}` : '')
const regShareTitle = computed(() =>
    regSharing.value ? `I'm racing — ${regSharing.value.itemName}` : '')
const regShareText = computed(() => {
    if (!regSharing.value) return ''
    const date = regSharing.value.eventStartsAtUtc
        ? dayjs.utc(regSharing.value.eventStartsAtUtc).tz(branding.timezone || 'UTC').format('MMM D, YYYY')
        : ''
    return date
        ? `I'm racing at ${branding.displayName} on ${date}! Come watch or sign up too 👇`
        : `I'm racing at ${branding.displayName}! Come watch or sign up too 👇`
})

// Share is offered for race entries only (not spectator passes), and only on
// paid/completed registrations — not pending or cancelled ones. eventId is
// required since the share URL is the public event page.
function canShareRegistration(p: MyPurchase): boolean {
    return p.kind === 'event_ticket'
        && p.tierKind === 'race_entry'
        && !!p.eventId
        && (p.status === 'paid' || p.status === 'redeemed')
}

function openRegistrationShare(p: MyPurchase) {
    regSharing.value = p
    regShareOpen.value = true
}

function openCancelDialog(p: MyPurchase) {
    cancelTarget.value = p
    cancelReason.value = ''
    cancelDialog.value = true
}

async function confirmCancel() {
    if (!cancelTarget.value) return
    cancelling.value = true
    try {
        const reason = cancelReason.value.trim() || null
        const r = cancelTarget.value.kind === 'pass'
            ? await service.cancelMyPass(cancelTarget.value.id, reason)
            : await service.cancelMyTicket(cancelTarget.value.id, reason)
        const data = (r.data as any).data
        if (data.status === 'request_submitted') {
            snackbarText.value = 'Cancellation request sent to the track admins. They will follow up with you.'
        } else if (data.refundCents > 0) {
            snackbarText.value = `Cancelled — $${(data.refundCents / 100).toFixed(2)} refunded.`
        } else {
            snackbarText.value = 'Cancelled.'
        }
        snackbarColor.value = 'success'
        snackbar.value = true
        cancelDialog.value = false
        await load()
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Cancel failed.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        cancelling.value = false
    }
}

const validShareEmail = computed(() => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(shareForm.value.recipientEmail.trim()))

function openShareDialog(c: MyCoupon) {
    shareTarget.value = c
    shareForm.value = { recipientEmail: '', recipientName: '', personalNote: '' }
    shareError.value = ''
    shareDialog.value = true
}

async function sendShare() {
    if (!shareTarget.value) return
    if (!validShareEmail.value) { shareError.value = 'Enter a valid email.'; return }
    try {
        sharing.value = true
        await service.shareCoupon(shareTarget.value.id, {
            recipientEmail: shareForm.value.recipientEmail.trim(),
            recipientName: shareForm.value.recipientName.trim() || null,
            personalNote: shareForm.value.personalNote.trim() || null,
        })
        shareDialog.value = false
        flash('Coupon sent.', 'success')
        // Reload so the share badge under the code updates immediately.
        await load()
    } catch (err: any) {
        shareError.value = err.response?.data?.error || 'Failed to send coupon.'
    } finally {
        sharing.value = false
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

function redeemUrl(token: string): string {
    return `${window.location.protocol}//${window.location.host}/redeem/${token}`
}

function formatInTenant(utc: string): string {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('YYYY-MM-DD HH:mm')
}

function statusColor(status: string): string {
    switch (status) {
        case 'paid': return 'success'
        case 'pending': return 'warning'
        case 'failed': return 'error'
        case 'refunded': return 'grey'
        case 'redeemed': return 'primary'
        default: return 'default'
    }
}
</script>

<style scoped>
.coupon-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    padding: 6px 0;
    border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
}
.coupon-row:last-child { border-bottom: none; }
.coupon-code {
    font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
    font-weight: 600;
    font-size: 0.95rem;
}
</style>
