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
                <img v-if="navLogoUrl" :src="navLogoUrl" class="nav-logo" :alt="navDisplayName" />
                <span v-else>{{ navDisplayName }}</span>
            </router-link>
        </v-app-bar-title>

        <template v-if="!isMobile">
            <!-- Public site links: shown to everyone (including signed-in admins / super admins)
                 so they can always navigate back to Home / Events. -->
            <v-btn to="/" variant="text">Home</v-btn>
            <v-btn to="/Events" variant="text">Events</v-btn>
            <v-btn v-if="branding.seasonPassesEnabled" to="/SeasonPasses" variant="text">Season Passes</v-btn>
            <v-btn v-if="branding.membershipEnabled" to="/Membership" variant="text">Membership</v-btn>
            <v-btn v-if="branding.giftCardsEnabled" to="/GiftCard" variant="text" prepend-icon="mdi-gift">Gift Cards</v-btn>
            <v-btn v-if="branding.bikeShopEnabled" to="/Shop" variant="text" prepend-icon="mdi-bike">Shop</v-btn>
            <v-btn v-if="branding.bikeShopEnabled" to="/Rentals" variant="text" prepend-icon="mdi-bike-fast">Rentals</v-btn>
            <!-- Standalone /Rentals retired — rentals are booked at the Bike Shop counter now. -->

            <v-btn v-if="showOrderFood" to="/Order" variant="text" prepend-icon="mdi-silverware-fork-knife">Order Food</v-btn>
            <v-btn v-if="branding.blogEnabled" to="/Blog" variant="text">Blog</v-btn>
            <v-btn v-for="p in branding.navPages" :key="p.slug" :to="'/' + p.slug" variant="text">{{ p.label }}</v-btn>
            <!-- Apex only: operator-acquisition page. Meaningless on a tenant's own site. -->
            <v-btn v-if="isApex" to="/ForTracks" variant="text">For Tracks</v-btn>

            <v-spacer></v-spacer>

            <template v-if="isAuthenticated">
                <ImpersonationMenu />
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
                            <v-list-item-title>My Events</v-list-item-title>
                        </v-list-item>
                        <v-list-item to="/User/Profile" prepend-icon="mdi-account">
                            <v-list-item-title>Profile</v-list-item-title>
                        </v-list-item>
                        <v-list-item v-if="!isSuperAdmin" to="/User/MyPasses" prepend-icon="mdi-ticket-account">
                            <v-list-item-title>My Passes</v-list-item-title>
                        </v-list-item>
                        <v-list-item v-if="!isSuperAdmin && branding.bikeShopEnabled" to="/User/MyOrders"
                            prepend-icon="mdi-package-variant-closed">
                            <v-list-item-title>My Orders</v-list-item-title>
                        </v-list-item>
                        <v-list-item v-if="!isSuperAdmin" to="/User/SeasonPasses" prepend-icon="mdi-ticket-percent">
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
            </template>
        </template>

        <template v-else>
            <v-spacer></v-spacer>
            <ImpersonationMenu />
            <NotificationBell v-if="isAuthenticated" />
            <v-app-bar-nav-icon @click="drawer = !drawer"></v-app-bar-nav-icon>
        </template>
    </v-app-bar>

    <!-- Drawer: SuperAdmin (platform-level pages) when role=super_admin,
         Tenant Admin otherwise. Also used as the mobile menu container. -->
    <v-navigation-drawer v-model="drawer" location="right" temporary width="320">
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
                <!-- Mobile only: the public top-bar links (hidden on small screens up
                     top) collapse into the drawer so visitors can still reach them. -->
                <template v-if="isMobile">
                    <v-list-item to="/" title="Home" prepend-icon="mdi-home"></v-list-item>
                    <v-list-item to="/Events" title="Events" prepend-icon="mdi-calendar"></v-list-item>
                    <v-list-item v-if="branding.seasonPassesEnabled" to="/SeasonPasses" title="Season Passes" prepend-icon="mdi-ticket-percent"></v-list-item>
                    <v-list-item v-if="branding.membershipEnabled" to="/Membership" title="Membership" prepend-icon="mdi-card-account-details"></v-list-item>
                    <v-list-item v-if="branding.giftCardsEnabled" to="/GiftCard" title="Gift Cards" prepend-icon="mdi-gift"></v-list-item>
                    <v-list-item v-if="branding.bikeShopEnabled" to="/Shop" title="Shop" prepend-icon="mdi-bike"></v-list-item>
                    <v-list-item v-if="branding.bikeShopEnabled" to="/Rentals" title="Rentals" prepend-icon="mdi-bike-fast"></v-list-item>
                    <v-list-item v-if="showOrderFood" to="/Order" title="Order Food" prepend-icon="mdi-silverware-fork-knife"></v-list-item>
                    <v-list-item v-if="branding.blogEnabled" to="/Blog" title="Blog" prepend-icon="mdi-post"></v-list-item>
                    <v-list-item v-for="p in branding.navPages" :key="p.slug" :to="'/' + p.slug" :title="p.label"
                        prepend-icon="mdi-file-document-outline"></v-list-item>
                    <v-divider></v-divider>
                </template>
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
                        <v-list-item to="/User/Upcoming" prepend-icon="mdi-calendar-clock" title="My Events"></v-list-item>
                        <v-list-item to="/User/Profile" prepend-icon="mdi-account" title="Profile"></v-list-item>
                        <v-list-item to="/User/MyPasses" prepend-icon="mdi-ticket-account" title="My Passes"></v-list-item>
                        <v-list-item v-if="branding.bikeShopEnabled" to="/User/MyOrders"
                            prepend-icon="mdi-package-variant-closed" title="My Orders"></v-list-item>
                        <v-list-item prepend-icon="mdi-logout" title="Logout" @click="logout"></v-list-item>
                    </v-list-group>
                </template>
                <template v-else>
                    <v-list-item to="/Login" title="Login" prepend-icon="mdi-login"></v-list-item>
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
import { platformBranding, platformImageUrl } from '../stores/platformBranding'
import tenantHelper from '../helpers/TenantHelper'
import NotificationBell from './NotificationBell.vue'
import ImpersonationMenu from './ImpersonationMenu.vue'
import { Perm, type Permission } from '@/helpers/TenantPermissions'

// Every tenant permission, for the staffOnly test below.
const ALL_PERMS = Object.values(Perm) as Permission[]
import { ConcessionService } from '@/services/ConcessionService'

const router = useRouter()
const route = useRoute()
const { mobile } = useDisplay()
const drawer = ref(false)

const isMobile = computed(() => mobile.value)
const isAuthenticated = computed(() => authHelper.isAuthenticated())

// Order Food is only offered to a logged-in rider when F&B is enabled AND currently within operating
// hours. Default closed and only reveal the link once the server confirms ordering is open now (it
// returns true when no hours are configured), so a closed stand never flashes the link. On fetch
// failure we stay hidden; the page + server enforce regardless.
const orderingOpenNow = ref(false)
const showOrderFood = computed(() => isAuthenticated.value && branding.concessionsEnabled && orderingOpenNow.value)
let fetchedOrdering = false
watch(() => [isAuthenticated.value, branding.concessionsEnabled] as const, async ([auth, enabled]) => {
    if (auth && enabled && !fetchedOrdering) {
        fetchedOrdering = true
        try { orderingOpenNow.value = (await new ConcessionService().menuSettings() as any).data.data.orderingOpenNow }
        catch { /* leave closed; the page + server still enforce */ }
    }
}, { immediate: true })

// Pull nav bar styling from whichever branding scope applies: apex (no
// tenant subdomain) uses the platform branding singleton; everything else
// uses the per-tenant branding. The home-page color overrides the rest-
// of-site color only on the home route. NULL falls back to the theme
// primary for background and white for text/icons.
const isApex = computed(() => !tenantHelper.getSubdomain())

// Nav-bar branding: the apex domain pulls its logo from the platform branding
// singleton (super-admin editable); tenant subdomains use the per-tenant logo.
// platformImageUrl makes a relative /uploads/... path absolute against the API host.
const navLogoUrl = computed(() => isApex.value
    ? platformImageUrl(platformBranding.data?.logoUrl)
    : branding.logoUrl)
const navDisplayName = computed(() => branding.displayName)

// Emit the resolved colors as CSS custom properties on the v-app-bar root.
// The :deep selectors in <style> below pull them onto `.v-toolbar__background`
// (the element Vuetify actually paints) and onto child buttons + icons.
const navBarVars = computed(() => {
    const a = isApex.value ? platformBranding.data : null
    const t = !isApex.value ? branding : null

    const bg = a?.navBarColor     ?? t?.navBarColor     ?? null
    const fg = a?.navBarTextColor ?? t?.navBarTextColor ?? null

    // Fallbacks when the tenant left the nav color blank: the theme primary for the
    // background (so a tenant that customized only their primary still gets their color,
    // not a hardcoded brand orange) and white for text/icons.
    const bgValue = bg ?? 'rgb(var(--v-theme-primary))'
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
    { to: '/SuperAdmin/ForTracks', icon: 'mdi-store-plus',          title: 'For Tracks page' },
    { to: '/SuperAdmin/Marketing', icon: 'mdi-bullhorn',            title: 'Marketing' },
    { to: '/SuperAdmin/MiscSettings', icon: 'mdi-cog',              title: 'Misc settings' },
]

// Platform features the super-admin gates per tenant. A link carrying one of these
// is hidden unless the tenant has that feature enabled (so disabled features don't
// show in the admin nav at all).
type FeatureFlag = 'seasonPassesEnabled' | 'extrasEnabled' | 'concessionsEnabled' | 'bikeShopEnabled' | 'sellsSpectatorPasses'
    | 'concessionsEnabled' | 'blogEnabled' | 'membershipEnabled'
// staffOnly: visible to anyone holding at least one tenant permission, i.e. any staff role, but
// not to riders. For links every staffer needs and no single permission describes.
interface AdminLink { to: string; icon: string; title: string; perm: Permission | null; feature?: FeatureFlag; staffOnly?: boolean }
interface AdminGroup { value: string; title: string; icon: string; links: AdminLink[] }

// Direct links: pinned at top of admin menu, no group header.
const allDirectLinks: AdminLink[] = [
    // No single permission describes "works here", so this is staffOnly rather than perm-gated:
    // every staff role should land on the dashboard, and no rider should see it.
    { to: '/Admin/Dashboard', icon: 'mdi-view-dashboard',   title: 'Dashboard', perm: null, staffOnly: true },
    { to: '/Admin/Users',     icon: 'mdi-account-multiple', title: 'Users',     perm: Perm.UsersManage },
    { to: '/Admin/EmployeePasses', icon: 'mdi-card-account-details', title: 'Employee Passes', perm: Perm.UsersManage },
    { to: '/Admin/Customers', icon: 'mdi-account-group',    title: 'Customers', perm: Perm.CustomersView },
    { to: '/Admin/Reports',   icon: 'mdi-chart-line',       title: 'Reporting', perm: Perm.ReportsView },
    { to: '/Admin/Feedback',  icon: 'mdi-message-text',     title: 'Feedback',  perm: Perm.SettingsManage },
    { to: '/Admin/Inbox',     icon: 'mdi-inbox',            title: 'Inbox',     perm: Perm.SettingsManage },
    // Every staffer sees this and lands on their own activity; the whole-track view inside is
    // gated on audit.view by the page and the API. A log people know is there and can open
    // deters more than one they only meet during an investigation. staffOnly keeps it off a
    // rider's menu, since no single permission means "is staff".
    { to: '/Admin/StaffActivity', icon: 'mdi-history',      title: 'Activity',  perm: null, staffOnly: true },
]

// Grouped links: each group is a collapsible accordion. Groups with no permitted items are hidden.
const allGroups: AdminGroup[] = [
    {
        value: 'fnb',
        title: 'Food & Beverage',
        icon: 'mdi-silverware-fork-knife',
        links: [
            { to: '/Admin/Concessions',       icon: 'mdi-cog',                   title: 'Administration', perm: Perm.CatalogManage, feature: 'concessionsEnabled' },
            { to: '/Admin/ConcessionPos',     icon: 'mdi-point-of-sale',         title: 'Cashier Screen', perm: Perm.ConcessionsCounter, feature: 'concessionsEnabled' },
            { to: '/Admin/ConcessionKitchen', icon: 'mdi-stove',                 title: 'Cook Screen',    perm: Perm.ConcessionsCounter, feature: 'concessionsEnabled' },
            { to: '/Admin/ConcessionMenu',    icon: 'mdi-silverware-fork-knife', title: 'Menu Board',     perm: Perm.ConcessionsCounter, feature: 'concessionsEnabled' },
            { to: '/Admin/ConcessionPickupBoard', icon: 'mdi-bell-ring-outline', title: 'Pickup Board',   perm: Perm.ConcessionsCounter, feature: 'concessionsEnabled' },
            { to: '/Admin/ConcessionDisplay', icon: 'mdi-tablet',                title: 'Customer Display', perm: Perm.ConcessionsCounter, feature: 'concessionsEnabled' },
            { to: '/Admin/ConcessionOrders',  icon: 'mdi-receipt-text-clock',    title: 'Order History',  perm: Perm.ConcessionsCounter, feature: 'concessionsEnabled' },
        ],
    },
    {
        value: 'admission',
        title: 'Admission',
        icon: 'mdi-ticket-confirmation',
        links: [
            { to: '/Admin/Events', icon: 'mdi-calendar-month', title: 'Manage Events', perm: Perm.CatalogManage },
            { to: '/Admin/Counter', icon: 'mdi-cash-register', title: 'Gate Sale', perm: Perm.SalesCounter },
            { to: '/Admin/RedeemTickets', icon: 'mdi-qrcode-scan', title: 'Scan Tickets', perm: Perm.SalesRedeem },
            { to: '/Admin/AddOnCheckIn', icon: 'mdi-tent', title: 'Add-on Check-in', perm: Perm.SalesRedeem, feature: 'extrasEnabled' },
            { to: '/Admin/RiderReport', icon: 'mdi-account-group', title: 'Rider Report', perm: Perm.ReportsView },
            { to: '/Admin/SpectatorReport', icon: 'mdi-account-eye', title: 'Spectator Report', perm: Perm.ReportsView, feature: 'sellsSpectatorPasses' },
            { to: '/Admin/BuddyPassUsage', icon: 'mdi-account-multiple-plus', title: 'Buddy Passes', perm: Perm.ReportsView, feature: 'seasonPassesEnabled' },
            { to: '/Admin/EventTypes',   icon: 'mdi-tag-multiple',         title: 'Event Types',   perm: Perm.CatalogManage },
            { to: '/Admin/Instructors',  icon: 'mdi-whistle',              title: 'Instructors',   perm: Perm.CatalogManage },
            { to: '/Admin/Blackouts',    icon: 'mdi-calendar-remove',      title: 'Blackouts',     perm: Perm.CatalogManage },
            { to: '/Admin/SeasonPasses', icon: 'mdi-ticket-percent',       title: 'Season Passes', perm: Perm.CatalogManage, feature: 'seasonPassesEnabled' },
            { to: '/Admin/PassUpgrades', icon: 'mdi-arrow-up-bold-box-outline', title: 'Pass Upgrades', perm: Perm.CatalogManage, feature: 'seasonPassesEnabled' },
            { to: '/Admin/Packages',     icon: 'mdi-package-variant-closed', title: 'Packages',    perm: Perm.CatalogManage },
            { to: '/Admin/Extras',       icon: 'mdi-tag-plus',             title: 'Add-ons',       perm: Perm.CatalogManage, feature: 'extrasEnabled' },
        ],
    },
    {
        value: 'bikeshop',
        title: 'Bike Shop',
        icon: 'mdi-bike',
        links: [
            { to: '/Admin/BikeShop',     icon: 'mdi-package-variant',      title: 'Inventory',   perm: Perm.CatalogManage, feature: 'bikeShopEnabled' },
            { to: '/Admin/BikeShop/Register', icon: 'mdi-cash-register',   title: 'Register',    perm: Perm.ShopCounter, feature: 'bikeShopEnabled' },
            { to: '/Admin/BikeShop/Rentals',  icon: 'mdi-bike-fast',       title: 'Rentals',     perm: Perm.ShopCounter, feature: 'bikeShopEnabled' },
            { to: '/Admin/BikeShop/WorkOrders', icon: 'mdi-wrench',        title: 'Work Orders', perm: Perm.ShopCounter, feature: 'bikeShopEnabled' },
            { to: '/Admin/BikeShop/Sales',    icon: 'mdi-receipt-text-clock', title: 'Shop Sales', perm: Perm.ShopCounter, feature: 'bikeShopEnabled' },
            { to: '/Admin/BikeShop/CustomerDisplay', icon: 'mdi-tablet',    title: 'Customer Display', perm: Perm.ShopCounter, feature: 'bikeShopEnabled' },
            { to: '/Admin/BikeShop/Settings', icon: 'mdi-cog',             title: 'Shop Settings', perm: Perm.CatalogManage, feature: 'bikeShopEnabled' },
        ],
    },
    {
        value: 'waivers',
        title: 'Waivers',
        icon: 'mdi-file-sign',
        links: [
            { to: '/Admin/Waiver', icon: 'mdi-file-document-edit-outline', title: 'Manage Waivers', perm: Perm.CatalogManage },
            { to: '/Admin/SignedWaivers', icon: 'mdi-draw', title: 'Signed Waivers', perm: Perm.CustomersView },
            { to: '/Admin/WaiverCompliance', icon: 'mdi-clipboard-check-outline', title: 'Compliance Today', perm: Perm.CustomersView },
            { to: '/Admin/WaiverRequests', icon: 'mdi-email-arrow-right-outline', title: 'Signature Requests', perm: Perm.CustomersView },
        ],
    },
    {
        value: 'sales',
        title: 'Sales',
        icon: 'mdi-cart-check',
        links: [
            { to: '/Admin/Purchases',     icon: 'mdi-cart-check',     title: 'Purchases',      perm: Perm.SalesView },
            { to: '/Admin/StoreCredit',   icon: 'mdi-wallet-giftcard', title: 'Store Credit',  perm: Perm.CustomersView },
            { to: '/Admin/Payouts',       icon: 'mdi-bank-transfer',  title: 'Payouts',        perm: Perm.ReportsView },
        ],
    },
    {
        value: 'marketing',
        title: 'Marketing',
        icon: 'mdi-bullhorn',
        links: [
            { to: '/Admin/Blog',        icon: 'mdi-post',              title: 'Blog',        perm: Perm.BlogManage, feature: 'blogEnabled' },
            { to: '/Admin/Coupons',     icon: 'mdi-tag-outline',       title: 'Coupons',     perm: Perm.CampaignsManage },
            { to: '/Admin/Subscribers', icon: 'mdi-email-multiple',    title: 'Subscribers', perm: Perm.CampaignsManage },
            { to: '/Admin/Campaigns',   icon: 'mdi-email-newsletter',  title: 'Campaigns',   perm: Perm.CampaignsManage },
            { to: '/Admin/Automations', icon: 'mdi-robot-outline',     title: 'Automations', perm: Perm.CampaignsManage },
            { to: '/Admin/Suppression', icon: 'mdi-email-off',         title: 'Suppression', perm: Perm.CampaignsManage },
            { to: '/Admin/Surveys',     icon: 'mdi-poll',              title: 'Surveys',     perm: Perm.CampaignsManage },
            { to: '/Admin/Settings/Sms', icon: 'mdi-cellphone-message', title: 'SMS',        perm: Perm.SettingsManage },
        ],
    },
    {
        value: 'settings',
        title: 'Settings',
        icon: 'mdi-cog-outline',
        links: [
            { to: '/Admin/Settings/General',  icon: 'mdi-tune',          title: 'General',   perm: Perm.SettingsManage },
            { to: '/Admin/Settings/Features', icon: 'mdi-toggle-switch', title: 'Features',  perm: Perm.SettingsManage },
            { to: '/Admin/Settings/Discounts', icon: 'mdi-sale', title: 'Discounts', perm: Perm.SettingsManage },
            { to: '/Admin/Settings/StaffAccess', icon: 'mdi-shield-lock-outline', title: 'Staff Access', perm: Perm.SettingsManage },
            { to: '/Admin/Settings/HomePage', icon: 'mdi-home-edit',     title: 'Home Page', perm: Perm.SettingsManage },
            { to: '/Admin/Pages',             icon: 'mdi-file-document-outline', title: 'Pages', perm: Perm.SettingsManage },
            { to: '/Admin/Settings/Branding', icon: 'mdi-palette',       title: 'Branding',  perm: Perm.SettingsManage },
            { to: '/Admin/Settings/Payments', icon: 'mdi-credit-card',   title: 'Payments',  perm: Perm.SettingsManage },
            { to: '/Admin/Settings/QuickBooks', icon: 'mdi-book-open-variant', title: 'QuickBooks', perm: Perm.AccountingManage },
            { to: '/Admin/Settings/Membership', icon: 'mdi-card-account-details', title: 'Membership', perm: Perm.SettingsManage, feature: 'membershipEnabled' },
        ],
    },
]

function allowed(link: AdminLink): boolean {
    if (link.perm !== null && !authHelper.hasPermission(link.perm)) return false
    // "Any tenant permission at all" is the closest thing to an is-staff test: every staff role
    // carries at least one, and a rider carries none.
    if (link.staffOnly && !authHelper.hasAnyPermission(...ALL_PERMS)) return false
    // Feature-gated links hide entirely when the super-admin hasn't enabled that
    // platform feature for the tenant.
    if (link.feature && !branding[link.feature]) return false
    return true
}

const directLinks = computed(() => allDirectLinks.filter(allowed))
const visibleGroups = computed<AdminGroup[]>(() =>
    allGroups
        .map(g => ({ ...g, links: g.links.filter(allowed) }))
        .filter(g => g.links.length > 0)
)

// Requires actually being staff, not merely having a visible link. Deriving this from the link
// list alone meant one entry with perm: null (the dashboard) made it true for every signed-in
// user, so riders were shown the admin gear. The staff test is the durable half: a future
// perm: null link cannot bring the menu back for riders on its own.
const hasAdminAccess = computed(() =>
    authHelper.hasAnyPermission(...ALL_PERMS)
    && (directLinks.value.length > 0 || visibleGroups.value.length > 0))

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
    margin-left: 12px;          /* breathing room from the bar's left edge */
    max-height: 40px;
    max-width: 160px;
    width: auto;
    object-fit: contain;
    display: block;
}
/* v-app-bar-title clips its content for text ellipsis, which cropped wide logos.
   Let the title slot show the full image, and flex-center it vertically so a
   block logo doesn't top-align and leave a gap under it. */
.nav-bar-themed :deep(.v-toolbar-title__placeholder) {
    overflow: visible;
    display: flex;
    align-items: center;
}
.nav-bar-themed :deep(.v-toolbar-title) {
    margin-inline-start: 0;
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

/* Exception: the red Impersonating pill keeps its own error-contrast text/icon
   color regardless of the tenant's nav foreground. Must come after the generic
   pin above (equal specificity, so the later rule wins). */
.nav-bar-themed :deep(.impersonation-btn),
.nav-bar-themed :deep(.impersonation-btn .v-btn__content),
.nav-bar-themed :deep(.impersonation-btn .v-icon) {
    color: rgb(var(--v-theme-on-error)) !important;
}

/* Reduce the indent on nested admin nav items so they're slightly inset from the parent
   group rather than way over to the right (Vuetify's default is ~36px). */
:deep(.v-list-group__items > .v-list-item) {
    padding-inline-start: 40px !important;
}
</style>
