import { createRouter, createWebHistory } from 'vue-router'
import authHelper from '../helpers/AuthHelper'

const routes = [
    // Public routes
    { path: '/', name: 'Home', component: () => import('../views/Home.vue') },
    { path: '/Login', name: 'Login', component: () => import('../views/Login.vue') },
    { path: '/CreateAccount', name: 'CreateAccount', component: () => import('../views/CreateAccount.vue') },
    { path: '/ResetPassword', name: 'ResetPassword', component: () => import('../views/ResetPassword.vue') },

    // Authenticated user routes
    {
        path: '/User/Profile',
        name: 'Profile',
        component: () => import('../views/User/Profile.vue'),
        meta: { requiresAuth: true }
    },
    {
        path: '/BuyPass',
        name: 'BuyPass',
        component: () => import('../views/BuyPass.vue'),
        meta: { requiresAuth: true }
    },
    {
        path: '/BuyTicket/:eventId',
        name: 'BuyTicket',
        component: () => import('../views/BuyTicket.vue'),
        meta: { requiresAuth: true }
    },
    {
        path: '/User/MyPasses',
        name: 'MyPasses',
        component: () => import('../views/User/MyPasses.vue'),
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
    {
        path: '/SuperAdmin',
        name: 'SuperAdmin',
        component: () => import('../views/SuperAdmin/Dashboard.vue'),
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
        path: '/Admin/Branding',
        name: 'AdminBranding',
        component: () => import('../views/Admin/Branding.vue'),
        meta: { requiresAuth: true, requiresPermission: 'settings.manage', hideFooter: true }
    },
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
        path: '/Admin/DayPasses',
        name: 'AdminDayPasses',
        component: () => import('../views/Admin/DayPasses.vue'),
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
        path: '/Unsubscribe/:token',
        name: 'Unsubscribe',
        component: () => import('../views/Unsubscribe.vue'),
    },
    // Legacy path — redirect to the new name so existing bookmarks keep working.
    { path: '/Admin/Scanner', redirect: '/Admin/RedeemTickets' },

    {
        path: '/Discover',
        name: 'Discover',
        component: () => import('../views/Discover.vue'),
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

router.beforeEach((to, _from, next) => {
    if (to.meta.requiresAuth && !authHelper.isAuthenticated()) {
        next('/Login')
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
