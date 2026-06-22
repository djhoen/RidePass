import { createRouter, createWebHistory } from 'vue-router'
import authHelper from '../helpers/AuthHelper'

const routes = [
    // Public routes
    { path: '/', name: 'Home', component: () => import('../views/Home.vue') },
    { path: '/Login', name: 'Login', component: () => import('../views/Login.vue') },
    { path: '/SignUp', name: 'SignUp', component: () => import('../views/SignUp.vue') },
    { path: '/ResetPassword', name: 'ResetPassword', component: () => import('../views/ResetPassword.vue') },
    { path: '/VerifyEmail', name: 'VerifyEmail', component: () => import('../views/VerifyEmail.vue') },
    { path: '/Events', name: 'Events', component: () => import('../views/Events.vue') },
    { path: '/Blog', name: 'Blog', component: () => import('../views/Blog.vue') },
    { path: '/Blog/:slug', name: 'BlogPost', component: () => import('../views/BlogPost.vue') },
    { path: '/Waiver', name: 'Waiver', component: () => import('../views/Waiver.vue'), meta: { requiresAuth: true } },
    { path: '/EventUnsubscribe/:token', name: 'EventUnsubscribe', component: () => import('../views/EventUnsubscribe.vue') },
    { path: '/SeasonPasses', name: 'SeasonPasses', component: () => import('../views/BuySeasonPass.vue') },
    { path: '/GiftCard', name: 'BuyGiftCard', component: () => import('../views/BuyGiftCard.vue'), meta: { requiresAuth: true } },
    { path: '/Rentals', name: 'Rentals', component: () => import('../views/Rentals.vue'), meta: { requiresAuth: true } },
    {
        path: '/Waitlist/Confirm/:token',
        name: 'WaitlistConfirm',
        component: () => import('../views/WaitlistConfirm.vue'),
        meta: { requiresAuth: true },
    },

    // Authenticated user routes
    {
        path: '/User/Profile',
        name: 'Profile',
        component: () => import('../views/User/Profile.vue'),
        meta: { requiresAuth: true }
    },
    // /BuyTicket and /BuySpectator retired — the event page (/Event/:id) is the single
    // checkout surface now (EventCheckout). Their view files + the old flow components
    // are dead pending cleanup (Home's quick-buy dialog still references BuyAdmissionFlow).
    {
        path: '/Feedback',
        name: 'Feedback',
        component: () => import('../views/Feedback.vue'),
        // Public — guests can submit feedback without an account.
    },
    {
        path: '/User/MyPasses',
        name: 'MyPasses',
        component: () => import('../views/User/MyPasses.vue'),
        meta: { requiresAuth: true }
    },
    {
        path: '/User/Upcoming',
        name: 'UserUpcoming',
        component: () => import('../views/User/Upcoming.vue'),
        meta: { requiresAuth: true }
    },
    {
        path: '/Membership',
        name: 'Membership',
        component: () => import('../views/User/Membership.vue'),
        // Buying a membership requires an account (the purchase needs a user), and the page
        // loads auth-only status on mount, so gate it and let Login return them via ?next.
        meta: { requiresAuth: true }
    },
    {
        path: '/User/Rewards',
        name: 'UserRewards',
        component: () => import('../views/User/Rewards.vue'),
        meta: { requiresAuth: true }
    },
    {
        path: '/User/SeasonPasses',
        name: 'UserSeasonPasses',
        component: () => import('../views/User/SeasonPasses.vue'),
        meta: { requiresAuth: true }
    },
    {
        path: '/redeem/:token',
        name: 'Redeem',
        component: () => import('../views/Redeem.vue'),
        meta: { requiresAuth: true, requiresRoles: ['tenant_admin', 'super_admin'] }
    },

    // Super admin routes
    {
        path: '/SuperAdmin/Bootstrap',
        name: 'SuperAdminBootstrap',
        component: () => import('../views/SuperAdmin/Bootstrap.vue'),
    },
    // /SuperAdmin lands on the Analytics page. Each former Dashboard tab is now its own route.
    { path: '/SuperAdmin', redirect: '/SuperAdmin/Analytics' },
    {
        path: '/SuperAdmin/Analytics',
        name: 'SuperAdminAnalytics',
        component: () => import('../views/SuperAdmin/Analytics.vue'),
        meta: { requiresAuth: true, requiresRoles: ['super_admin'], hideFooter: true }
    },
    {
        path: '/SuperAdmin/Tenants',
        name: 'SuperAdminTenants',
        component: () => import('../views/SuperAdmin/Tenants.vue'),
        meta: { requiresAuth: true, requiresRoles: ['super_admin'], hideFooter: true }
    },
    {
        path: '/SuperAdmin/Users',
        name: 'SuperAdminUsers',
        component: () => import('../views/SuperAdmin/Users.vue'),
        meta: { requiresAuth: true, requiresRoles: ['super_admin'], hideFooter: true }
    },
    {
        path: '/SuperAdmin/Refunds',
        name: 'SuperAdminRefunds',
        component: () => import('../views/SuperAdmin/Refunds.vue'),
        meta: { requiresAuth: true, requiresRoles: ['super_admin'], hideFooter: true }
    },
    {
        path: '/SuperAdmin/Disputes',
        name: 'SuperAdminDisputes',
        component: () => import('../views/SuperAdmin/Disputes.vue'),
        meta: { requiresAuth: true, requiresRoles: ['super_admin'], hideFooter: true }
    },
    {
        path: '/SuperAdmin/Payouts',
        name: 'SuperAdminPayouts',
        component: () => import('../views/SuperAdmin/Payouts.vue'),
        meta: { requiresAuth: true, requiresRoles: ['super_admin'], hideFooter: true }
    },
    {
        path: '/SuperAdmin/Audit',
        name: 'SuperAdminAudit',
        component: () => import('../views/SuperAdmin/Audit.vue'),
        meta: { requiresAuth: true, requiresRoles: ['super_admin'], hideFooter: true }
    },
    {
        path: '/SuperAdmin/Reconcile',
        name: 'SuperAdminReconcile',
        component: () => import('../views/SuperAdmin/Reconcile.vue'),
        meta: { requiresAuth: true, requiresRoles: ['super_admin'], hideFooter: true }
    },
    {
        path: '/SuperAdmin/Marketing',
        name: 'SuperAdminMarketing',
        component: () => import('../views/SuperAdmin/Marketing.vue'),
        meta: { requiresAuth: true, requiresRoles: ['super_admin'], hideFooter: true }
    },
    {
        path: '/SuperAdmin/HomePage',
        name: 'SuperAdminHomePage',
        component: () => import('../views/SuperAdmin/HomePage.vue'),
        meta: { requiresAuth: true, requiresRoles: ['super_admin'], hideFooter: true }
    },
    {
        path: '/SuperAdmin/ForTracks',
        name: 'SuperAdminForTracks',
        component: () => import('../views/SuperAdmin/ForTracks.vue'),
        meta: { requiresAuth: true, requiresRoles: ['super_admin'], hideFooter: true }
    },
    {
        path: '/SuperAdmin/MiscSettings',
        name: 'SuperAdminMiscSettings',
        component: () => import('../views/SuperAdmin/MiscSettings.vue'),
        meta: { requiresAuth: true, requiresRoles: ['super_admin'], hideFooter: true }
    },

    // Admin routes (tenant_admin or super_admin)
    {
        path: '/Admin/Dashboard',
        name: 'AdminDashboard',
        component: () => import('../views/Admin/Dashboard.vue'),
        meta: { requiresAuth: true, hideFooter: true }
    },
    {
        path: '/Admin/Users',
        name: 'AdminUsers',
        component: () => import('../views/Admin/Users.vue'),
        meta: { requiresAuth: true, requiresPermission: 'users.manage', hideFooter: true }
    },
    {
        path: '/Admin/Settings/General',
        name: 'AdminSettingsGeneral',
        component: () => import('../views/Admin/Settings/General.vue'),
        meta: { requiresAuth: true, requiresPermission: 'settings.manage', hideFooter: true }
    },
    {
        path: '/Admin/Settings/Branding',
        name: 'AdminSettingsBranding',
        component: () => import('../views/Admin/Settings/Branding.vue'),
        meta: { requiresAuth: true, requiresPermission: 'settings.manage', hideFooter: true }
    },
    {
        path: '/Admin/Settings/Payments',
        name: 'AdminSettingsPayments',
        component: () => import('../views/Admin/Settings/Payments.vue'),
        meta: { requiresAuth: true, requiresPermission: 'settings.manage', hideFooter: true }
    },
    {
        path: '/Admin/Settings/HomePage',
        name: 'AdminSettingsHomePage',
        component: () => import('../views/Admin/Settings/HomePage.vue'),
        meta: { requiresAuth: true, requiresPermission: 'settings.manage', hideFooter: true }
    },
    {
        path: '/Admin/Settings/Membership',
        name: 'AdminSettingsMembership',
        component: () => import('../views/Admin/Settings/Membership.vue'),
        meta: { requiresAuth: true, requiresPermission: 'settings.manage', hideFooter: true }
    },
    {
        path: '/Admin/Settings/Features',
        name: 'AdminSettingsFeatures',
        component: () => import('../views/Admin/Settings/Features.vue'),
        meta: { requiresAuth: true, requiresPermission: 'settings.manage', hideFooter: true }
    },
    {
        path: '/Admin/Settings/Sms',
        name: 'AdminSettingsSms',
        component: () => import('../views/Admin/Settings/Sms.vue'),
        meta: { requiresAuth: true, requiresPermission: 'settings.manage', hideFooter: true }
    },
    {
        path: '/Admin/Inbox',
        name: 'AdminInbox',
        component: () => import('../views/Admin/Inbox.vue'),
        meta: { requiresAuth: true, requiresPermission: 'settings.manage', hideFooter: true }
    },
    {
        path: '/Admin/Extras',
        name: 'AdminExtras',
        component: () => import('../views/Admin/Extras.vue'),
        meta: { requiresAuth: true, requiresPermission: 'catalog.manage', hideFooter: true }
    },
    {
        path: '/Admin/Concessions',
        name: 'AdminConcessions',
        component: () => import('../views/Admin/Concessions.vue'),
        meta: { requiresAuth: true, requiresPermission: 'catalog.manage', hideFooter: true }
    },
    {
        path: '/Admin/Rentals',
        name: 'AdminRentals',
        component: () => import('../views/Admin/Rentals.vue'),
        meta: { requiresAuth: true, requiresPermission: 'catalog.manage', hideFooter: true }
    },
    {
        path: '/Admin/RentalCounter',
        name: 'AdminRentalCounter',
        component: () => import('../views/Admin/RentalCounter.vue'),
        meta: { requiresAuth: true, requiresPermission: 'sales.counter', hideFooter: true }
    },
    // Old single-page Branding lives under Settings/Branding now; preserve bookmarks.
    { path: '/Admin/Branding', redirect: '/Admin/Settings/Branding' },
    {
        path: '/Admin/EventTypes',
        name: 'AdminEventTypes',
        component: () => import('../views/Admin/EventTypes.vue'),
        meta: { requiresAuth: true, requiresPermission: 'catalog.manage', hideFooter: true }
    },
    {
        path: '/Admin/Events',
        name: 'AdminEvents',
        component: () => import('../views/Admin/Events.vue'),
        meta: { requiresAuth: true, requiresPermission: 'catalog.manage', hideFooter: true }
    },
    {
        path: '/Admin/Blackouts',
        name: 'AdminBlackouts',
        component: () => import('../views/Admin/Blackouts.vue'),
        meta: { requiresAuth: true, requiresPermission: 'catalog.manage', hideFooter: true }
    },
    {
        path: '/Admin/SeasonPasses',
        name: 'AdminSeasonPasses',
        component: () => import('../views/Admin/SeasonPasses.vue'),
        meta: { requiresAuth: true, requiresPermission: 'catalog.manage', hideFooter: true }
    },
    {
        path: '/Admin/Rewards',
        name: 'AdminRewards',
        component: () => import('../views/Admin/Rewards.vue'),
        meta: { requiresAuth: true, requiresPermission: 'catalog.manage', hideFooter: true }
    },
    {
        path: '/Admin/Waiver',
        name: 'AdminWaiver',
        component: () => import('../views/Admin/Waiver.vue'),
        meta: { requiresAuth: true, requiresPermission: 'catalog.manage', hideFooter: true }
    },
    {
        path: '/Admin/Purchases',
        name: 'AdminPurchases',
        component: () => import('../views/Admin/Purchases.vue'),
        meta: { requiresAuth: true, requiresPermission: 'sales.view', hideFooter: true }
    },
    {
        path: '/Admin/Customers',
        name: 'AdminCustomers',
        component: () => import('../views/Admin/Customers.vue'),
        meta: { requiresAuth: true, requiresPermission: 'customers.view', hideFooter: true }
    },
    {
        path: '/Admin/Customers/:userId',
        name: 'AdminCustomerDetail',
        component: () => import('../views/Admin/CustomerDetail.vue'),
        meta: { requiresAuth: true, requiresPermission: 'customers.view', hideFooter: true }
    },
    {
        path: '/Admin/Counter',
        name: 'AdminCounter',
        component: () => import('../views/Admin/Counter.vue'),
        meta: { requiresAuth: true, requiresPermission: 'sales.counter', hideFooter: true }
    },
    {
        path: '/Admin/RedeemTickets',
        name: 'AdminRedeemTickets',
        component: () => import('../views/Admin/RedeemTickets.vue'),
        meta: { requiresAuth: true, requiresPermission: 'sales.redeem', hideFooter: true }
    },
    {
        path: '/Admin/Reports',
        name: 'AdminReports',
        component: () => import('../views/Admin/Reports.vue'),
        meta: { requiresAuth: true, requiresPermission: 'reports.view', hideFooter: true }
    },
    {
        path: '/Admin/Feedback',
        name: 'AdminFeedback',
        component: () => import('../views/Admin/Feedback.vue'),
        meta: { requiresAuth: true, requiresPermission: 'settings.manage', hideFooter: true }
    },
    {
        path: '/Admin/Payouts',
        name: 'AdminPayouts',
        component: () => import('../views/Admin/Payouts.vue'),
        meta: { requiresAuth: true, requiresPermission: 'reports.view', hideFooter: true }
    },
    {
        path: '/Admin/Coupons',
        name: 'AdminCoupons',
        component: () => import('../views/Admin/Coupons.vue'),
        meta: { requiresAuth: true, requiresPermission: 'campaigns.manage', hideFooter: true }
    },
    {
        path: '/Admin/Subscribers',
        name: 'AdminSubscribers',
        component: () => import('../views/Admin/Subscribers.vue'),
        meta: { requiresAuth: true, requiresPermission: 'campaigns.manage', hideFooter: true }
    },
    {
        path: '/Admin/Campaigns',
        name: 'AdminCampaigns',
        component: () => import('../views/Admin/Campaigns.vue'),
        meta: { requiresAuth: true, requiresPermission: 'campaigns.manage', hideFooter: true }
    },
    {
        path: '/Admin/Blog',
        name: 'AdminBlog',
        component: () => import('../views/Admin/Blog.vue'),
        meta: { requiresAuth: true, requiresPermission: 'blog.manage', hideFooter: true }
    },
    {
        // Literal "New" must precede the ":id" route so it isn't captured as an id.
        path: '/Admin/Blog/New',
        name: 'AdminBlogNew',
        component: () => import('../views/Admin/BlogPostEditor.vue'),
        meta: { requiresAuth: true, requiresPermission: 'blog.manage', hideFooter: true }
    },
    {
        path: '/Admin/Blog/:id',
        name: 'AdminBlogEdit',
        component: () => import('../views/Admin/BlogPostEditor.vue'),
        meta: { requiresAuth: true, requiresPermission: 'blog.manage', hideFooter: true }
    },
    {
        path: '/Admin/Suppression',
        name: 'AdminSuppression',
        component: () => import('../views/Admin/Suppression.vue'),
        meta: { requiresAuth: true, requiresPermission: 'campaigns.manage', hideFooter: true }
    },
    {
        path: '/Admin/Surveys',
        name: 'AdminSurveys',
        component: () => import('../views/Admin/Surveys.vue'),
        meta: { requiresAuth: true, requiresPermission: 'campaigns.manage', hideFooter: true }
    },
    {
        path: '/Admin/Surveys/:id',
        name: 'AdminSurveyEdit',
        component: () => import('../views/Admin/SurveyEdit.vue'),
        meta: { requiresAuth: true, requiresPermission: 'campaigns.manage', hideFooter: true }
    },
    {
        path: '/Admin/Surveys/:id/Results',
        name: 'AdminSurveyResults',
        component: () => import('../views/Admin/SurveyResults.vue'),
        meta: { requiresAuth: true, requiresPermission: 'campaigns.manage', hideFooter: true }
    },
    {
        path: '/Survey/:token',
        name: 'PublicSurvey',
        component: () => import('../views/Survey.vue'),
    },
    {
        path: '/Event/:id',
        name: 'PublicEvent',
        component: () => import('../views/Event.vue'),
    },
    // Embedded widgets: chromeless routes a track frames on their own site via embed.js.
    // hideNav/hideFooter strip the app chrome; `embed` drives the auto-resize + origin guard.
    {
        path: '/embed/events',
        name: 'EmbedEvents',
        component: () => import('../views/Embed/EmbedEvents.vue'),
        meta: { hideNav: true, hideFooter: true, embed: true },
    },
    {
        path: '/embed/calendar',
        name: 'EmbedCalendar',
        component: () => import('../views/Embed/EmbedCalendar.vue'),
        meta: { hideNav: true, hideFooter: true, embed: true },
    },
    {
        path: '/embed/event/:id',
        name: 'EmbedEvent',
        component: () => import('../views/Event.vue'),
        meta: { hideNav: true, hideFooter: true, embed: true },
    },
    {
        // Resume page from the "finish your registration" reminder email — guests land
        // here to add rider details + sign the waiver for a paid-but-unregistered order.
        path: '/FinishRegistration/:token',
        name: 'FinishRegistration',
        component: () => import('../views/FinishRegistration.vue'),
    },
    {
        path: '/Unsubscribe/:token',
        name: 'Unsubscribe',
        component: () => import('../views/Unsubscribe.vue'),
    },
    // New suppression-based one-click unsubscribe (campaigns/marketing). Token is in the query.
    {
        path: '/EmailUnsubscribe',
        name: 'EmailUnsubscribe',
        component: () => import('../views/EmailUnsubscribe.vue'),
    },
    // Legacy path — redirect to the new name so existing bookmarks keep working.
    { path: '/Admin/Scanner', redirect: '/Admin/RedeemTickets' },

    {
        path: '/Discover',
        name: 'Discover',
        component: () => import('../views/Discover.vue'),
    },
    {
        path: '/ForTracks',
        name: 'ForTracks',
        component: () => import('../views/ForTracks.vue'),
        // Public apex marketing page — sells RidePass to prospective track operators.
    },

    // Error pages
    { path: '/NotFound', name: 'NotFound', component: () => import('../views/NotFound.vue') },
    { path: '/:pathMatch(.*)*', redirect: '/NotFound' }
]

const router = createRouter({
    history: createWebHistory(),
    routes,
    scrollBehavior() {
        return { top: 0 }
    }
})

router.beforeEach((to, from, next) => {
    // Keep the dashboard-preview flag (and resize frame id) across in-iframe navigations
    // within an embed preview. Without this, clicking an event in the previewed
    // calendar/list lands on /embed/event/:id with no ?preview=1 and trips the
    // "not authorized to embed" guard. Only fires inside a preview session (the
    // source route already carried preview), so real embeds are unaffected.
    if (to.meta.embed && from.query.preview && !to.query.preview) {
        next({
            path: to.path,
            replace: true,
            query: {
                ...to.query,
                preview: String(from.query.preview),
                ...(from.query.rpfid ? { rpfid: String(from.query.rpfid) } : {}),
            },
        })
        return
    }
    if (to.meta.requiresAuth && !authHelper.isAuthenticated()) {
        // Preserve the intended destination so Login can return them after sign-in.
        next(to.path === '/Login' ? '/Login' : { path: '/Login', query: { next: to.fullPath } })
        return
    }
    const requiredPerm = to.meta.requiresPermission as string | undefined
    if (requiredPerm && !authHelper.hasPermission(requiredPerm as any)) {
        next('/')
        return
    }
    const requiredRoles = to.meta.requiresRoles as string[] | undefined
    if (requiredRoles && requiredRoles.length > 0 && !authHelper.hasRole(...requiredRoles)) {
        next('/')
        return
    }
    next()
})

export default router
