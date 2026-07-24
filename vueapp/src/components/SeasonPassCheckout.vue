<template>
    <div class="sp-checkout">
        <!-- ── 1. Select passes ──────────────────────────────────────────── -->
        <template v-if="step === 'select'">
            <h2 class="text-h5 font-weight-bold font-display mb-1">Choose Your Pass</h2>
            <p class="text-body-2 text-medium-emphasis mb-4">
                Buying for the family? Add one pass per rider — you'll name each holder after payment.
            </p>

            <div v-for="p in products" :key="p.id" class="sp-line pl-1">
                <div class="flex-grow-1 pr-2">
                    <div class="font-weight-medium">{{ p.name }}</div>
                    <div class="text-caption text-medium-emphasis">
                        {{ priceLabel(p.priceCents) }} · {{ accessLabel(p) }}
                    </div>
                    <div class="text-caption text-medium-emphasis">{{ validLabel(p) }}</div>
                </div>
                <div class="d-flex align-center ga-1">
                    <v-btn icon="mdi-minus" size="x-small" variant="tonal"
                        :disabled="(qty[p.id] || 0) <= 0" @click="bump(p, -1)"></v-btn>
                    <span class="sp-qty">{{ qty[p.id] || 0 }}</span>
                    <v-btn icon="mdi-plus" size="x-small" variant="tonal"
                        :disabled="(qty[p.id] || 0) >= maxPerProduct" @click="bump(p, 1)"></v-btn>
                </div>
            </div>

            <template v-if="hasSelection">
                <div v-if="serviceFeeCents > 0" class="d-flex justify-space-between text-body-2 text-medium-emphasis mt-4 mb-1">
                    <span>Subtotal</span>
                    <span>{{ priceLabel(subtotalCents) }}</span>
                </div>
                <div v-if="serviceFeeCents > 0" class="d-flex justify-space-between text-body-2 text-medium-emphasis mb-1">
                    <span>Service fee</span>
                    <span>{{ priceLabel(serviceFeeCents) }}</span>
                </div>
                <div class="d-flex align-center justify-space-between mb-3" :class="serviceFeeCents > 0 ? '' : 'mt-4'">
                    <span class="text-h6">Total</span>
                    <span class="text-h6 font-weight-bold">{{ priceLabel(estTotalCents) }}</span>
                </div>
                <p class="text-caption text-medium-emphasis mb-3">Final total is shown before you pay.</p>
                <v-btn block color="primary" size="large" @click="step = 'details'">Continue</v-btn>
            </template>
            <p v-else class="text-body-2 text-medium-emphasis text-center py-2">
                Add a pass above to get started.
            </p>
        </template>

        <!-- ── 2. Your info + codes ──────────────────────────────────────── -->
        <template v-else-if="step === 'details'">
            <div class="d-flex align-center mb-3">
                <h2 class="text-h6 font-weight-bold">Almost done</h2>
                <v-spacer></v-spacer>
                <v-btn variant="text" size="small" prepend-icon="mdi-arrow-left" @click="step = 'select'">Back</v-btn>
            </div>

            <!-- A season pass lives on a rider account (it's how holders find their passes and
                 reserve spots), so unlike event tickets there's no guest path — log in here
                 rather than after they've filled everything in. -->
            <template v-if="!isAuthed">
                <div class="sp-login pa-3">
                    <p class="text-body-2 mb-3">
                        Season passes are saved to your account, so please log in to continue.
                    </p>
                    <v-text-field v-model="email" type="email" label="Email" density="compact" hide-details></v-text-field>
                    <v-text-field v-model="loginPassword" type="password" label="Password" density="compact"
                        class="mt-4" :error-messages="loginError ? [loginError] : []"
                        @keyup.enter="doLogin"></v-text-field>
                    <v-btn class="mt-4" color="primary" :loading="loggingIn"
                        :disabled="!email.trim() || !loginPassword" @click="doLogin">Log in</v-btn>
                    <div class="text-caption text-medium-emphasis mt-3">
                        No account yet?
                        <a :href="signUpHref" :target="isEmbed ? '_blank' : '_self'" rel="noopener"
                            class="text-primary" style="text-decoration: underline">Create one</a>
                        — it takes a minute.
                    </div>
                </div>
            </template>

            <template v-else>
                <div class="sp-group-label mb-2">Your info</div>
                <v-text-field v-model="name" label="Full name" density="compact" class="mt-4" readonly></v-text-field>
                <v-text-field v-model="email" type="email" label="Email" density="compact" class="mt-4" readonly></v-text-field>
                <div class="text-caption text-medium-emphasis mt-1">
                    Not you? <a href="#" class="text-primary" style="text-decoration: underline"
                        @click.prevent="switchUser">Log out</a>
                </div>

                <div class="sp-group-label mt-5 mb-2">Promo or gift code</div>
                <v-text-field v-model="couponCode" label="Promo code" density="compact" class="mt-4"
                    :error-messages="couponError ? [couponError] : []"></v-text-field>
                <v-text-field v-model="giftCardCode" label="Gift card code" density="compact" class="mt-4"
                    :error-messages="giftCardError ? [giftCardError] : []"></v-text-field>

                <div class="sp-group-label mt-5 mb-2">Order summary</div>
                <div v-for="l in orderLines" :key="l.productId"
                    class="d-flex justify-space-between text-body-2 py-1 pl-4">
                    <span>{{ l.name }}<span v-if="l.qty > 1" class="text-medium-emphasis"> × {{ l.qty }}</span></span>
                    <span>{{ priceLabel(l.lineCents) }}</span>
                </div>
                <div v-if="serviceFeeCents > 0" class="d-flex justify-space-between text-body-2 py-1 pl-4">
                    <span class="text-medium-emphasis">Service fee</span>
                    <span class="text-medium-emphasis">{{ priceLabel(serviceFeeCents) }}</span>
                </div>
                <v-divider class="my-2"></v-divider>
                <div class="d-flex justify-space-between">
                    <span class="text-subtitle-1 font-weight-bold">Total</span>
                    <span class="text-subtitle-1 font-weight-bold">{{ priceLabel(estTotalCents) }}</span>
                </div>
                <p class="text-caption text-medium-emphasis mt-1">Final total is shown before you pay.</p>

                <div v-if="errorMessage" class="text-error text-body-2 mt-3">{{ errorMessage }}</div>
                <div class="d-flex align-center ga-2 mt-4">
                    <v-btn variant="text" @click="step = 'select'">Back</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn color="primary" :loading="creating" @click="createIntent">Pay Now</v-btn>
                </div>
            </template>
        </template>

        <!-- ── 3. Payment ────────────────────────────────────────────────── -->
        <template v-else-if="step === 'payment'">
            <div class="d-flex align-center mb-3">
                <h2 class="text-h6 font-weight-bold">Payment</h2>
                <v-spacer></v-spacer>
                <v-btn variant="text" size="small" prepend-icon="mdi-arrow-left" :disabled="paying"
                    @click="step = 'details'">Back</v-btn>
            </div>
            <div v-if="!branding.stripePublishableKey" class="text-error">
                Payments aren't configured for this track yet.
            </div>
            <template v-else>
                <div :id="paymentElementId" class="mb-4"></div>
                <div v-if="paymentError" class="text-error text-body-2 mb-2">{{ paymentError }}</div>
                <v-btn block color="primary" size="large" :loading="paying" :disabled="!stripeReady" @click="pay">
                    Pay {{ priceLabel(chargeCents) }}
                </v-btn>
            </template>
        </template>

        <!-- ── 4. Register each holder (post-payment) ────────────────────── -->
        <template v-else-if="step === 'register'">
            <h2 class="text-h6 font-weight-bold mb-1">Who are these passes for?</h2>
            <p class="text-body-2 text-medium-emphasis mb-4">
                Payment received. Add each holder's details{{ anyWaiver ? ' and waiver signature' : '' }} to
                finish — the gate can't check a pass in until it's registered.
            </p>

            <div v-for="(h, i) in holders" :key="h.purchaseId" class="sp-reg mb-4 pa-3">
                <div class="font-weight-medium mb-1">
                    {{ h.productName }}<span v-if="holders.length > 1"> — pass {{ i + 1 }}</span>
                </div>
                <v-row dense class="pt-2">
                    <v-col cols="6">
                        <v-text-field v-model="h.firstName" label="First name" density="compact" hide-details></v-text-field>
                    </v-col>
                    <v-col cols="6">
                        <v-text-field v-model="h.lastName" label="Last name" density="compact" hide-details></v-text-field>
                    </v-col>
                </v-row>
                <v-text-field v-model="h.birthdate" type="date" label="Date of birth" density="compact"
                    class="mt-4" :max="todayIso" hide-details></v-text-field>

                <div class="text-caption text-medium-emphasis mt-3 mb-1">
                    Photo — the gate checks this against the person at the gate.
                </div>
                <PhotoCapture v-model="h.photoDataUrl" />

                <template v-if="h.requiresWaiver">
                    <div class="text-caption text-medium-emphasis mt-3 mb-1">
                        {{ isMinor(h.birthdate) ? 'Holder is under 18 — a parent/guardian must sign' : 'Signature' }}
                    </div>
                    <v-row v-if="isMinor(h.birthdate)" dense>
                        <v-col cols="6">
                            <v-text-field v-model="h.parentName" label="Parent/guardian name"
                                density="compact" hide-details></v-text-field>
                        </v-col>
                        <v-col cols="6">
                            <v-text-field v-model="h.parentPhone" type="tel" label="Parent/guardian phone"
                                density="compact" hide-details></v-text-field>
                        </v-col>
                    </v-row>
                    <div :class="isMinor(h.birthdate) ? 'mt-3' : ''">
                        <SignaturePad v-model="h.signatureDataUrl" />
                    </div>
                </template>
            </div>

            <div v-if="errorMessage" class="text-error text-body-2 mb-2">{{ errorMessage }}</div>
            <v-btn block color="primary" size="large" :loading="finishing" @click="finish">
                Finish registration
            </v-btn>
        </template>

        <!-- ── 5. Done ───────────────────────────────────────────────────── -->
        <template v-else>
            <div class="text-center py-4">
                <v-icon color="success" size="56">mdi-check-circle</v-icon>
                <h2 class="text-h6 font-weight-bold mt-2 mb-1">You're all set!</h2>
                <p class="text-body-2 text-medium-emphasis mb-4">
                    Your {{ holders.length === 1 ? 'pass is' : 'passes are' }} ready. A confirmation and
                    {{ holders.length === 1 ? 'your QR code' : 'a QR code for each pass' }} are on the way to {{ email }}.
                </p>
                <v-btn v-if="isEmbed && embedCameFromWidget" color="primary" @click="router.back()">Done</v-btn>
                <v-btn v-else-if="!isEmbed" color="primary" to="/User/SeasonPasses">View my passes</v-btn>
            </div>
        </template>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import dayjs from 'dayjs'
import {
    SeasonPassService,
    type SeasonPassProduct,
    type SeasonPassRegistrationItem,
} from '@/services/SeasonPassService'
import { UserService } from '@/services/UserService'
import { branding } from '@/stores/branding'
import authHelper from '@/helpers/AuthHelper'
import { getStripe } from '@/helpers/StripeHelper'
import PhotoCapture from '@/components/PhotoCapture.vue'
import SignaturePad from '@/components/SignaturePad.vue'

const props = defineProps<{ products: SeasonPassProduct[] }>()

const route = useRoute()
const router = useRouter()

// Embed mode: framed on the track's own site, so the flow must not navigate the iframe out.
// "Done" only returns to a widget the visitor actually came from in this same iframe.
const isEmbed = computed(() => !!route.meta.embed)
const embedCameFromWidget: boolean = (() => {
    const back = String(window.history.state?.back ?? '')
    return back.startsWith('/embed/seasonpasses') || back.startsWith('/embed/seasonpass')
})()

const service = new SeasonPassService()
const userService = new UserService()

type Step = 'select' | 'details' | 'payment' | 'register' | 'done'
const step = ref<Step>('select')

// Matches the server's per-line cap (SeasonPassCartItem.Quantity).
const maxPerProduct = 20

const qty = reactive<Record<string, number>>({})
const name = ref('')
const email = ref('')
const couponCode = ref('')
const couponError = ref('')
const giftCardCode = ref('')
const giftCardError = ref('')
const errorMessage = ref('')
const creating = ref(false)

const isAuthed = ref(authHelper.isAuthenticated())
const loginPassword = ref('')
const loginError = ref('')
const loggingIn = ref(false)

const chargeCents = ref(0)
const clientSecret = ref<string | null>(null)
const stripeReady = ref(false)
const paying = ref(false)
const paymentError = ref<string | null>(null)
let stripe: any = null
let elements: any = null

const finishing = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

// Unique per instance so the single-pass and all-passes embeds can't collide on the mount node.
const paymentElementId = `sp-payment-element-${Math.random().toString(36).slice(2, 9)}`
const todayIso = dayjs().format('YYYY-MM-DD')

interface Holder {
    purchaseId: string
    productName: string
    requiresWaiver: boolean
    firstName: string
    lastName: string
    birthdate: string
    photoDataUrl: string | null
    signatureDataUrl: string | null
    parentName: string
    parentPhone: string
}
const holders = ref<Holder[]>([])
const anyWaiver = computed(() => holders.value.some(h => h.requiresWaiver))

const signUpHref = computed(() => `/SignUp?next=${encodeURIComponent(route.fullPath)}`)

function priceLabel(cents: number): string {
    return cents === 0 ? 'Free' : `$${(cents / 100).toFixed(2)}`
}
function bump(p: SeasonPassProduct, delta: number) {
    qty[p.id] = Math.min(maxPerProduct, Math.max(0, (qty[p.id] || 0) + delta))
}
function isMinor(birthdate: string): boolean {
    if (!birthdate) return false
    return dayjs().diff(dayjs(birthdate), 'year') < 18
}
function accessLabel(p: SeasonPassProduct): string {
    if (p.kind === 'days_of_week') return `${daysLabel(p.validDaysOfWeek)} only`
    if (p.kind === 'credits') return `${p.totalCredits} ride credits`
    return 'Unlimited rides'
}
function daysLabel(days: number[] | null): string {
    if (!days || days.length === 0) return ''
    const names = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
    return days.slice().sort((a, b) => a - b).map(d => names[d]).join('/')
}
function validLabel(p: SeasonPassProduct): string {
    return `Valid ${dayjs(p.validFromDate).format('MMM D, YYYY')} to ${dayjs(p.validToDate).format('MMM D, YYYY')}`
}

const hasSelection = computed(() => props.products.some(p => (qty[p.id] || 0) > 0))
const subtotalCents = computed(() =>
    props.products.reduce((sum, p) => sum + p.priceCents * (qty[p.id] || 0), 0))

// Pre-pay estimate only — the server recomputes and returns the exact charge. Mirrors the
// server's per-product split: the tenant's service charge, of which each product passes on
// its own rider-paid share.
const serviceFeeCents = computed(() =>
    props.products.reduce((sum, p) => {
        const q = qty[p.id] || 0
        if (q === 0) return sum
        const charge = Math.floor(p.priceCents * (branding.serviceChargeBps ?? 0) / 10000)
        return sum + Math.floor(charge * (p.riderPaidServiceChargeBps ?? 10000) / 10000) * q
    }, 0))
const estTotalCents = computed(() => subtotalCents.value + serviceFeeCents.value)

const orderLines = computed(() =>
    props.products
        .filter(p => (qty[p.id] || 0) > 0)
        .map(p => ({
            productId: p.id,
            name: p.name,
            qty: qty[p.id],
            lineCents: p.priceCents * qty[p.id],
        })))

onMounted(async () => {
    if (isAuthed.value) await loadProfile()
})

async function loadProfile() {
    try {
        const r = await userService.getProfile()
        const p: any = (r.data as any).data ?? r.data
        name.value = `${p.firstName ?? ''} ${p.lastName ?? ''}`.trim()
        email.value = p.email ?? ''
    } catch {
        // Leave blank; the fields are read-only display and the server uses the token's account.
    }
}

// Shared-device case: drop the session (keeping the cart) so the next person logs in as
// themselves rather than buying a pass onto someone else's account.
function switchUser() {
    authHelper.logout()
    isAuthed.value = false
    name.value = ''
    email.value = ''
    loginPassword.value = ''
}

async function doLogin() {
    loginError.value = ''
    loggingIn.value = true
    try {
        const r = await userService.login({ email: email.value.trim(), password: loginPassword.value })
        const d: any = (r.data as any).data ?? r.data
        authHelper.setToken(d.token)
        authHelper.setUserId(d.userId)
        authHelper.setRole(d.role)
        isAuthed.value = true
        name.value = `${d.firstName ?? ''} ${d.lastName ?? ''}`.trim() || name.value
        email.value = d.email ?? email.value
        loginPassword.value = ''
        flash('Logged in successfully.', 'success')
    } catch (err: any) {
        loginError.value = err.response?.data?.error || 'Could not log in. Check your password and try again.'
    } finally {
        loggingIn.value = false
    }
}

async function createIntent() {
    errorMessage.value = ''
    couponError.value = ''
    giftCardError.value = ''
    creating.value = true
    try {
        const items = props.products
            .filter(p => (qty[p.id] || 0) > 0)
            .map(p => ({ productId: p.id, quantity: qty[p.id] }))
        const r = await service.buy(items,
            couponCode.value.trim() || null,
            giftCardCode.value.trim() || null)
        const data = (r.data as any).data
        chargeCents.value = data.amountCents
        buildHolders(data.passes)

        // A gift card or coupon can cover the order outright, in which case the server marks the
        // passes paid and returns no client secret — skip payment, but still register the holders.
        if (!data.clientSecret) {
            step.value = 'register'
            return
        }
        clientSecret.value = data.clientSecret
        step.value = 'payment'
        await nextTick()
        await mountPaymentElement()
    } catch (err: any) {
        const message = err.response?.data?.error as string | undefined
        if (message && /coupon|promo/i.test(message)) couponError.value = message
        else if (message && /gift card/i.test(message)) giftCardError.value = message
        else errorMessage.value = message || 'Could not start checkout. Please try again.'
    } finally {
        creating.value = false
    }
}

// One registration card per pass the server created, prefilling the first with the buyer's
// name (they're usually one of the holders).
function buildHolders(passes: Array<{
    purchaseId: string; productName: string; requiresWaiver: boolean
}>) {
    holders.value = passes.map(p => ({
        purchaseId: p.purchaseId,
        productName: p.productName,
        requiresWaiver: p.requiresWaiver,
        firstName: '', lastName: '', birthdate: '',
        photoDataUrl: null, signatureDataUrl: null,
        parentName: '', parentPhone: '',
    }))
    const parts = name.value.trim().split(/\s+/)
    if (holders.value.length > 0 && parts[0]) {
        holders.value[0].firstName = parts[0]
        holders.value[0].lastName = parts.slice(1).join(' ')
    }
}

async function mountPaymentElement() {
    if (!clientSecret.value) return
    // Direct-charge tenants confirm on their own connected account; platform tenants pass none.
    const stripeAccount = branding.stripeChargeMode === 'direct' ? branding.stripeConnectAccountId : null
    stripe = await getStripe(branding.stripePublishableKey, stripeAccount)
    if (!stripe) { paymentError.value = 'Payments are unavailable right now. Please try again shortly.'; return }
    elements = stripe.elements({ clientSecret: clientSecret.value })
    elements.create('payment').mount(`#${paymentElementId}`)
    stripeReady.value = true
}

async function pay() {
    if (!stripe || !elements) return
    paying.value = true
    paymentError.value = null
    try {
        const { error, paymentIntent } = await stripe.confirmPayment({
            elements,
            // Redirect-based methods come back to the rider's pass list, where unregistered
            // passes are flagged so they can finish there. Returning to this component would
            // remount it blank with the cart gone.
            confirmParams: { return_url: `${window.location.origin}/User/SeasonPasses` },
            redirect: 'if_required',
        })
        if (error) {
            paymentError.value = error.message || 'Payment failed. Please check your card details and try again.'
        } else if (paymentIntent?.status === 'succeeded') {
            // Finalize server-side now (idempotent, re-verified with Stripe) so the passes are
            // 'paid' before registration writes to them; the webhook is the backup.
            try { await service.confirmIntent(paymentIntent.id) } catch { /* webhook/reconciler finalizes */ }
            step.value = 'register'
        } else {
            paymentError.value = paymentIntent?.status === 'processing'
                ? "Your payment is processing. We'll email your confirmation as soon as it clears."
                : "We couldn't confirm your payment. If you were charged, your confirmation will arrive by email."
        }
    } catch (err: any) {
        paymentError.value = err?.message || 'Payment failed. Please try again.'
    } finally {
        paying.value = false
    }
}

async function finish() {
    errorMessage.value = ''
    const payload: SeasonPassRegistrationItem[] = []

    for (let i = 0; i < holders.value.length; i++) {
        const h = holders.value[i]
        const who = h.firstName.trim() || `Pass ${i + 1}`
        if (!h.firstName.trim() || !h.lastName.trim()) {
            errorMessage.value = `Pass ${i + 1} needs the holder's first and last name.`; return
        }
        if (!h.photoDataUrl) {
            errorMessage.value = `${who} needs a photo — the gate uses it to verify the pass holder.`; return
        }
        if (h.requiresWaiver) {
            if (!h.birthdate) {
                errorMessage.value = `${who} needs a date of birth to sign the waiver.`; return
            }
            if (!h.signatureDataUrl) {
                errorMessage.value = `${who} needs to sign the waiver.`; return
            }
            if (isMinor(h.birthdate) && !h.parentName.trim()) {
                errorMessage.value = `A parent/guardian name is required for ${who}.`; return
            }
        }
        payload.push({
            purchaseId: h.purchaseId,
            firstName: h.firstName.trim(),
            lastName: h.lastName.trim(),
            birthdate: h.birthdate || null,
            photoDataUrl: h.photoDataUrl,
            waiverSignatureDataUrl: h.requiresWaiver ? h.signatureDataUrl : null,
            parentGuardianName: h.requiresWaiver && isMinor(h.birthdate) ? h.parentName.trim() : null,
            parentGuardianPhone: h.requiresWaiver && isMinor(h.birthdate) ? h.parentPhone.trim() || null : null,
        })
    }

    finishing.value = true
    try {
        await service.completeRegistration(payload)
        step.value = 'done'
    } catch (err: any) {
        errorMessage.value = err.response?.data?.error
            || 'Could not save the registration. Your passes are paid for — try again, or finish from My Season Passes.'
    } finally {
        finishing.value = false
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>

<style scoped>
.sp-line {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 10px 0;
    border-bottom: 1px solid rgba(var(--v-theme-on-surface), 0.06);
}
.sp-line:last-of-type { border-bottom: none; }
/* on-surface rather than a hardcoded black so dark-theme tenants keep readable labels. */
.sp-group-label {
    font-size: 13px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: rgba(var(--v-theme-on-surface), 0.6);
}
.sp-qty {
    min-width: 26px;
    text-align: center;
    font-variant-numeric: tabular-nums;
}
.sp-login,
.sp-reg {
    /* Surface (white on light themes) with a hairline border so these nested panels stay
       distinct against the light-gray selection card that now wraps the checkout. */
    background: rgb(var(--v-theme-surface));
    border: 1px solid rgba(var(--v-theme-on-surface), 0.1);
    border-radius: 8px;
}
</style>
