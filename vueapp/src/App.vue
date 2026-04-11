<template>
    <v-app>
        <NavBar v-if="!$route.meta.hideNav" />
        <v-main>
            <router-view />
        </v-main>
        <Footer v-if="!$route.meta.hideFooter" />
    </v-app>
</template>

<script setup lang="ts">
import NavBar from './components/NavBar.vue'
import Footer from './components/Footer.vue'
</script>
