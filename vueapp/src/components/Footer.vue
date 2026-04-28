<template>
    <v-footer class="bg-grey-darken-4 text-white pa-6">
        <v-container>
            <v-row>
                <v-col cols="12" md="4">
                    <h3>{{ branding.displayName }}</h3>
                    <p v-if="branding.tagline" class="mt-2 text-grey-lighten-1">{{ branding.tagline }}</p>
                    <p v-if="locationLine" class="mt-2 text-grey-lighten-2 text-caption">
                        <v-icon size="x-small" class="mr-1">mdi-map-marker</v-icon>{{ locationLine }}
                    </p>
                </v-col>

                <v-col cols="12" md="4">
                    <h4>Links</h4>
                    <div class="mt-2">
                        <div><router-link to="/" class="footer-link">Home</router-link></div>
                        <div v-if="!isApex"><router-link to="/Login" class="footer-link">Sign In</router-link></div>
                        <div v-if="!isApex"><router-link to="/CreateAccount" class="footer-link">Sign Up</router-link></div>
                        <div v-if="isApex"><router-link to="/Discover" class="footer-link">Find Tracks</router-link></div>
                    </div>
                </v-col>

                <v-col v-if="!isApex" cols="12" md="4">
                    <h4>Newsletter</h4>
                    <p class="mt-2 text-grey-lighten-1 text-caption">Event updates and announcements.</p>
                    <div class="mt-2 d-flex ga-2">
                        <v-text-field v-model="footerEmail" type="email" placeholder="you@example.com" density="compact"
                            hide-details variant="outlined" bg-color="grey-darken-3" class="flex-grow-1"
                            :disabled="footerSubscribed || footerSubmitting"></v-text-field>
                        <v-btn color="primary" density="default" :loading="footerSubmitting" :disabled="footerSubscribed"
                            @click="submitFooterSignup">
                            {{ footerSubscribed ? '✓' : 'Sign up' }}
                        </v-btn>
                    </div>
                    <div v-if="footerError" class="text-caption text-error mt-1">{{ footerError }}</div>
                    <div v-else-if="footerSubscribed" class="text-caption text-success mt-1">
                        You're on the list. Thanks!
                    </div>
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
    </v-footer>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { branding } from '@/stores/branding'
import tenantHelper from '@/helpers/TenantHelper'
import { NewsletterService } from '@/services/NewsletterService'

const service = new NewsletterService()
const isApex = computed(() => !tenantHelper.getSubdomain())

const locationLine = computed(() => {
    const parts = [branding.city, branding.region].filter((p): p is string => !!p && p.trim().length > 0)
    return parts.length > 0 ? parts.join(', ') : ''
})

const footerEmail = ref('')
const footerSubmitting = ref(false)
const footerSubscribed = ref(false)
const footerError = ref('')

async function submitFooterSignup() {
    const email = footerEmail.value.trim()
    if (!email || !email.includes('@')) {
        footerError.value = 'Enter a valid email.'
        return
    }
    footerError.value = ''
    footerSubmitting.value = true
    try {
        await service.subscribe(email, null)
        footerSubscribed.value = true
    } catch (err: any) {
        footerError.value = err.response?.data?.error || 'Could not subscribe. Try again later.'
    } finally {
        footerSubmitting.value = false
    }
}
</script>

<style scoped>
.footer-link {
    color: #9e9e9e;
    text-decoration: none;
    line-height: 2;
}
.footer-link:hover {
    color: white;
}
</style>
