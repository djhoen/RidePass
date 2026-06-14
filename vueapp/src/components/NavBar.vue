<template>
    <!-- color="transparent" disables Vuetify's own background paint and the
         theme-based surface color, so the inline backgroundColor on :style
         wins on the v-app-bar root element. The foreground color is also set
         inline + exposed as a CSS variable so the :deep selectors below can
         pin it onto child buttons + icons (Vuetify's variant="text" buttons
         derive their color from the theme otherwise). No theme="dark" so the
         dark theme's surface color can't override the bar background. -->
    <v-app-bar color="transparent" :style="navBarVars" class="nav-bar-themed">
        <v-app-bar-title>
            <router-link to="/" class="nav-title">
                <img v-if="branding.logoUrl" :src="branding.logoUrl" class="nav-logo" :alt="branding.displayName" />
                <span v-else>{{ branding.displayName }}</span>
            </router-link>
        </v-app-bar-title>

        <template v-if="!isMobile">
            <!-- Tenant-context links: hidden for super admins (those live above tenants). -->
            <template v-if="!isSuperAdmin">
                <v-btn to="/" variant="text">Home</v-btn>
                <v-btn to="/Events" variant="text">Events</v-btn>
                <v-btn v-if="branding.giftCardsEnabled" to="/GiftCard" variant="text" prepend-icon="mdi-gift">Gift Cards</v-btn>
                <v-btn v-if="branding.rentalsEnabled" to="/Rentals" variant="text">Rentals</v-btn>
                <!-- Apex only: operator-acquisition page. Meaningless on a tenant's own site. -->
                <v-btn v-if="isApex" to="/ForTracks" variant="text">For Tracks</v-btn>
            </template>

            <v-spacer></v-spacer>

            <template v-if="isAuthenticated">
                <NotificationBell />
                <!-- Super Admin gear: dedicated platform-level drawer. -->
                <v-btn v-if="isSuperAdmin" icon variant="text" aria-label="Super Admin"
                    @click="drawer = !drawer">
                    <v-icon>mdi-shield-account</v-icon>
                </v-btn>
                <!-- Tenant Admin gear: only when actually a tenant admin (not a super admin). -->
                <v-btn v-else-if="hasAdminAccess" icon variant="text" aria-label="Tenant Admin"
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

    <!-- Drawer: SuperAdmin (platform-level pages) when role=super_admin,
         Tenant Admin otherwise. Also used as the mobile menu container. -->
    <v-navigation-drawer v-model="drawer" location="right" temporary>
        <v-list density="compact" v-model:opened="openedGroups">
            <!-- Super admin drawer: flat list of platform-level pages. No
                 tenant-context links (those are meaningless for super admins). -->
            <template v-if="isAuthenticated && isSuperAdmin">
                <v-list-subheader>Super Admin</v-list-subheader>
                <v-list-item v-for="link in superAdminLinks" :key="link.to" :to="link.to"
                    :prepend-icon="link.icon" :title="link.title"></v-list-item>
                <v-divider></v-divider>
                <v-list-item prepend-icon="mdi-account" to="/User/Profile" title="Profile"></v-list-item>
                <v-list-item prepend-icon="mdi-logout" title="Logout" @click="logout"></v-list-item>
            </template>

            <!-- Non-super-admin: tenant-context drawer (existing behavior). -->
            <template v-else>
                <v-list-item v-if="isApex" to="/ForTracks" title="For Tracks" prepend-icon="mdi-store-plus"></v-list-item>
                <v-divider v-if="isApex"></v-divider>
                <template v-if="isAuthenticated">
                    <template v-if="hasAdminAccess">
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
import { platformBranding } from '../stores/platformBranding'
import tenantHelper from '../helpers/TenantHelper'
import NotificationBell from './NotificationBell.vue'
import { Perm, type Permission } from '@/helpers/TenantPermissions'

const router = useRouter()
const route = useRoute()
const { mobile } = useDisplay()
const drawer = ref(false)

const isMobile = computed(() => mobile.value)
const isAuthenticated = computed(() => authHelper.isAuthenticated())

// Pull nav bar styling from whichever branding scope applies: apex (no
// tenant subdomain) uses the platform branding singleton; everything else
// uses the per-tenant branding. The home-page color overrides the rest-
// of-site color only on the home route. NULL falls back to the theme
// primary for background and white for text/icons.
const isApex = computed(() => !tenantHelper.getSubdomain())
const isHomeRoute = computed(() => route.path === '/' || route.path === '/Home')

// Emit the resolved colors as CSS custom properties on the v-app-bar root.
// The :deep selectors in <style> below pull them onto `.v-toolbar__background`
// (the element Vuetify actually paints) and onto child buttons + icons.
const navBarVars = computed(() => {
    const a = isApex.value ? platformBranding.data : null
    const t = !isApex.value ? branding : null

    const restBg = a?.navBarColor         ?? t?.navBarColor         ?? null
    const restFg = a?.navBarTextColor     ?? t?.navBarTextColor     ?? null
    const homeBg = a?.navBarHomeColor     ?? t?.navBarHomeColor     ?? null
    const homeFg = a?.navBarHomeTextColor ?? t?.navBarHomeTextColor ?? null

    const bg = isHomeRoute.value ? (homeBg ?? restBg) : restBg
    const fg = isHomeRoute.value ? (homeFg ?? restFg) : restFg

    // Hex literals as fallbacks (not CSS variable references) so the values
    // are always concrete strings — avoids any ambiguity in how the inline
    // style gets resolved further down the cascade.
    const bgValue = bg ?? '#FF6B1A'
    const fgValue = fg ?? '#FFFFFF'
    return {
        // Exposed as CSS custom properties so the scoped `<style>` block can
        // paint the bar (root + toolbar background pseudo) and cascade onto
        // child buttons + icons with !important rules.
        '--nav-bar-bg': bgValue,
        '--nav-bar-fg': fgValue,
    } as Record<string, string>
})
// Super admins live above tenant context. The tenant home, calendar, passes,
// etc. are meaningless to them; they get a dedicated super-admin drawer
// listing every platform-level page.
const isSuperAdmin = computed(() => authHelper.hasRole('super_admin'))

interface SuperAdminLink { to: string; icon: string; title: string }
const superAdminLinks: SuperAdminLink[] = [
    { to: '/SuperAdmin/Analytics', icon: 'mdi-chart-line',          title: 'Analytics' },
    { to: '/SuperAdmin/Tenants',   icon: 'mdi-domain',              title: 'Tenants' },
    { to: '/SuperAdmin/Users',     icon: 'mdi-account-multiple',    title: 'Users' },
    { to: '/SuperAdmin/Refunds',   icon: 'mdi-cash-refund',         title: 'Refunds' },
    { to: '/SuperAdmin/Disputes',  icon: 'mdi-alert-circle-outline', title: 'Disputes' },
    { to: '/SuperAdmin/Payouts',   icon: 'mdi-bank-transfer',       title: 'Payouts' },
    { to: '/SuperAdmin/Audit',     icon: 'mdi-shield-check',        title: 'Audit log' },
    { to: '/SuperAdmin/Reconcile', icon: 'mdi-scale-balance',       title: 'Reconcile' },
    { to: '/SuperAdmin/HomePage',  icon: 'mdi-home-edit',           title: 'Home page' },
    { to: '/SuperAdmin/Marketing', icon: 'mdi-bullhorn',            title: 'Marketing' },
]

interface AdminLink { to: string; icon: string; title: string; perm: Permission | null }
interface AdminGroup { value: string; title: string; icon: string; links: AdminLink[] }

// Direct links: pinned at top of admin menu, no group header.
const allDirectLinks: AdminLink[] = [
    { to: '/Admin/Dashboard', icon: 'mdi-view-dashboard',   title: 'Dashboard', perm: null },
    { to: '/Events',          icon: 'mdi-calendar',         title: 'Events',    perm: null },
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
/* Paint the bar via CSS variables. Variables flow down from the v-app-bar
   root (set inline via :style above). We force the configured background on
   both the root element AND the toolbar background pseudo — Vuetify's
   internal CSS paints whichever one its build/version targets, so covering
   both makes the result independent of the Vuetify version. The !important
   here overrides the theme-derived surface color (which is what was giving
   you the always-black bar before, and the always-white bar just now). */
.nav-bar-themed,
.nav-bar-themed :deep(.v-toolbar),
.nav-bar-themed :deep(.v-toolbar__background) {
    background-color: var(--nav-bar-bg) !important;
    background-image: none !important;
    opacity: 1 !important;
}
.nav-bar-themed {
    color: var(--nav-bar-fg) !important;
}
/* Title link inherits the bar foreground. currentColor cascades to the
   underline and visited states so theming reads consistently. */
.nav-title,
.nav-title:hover,
.nav-title:focus,
.nav-title:visited {
    color: var(--nav-bar-fg, #ffffff);
    text-decoration: none;
    font-weight: bold;
    display: inline-flex;
    align-items: center;
}
.nav-logo {
    max-height: 40px;
    width: auto;
}
/* Pin child buttons + icons to the bar's configured foreground. Variant="text"
   buttons normally derive color from the Vuetify theme; the !important here
   overrides that so the chosen text color reads regardless of theme. */
.nav-bar-themed :deep(.v-btn),
.nav-bar-themed :deep(.v-btn__content),
.nav-bar-themed :deep(.v-app-bar-title),
.nav-bar-themed :deep(.v-icon) {
    color: var(--nav-bar-fg, #ffffff) !important;
}

/* Reduce the indent on nested admin nav items so they're slightly inset from the parent
   group rather than way over to the right (Vuetify's default is ~36px). */
:deep(.v-list-group__items > .v-list-item) {
    padding-inline-start: 40px !important;
}
</style>
