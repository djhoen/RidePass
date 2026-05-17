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

        <ImpersonationBanner />
        <NavBar v-if="!$route.meta.hideNav" />
        <v-main>
            <router-view />
        </v-main>
        <Footer v-if="!$route.meta.hideFooter" />
    </v-app>
</template>

<script setup lang="ts">
import { onMounted, watchEffect } from 'vue'
import { useTheme } from 'vuetify'
import NavBar from './components/NavBar.vue'
import Footer from './components/Footer.vue'
import ImpersonationBanner from './components/ImpersonationBanner.vue'
import { branding, loadBranding } from './stores/branding'
import splashLogo from './assets/helmet.png'

const theme = useTheme()

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
</style>
