<template>
    <div class="pkg-page">
        <v-container v-if="loadError" class="py-6"><v-alert type="error" variant="tonal">{{ loadError }}</v-alert></v-container>
        <v-container v-else-if="notFound" class="py-10 text-center">
            <v-icon icon="mdi-package-variant" size="44" class="text-medium-emphasis mb-3" />
            <h1 class="text-h5 font-weight-bold mb-2">This package isn't available</h1>
            <p class="text-body-2 text-medium-emphasis">It may have been unpublished or the link is out of date.</p>
        </v-container>
        <v-container v-else-if="loading" class="py-6"><v-skeleton-loader type="image, article, actions" /></v-container>

        <template v-else-if="pkg">
            <v-alert v-if="!pkg.landingPublished" type="warning" variant="flat" density="compact" class="text-center" rounded="0">
                Draft: riders can't see this page yet. Publish it from Admin &gt; Packages.
            </v-alert>

            <!-- Hero (hosted only) -->
            <section v-if="!isEmbed" class="pkg-hero" :style="heroStyle">
                <div class="pkg-hero-overlay">
                    <v-container class="pkg-hero-inner">
                        <h1 class="pkg-title font-display text-white">{{ pkg.name }}</h1>
                        <p v-if="pkg.summary" class="pkg-hero-sub text-white">{{ pkg.summary }}</p>
                        <v-btn color="primary" size="large" class="mt-4" @click="scrollToBook">Book now</v-btn>
                    </v-container>
                </div>
            </section>
            <v-container v-else class="pt-3 pb-0">
                <h1 class="text-h6 font-weight-bold font-display">{{ pkg.name }}</h1>
                <div v-if="pkg.summary" class="text-caption text-medium-emphasis">{{ pkg.summary }}</div>
            </v-container>

            <v-container :class="isEmbed ? 'py-4' : 'py-8'">
                <!-- Item selection (the booking card) sits on the RIGHT, marketing info on
                     the LEFT. In embed the mobile stack leads with the booking card (order 1
                     below md); md+ restores info-left / selection-right (order-md). -->
                <v-row>
                    <!-- Marketing + what's included (info, left) -->
                    <v-col cols="12" md="7" :order="isEmbed ? 2 : undefined" :order-md="isEmbed ? 1 : undefined">
                        <div v-if="pkg.description" class="pkg-body mb-6" v-html="pkg.description"></div>
                        <h2 class="text-h5 font-weight-bold font-display mb-3">What's included</h2>
                        <ul class="pkg-checklist mb-6">
                            <li v-if="pkg.includesDayTicket"><v-icon icon="mdi-ticket-confirmation" size="18" class="pkg-check" /><span>Day lift ticket</span></li>
                            <li v-if="pkg.coachingMinutes"><v-icon icon="mdi-whistle" size="18" class="pkg-check" /><span>{{ pkg.coachingMinutes }}-minute {{ pkg.coachingLabel || 'coached session' }}</span></li>
                            <li v-for="it in pkg.items" :key="it.id">
                                <v-icon :icon="it.itemType === 'bike' ? 'mdi-bike' : 'mdi-shield-outline'" size="18" class="pkg-check" />
                                <span>{{ it.name }}<span v-if="it.variantLabel" class="text-medium-emphasis"> ({{ it.variantLabel }})</span></span>
                            </li>
                        </ul>
                    </v-col>

                    <!-- Booking -->
                    <v-col cols="12" md="5" :order="isEmbed ? 1 : undefined">
                        <v-card ref="bookCard" id="pkg-book" class="pkg-book-card" variant="flat">
                            <v-card-text class="pa-5">
                                <div class="text-subtitle-1 font-weight-bold mb-3">Book your day</div>

                                <div class="text-caption text-medium-emphasis mb-1">Option</div>
                                <v-btn-toggle v-model="tierId" mandatory divided density="comfortable" class="d-flex flex-wrap mb-4" @update:model-value="refreshAvailability">
                                    <v-btn v-for="t in pkg.tiers" :key="t.id" :value="t.id" size="small" class="flex-grow-1">
                                        <div class="d-flex flex-column">
                                            <span>{{ t.name }}</span>
                                            <span class="text-caption">{{ money(t.priceCents) }}</span>
                                        </div>
                                    </v-btn>
                                </v-btn-toggle>

                                <div class="text-caption text-medium-emphasis mb-1">Date</div>
                                <input type="date" v-model="rideDate" :min="today" class="pkg-date mb-4" @change="refreshAvailability" />

                                <template v-if="bikeSizeItem">
                                    <div class="text-caption text-medium-emphasis mb-1">Bike size</div>
                                    <v-select v-model="bikeVariantId" :items="bikeSizeItem.sizeOptions" item-title="label" item-value="variantId"
                                        density="compact" hide-details class="mb-4" />
                                </template>

                                <template v-if="pkg.coachingMinutes">
                                    <div class="text-caption text-medium-emphasis mb-1">Session time</div>
                                    <v-select v-model="slotId" :items="sessionItems" item-title="title" item-value="slotId"
                                        density="compact" hide-details :disabled="sessionItems.length === 0"
                                        :placeholder="availLoading ? 'Checking...' : 'Select a time'" class="mb-4" />
                                </template>

                                <v-alert v-if="avail && !avail.available" type="info" variant="tonal" density="compact" class="mb-3">
                                    {{ avail.reason || 'Not available for these choices.' }}
                                </v-alert>

                                <div v-if="avail?.available" class="pkg-price-row mb-2">
                                    <span class="text-h5 font-weight-bold">{{ money(insurance && avail ? avail.priceCents + avail.insuranceCents : avail.priceCents) }}</span>
                                    <span v-if="!insurance && avail.depositCents > 0" class="text-caption text-medium-emphasis">
                                        + {{ money(avail.depositCents) }} refundable deposit hold
                                    </span>
                                </div>

                                <div v-if="pkg.insuranceOffered" class="mb-3">
                                    <v-checkbox v-model="insurance" :label="insuranceLabel" hide-details density="compact" class="mt-0"></v-checkbox>
                                    <div v-if="insurance" class="text-caption text-medium-emphasis">The refundable deposit is waived.</div>
                                </div>

                                <v-alert v-if="bookError" type="error" variant="tonal" density="compact" class="mb-3">{{ bookError }}</v-alert>

                                <v-btn color="primary" size="large" block :loading="booking"
                                    :disabled="!canBook" @click="startBooking">Reserve</v-btn>
                                <p class="text-caption text-medium-emphasis mt-3 mb-0">
                                    Sign the waiver and collect your bike and gear at the shop. Deposit released at return.
                                </p>
                            </v-card-text>
                        </v-card>
                    </v-col>
                </v-row>

                <div v-if="isEmbed" class="text-center mt-4">
                    <a class="rp-powered" href="https://ridepass.io" target="_blank" rel="noopener">Powered by <strong>RidePass</strong></a>
                </div>
            </v-container>
        </template>

        <!-- Payment (fee then deposit hold) -->
        <v-dialog v-model="payOpen" persistent max-width="480">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ payStage === 'fee' ? 'Payment' : 'Deposit hold' }}</span>
                    <v-spacer /><v-btn icon="mdi-close" variant="text" size="small" :disabled="paying" @click="payOpen = false" />
                </v-card-title>
                <v-card-text>
                    <div v-if="payStage === 'fee'" class="text-h6 mb-1">{{ money(pendingTotal) }}</div>
                    <div v-else class="mb-2">
                        <div class="text-h6">{{ money(pendingDeposit) }}</div>
                        <div class="text-caption text-medium-emphasis">Refundable hold, released when you return the gear.</div>
                    </div>
                    <div id="pkg-payment-element" class="mb-4"></div>
                    <div v-if="payError" class="text-error text-body-2 mb-2">{{ payError }}</div>
                    <v-btn block color="primary" size="large" :loading="paying" :disabled="!stripeReady" @click="payCurrent">
                        {{ payStage === 'fee' ? `Pay ${money(pendingTotal)}` : `Authorize ${money(pendingDeposit)} hold` }}
                    </v-btn>
                </v-card-text>
            </v-card>
        </v-dialog>

        <v-dialog v-model="doneOpen" max-width="440">
            <v-card class="text-center pa-4">
                <v-icon size="56" color="success" class="mx-auto">mdi-check-circle</v-icon>
                <div class="text-h6 mt-2">You're booked!</div>
                <p class="text-body-2 text-medium-emphasis mt-2">
                    We've emailed your confirmation. Sign the waiver and collect your gear at the shop on {{ formatDay(rideDate) }}.
                </p>
                <v-btn color="primary" class="mt-2" @click="doneOpen = false">Done</v-btn>
            </v-card>
        </v-dialog>

        <InlineAuthDialog v-model="authOpen" @authed="onAuthed" />
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import authHelper from '@/helpers/AuthHelper'
import InlineAuthDialog from '@/components/InlineAuthDialog.vue'
import { getStripe } from '@/helpers/StripeHelper'
import { branding, loadBranding } from '@/stores/branding'
import { PackageService, type PackageProduct, type PackageAvailability } from '@/services/PackageService'
import { absoluteUrl } from '@/helpers/ImageUrl'

const route = useRoute()
const service = new PackageService()
const isEmbed = computed(() => !!route.meta.embed)
const slugOrId = computed(() => String(route.params.slug ?? route.params.id ?? ''))

const pkg = ref<PackageProduct | null>(null)
const loading = ref(true)
const loadError = ref('')
const notFound = ref(false)

const tz = () => branding.timezone || 'UTC'
const today = dayjs().tz(tz()).format('YYYY-MM-DD')
const rideDate = ref(dayjs().tz(tz()).add(1, 'day').format('YYYY-MM-DD'))
const tierId = ref<string | null>(null)
const slotId = ref<string | null>(null)
const bikeVariantId = ref<string | null>(null)
const insurance = ref(false)

const avail = ref<PackageAvailability | null>(null)
const availLoading = ref(false)

function money(c: number) { return '$' + (c / 100).toFixed(2) }
function formatDay(d: string) { return dayjs(d).format('ddd, MMM D') }

const heroStyle = computed(() => pkg.value?.heroImageUrl
    ? { backgroundImage: `url(${absoluteUrl(pkg.value.heroImageUrl)})` } : {})

// The bike item (if any) that has rider-selectable sizes; drives the "Bike size" picker.
const bikeSizeItem = computed(() => pkg.value?.items.find(it => it.itemType === 'bike' && it.sizeOptions.length > 0) ?? null)

const insuranceLabel = computed(() => {
    const base = pkg.value?.insuranceLabel || 'Damage Protection'
    return avail.value && avail.value.insuranceCents > 0 ? `${base} (+${money(avail.value.insuranceCents)})` : base
})

const sessionItems = computed(() => (avail.value?.sessions ?? []).map(s => ({
    slotId: s.slotId,
    title: `${dayjs('2000-01-01T' + s.startTime).format('h:mm A')}  (${s.remaining} left)`,
})))

const canBook = computed(() => !!tierId.value && !!avail.value?.available
    && (!pkg.value?.coachingMinutes || !!slotId.value))

async function load() {
    loading.value = true
    loadError.value = ''
    try {
        const r = await service.getLanding(slugOrId.value)
        pkg.value = r.data.data
        tierId.value = pkg.value.tiers[0]?.id ?? null
        bikeVariantId.value = bikeSizeItem.value?.sizeOptions[0]?.variantId ?? null
        await refreshAvailability()
    } catch (err: any) {
        if (err.response?.status === 404) notFound.value = true
        else loadError.value = err.response?.data?.error || 'Could not load this package. Refresh to try again.'
    } finally {
        loading.value = false
    }
}

let availSeq = 0
async function refreshAvailability() {
    if (!pkg.value || !tierId.value || !rideDate.value) return
    const seq = ++availSeq
    availLoading.value = true
    slotId.value = null
    try {
        const dateIso = dayjs.tz(rideDate.value, tz()).startOf('day').toISOString()
        const r = await service.availability(pkg.value.id, dateIso, tierId.value)
        if (seq !== availSeq) return
        avail.value = r.data.data
        // Auto-pick the only session, or leave the picker for the rider.
        if (avail.value.sessions.length === 1) slotId.value = avail.value.sessions[0].slotId
    } catch (err: any) {
        if (seq !== availSeq) return
        avail.value = { available: false, reason: err.response?.data?.error || 'Could not check availability.', priceCents: 0, depositCents: 0, insuranceCents: 0, sessions: [] }
    } finally {
        if (seq === availSeq) availLoading.value = false
    }
}

// ── Booking + payment ────────────────────────────────────────────────────────
const booking = ref(false)
const bookError = ref('')
const authOpen = ref(false)
const payOpen = ref(false)
const payStage = ref<'fee' | 'deposit'>('fee')
const paying = ref(false)
const payError = ref('')
const stripeReady = ref(false)
const pendingTotal = ref(0)
const pendingDeposit = ref(0)
const feeSecret = ref<string | null>(null)
const depositSecret = ref<string | null>(null)
let stripe: any = null
let elements: any = null
const doneOpen = ref(false)

function scrollToBook() {
    document.getElementById('pkg-book')?.scrollIntoView({ behavior: 'smooth' })
}

function startBooking() {
    if (!canBook.value) return
    if (!authHelper.isAuthenticated()) { authOpen.value = true; return }
    doBook()
}
function onAuthed() { doBook() }

async function doBook() {
    if (!pkg.value || !tierId.value) return
    booking.value = true
    bookError.value = ''
    try {
        const r = await service.book({
            packageId: pkg.value.id,
            tierId: tierId.value,
            rideDate: dayjs.tz(rideDate.value, tz()).startOf('day').toISOString(),
            slotId: slotId.value,
            bikeVariantId: bikeSizeItem.value ? bikeVariantId.value : null,
            insurance: insurance.value,
        })
        const d = r.data.data
        pendingTotal.value = d.totalCents
        pendingDeposit.value = d.depositCents
        feeSecret.value = d.clientSecret
        depositSecret.value = d.depositClientSecret
        payStage.value = 'fee'
        payOpen.value = true
        await nextTick()
        await mountPayment(feeSecret.value)
    } catch (err: any) {
        bookError.value = err.response?.data?.error || 'Could not reserve. Nothing was charged.'
    } finally {
        booking.value = false
    }
}

async function mountPayment(secret: string | null) {
    payError.value = ''
    stripeReady.value = false
    if (!secret) { payError.value = 'Payment could not be started.'; return }
    const account = branding.stripeChargeMode === 'direct' ? branding.stripeConnectAccountId : null
    stripe = await getStripe(branding.stripePublishableKey, account)
    if (!stripe) { payError.value = 'Payments are unavailable right now.'; return }
    const host = document.getElementById('pkg-payment-element')
    if (host) host.innerHTML = ''
    elements = stripe.elements({ clientSecret: secret })
    elements.create('payment').mount('#pkg-payment-element')
    stripeReady.value = true
}

async function payCurrent() {
    if (!stripe || !elements) return
    paying.value = true
    payError.value = ''
    try {
        const { error, paymentIntent } = await stripe.confirmPayment({ elements, redirect: 'if_required' })
        if (error) {
            payError.value = error.message || 'Payment failed. Check the card and try again.'
        } else if (payStage.value === 'fee') {
            if (paymentIntent?.status === 'succeeded') {
                if (depositSecret.value && pendingDeposit.value > 0) {
                    payStage.value = 'deposit'
                    await nextTick()
                    await mountPayment(depositSecret.value)
                } else { finish() }
            } else {
                payError.value = 'The payment has not settled yet. It will complete shortly; watch your email.'
            }
        } else {
            if (paymentIntent?.status === 'requires_capture' || paymentIntent?.status === 'succeeded') finish()
            else payError.value = 'The deposit hold did not authorize. Try a different card.'
        }
    } catch (e: any) {
        payError.value = e?.message || 'Payment failed. Please try again.'
    } finally {
        paying.value = false
    }
}

function finish() {
    payOpen.value = false
    doneOpen.value = true
    refreshAvailability()
}

onMounted(async () => {
    if (!branding.loaded) await loadBranding()
    await load()
})
</script>

<style scoped>
/* Plain theme background (no gray tint) so the widget blends into the host site. */
.pkg-page { min-height: 60vh; background: rgb(var(--v-theme-background)); }
.pkg-hero { min-height: 340px; background-size: cover; background-position: center; background-color: #1d1d1d; }
.pkg-hero-overlay { min-height: 340px; display: flex; align-items: flex-end; background: linear-gradient(to top, rgba(0,0,0,0.6), rgba(0,0,0,0.1)); }
.pkg-hero-inner { padding-bottom: 32px; }
.pkg-title { font-size: 2.4rem; font-weight: 800; }
.pkg-hero-sub { font-size: 1.05rem; opacity: 0.95; }
.pkg-body { font-size: 15px; line-height: 1.6; }
.pkg-checklist { list-style: none; padding: 0; }
.pkg-checklist li { display: flex; align-items: center; gap: 8px; padding: 4px 0; }
.pkg-check { color: rgb(var(--v-theme-primary)); }
/* Booking card = the item-selection container: a very light gray tint on light themes and a
   slightly-lighter-than-background tint on dark themes (on-surface alpha inverts per theme),
   lifted with a soft drop shadow. !important beats Vuetify's flat variant surface + no-elevation. */
.pkg-book-card {
    /* Hairline border (0.2 on-surface = #cccccc on a white page) instead of a drop shadow,
       which looked harsh against the white page. */
    border: 1px solid rgba(var(--v-theme-on-surface), 0.2);
    border-radius: 12px;
    background-color: rgba(var(--v-theme-on-surface), 0.05) !important;
}
.pkg-date { border: 1px solid rgba(var(--v-theme-on-surface), 0.3); border-radius: 6px; padding: 8px 10px; font-size: 14px; width: 100%; background: rgb(var(--v-theme-surface)); color: rgb(var(--v-theme-on-surface)); }
.pkg-price-row { display: flex; align-items: baseline; gap: 10px; flex-wrap: wrap; }
.rp-powered { color: rgba(var(--v-theme-on-surface), 0.6); text-decoration: none; font-size: 13px; }
</style>
