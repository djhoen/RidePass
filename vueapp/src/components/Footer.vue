<template>
    <v-footer class="rp-footer pa-0 d-block">
        <v-container class="rp-footer-inner">
            <!-- Details row: only rendered when the tenant has something to show here.
                 The apex site has no address, contact or newsletter, so it collapses to
                 the single bottom row, matching the landing-page mockup. -->
            <v-row v-if="showDetails" class="rp-footer-details">
                <v-col cols="12" md="4">
                    <div class="rp-footer-h">{{ branding.displayName }}</div>
                    <p v-if="branding.tagline" class="rp-footer-text">{{ branding.tagline }}</p>
                    <div v-if="branding.addressLine" class="rp-footer-text">{{ branding.addressLine }}</div>
                    <div v-if="locationLine" class="rp-footer-text">{{ locationLine }}</div>
                    <div v-if="branding.refundPolicyHtml" class="mt-3">
                        <a href="javascript:void(0)" class="rp-footer-link" @click="refundDialog = true">Refund policy</a>
                    </div>
                </v-col>

                <v-col cols="12" md="4">
                    <template v-if="hasAnySocial">
                        <div class="rp-footer-h">Follow</div>
                        <div class="d-flex ga-3 mb-4">
                            <a v-if="branding.socialFacebookUrl" :href="branding.socialFacebookUrl" target="_blank"
                                rel="noopener" class="rp-footer-link" aria-label="Facebook"><v-icon size="20">mdi-facebook</v-icon></a>
                            <a v-if="branding.socialInstagramUrl" :href="branding.socialInstagramUrl" target="_blank"
                                rel="noopener" class="rp-footer-link" aria-label="Instagram"><v-icon size="20">mdi-instagram</v-icon></a>
                            <a v-if="branding.socialTiktokUrl" :href="branding.socialTiktokUrl" target="_blank"
                                rel="noopener" class="rp-footer-link" aria-label="TikTok"><v-icon size="20">mdi-music-note</v-icon></a>
                            <a v-if="branding.socialYoutubeUrl" :href="branding.socialYoutubeUrl" target="_blank"
                                rel="noopener" class="rp-footer-link" aria-label="YouTube"><v-icon size="20">mdi-youtube</v-icon></a>
                        </div>
                    </template>
                    <template v-if="branding.contactEmail || branding.phone">
                        <div class="rp-footer-h">Contact</div>
                        <div v-if="branding.contactEmail" class="d-flex align-center mb-1">
                            <v-icon size="14" class="mr-2">mdi-email-outline</v-icon>
                            <a :href="`mailto:${branding.contactEmail}`" class="rp-footer-link">{{ branding.contactEmail }}</a>
                        </div>
                        <div v-if="branding.phone" class="d-flex align-center">
                            <v-icon size="14" class="mr-2">mdi-phone-outline</v-icon>
                            <a :href="`tel:${phoneTel}`" class="rp-footer-link">{{ phoneFormatted }}</a>
                        </div>
                    </template>
                </v-col>

                <v-col cols="12" md="4">
                    <NewsletterSignup v-if="showNewsletter"
                        title="Stay in the loop"
                        :subtitle="`Event updates and announcements from ${branding.displayName}.`" />
                </v-col>
            </v-row>

            <!-- Bottom row: brand, links, copyright. -->
            <div class="rp-footer-row">
                <router-link to="/" class="rp-footer-brand">
                    <img v-if="footerLogo" :src="footerLogo" :alt="branding.displayName" class="rp-footer-logo" />
                    <BrandLogo v-else-if="isApex" :height="30" class="rp-footer-brand-svg" />
                    <span v-else class="font-display rp-footer-wordmark">{{ branding.displayName }}</span>
                </router-link>
                <div class="rp-footer-links">
                    <router-link v-if="!isAuthenticated" to="/Login" class="rp-footer-link">Sign in</router-link>
                    <router-link v-if="isApex" to="/Discover" class="rp-footer-link">Tracks</router-link>
                    <router-link to="/Events" class="rp-footer-link">Events</router-link>
                    <router-link v-if="!isApex && !inAccountArea" to="/Feedback" class="rp-footer-link">Feedback</router-link>
                    <a v-if="!isApex && !inAccountArea" :href="forTracksUrl" class="rp-footer-link">For track owners</a>
                    <router-link v-if="isApex" to="/ForTracks" class="rp-footer-link">For track owners</router-link>
                </div>
                <div class="rp-footer-copy">
                    &copy; {{ new Date().getFullYear() }} {{ branding.displayName }}
                    <span v-if="!isApex" class="rp-footer-powered">
                        &nbsp;·&nbsp;<a href="https://ridepass.io" class="rp-footer-link">Powered by RidePass</a>
                    </span>
                </div>
            </div>
            <p class="rp-footer-note">
                Season passes, day passes, and event entries are sold through RidePass with secure in-app payment.
            </p>
        </v-container>

        <v-dialog v-model="refundDialog" max-width="700">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Refund policy</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="refundDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <RichTextView :html="branding.refundPolicyHtml ?? ''" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn variant="outlined" @click="refundDialog = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>
    </v-footer>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRoute } from 'vue-router'
import { branding } from '@/stores/branding'
import { platformBranding, platformImageUrl } from '@/stores/platformBranding'
import tenantHelper from '@/helpers/TenantHelper'
import authHelper from '@/helpers/AuthHelper'
import NewsletterSignup from '@/components/NewsletterSignup.vue'
import RichTextView from '@/components/RichTextView.vue'
import BrandLogo from '@/components/BrandLogo.vue'

const isApex = computed(() => !tenantHelper.getSubdomain())
const isAuthenticated = computed(() => authHelper.isAuthenticated())

// A rider who has signed in to look at their own passes and orders is not an audience for
// "run your own track", and asking them to subscribe with an email we already have is noise.
const route = useRoute()
const inAccountArea = computed(() => route.path.startsWith('/User/'))
const refundDialog = ref(false)

const showNewsletter = computed(() => !isApex.value && !inAccountArea.value && branding.allowEventSubscriptions)
const showDetails = computed(() => !isApex.value && (
    !!branding.tagline || !!branding.addressLine || !!locationLine.value || !!branding.refundPolicyHtml
    || hasAnySocial.value || !!branding.contactEmail || !!branding.phone || showNewsletter.value))

// Prefer the white/mono logo in the footer (dark surface); fall back to the main logo.
const footerLogo = computed(() => isApex.value
    ? platformImageUrl(platformBranding.data?.logoUrl)
    : (branding.logoWhiteUrl ?? branding.logoUrl))

// The For-track-owners link always targets the apex root /ForTracks, never the
// tenant's, so the operator marketing page is unambiguously the platform's.
// Derive the apex host from the ACTUAL hostname (strip a leading subdomain label).
function computeApexHost(): string {
    const host = window.location.hostname
    if (host === 'localhost' || /^(\d+\.){3}\d+$/.test(host)) return host
    const labels = host.split('.')
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

// Phone display: US-style 10/11-digit numbers as "(555) 123-4567"; anything else as entered.
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
    return raw.replace(/[^\d+]/g, '')
})
</script>

<style scoped>
.rp-footer {
    border-top: 1px solid var(--rp-line);
    color: var(--rp-muted2);
    font-size: 12.5px;
}
.rp-footer-inner {
    padding: 34px 22px;
    max-width: 1140px;
}
.rp-footer-details {
    padding-bottom: 22px;
    margin-bottom: 22px;
    border-bottom: 1px solid var(--rp-line);
}
.rp-footer-h {
    font-size: 11px;
    font-weight: 800;
    letter-spacing: 0.18em;
    text-transform: uppercase;
    color: var(--rp-muted);
    margin-bottom: 10px;
}
.rp-footer-text {
    color: var(--rp-muted);
    font-size: 13px;
    line-height: 1.6;
    margin: 0;
}
.rp-footer-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 16px;
    flex-wrap: wrap;
}
.rp-footer-brand {
    display: inline-flex;
    align-items: center;
    color: var(--rp-ink);
}
.rp-footer-logo {
    height: 30px;
    width: auto;
    opacity: 0.9;
    display: block;
}
.rp-footer-wordmark {
    font-size: 24px;
    color: var(--rp-ink);
}
.rp-footer-brand-svg {
    color: var(--rp-ink);
    opacity: 0.9;
}
.rp-footer-links {
    display: flex;
    gap: 18px;
    flex-wrap: wrap;
    font-size: 11.5px;
    font-weight: 800;
    letter-spacing: 0.1em;
    text-transform: uppercase;
}
.rp-footer-link {
    color: var(--rp-muted);
    text-decoration: none;
    transition: color 0.15s;
}
.rp-footer-link:hover {
    color: var(--rp-ink);
}
.rp-footer-copy {
    color: var(--rp-muted2);
}
.rp-footer-powered a { color: var(--rp-muted); }
.rp-footer-note {
    font-size: 11px;
    color: var(--rp-muted2);
    margin: 10px 0 0;
}
</style>
