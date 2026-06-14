<template>
    <v-footer class="bg-grey-darken-4 text-white pa-0 d-block">
        <!-- Apex only: prominent operator CTA band at the top of the footer.
             Tenant sites get a smaller nudge below the footer instead (see end). -->
        <div v-if="isApex" class="footer-cta">
            <v-container class="py-6">
                <div class="d-flex flex-column flex-sm-row align-center ga-4 text-center text-sm-left">
                    <div class="flex-grow-1">
                        <div class="text-h5 font-weight-bold font-display mb-1">Run a track?</div>
                        <div class="text-body-2" style="opacity: 0.92">
                            Sell passes and tickets online, check riders in at the gate, and run your
                            events. All in one place.
                        </div>
                    </div>
                    <v-btn color="white" class="text-primary flex-shrink-0" size="large"
                        to="/ForTracks">See more</v-btn>
                </div>
            </v-container>
        </div>

        <v-container class="pa-6">
            <v-row>
                <!-- Left: address + refund policy link -->
                <v-col cols="12" md="4">
                    <h3 class="text-subtitle-1 font-weight-bold mb-2">{{ branding.displayName }}</h3>
                    <p v-if="branding.tagline" class="text-grey-lighten-1 text-caption mb-2">{{ branding.tagline }}</p>
                    <div v-if="branding.addressLine" class="text-grey-lighten-2 text-body-2">
                        {{ branding.addressLine }}
                    </div>
                    <div v-if="locationLine" class="text-grey-lighten-2 text-body-2">{{ locationLine }}</div>
                    <div v-if="branding.refundPolicyHtml" class="mt-3">
                        <a href="javascript:void(0)" class="footer-link" @click="refundDialog = true">Refund Policy</a>
                    </div>
                </v-col>

                <!-- Center: Follow (only when at least one social URL is set) + Contact (email + phone) -->
                <v-col cols="12" md="4">
                    <h4 v-if="hasAnySocial" class="text-subtitle-1 font-weight-bold mb-2">Follow</h4>
                    <div v-if="hasAnySocial" class="d-flex ga-3">
                        <a v-if="branding.socialFacebookUrl" :href="branding.socialFacebookUrl" target="_blank"
                            rel="noopener" class="footer-link"><v-icon>mdi-facebook</v-icon></a>
                        <a v-if="branding.socialInstagramUrl" :href="branding.socialInstagramUrl" target="_blank"
                            rel="noopener" class="footer-link"><v-icon>mdi-instagram</v-icon></a>
                        <a v-if="branding.socialTiktokUrl" :href="branding.socialTiktokUrl" target="_blank"
                            rel="noopener" class="footer-link"><v-icon>mdi-music-note</v-icon></a>
                        <a v-if="branding.socialYoutubeUrl" :href="branding.socialYoutubeUrl" target="_blank"
                            rel="noopener" class="footer-link"><v-icon>mdi-youtube</v-icon></a>
                    </div>

                    <div v-if="branding.contactEmail || branding.phone"
                        :class="hasAnySocial ? 'mt-4' : ''">
                        <h4 class="text-subtitle-1 font-weight-bold mb-2">Contact</h4>
                        <div v-if="branding.contactEmail" class="d-flex align-center mb-1">
                            <v-icon size="small" class="mr-2">mdi-email</v-icon>
                            <a :href="`mailto:${branding.contactEmail}`" class="footer-link">{{ branding.contactEmail }}</a>
                        </div>
                        <div v-if="branding.phone" class="d-flex align-center">
                            <v-icon size="small" class="mr-2">mdi-phone</v-icon>
                            <a :href="`tel:${phoneTel}`" class="footer-link">{{ phoneFormatted }}</a>
                        </div>
                    </div>
                </v-col>

                <!-- Right: newsletter signup (apex has no event subscriptions, so we show a links column instead) -->
                <v-col cols="12" md="4">
                    <NewsletterSignup v-if="!isApex && branding.allowEventSubscriptions"
                        title="Stay in the loop"
                        :subtitle="`Event updates and announcements from ${branding.displayName}.`" />
                    <template v-else>
                        <h4 class="text-subtitle-1 font-weight-bold mb-2">Links</h4>
                        <div class="mt-2">
                            <div><router-link to="/" class="footer-link">Home</router-link></div>
                            <div v-if="!isApex"><router-link to="/Login" class="footer-link">Sign In</router-link></div>
                            <div v-if="!isApex"><router-link to="/CreateAccount" class="footer-link">Sign Up</router-link></div>
                            <div v-if="!isApex"><router-link to="/Feedback" class="footer-link">Send Feedback</router-link></div>
                            <div v-if="isApex"><router-link to="/Discover" class="footer-link">Find Tracks</router-link></div>
                        </div>
                    </template>
                </v-col>
            </v-row>

            <v-divider class="mt-6 mb-3"></v-divider>
            <v-row>
                <v-col class="text-center text-grey-lighten-1">
                    &copy; {{ new Date().getFullYear() }} {{ branding.displayName }}
                    <span class="ml-4"><a href="https://ridepass.io" class="footer-link">Powered by RidePass</a></span>
                </v-col>
            </v-row>
        </v-container>

        <!-- Tenant only: compact operator CTA below the regular footer. Same
             gradient as the apex band, just smaller. Links to the apex
             /ForTracks (cross-host on a tenant subdomain). -->
        <div v-if="!isApex" class="footer-cta">
            <v-container class="py-3">
                <div class="d-flex flex-column flex-sm-row align-center justify-center ga-3 text-center">
                    <span class="text-body-2 font-weight-medium">Run a track? See how RidePass can power yours.</span>
                    <v-btn color="white" class="text-primary flex-shrink-0" size="small" :href="forTracksUrl">See more</v-btn>
                </div>
            </v-container>
        </div>

        <v-dialog v-model="refundDialog" max-width="700">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Refund Policy</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="refundDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <RichTextView :html="branding.refundPolicyHtml ?? ''" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="refundDialog = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>
    </v-footer>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { branding } from '@/stores/branding'
import tenantHelper from '@/helpers/TenantHelper'
import NewsletterSignup from '@/components/NewsletterSignup.vue'
import RichTextView from '@/components/RichTextView.vue'

const isApex = computed(() => !tenantHelper.getSubdomain())
const refundDialog = ref(false)

// The See-more button always targets the apex root /ForTracks, never the
// tenant's, so the operator marketing page is unambiguously the platform's.
// We derive the apex host from the ACTUAL current hostname (strip a leading
// subdomain label) rather than from VITE_ROOT_DOMAIN — that env var isn't set
// in production and can't be relied on here.
function computeApexHost(): string {
    const host = window.location.hostname
    if (host === 'localhost' || /^(\d+\.){3}\d+$/.test(host)) return host
    const labels = host.split('.')
    // 2 labels (ridepass.io, ridepass.local) is already the apex; 3+ means a
    // tenant subdomain, so keep the last two labels.
    return labels.length <= 2 ? host : labels.slice(-2).join('.')
}
const forTracksUrl = computed(() => {
    const proto = window.location.protocol
    const port = window.location.port ? `:${window.location.port}` : ''
    return `${proto}//${computeApexHost()}${port}/ForTracks`
})

const locationLine = computed(() => {
    const parts = [branding.city, branding.region, branding.postalCode]
        .filter((p): p is string => !!p && p.trim().length > 0)
    return parts.length > 0 ? parts.join(', ') : ''
})

const hasAnySocial = computed(() => !!(
    branding.socialFacebookUrl
    || branding.socialInstagramUrl
    || branding.socialTiktokUrl
    || branding.socialYoutubeUrl))

// Phone display: format US-style 10-digit and 11-digit (1+10) numbers as
// "(555) 123-4567" or "+1 (555) 123-4567". Anything else (international,
// vanity numbers, already-formatted) shows as the admin entered it.
// `phoneTel` strips to digits + leading + so the tel: link dials reliably.
const phoneFormatted = computed(() => {
    const raw = branding.phone?.trim() ?? ''
    if (!raw) return ''
    const digits = raw.replace(/[^\d]/g, '')
    if (digits.length === 10) {
        return `(${digits.slice(0, 3)}) ${digits.slice(3, 6)}-${digits.slice(6)}`
    }
    if (digits.length === 11 && digits.startsWith('1')) {
        return `+1 (${digits.slice(1, 4)}) ${digits.slice(4, 7)}-${digits.slice(7)}`
    }
    return raw
})
const phoneTel = computed(() => {
    const raw = branding.phone?.trim() ?? ''
    if (!raw) return ''
    // Keep a leading + for international, otherwise digits only.
    const digits = raw.replace(/[^\d+]/g, '')
    return digits
})
</script>

<style scoped>
/* Gradient is driven by the active theme primary so it harmonizes with each
   tenant's brand (App.vue sets theme primary = tenant primaryColor) and shows
   the RidePass orange on the apex site. The darker end is the same hue mixed
   toward black. background-color is a solid fallback for browsers without
   color-mix() support (the gradient image is simply dropped there). */
.footer-cta {
    background-color: rgb(var(--v-theme-primary));
    background-image: linear-gradient(135deg,
        rgb(var(--v-theme-primary)) 0%,
        color-mix(in srgb, rgb(var(--v-theme-primary)) 60%, #000) 100%);
}
.footer-link {
    color: #cfcfcf;
    text-decoration: none;
    line-height: 2;
}
.footer-link:hover {
    color: white;
}
</style>
