<template>
    <!-- Public operator-acquisition marketing page, served on the apex domain.
         Sells RidePass to prospective track operators: feature highlights for
         track management + point of sale, then a lead form that notifies every
         super admin. Product screenshots are placeholders for now. -->
    <div class="fortracks-page">
        <!-- ── HERO ──────────────────────────────────────────────────────── -->
        <section class="ft-hero">
            <div class="ft-hero-overlay">
                <v-container>
                    <v-row justify="center" class="text-center">
                        <v-col cols="12" md="9" lg="8">
                            <div class="ft-hero-eyebrow mb-3">{{ heroEyebrow }}</div>
                            <h1 class="text-h2 font-weight-bold text-white mb-4 ft-hero-headline">
                                {{ heroHeadline }}
                            </h1>
                            <p class="text-h6 text-white mb-8 mx-auto" style="max-width: 640px; opacity: 0.92">
                                {{ heroSubhead }}
                            </p>
                            <div class="d-flex flex-wrap ga-3 justify-center">
                                <v-btn color="primary" size="x-large" @click="scrollToForm">
                                    Request a demo
                                </v-btn>
                                <v-btn variant="outlined" color="white" size="x-large" @click="scrollToFeatures">
                                    See what it does
                                </v-btn>
                            </div>
                        </v-col>
                    </v-row>
                </v-container>
            </div>
        </section>

        <v-container>
            <!-- ── VALUE STRIP ───────────────────────────────────────────── -->
            <section class="my-12">
                <v-row dense>
                    <v-col v-for="v in valueProps" :key="v.title" cols="12" sm="4">
                        <div class="text-center pa-4">
                            <v-icon :icon="v.icon" size="40" color="primary" class="mb-3"></v-icon>
                            <div class="text-h6 font-weight-bold mb-1">{{ v.title }}</div>
                            <div class="text-body-2 text-medium-emphasis">{{ v.text }}</div>
                        </div>
                    </v-col>
                </v-row>
            </section>

            <!-- ── WHY TRACKS LOVE RIDEPASS (benefits band, super-admin editable;
                    moved here from the apex home) ──────────────────────────── -->
            <section v-if="benefitsHtml" class="my-12">
                <div class="text-center mb-8">
                    <h2 class="text-h4 font-weight-bold">{{ benefitsTitle }}</h2>
                </div>
                <v-row align="center">
                    <v-col v-if="benefitsImageUrl" cols="12" md="6">
                        <div class="ft-benefits-photo" :style="{ backgroundImage: `url(${benefitsImageUrl})` }"></div>
                    </v-col>
                    <v-col cols="12" :md="benefitsImageUrl ? 6 : 12">
                        <div class="ft-benefits-list" v-html="benefitsHtml"></div>
                    </v-col>
                </v-row>
            </section>

            <!-- ── TRACK MANAGEMENT ──────────────────────────────────────── -->
            <section ref="featuresSection" class="my-12">
                <div class="text-center mb-8">
                    <div class="ft-section-eyebrow">Track management</div>
                    <h2 class="text-h4 font-weight-bold">Everything to run the day</h2>
                </div>
                <v-row align="center">
                    <v-col cols="12" md="6">
                        <div class="ft-shot">
                            <v-icon icon="mdi-image-outline" size="48" class="mb-2"></v-icon>
                            <div class="text-body-2">Screenshot: events calendar &amp; check-in</div>
                        </div>
                    </v-col>
                    <v-col cols="12" md="6">
                        <v-list class="bg-transparent">
                            <v-list-item v-for="f in trackManagementFeatures" :key="f.title" class="px-0">
                                <template #prepend>
                                    <v-icon :icon="f.icon" color="primary" class="mr-3"></v-icon>
                                </template>
                                <v-list-item-title class="font-weight-medium">{{ f.title }}</v-list-item-title>
                                <v-list-item-subtitle class="ft-feature-sub">{{ f.text }}</v-list-item-subtitle>
                            </v-list-item>
                        </v-list>
                    </v-col>
                </v-row>
            </section>

            <!-- ── POINT OF SALE ─────────────────────────────────────────── -->
            <section class="my-12">
                <div class="text-center mb-8">
                    <div class="ft-section-eyebrow">Point of sale</div>
                    <h2 class="text-h4 font-weight-bold">Sell anywhere, get paid fast</h2>
                </div>
                <v-row align="center">
                    <!-- Image second on desktop (order-md-last) so the two bands alternate. -->
                    <v-col cols="12" md="6" class="order-md-last">
                        <div class="ft-shot">
                            <v-icon icon="mdi-image-outline" size="48" class="mb-2"></v-icon>
                            <div class="text-body-2">Screenshot: counter sale &amp; checkout</div>
                        </div>
                    </v-col>
                    <v-col cols="12" md="6">
                        <v-list class="bg-transparent">
                            <v-list-item v-for="f in pointOfSaleFeatures" :key="f.title" class="px-0">
                                <template #prepend>
                                    <v-icon :icon="f.icon" color="primary" class="mr-3"></v-icon>
                                </template>
                                <v-list-item-title class="font-weight-medium">{{ f.title }}</v-list-item-title>
                                <v-list-item-subtitle class="ft-feature-sub">{{ f.text }}</v-list-item-subtitle>
                            </v-list-item>
                        </v-list>
                    </v-col>
                </v-row>
            </section>

            <!-- ── PRICING ───────────────────────────────────────────────── -->
            <section class="my-12">
                <div class="text-center mb-8">
                    <div class="ft-section-eyebrow">Pricing</div>
                    <h2 class="text-h4 font-weight-bold">Pricing that scales with you</h2>
                </div>
                <v-row>
                    <v-col v-for="p in pricingPoints" :key="p.title" cols="12" sm="6" md="3">
                        <v-card variant="tonal" class="h-100 pa-6 text-center">
                            <v-icon :icon="p.icon" size="40" color="primary" class="mb-3"></v-icon>
                            <div class="text-h6 font-weight-bold mb-2">{{ p.title }}</div>
                            <div class="text-body-2 text-medium-emphasis">{{ p.text }}</div>
                        </v-card>
                    </v-col>
                </v-row>
                <div class="text-center mt-8">
                    <p class="text-body-1 text-medium-emphasis mb-3">
                        Want the exact numbers for your track?
                    </p>
                    <v-btn color="primary" size="large" @click="scrollToForm">Talk pricing</v-btn>
                </div>
            </section>

            <!-- ── HOW IT WORKS ──────────────────────────────────────────── -->
            <section class="my-12">
                <div class="text-center mb-8">
                    <h2 class="text-h4 font-weight-bold">Up and running in three steps</h2>
                </div>
                <v-row>
                    <v-col v-for="(s, i) in steps" :key="s.title" cols="12" md="4">
                        <div class="text-center pa-2">
                            <div class="ft-step-num mx-auto mb-3">{{ i + 1 }}</div>
                            <div class="text-h6 font-weight-bold mb-2">{{ s.title }}</div>
                            <div class="text-body-2 text-medium-emphasis">{{ s.text }}</div>
                        </div>
                    </v-col>
                </v-row>
            </section>
        </v-container>

        <!-- ── LEAD FORM ─────────────────────────────────────────────────── -->
        <section ref="formSection" class="ft-form-band py-12">
            <v-container>
                <v-row justify="center">
                    <v-col cols="12" md="8" lg="6">
                        <div class="text-center mb-6">
                            <h2 class="text-h4 font-weight-bold text-white mb-2">Bring your track online</h2>
                            <p class="text-body-1 text-white" style="opacity: 0.9">
                                Tell us about your track and we'll reach out to get you set up.
                            </p>
                        </div>

                        <v-card v-if="!submitted" class="pa-6">
                            <v-row>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="form.contactName" label="Your name"
                                        density="compact" maxlength="120"></v-text-field>
                                </v-col>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="form.trackName" label="Track name"
                                        density="compact" maxlength="160"></v-text-field>
                                </v-col>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="form.email" type="email" label="Email"
                                        density="compact" maxlength="200"></v-text-field>
                                </v-col>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="form.phone" type="tel" label="Phone (optional)"
                                        density="compact" maxlength="40"></v-text-field>
                                </v-col>
                            </v-row>

                            <v-textarea v-model="form.message" label="Tell us about your track (optional)"
                                class="mt-4" rows="4" auto-grow counter maxlength="4000"
                                hint="Location, what you run (practice, races, rentals), and anything else."
                                persistent-hint></v-textarea>

                            <div v-if="errorMessage" class="text-error text-caption mt-2">{{ errorMessage }}</div>

                            <div class="d-flex mt-4">
                                <v-spacer></v-spacer>
                                <v-btn color="primary" size="large" :loading="submitting"
                                    :disabled="!canSubmit" @click="submit">
                                    Request a demo
                                </v-btn>
                            </div>
                        </v-card>

                        <v-card v-else class="pa-8 text-center">
                            <v-icon size="56" color="success" class="mb-3">mdi-check-circle</v-icon>
                            <h3 class="text-h5 mb-2">Thanks, we got it!</h3>
                            <p class="text-body-2 text-medium-emphasis mb-4">
                                We'll reach out to {{ form.email }} shortly to talk about getting
                                {{ form.trackName || 'your track' }} set up on RidePass.
                            </p>
                            <v-btn variant="text" @click="resetForm">Submit another track</v-btn>
                        </v-card>
                    </v-col>
                </v-row>
            </v-container>
        </section>
    </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import DOMPurify from 'dompurify'
import { TrackLeadService } from '@/services/TrackLeadService'
import { platformBranding, platformImageUrl } from '@/stores/platformBranding'

const trackLeadService = new TrackLeadService()

// Hero copy + the "Why Tracks love RidePass" benefits block are super-admin editable
// (Super Admin -> For Tracks). Fall back to the original hardcoded hero copy when a
// field hasn't been set. The benefits block reuses the platform benefits content that
// used to render on the apex home page.
const pb = computed(() => platformBranding.data)
const heroEyebrow = computed(() => pb.value?.forTracksHeroEyebrow?.trim() || 'RidePass for track operators')
const heroHeadline = computed(() => pb.value?.forTracksHeroHeadline?.trim() || 'Run your track on one platform')
const heroSubhead = computed(() => pb.value?.forTracksHeroSubhead?.trim()
    || 'From the front gate to the finish line, RidePass handles your events, passes, and payments so you can spend less time at the computer and more time on the track.')
const benefitsTitle = computed(() => pb.value?.sectionBenefitsTitle?.trim() || 'Why Tracks love RidePass')
const benefitsHtml = computed(() => DOMPurify.sanitize(pb.value?.benefitsHtml ?? ''))
const benefitsImageUrl = computed(() => platformImageUrl(pb.value?.benefitsImageUrl))

const valueProps = [
    { icon: 'mdi-palette', title: 'Your own branded site', text: 'Your logo, your colors, and your own web address. Riders get a site that looks like your track, not ours.' },
    { icon: 'mdi-cash-multiple', title: 'Sell more', text: 'Take passes, tickets, and race entries online and at the gate with tap-to-pay.' },
    { icon: 'mdi-cellphone-message', title: 'Stay connected', text: 'See who has checked in at a glance, and text riders with one-off or scheduled messages.' },
]

const trackManagementFeatures = [
    { icon: 'mdi-calendar-month', title: 'Events & calendar', text: 'Schedule practices, races, and lessons with event types and blackout dates.' },
    { icon: 'mdi-calendar-check', title: 'Reservations', text: 'Limited-slot booking for sessions that need a cap.' },
    { icon: 'mdi-qrcode-scan', title: 'QR check-in', text: 'Scan passes and tickets at the gate from any phone.' },
    { icon: 'mdi-file-sign', title: 'Digital waivers', text: 'Unlimited waivers where you control who is required to sign and for what events' },
    { icon: 'mdi-account-group', title: 'Rider CRM', text: 'Every rider, purchase, and visit in one customer profile.' },
    { icon: 'mdi-chart-line', title: 'Reports & dashboards', text: 'Daily sales, event turnout, and who is riding.' },
    { icon: 'mdi-bullhorn', title: 'Built-in marketing', text: 'Email and text campaigns, loyalty rewards, coupons, and surveys.' },
]

const pointOfSaleFeatures = [
    { icon: 'mdi-cash-register', title: 'Counter sales', text: 'Ring up in-person sales fast on race day.' },
    { icon: 'mdi-ticket-confirmation', title: 'Passes & tickets', text: 'Day passes, season passes, event tickets, spectator passes, race entries.' },
    { icon: 'mdi-gift', title: 'Gift cards', text: 'Sell and redeem gift cards online and at the counter.' },
    { icon: 'mdi-credit-card', title: 'Card payments', text: 'Secure Stripe checkout with payouts straight to your account.' },
    { icon: 'mdi-palette', title: 'Branded checkout', text: 'Your logo, colors, and domain on every page riders see.' },
]

const steps = [
    { title: 'Sign up', text: 'We set up your track and get you a branded site in minutes.' },
    { title: 'Set up your catalog', text: 'Add your events, passes, and prices. We help with the first one.' },
    { title: 'Start selling', text: 'Share your link and take payments online and at the gate.' },
]

// Pricing framed from the operator's economics: no fixed cost, you set prices,
// fast payouts. The "small service fee per order" line is deliberately silent on
// who absorbs it (configurable per track) — the exact split and rate are settled
// in the sales conversation.
const pricingPoints = [
    { icon: 'mdi-calendar-remove-outline', title: 'No monthly fees',
      text: 'Nothing to pay until you make a sale, just a small fee per order. No setup costs to get started.' },
    { icon: 'mdi-tag-outline', title: 'You set your prices',
      text: 'Charge what you want for entries, passes, and add-ons. A small service fee per order is all it takes to run on RidePass.' },
    { icon: 'mdi-bank-outline', title: 'Get paid fast',
      text: 'Card payments settle straight to your bank through Stripe, with every sale tracked in one ledger.' },
    { icon: 'mdi-email-fast-outline', title: 'Affordable marketing',
      text: 'The email and text campaign tools are built in. You only pay a small flat rate per message to send, far less than the big marketing tools charge.' },
]

const featuresSection = ref<HTMLElement | null>(null)
const formSection = ref<HTMLElement | null>(null)
function scrollToFeatures() {
    featuresSection.value?.scrollIntoView({ behavior: 'smooth' })
}
function scrollToForm() {
    formSection.value?.scrollIntoView({ behavior: 'smooth' })
}

const form = ref({
    contactName: '',
    trackName: '',
    email: '',
    phone: '',
    message: '',
})
const submitting = ref(false)
const submitted = ref(false)
const errorMessage = ref<string | null>(null)

const canSubmit = computed(() =>
    form.value.contactName.trim().length > 0
    && form.value.trackName.trim().length > 0
    && /\S+@\S+/.test(form.value.email))

async function submit() {
    if (!canSubmit.value) return
    submitting.value = true
    errorMessage.value = null
    try {
        await trackLeadService.submit({
            contactName: form.value.contactName.trim(),
            trackName: form.value.trackName.trim(),
            email: form.value.email.trim(),
            phone: form.value.phone.trim() || undefined,
            message: form.value.message.trim() || undefined,
        })
        submitted.value = true
    } catch (err: any) {
        errorMessage.value = err.response?.data?.error || 'Could not submit your request. Please try again.'
    } finally {
        submitting.value = false
    }
}

function resetForm() {
    form.value = { contactName: '', trackName: '', email: '', phone: '', message: '' }
    submitted.value = false
    errorMessage.value = null
}
</script>

<style scoped>
.fortracks-page {
    background-color: #f5f5f5;
}

/* Hero: dark band with a centered pitch. Gradient gives depth without needing
   a background image (none is configured for this page yet). */
.ft-hero {
    background: linear-gradient(135deg, #141820 0%, #20262f 100%);
}
.ft-hero-overlay {
    padding: 5rem 0;
}
.ft-hero-eyebrow {
    text-transform: uppercase;
    letter-spacing: 0.14em;
    font-size: 0.85rem;
    font-weight: 700;
    color: rgb(var(--v-theme-primary));
}
.ft-hero-headline {
    line-height: 1.05;
}

/* Section eyebrow label above each feature band's heading. */
.ft-section-eyebrow {
    text-transform: uppercase;
    letter-spacing: 0.12em;
    font-size: 0.8rem;
    font-weight: 700;
    color: rgb(var(--v-theme-primary));
    margin-bottom: 0.25rem;
}

/* Feature list subtitles wrap fully instead of truncating to one line. */
.ft-feature-sub {
    white-space: normal;
    opacity: 0.8;
}

/* Screenshot placeholder: dashed frame with a centered icon + caption, sized
   to a typical app screenshot ratio. Swap for a real <v-img> when assets land. */
.ft-shot {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    text-align: center;
    aspect-ratio: 16 / 10;
    border: 2px dashed rgba(0, 0, 0, 0.18);
    border-radius: 12px;
    background: rgba(0, 0, 0, 0.03);
    color: rgba(0, 0, 0, 0.5);
    padding: 1.5rem;
}

/* Numbered step badge for "how it works". */
.ft-step-num {
    height: 44px;
    width: 44px;
    border-radius: 50%;
    background: rgb(var(--v-theme-primary));
    color: #fff;
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 1.25rem;
    font-weight: 700;
}

/* Lead form sits on a primary-tinted band so it reads as the page's main CTA. */
.ft-form-band {
    background: linear-gradient(135deg, #f57c00 0%, #e65100 100%);
}

/* "Why Tracks love RidePass" benefits band (moved from the apex home). */
.ft-benefits-photo {
    min-height: 300px;
    height: 100%;
    border-radius: 12px;
    background-size: cover;
    background-position: center;
    border: 1px solid rgba(0, 0, 0, 0.1);
}
.ft-benefits-list :deep(ul) {
    list-style: none;
    padding-left: 0;
    margin: 0;
}
.ft-benefits-list :deep(li) {
    position: relative;
    padding-left: 2rem;
    margin-bottom: 0.75rem;
    line-height: 1.5;
}
.ft-benefits-list :deep(li::before) {
    content: '✓';
    position: absolute;
    left: 0;
    top: 0;
    color: rgb(var(--v-theme-primary));
    font-weight: 700;
}
</style>
