<template>
    <v-app-bar color="primary" dark>
        <v-app-bar-title>
            <router-link to="/" class="nav-title">Template App</router-link>
        </v-app-bar-title>

        <template v-if="!isMobile">
            <v-btn to="/" variant="text">Home</v-btn>
            <v-btn to="/BlogFeed" variant="text">Blog</v-btn>
            <v-btn to="/Faqs" variant="text">FAQs</v-btn>

            <v-spacer></v-spacer>

            <template v-if="isAuthenticated">
                <v-btn to="/User/Profile" variant="text">
                    <v-icon class="m-r-5">mdi-account</v-icon> Profile
                </v-btn>
                <v-btn to="/User/OrderHistory" variant="text">Orders</v-btn>
                <v-btn @click="logout" variant="text">Logout</v-btn>
            </template>
            <template v-else>
                <v-btn to="/Login" variant="text">Login</v-btn>
                <v-btn to="/CreateAccount" variant="outlined">Sign Up</v-btn>
            </template>
        </template>

        <template v-else>
            <v-spacer></v-spacer>
            <v-app-bar-nav-icon @click="drawer = !drawer"></v-app-bar-nav-icon>
        </template>
    </v-app-bar>

    <!-- Mobile Drawer -->
    <v-navigation-drawer v-model="drawer" location="right" temporary>
        <v-list>
            <v-list-item to="/" title="Home" prepend-icon="mdi-home"></v-list-item>
            <v-list-item to="/BlogFeed" title="Blog" prepend-icon="mdi-post"></v-list-item>
            <v-list-item to="/Faqs" title="FAQs" prepend-icon="mdi-help-circle"></v-list-item>
            <v-divider></v-divider>
            <template v-if="isAuthenticated">
                <v-list-item to="/User/Profile" title="Profile" prepend-icon="mdi-account"></v-list-item>
                <v-list-item to="/User/OrderHistory" title="Orders" prepend-icon="mdi-receipt"></v-list-item>
                <v-list-item @click="logout" title="Logout" prepend-icon="mdi-logout"></v-list-item>
            </template>
            <template v-else>
                <v-list-item to="/Login" title="Login" prepend-icon="mdi-login"></v-list-item>
                <v-list-item to="/CreateAccount" title="Sign Up" prepend-icon="mdi-account-plus"></v-list-item>
            </template>
        </v-list>
    </v-navigation-drawer>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useDisplay } from 'vuetify'
import authHelper from '../helpers/AuthHelper'

const router = useRouter()
const { mobile } = useDisplay()
const drawer = ref(false)

const isMobile = computed(() => mobile.value)
const isAuthenticated = computed(() => authHelper.isAuthenticated())

const logout = () => {
    authHelper.logout()
    router.push('/Login')
}
</script>

<style scoped>
.nav-title {
    color: white;
    text-decoration: none;
    font-weight: bold;
}
</style>
