<template>
    <v-footer class="bg-grey-darken-4 text-white pa-6">
        <v-container>
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

        <v-dialog v-model="refundDialog" max-width="700">
            <v-card>
                <v-card-title>Refund Policy</v-card-title>
                <v-card-text>
                    <div class="rich-text-body" v-html="branding.refundPolicyHtml"></div>
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

const isApex = computed(() => !tenantHelper.getSubdomain())
const refundDialog = ref(false)

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
.footer-link {
    color: #cfcfcf;
    text-decoration: none;
    line-height: 2;
}
.footer-link:hover {
    color: white;
}
</style>
