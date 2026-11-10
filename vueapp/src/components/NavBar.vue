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
                <!-- Apex default: the RidePass badge in the bar's foreground color. -->
                <BrandLogo v-else-if="isApex" :height="30" class="nav-brand-svg" />
                <span v-else>{{ navDisplayName }}</span>
            </router-link>
        </v-app-bar-title>

        <template v-if="!isMobile">
            <!-- Public site links: shown to everyone (including signed-in admins / super admins)
                 so they can always navigate back to Events etc. The logo is the Home link, as
                 in the landing-page mockup. Tracked-uppercase "nav-link" styling below. -->
            <div class="nav-links">
                <template v-if="isApex">
                    <v-btn to="/Events" variant="text" class="nav-link">Events</v-btn>
                    <v-btn to="/Discover" variant="text" class="nav-link">Tracks</v-btn>
                    <!-- "Season passes" (/#passes) returns when platform branding carries real pass
                         tiers; the landing section is hidden until then (see Home.vue apexPassTiers). -->
                    <!-- Apex only: operator-acquisition page. Meaningless on a tenant's own site. -->
                    <v-btn to="/ForTracks" variant="text" class="nav-link">For track owners</v-btn>
                </template>
                <template v-else>
                    <v-btn to="/Events" variant="text" class="nav-link">Events</v-btn>
                    <v-btn v-if="branding.seasonPassesEnabled" to="/SeasonPasses" variant="text" class="nav-link">Season passes</v-btn>
                    <v-btn v-if="branding.membershipEnabled" to="/Membership" variant="text" class="nav-link">Membership</v-btn>
                    <v-btn v-if="branding.giftCardsEnabled" to="/GiftCard" variant="text" class="nav-link">Gift cards</v-btn>
                    <v-btn v-if="branding.bikeShopEnabled" to="/Shop" variant="text" class="nav-link">Shop</v-btn>
                    <v-btn v-if="branding.bikeShopEnabled" to="/Rentals" variant="text" class="nav-link">Rentals</v-btn>
                    <!-- Standalone /Rentals retired — rentals are booked at the Bike Shop counter now. -->
                    <v-btn v-if="showOrderFood" to="/Order" variant="text" class="nav-link">Order food</v-btn>
                    <v-btn v-if="branding.blogEnabled" to="/Blog" variant="text" class="nav-link">Blog</v-btn>
                    <v-btn v-for="p in branding.navPages" :key="p.slug" :to="'/' + p.slug" variant="text" class="nav-link">{{ p.label }}</v-btn>
                </template>
            </div>

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
                <!-- Mockup: ghost "Sign in" + solid "Get the pass". On a tenant site the solid
                     CTA points at season passes when the track sells them, else at events. -->
                <v-btn to="/Login" variant="outlined" class="nav-cta nav-cta-ghost mr-2">Sign in</v-btn>
                <v-btn v-if="isApex" to="/SignUp" color="primary" variant="flat" class="nav-cta nav-cta-solid">Get the pass</v-btn>
                <v-btn v-else-if="branding.seasonPassesEnabled" to="/SeasonPasses" color="primary" variant="flat" class="nav-cta nav-cta-solid">Get the pass</v-btn>
                <v-btn v-else to="/Events" color="primary" variant="flat" class="nav-cta nav-cta-solid">See events</v-btn>
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
         Tenant Admin otherwise. Also used as the mobile menu container.

         On admin routes at desktop width it becomes the operator-console sidebar from the
         design reference: a permanent 236px panel with a brand block (tenant admin on the left,
         super admin on the right), so the
         console reads as an app rather than a public page with a pop-out menu. Everywhere
         else (public pages, phones) it stays the temporary right-hand menu. -->
    <v-navigation-drawer v-model="drawer"
        :location="adminShell && !isSuperAdmin ? 'left' : 'right'"
        :permanent="adminShell"
        :temporary="!adminShell"
        :width="adminShell ? 236 : 320"
        class="rp-drawer" :class="{ 'rp-drawer--shell': adminShell }">
        <div v-if="adminShell" class="rp-side-brand">
            <router-link to="/" class="rp-side-brand-link">
                <img v-if="navLogoUrl" :src="navLogoUrl" class="rp-side-logo" :alt="navDisplayName" />
                <BrandLogo v-else-if="isApex || isSuperAdmin" :height="42" class="rp-side-brand-svg" />
                <span v-else class="font-display rp-side-wordmark">{{ navDisplayName }}</span>
            </router-link>
            <div class="rp-side-tag">{{ isSuperAdmin ? 'Platform admin' : 'Track console' }}</div>
        </div>
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
                                :active="link.exactQuery ? isExactLink(link.to) : undefined"
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
                    <v-list-item to="/Login" title="Sign in" prepend-icon="mdi-login"></v-list-item>
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
import BrandLogo from './BrandLogo.vue'
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

// Operator-console shell: on /Admin and /SuperAdmin at desktop width the drawer is a
// permanent left sidebar (see the template comment). Keyed off the PATH so every admin
// route gets it automatically, matching the full-width policy in App.vue.
const adminShell = computed(() => !isMobile.value
    && isAuthenticated.value
    && (route.path.startsWith('/Admin') || route.path.startsWith('/SuperAdmin')))
// Open the sidebar when entering the console; fold it away when leaving, so a public page
// visited afterwards does not inherit an open right-hand menu.
watch(adminShell, (on) => { drawer.value = on }, { immediate: true })

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

    // Fallbacks when the nav color is blank: the design-system bar — translucent page
    // color (blurred over the hero) with ink text, so it follows light/dark. A tenant or
    // the platform admin can still pin an explicit color pair in Branding; it is honored
    // as-is.
    const bgValue = bg ?? 'var(--rp-bar)'
    const fgValue = fg ?? 'var(--rp-ink)'
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
    { to: '/SuperAdmin/TenantProvisioning', icon: 'mdi-clipboard-check-outline', title: 'Tenant provisioning' },
]

// Platform features the super-admin gates per tenant. A link carrying one of these
// is hidden unless the tenant has that feature enabled (so disabled features don't
// show in the admin nav at all).
type FeatureFlag = 'seasonPassesEnabled' | 'extrasEnabled' | 'concessionsEnabled' | 'bikeShopEnabled' | 'sellsSpectatorPasses' | 'giftCardsEnabled'
    | 'concessionsEnabled' | 'blogEnabled' | 'membershipEnabled'
// staffOnly: visible to anyone holding at least one tenant permission, i.e. any staff role, but
// not to riders. For links every staffer needs and no single permission describes.
interface AdminLink {
    to: string; icon: string; title: string; perm: Permission | null
    feature?: FeatureFlag; staffOnly?: boolean
    /**
     * Match the query string too when deciding whether this link is the active one. Needed for
     * the Reporting hub's `?report=` deep links: router-link's own active matching compares
     * paths only, so without this every report link would highlight at once.
     */
    exactQuery?: boolean
}
interface AdminGroup { value: string; title: string; icon: string; links: AdminLink[] }

// Direct links: pinned at top of admin menu, no group header.
const allDirectLinks: AdminLink[] = [
    // No single permission describes "works here", so this is staffOnly rather than perm-gated:
    // every staff role should land on the dashboard, and no rider should see it.
    { to: '/Admin/Dashboard', icon: 'mdi-view-dashboard',   title: 'Dashboard', perm: null, staffOnly: true },
    { to: '/Admin/Users',     icon: 'mdi-account-multiple', title: 'Users',     perm: Perm.UsersManage },
    { to: '/Admin/EmployeePasses', icon: 'mdi-card-account-details', title: 'Employee Passes', perm: Perm.UsersManage },
    // Every staffer gives vouchers from their own allowance (Script0315); Admins manage them there.
    { to: '/Admin/Vouchers', icon: 'mdi-gift-outline', title: 'Vouchers', perm: null, staffOnly: true },
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
        title: 'Tickets and Passes',
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
            { to: '/Admin/DayPricing',   icon: 'mdi-calendar-star',        title: 'Peak Days',     perm: Perm.CatalogManage },
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
            { to: '/Admin/GiftCards',     icon: 'mdi-gift',           title: 'Gift Cards',     perm: Perm.SalesView, feature: 'giftCardsEnabled' },
            { to: '/Admin/StoreCredit',   icon: 'mdi-wallet-giftcard', title: 'Store Credit',  perm: Perm.CustomersView },
        ],
    },
    {
        // Everything the person who closes the books touches, in the order they touch it: close
        // the day, see where the money came from, work out what to remit, check what was paid
        // out, then the setup that decides how all of it lands in QuickBooks.
        //
        // The first four are panes of the Reporting hub rather than pages of their own, so they
        // are deep links (`?report=`) and need `exactQuery` to keep the active highlight on the
        // one actually open, since router-link matching ignores the query string.
        value: 'accounting',
        title: 'Accounting',
        icon: 'mdi-calculator-variant-outline',
        links: [
            { to: '/Admin/Reports?report=end-of-day', icon: 'mdi-cash-register', title: 'End of Day', perm: Perm.ReportsView, exactQuery: true },
            { to: '/Admin/Reports/TipsOwed', icon: 'mdi-hand-coin-outline', title: 'Tips owed', perm: Perm.CashReconcile },
            { to: '/Admin/Reports?report=revenue-by-department', icon: 'mdi-office-building-outline', title: 'Revenue by Department', perm: Perm.ReportsView, exactQuery: true },
            { to: '/Admin/Reports?report=tax', icon: 'mdi-receipt-text-outline', title: 'Tax', perm: Perm.ReportsView, exactQuery: true },
            { to: '/Admin/Reports?report=sales-summary', icon: 'mdi-chart-line', title: 'Sales Summary', perm: Perm.ReportsView, exactQuery: true },
            { to: '/Admin/Payouts',       icon: 'mdi-bank-transfer',  title: 'Payouts',        perm: Perm.ReportsView },
            { to: '/Admin/Settings/ProfitCenters', icon: 'mdi-chart-pie', title: 'Profit Centers', perm: Perm.AccountingManage },
            { to: '/Admin/Settings/QuickBooks', icon: 'mdi-book-open-variant', title: 'QuickBooks', perm: Perm.AccountingManage },
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
            { to: '/Admin/Email',       icon: 'mdi-bullhorn-outline',  title: 'Campaigns',   perm: Perm.CampaignsManage },
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
            // QuickBooks and Profit Centers live under Accounting now, with the reports they feed.
            { to: '/Admin/Settings/Membership', icon: 'mdi-card-account-details', title: 'Membership', perm: Perm.SettingsManage, feature: 'membershipEnabled' },
        ],
    },
]

/**
 * Is this query-carrying link the one currently open? router-link's active matching compares
 * paths only, so without this every `/Admin/Reports?report=…` link in the Accounting group would
 * highlight at once whenever any report was open.
 */
function isExactLink(to: string): boolean {
    const [path, queryString] = to.split('?')
    if (route.path !== path) return false
    const params = new URLSearchParams(queryString ?? '')
    for (const [key, value] of params) {
        let current = route.query[key]
        // The Reporting hub falls back to Sales Summary when the URL carries no report, so treat
        // a bare /Admin/Reports as that pane rather than leaving nothing highlighted.
        if (current === undefined && key === 'report' && path === '/Admin/Reports') {
            current = 'sales-summary'
        }
        if (current !== value) return false
    }
    return true
}

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
        // Compare against the PATH half only: the Accounting group's report links carry a
        // `?report=` query, and a raw startsWith against that would never match route.path, so
        // the group would stay collapsed on the very page it points at.
        if (group.links.some(l => route.path.startsWith(l.to.split('?')[0]))) {
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
    /* Design system bar: fixed, blurred over whatever scrolls under it, hairline
       bottom edge, no shadow. */
    backdrop-filter: blur(14px);
    -webkit-backdrop-filter: blur(14px);
    border-bottom: 1px solid var(--rp-line) !important;
    box-shadow: none !important;
}
/* Title link inherits the bar foreground. currentColor cascades to the
   underline and visited states so theming reads consistently. */
.nav-title,
.nav-title:hover,
.nav-title:focus,
.nav-title:visited {
    color: var(--nav-bar-fg, var(--rp-ink));
    text-decoration: none;
    font-family: var(--rp-font-display);
    font-style: italic;
    font-size: 26px;
    letter-spacing: 0.01em;
    text-transform: uppercase;
    display: inline-flex;
    align-items: center;
    margin-left: 12px;
}
.nav-brand-svg {
    margin-left: 12px;
    color: var(--nav-bar-fg, var(--rp-ink));
}
.rp-side-brand-svg {
    color: var(--rp-ink);
}
.nav-logo {
    margin-left: 12px;          /* breathing room from the bar's left edge */
    max-height: 34px;
    max-width: 160px;
    width: auto;
    object-fit: contain;
    display: block;
}
/* Tracked-uppercase text links: muted at rest, ink on hover/active. Must beat the
   generic foreground pin below (it has higher specificity, so no !important race). */
.nav-links {
    display: flex;
    align-items: center;
    gap: 6px;
    margin-left: 18px;
}
.nav-bar-themed :deep(.v-btn.nav-link) {
    font-size: 12px;
    font-weight: 800;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    padding: 0 10px;
    min-width: 0;
    color: color-mix(in srgb, var(--nav-bar-fg, var(--rp-ink)) 72%, transparent) !important;
}
.nav-bar-themed :deep(.v-btn.nav-link:hover),
.nav-bar-themed :deep(.v-btn.nav-link.v-btn--active) {
    color: var(--nav-bar-fg, var(--rp-ink)) !important;
}
.nav-bar-themed :deep(.v-btn.nav-link .v-btn__overlay) {
    opacity: 0 !important;
}
/* CTA pair on the right. The solid one is white-on-black regardless of the bar's
   foreground pin (which would otherwise paint its label white-on-white). */
.nav-bar-themed :deep(.v-btn.nav-cta) {
    height: 38px;
    padding: 0 18px;
    font-size: 12.5px;
}
.nav-bar-themed :deep(.v-btn.nav-cta-ghost) {
    border-color: color-mix(in srgb, var(--nav-bar-fg, var(--rp-ink)) 35%, transparent);
}
.nav-bar-themed :deep(.v-btn.nav-cta-ghost:hover) {
    border-color: var(--nav-bar-fg, var(--rp-ink));
}
.nav-bar-themed :deep(.v-btn.nav-cta-solid),
.nav-bar-themed :deep(.v-btn.nav-cta-solid .v-btn__content) {
    color: rgb(var(--v-theme-on-primary)) !important;
    margin-right: 12px;
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
    color: var(--nav-bar-fg, var(--rp-ink)) !important;
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

/* Operator-console sidebar (admin routes, desktop). Brand block on top, then the nav. */
.rp-side-brand {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 9px;
    padding: 10px 10px 16px;
    border-bottom: 1px solid var(--rp-line);
    margin-bottom: 6px;
}
.rp-side-brand-link {
    display: block;
    color: var(--rp-ink);
}
.rp-side-logo {
    width: 100%;
    max-width: 180px;
    max-height: 44px;
    height: auto;
    object-fit: contain;
    display: block;
}
.rp-side-wordmark {
    font-size: 26px;
    color: var(--rp-ink);
}
.rp-side-tag {
    font-size: 9px;
    letter-spacing: 0.2em;
    color: var(--rp-muted2);
    font-weight: 800;
    text-transform: uppercase;
    padding-left: 3px;
}
.rp-drawer--shell :deep(.v-list-item) {
    min-height: 38px;
}
.rp-drawer--shell :deep(.v-list-item__prepend > .v-icon) {
    font-size: 18px;
    opacity: 0.9;
    margin-inline-end: 11px;
}
.rp-drawer--shell :deep(.v-list-group__items > .v-list-item) {
    padding-inline-start: 36px !important;
    min-height: 34px;
}
</style>
