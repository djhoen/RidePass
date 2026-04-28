<template>
    <v-app-bar color="primary" dark>
        <v-app-bar-title>
            <router-link to="/" class="nav-title">
                <img v-if="branding.logoUrl" :src="branding.logoUrl" class="nav-logo" :alt="branding.displayName" />
                <span v-else>{{ branding.displayName }}</span>
            </router-link>
        </v-app-bar-title>

        <template v-if="!isMobile">
            <v-btn to="/" variant="text">Home</v-btn>

            <v-spacer></v-spacer>

            <template v-if="isAuthenticated">
                <!-- Tenant Admin dropdown (gear icon only) -->
                <v-menu v-if="isAdmin" location="bottom end">
                    <template #activator="{ props }">
                        <v-btn v-bind="props" icon variant="text" aria-label="Tenant Admin">
                            <v-icon>mdi-cog</v-icon>
                        </v-btn>
                    </template>
                    <v-list density="compact" min-width="220">
                        <v-list-item :to="dashboardLink.to" :prepend-icon="dashboardLink.icon">
                            <v-list-item-title>{{ dashboardLink.title }}</v-list-item-title>
                        </v-list-item>
                        <v-divider></v-divider>
                        <v-list-item v-for="link in adminLinks" :key="link.to" :to="link.to" :prepend-icon="link.icon">
                            <v-list-item-title>{{ link.title }}</v-list-item-title>
                        </v-list-item>
                    </v-list>
                </v-menu>

                <!-- Profile dropdown with Logout -->
                <v-menu location="bottom end">
                    <template #activator="{ props }">
                        <v-btn v-bind="props" icon variant="text" aria-label="Account">
                            <v-icon>mdi-account-circle</v-icon>
                        </v-btn>
                    </template>
                    <v-list density="compact" min-width="200">
                        <v-list-item to="/User/Profile" prepend-icon="mdi-account">
                            <v-list-item-title>Profile</v-list-item-title>
                        </v-list-item>
                        <v-list-item to="/User/MyPasses" prepend-icon="mdi-ticket-account">
                            <v-list-item-title>My Passes</v-list-item-title>
                        </v-list-item>
                        <v-divider></v-divider>
                        <v-list-item prepend-icon="mdi-logout" @click="logout">
                            <v-list-item-title>Logout</v-list-item-title>
                        </v-list-item>
                    </v-list>
                </v-menu>
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
        <v-list density="compact">
            <v-list-item to="/" title="Home" prepend-icon="mdi-home"></v-list-item>
            <v-divider></v-divider>
            <template v-if="isAuthenticated">
                <v-list-group v-if="isAdmin" value="tenant-admin">
                    <template #activator="{ props }">
                        <v-list-item v-bind="props" prepend-icon="mdi-cog" title="Tenant Admin"></v-list-item>
                    </template>
                    <v-list-item :to="dashboardLink.to" :prepend-icon="dashboardLink.icon"
                        :title="dashboardLink.title"></v-list-item>
                    <v-list-item v-for="link in adminLinks" :key="link.to" :to="link.to"
                        :prepend-icon="link.icon" :title="link.title"></v-list-item>
                </v-list-group>
                <v-list-group value="account">
                    <template #activator="{ props }">
                        <v-list-item v-bind="props" prepend-icon="mdi-account-circle" title="Account"></v-list-item>
                    </template>
                    <v-list-item to="/User/Profile" prepend-icon="mdi-account" title="Profile"></v-list-item>
                    <v-list-item to="/User/MyPasses" prepend-icon="mdi-ticket-account" title="My Passes"></v-list-item>
                    <v-list-item prepend-icon="mdi-logout" title="Logout" @click="logout"></v-list-item>
                </v-list-group>
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
import { branding } from '../stores/branding'

const router = useRouter()
const { mobile } = useDisplay()
const drawer = ref(false)

const isMobile = computed(() => mobile.value)
const isAuthenticated = computed(() => authHelper.isAuthenticated())
import { Perm, type Permission } from '@/helpers/TenantPermissions'

interface AdminLink { to: string; icon: string; title: string; perm: Permission }

interface DashboardLink { to: string; icon: string; title: string }
const dashboardLink: DashboardLink = { to: '/Admin/Dashboard', icon: 'mdi-view-dashboard', title: 'Dashboard' }

const allAdminLinks: AdminLink[] = [
    { to: '/Admin/Users',         icon: 'mdi-account-multiple',    title: 'Users',         perm: Perm.UsersManage },
    { to: '/Admin/Branding',      icon: 'mdi-palette',             title: 'Branding',      perm: Perm.SettingsManage },
    { to: '/Admin/EventTypes',    icon: 'mdi-tag-multiple',        title: 'Event Types',   perm: Perm.CatalogManage },
    { to: '/Admin/Events',        icon: 'mdi-calendar-month',      title: 'Events',        perm: Perm.CatalogManage },
    { to: '/Admin/Blackouts',     icon: 'mdi-calendar-remove',     title: 'Blackouts',     perm: Perm.CatalogManage },
    { to: '/Admin/DayPasses',     icon: 'mdi-ticket-confirmation', title: 'Day Passes',    perm: Perm.CatalogManage },
    { to: '/Admin/Waiver',        icon: 'mdi-file-sign',           title: 'Waiver',        perm: Perm.CatalogManage },
    { to: '/Admin/Purchases',     icon: 'mdi-cart-check',          title: 'Purchases',     perm: Perm.SalesView },
    { to: '/Admin/Reports',       icon: 'mdi-chart-line',          title: 'Reports',       perm: Perm.ReportsView },
    { to: '/Admin/Subscribers',   icon: 'mdi-email-multiple',      title: 'Subscribers',   perm: Perm.CampaignsManage },
    { to: '/Admin/Campaigns',     icon: 'mdi-email-newsletter',    title: 'Campaigns',     perm: Perm.CampaignsManage },
    { to: '/Admin/RedeemTickets', icon: 'mdi-qrcode-scan',         title: 'Redeem Tickets', perm: Perm.SalesRedeem },
]
const adminLinks = computed(() => allAdminLinks.filter(l => authHelper.hasPermission(l.perm)))
const isAdmin = computed(() => adminLinks.value.length > 0)

const logout = () => {
    authHelper.logout()
    router.push('/Login')
}
</script>

<style scoped>
.nav-title,
.nav-title:hover,
.nav-title:focus,
.nav-title:visited {
    color: white;
    text-decoration: none;
    font-weight: bold;
    display: inline-flex;
    align-items: center;
}
.nav-logo {
    max-height: 40px;
    width: auto;
}
</style>
