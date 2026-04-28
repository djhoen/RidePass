<template>
    <v-app>
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
