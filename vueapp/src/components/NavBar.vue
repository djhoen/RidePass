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
            <v-btn to="/Calendar" variant="text">Calendar</v-btn>
            <v-btn v-if="branding.seasonPassesEnabled" to="/SeasonPasses" variant="text">Season Passes</v-btn>
            <v-btn v-if="branding.giftCardsEnabled" to="/GiftCard" variant="text" prepend-icon="mdi-gift">Gift Cards</v-btn>
            <v-btn v-if="branding.rentalsEnabled" to="/Rentals" variant="text">Rentals</v-btn>

            <v-spacer></v-spacer>

            <template v-if="isAuthenticated">
                <NotificationBell />
                <!-- Tenant Admin gear — opens the same slide-out drawer mobile uses. -->
                <v-btn v-if="hasAdminAccess" icon variant="text" aria-label="Tenant Admin"
                    @click="drawer = !drawer">
                    <v-icon>mdi-cog</v-icon>
                </v-btn>

                <!-- Profile dropdown with Logout -->
                <v-menu location="bottom end">
                    <template #activator="{ props }">
                        <v-btn v-bind="props" icon variant="text" aria-label="Account">
                            <v-icon>mdi-account-circle</v-icon>
                        </v-btn>
                    </template>
                    <v-list density="compact" min-width="200">
                        <v-list-item to="/User/Upcoming" prepend-icon="mdi-calendar-clock">
                            <v-list-item-title>My Upcoming</v-list-item-title>
                        </v-list-item>
                        <v-list-item to="/User/Profile" prepend-icon="mdi-account">
                            <v-list-item-title>Profile</v-list-item-title>
                        </v-list-item>
                        <v-list-item to="/User/MyPasses" prepend-icon="mdi-ticket-account">
                            <v-list-item-title>My Passes</v-list-item-title>
                        </v-list-item>
                        <v-list-item to="/User/Rewards" prepend-icon="mdi-trophy">
                            <v-list-item-title>Rewards</v-list-item-title>
                        </v-list-item>
                        <v-list-item to="/User/SeasonPasses" prepend-icon="mdi-ticket-percent">
                            <v-list-item-title>Season Passes</v-list-item-title>
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
        <v-list density="compact" v-model:opened="openedGroups">
            <v-list-item to="/" title="Home" prepend-icon="mdi-home"></v-list-item>
            <v-list-item to="/Calendar" title="Calendar" prepend-icon="mdi-calendar"></v-list-item>
            <v-divider></v-divider>
            <template v-if="isAuthenticated">
                <template v-if="hasAdminAccess">
                    <v-list-subheader>Tenant Admin</v-list-subheader>
                    <v-list-item v-for="link in directLinks" :key="link.to" :to="link.to" :prepend-icon="link.icon"
                        :title="link.title"></v-list-item>
                    <v-list-group v-for="group in visibleGroups" :key="group.value" :value="group.value">
                        <template #activator="{ props }">
                            <v-list-item v-bind="props" :prepend-icon="group.icon" :title="group.title"></v-list-item>
                        </template>
                        <v-list-item v-for="link in group.links" :key="link.to" :to="link.to"
                            :prepend-icon="link.icon" :title="link.title"></v-list-item>
                    </v-list-group>
                    <v-divider></v-divider>
                </template>
                <v-list-group value="account">
                    <template #activator="{ props }">
                        <v-list-item v-bind="props" prepend-icon="mdi-account-circle" title="Account"></v-list-item>
                    </template>
                    <v-list-item to="/User/Upcoming" prepend-icon="mdi-calendar-clock" title="My Upcoming"></v-list-item>
                    <v-list-item to="/User/Profile" prepend-icon="mdi-account" title="Profile"></v-list-item>
                    <v-list-item to="/User/MyPasses" prepend-icon="mdi-ticket-account" title="My Passes"></v-list-item>
                    <v-list-item to="/User/Rewards" prepend-icon="mdi-trophy" title="Rewards"></v-list-item>
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
import { ref, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useDisplay } from 'vuetify'
import authHelper from '../helpers/AuthHelper'
import { branding } from '../stores/branding'
import NotificationBell from './NotificationBell.vue'
import { Perm, type Permission } from '@/helpers/TenantPermissions'

const router = useRouter()
const route = useRoute()
const { mobile } = useDisplay()
const drawer = ref(false)

const isMobile = computed(() => mobile.value)
const isAuthenticated = computed(() => authHelper.isAuthenticated())

interface AdminLink { to: string; icon: string; title: string; perm: Permission | null }
interface AdminGroup { value: string; title: string; icon: string; links: AdminLink[] }

// Direct links: pinned at top of admin menu, no group header.
const allDirectLinks: AdminLink[] = [
    { to: '/Admin/Dashboard', icon: 'mdi-view-dashboard',   title: 'Dashboard', perm: null },
    { to: '/Calendar',        icon: 'mdi-calendar',         title: 'Calendar',  perm: null },
    { to: '/Admin/Users',     icon: 'mdi-account-multiple', title: 'Users',     perm: Perm.UsersManage },
    { to: '/Admin/Customers', icon: 'mdi-account-group',    title: 'Customers', perm: Perm.CustomersView },
    { to: '/Admin/Reports',   icon: 'mdi-chart-line',       title: 'Reporting', perm: Perm.ReportsView },
    { to: '/Admin/Feedback',  icon: 'mdi-message-text',     title: 'Feedback',  perm: Perm.SettingsManage },
    { to: '/Admin/Inbox',     icon: 'mdi-inbox',            title: 'Inbox',     perm: Perm.SettingsManage },
]

// Grouped links: each group is a collapsible accordion. Groups with no permitted items are hidden.
const allGroups: AdminGroup[] = [
    {
        value: 'operations',
        title: 'Operations',
        icon: 'mdi-cash-register',
        links: [
            { to: '/Admin/Counter',       icon: 'mdi-cash-register',       title: 'Counter Sale',  perm: Perm.SalesCounter },
            { to: '/Admin/RedeemTickets', icon: 'mdi-qrcode-scan',         title: 'Redeem Tickets', perm: Perm.SalesRedeem },
            { to: '/Admin/PassCheckIn',   icon: 'mdi-card-account-details', title: 'Pass Check-In', perm: Perm.SalesRedeem },
        ],
    },
    {
        value: 'catalog',
        title: 'Catalog',
        icon: 'mdi-tag-multiple',
        links: [
            { to: '/Admin/EventTypes',   icon: 'mdi-tag-multiple',         title: 'Event Types',   perm: Perm.CatalogManage },
            { to: '/Admin/Events',       icon: 'mdi-calendar-month',       title: 'Events',        perm: Perm.CatalogManage },
            { to: '/Admin/Blackouts',    icon: 'mdi-calendar-remove',      title: 'Blackouts',     perm: Perm.CatalogManage },
            { to: '/Admin/Passes',    icon: 'mdi-ticket-confirmation',  title: 'Passes',    perm: Perm.CatalogManage },
            { to: '/Admin/SeasonPasses', icon: 'mdi-ticket-percent',       title: 'Season Passes', perm: Perm.CatalogManage },
            { to: '/Admin/Rentals',      icon: 'mdi-bike-fast',            title: 'Rentals',       perm: Perm.CatalogManage },
            { to: '/Admin/Extras',       icon: 'mdi-tag-plus',             title: 'Add-ons',       perm: Perm.CatalogManage },
        ],
    },
    {
        value: 'sales',
        title: 'Sales',
        icon: 'mdi-cart-check',
        links: [
            { to: '/Admin/Purchases',     icon: 'mdi-cart-check',     title: 'Purchases',      perm: Perm.SalesView },
            { to: '/Admin/RentalCounter', icon: 'mdi-store-clock',    title: 'Rental Counter', perm: Perm.SalesCounter },
            { to: '/Admin/Payouts',       icon: 'mdi-bank-transfer',  title: 'Payouts',        perm: Perm.ReportsView },
        ],
    },
    {
        value: 'marketing',
        title: 'Marketing',
        icon: 'mdi-bullhorn',
        links: [
            { to: '/Admin/Rewards',     icon: 'mdi-trophy',            title: 'Rewards',     perm: Perm.CatalogManage },
            { to: '/Admin/Coupons',     icon: 'mdi-tag-outline',       title: 'Coupons',     perm: Perm.CampaignsManage },
            { to: '/Admin/Subscribers', icon: 'mdi-email-multiple',    title: 'Subscribers', perm: Perm.CampaignsManage },
            { to: '/Admin/Campaigns',   icon: 'mdi-email-newsletter',  title: 'Campaigns',   perm: Perm.CampaignsManage },
            { to: '/Admin/Surveys',     icon: 'mdi-poll',              title: 'Surveys',     perm: Perm.CampaignsManage },
        ],
    },
    {
        value: 'settings',
        title: 'Settings',
        icon: 'mdi-cog-outline',
        links: [
            { to: '/Admin/Settings/General',  icon: 'mdi-tune',          title: 'General',   perm: Perm.SettingsManage },
            { to: '/Admin/Settings/Features', icon: 'mdi-toggle-switch', title: 'Features',  perm: Perm.SettingsManage },
            { to: '/Admin/Settings/HomePage', icon: 'mdi-home-edit',     title: 'Home Page', perm: Perm.SettingsManage },
            { to: '/Admin/Settings/Branding', icon: 'mdi-palette',       title: 'Branding',  perm: Perm.SettingsManage },
            { to: '/Admin/Settings/Payments', icon: 'mdi-credit-card',   title: 'Payments',  perm: Perm.SettingsManage },
            { to: '/Admin/Settings/Membership', icon: 'mdi-card-account-details', title: 'Membership', perm: Perm.SettingsManage },
            { to: '/Admin/Settings/Sms',      icon: 'mdi-cellphone-message', title: 'SMS',     perm: Perm.SettingsManage },
            { to: '/Admin/Waiver',            icon: 'mdi-file-sign',     title: 'Waivers',    perm: Perm.CatalogManage },
        ],
    },
]

function allowed(link: AdminLink): boolean {
    return link.perm === null || authHelper.hasPermission(link.perm)
}

const directLinks = computed(() => allDirectLinks.filter(allowed))
const visibleGroups = computed<AdminGroup[]>(() =>
    allGroups
        .map(g => ({ ...g, links: g.links.filter(allowed) }))
        .filter(g => g.links.length > 0)
)

const hasAdminAccess = computed(() => directLinks.value.length > 0 || visibleGroups.value.length > 0)

// Auto-expand: the group containing the current route stays open when the menu opens.
// Plus: if the user only has a single visible group, expand it by default so they don't
// have to click the header on every menu open.
const openedGroups = ref<string[]>([])

function computeInitialOpen(): string[] {
    const open: string[] = []
    for (const group of visibleGroups.value) {
        if (group.links.some(l => route.path.startsWith(l.to))) {
            open.push(group.value)
        }
    }
    if (open.length === 0 && visibleGroups.value.length === 1) {
        open.push(visibleGroups.value[0].value)
    }
    return open
}

// Recompute the initial open set whenever the route changes (so opening the cog after
// navigating to /Admin/Events shows Catalog already expanded).
watch(() => route.path, () => {
    openedGroups.value = computeInitialOpen()
}, { immediate: true })

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

/* Reduce the indent on nested admin nav items so they're slightly inset from the parent
   group rather than way over to the right (Vuetify's default is ~36px). */
:deep(.v-list-group__items > .v-list-item) {
    padding-inline-start: 40px !important;
}
</style>
