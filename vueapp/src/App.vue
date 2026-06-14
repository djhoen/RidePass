<template>
    <v-app>
        <!-- Pre-branding splash. Sits above everything in a neutral white state with a
             faint logo so the user doesn't see the default Vuetify theme flash before
             the tenant colors arrive. Vue's <transition> handles the fade-out the
             moment branding.loaded flips true. -->
        <transition name="splash-fade">
            <div v-if="!branding.loaded" class="branding-splash">
                <div class="branding-splash-content">
                    <img :src="splashLogo" alt="RidePass" class="branding-splash-img" />
                    <div class="branding-splash-text">RidePass</div>
                </div>
            </div>
        </transition>

        <!-- Tenant not available to this visitor (unknown / inactive / unpublished). -->
        <div v-if="branding.unavailable" class="tenant-unavailable">
            <div class="tenant-unavailable-content">
                <img :src="splashLogo" alt="" class="tenant-unavailable-img" />
                <h1 class="text-h5 font-weight-bold mt-3 mb-2">This track isn't available yet</h1>
                <p class="text-body-2 text-medium-emphasis mb-5">
                    This page hasn't been published. Check back soon, or explore other tracks on RidePass.
                </p>
                <v-btn color="primary" :href="apexUrl">Explore RidePass</v-btn>
            </div>
        </div>

        <template v-else>
            <ImpersonationBanner />
            <NavBar v-if="!$route.meta.hideNav" />
            <v-main>
                <router-view />
            </v-main>
            <Footer v-if="!$route.meta.hideFooter" />
            <ConfirmDialog />
        </template>
    </v-app>
</template>

<script setup lang="ts">
import { onMounted, watchEffect, computed } from 'vue'
import { useTheme } from 'vuetify'
import NavBar from './components/NavBar.vue'
import Footer from './components/Footer.vue'
import ImpersonationBanner from './components/ImpersonationBanner.vue'
import ConfirmDialog from './components/ConfirmDialog.vue'
import { branding, loadBranding } from './stores/branding'
import splashLogo from './assets/helmet.png'

const theme = useTheme()

// Apex root URL derived from the current host (strip a leading subdomain), used
// by the "not available" page's link out.
const apexUrl = computed(() => {
    const host = window.location.hostname
    const labels = host.split('.')
    const apexHost = (host === 'localhost' || labels.length <= 2) ? host : labels.slice(-2).join('.')
    const port = window.location.port ? `:${window.location.port}` : ''
    return `${window.location.protocol}//${apexHost}${port}/`
})

onMounted(() => {
    loadBranding()
})

watchEffect(() => {
    if (!branding.loaded) return
    const themeName = branding.themeMode === 'dark' ? 'tenantDark' : 'tenant'
    const target = theme.themes.value[themeName]
    if (!target) return
    // Direct mutation: Vuetify's theme stylesheet recomputes via deep reactivity.
    // Reassigning the whole theme object with a plain POJO loses reactive wiring
    // and Vuetify may fall back to the initial config.
    target.colors.primary = branding.primaryColor
    target.colors.secondary = branding.secondaryColor
    target.colors.accent = branding.accentColor
    theme.global.name.value = themeName
})
</script>

<style scoped>
.branding-splash {
    position: fixed;
    inset: 0;
    background: #ffffff;
    z-index: 10000;
    display: flex;
    align-items: center;
    justify-content: center;
}
.branding-splash-content {
    text-align: center;
    opacity: 0.22;
}
.branding-splash-icon {
    color: #888;
}
.branding-splash-img {
    width: 80px;
    max-width: 60vw;
    height: auto;
    display: block;
    margin: 0 auto;
}
.branding-splash-text {
    font-size: 22px;
    color: #888;
    margin-top: 6px;
    letter-spacing: 0.18em;
    font-weight: 500;
}
/* Fade out only — the splash never fades in (it's there from first paint).
   Long, gentle ease-out so the tenant colors don't rush in. */
.splash-fade-leave-active {
    transition: opacity 1200ms cubic-bezier(0.25, 0.1, 0.25, 1);
}
.splash-fade-leave-to {
    opacity: 0;
}

/* "Tenant not available" full-screen page (unpublished / inactive). */
.tenant-unavailable {
    position: fixed;
    inset: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    background: #ffffff;
    padding: 24px;
    z-index: 9999;
}
.tenant-unavailable-content {
    text-align: center;
    max-width: 420px;
}
.tenant-unavailable-img {
    width: 64px;
    height: auto;
    opacity: 0.5;
}
</style>
