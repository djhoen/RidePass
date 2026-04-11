import { createRouter, createWebHistory } from 'vue-router'
import authHelper from '../helpers/AuthHelper'

const routes = [
    // Public routes
    { path: '/', name: 'Home', component: () => import('../views/Home.vue') },
    { path: '/Login', name: 'Login', component: () => import('../views/Login.vue') },
    { path: '/CreateAccount', name: 'CreateAccount', component: () => import('../views/CreateAccount.vue') },
    { path: '/ResetPassword', name: 'ResetPassword', component: () => import('../views/ResetPassword.vue') },
    { path: '/BlogFeed', name: 'BlogFeed', component: () => import('../views/BlogFeed.vue') },
    { path: '/Blog/:url', name: 'BlogPost', component: () => import('../views/BlogPost.vue') },
    { path: '/Faqs', name: 'Faqs', component: () => import('../views/Faqs.vue') },
    { path: '/Cart', name: 'Cart', component: () => import('../views/Cart.vue') },
    { path: '/Checkout', name: 'Checkout', component: () => import('../views/Checkout.vue') },
    { path: '/OrderDetail/:id', name: 'OrderDetail', component: () => import('../views/OrderDetail.vue') },

    // Authenticated user routes
    {
        path: '/User/Profile',
        name: 'Profile',
        component: () => import('../views/User/Profile.vue'),
        meta: { requiresAuth: true }
    },
    {
        path: '/User/OrderHistory',
        name: 'OrderHistory',
        component: () => import('../views/User/OrderHistory.vue'),
        meta: { requiresAuth: true }
    },

    // Admin routes
    {
        path: '/Admin/Users',
        name: 'AdminUsers',
        component: () => import('../views/Admin/Users.vue'),
        meta: { requiresAuth: true, hideFooter: true }
    },
    {
        path: '/Admin/Orders',
        name: 'AdminOrders',
        component: () => import('../views/Admin/AdminOrders.vue'),
        meta: { requiresAuth: true, hideFooter: true }
    },
    {
        path: '/Admin/Order/:id',
        name: 'AdminOrder',
        component: () => import('../views/Admin/AdminOrder.vue'),
        meta: { requiresAuth: true, hideFooter: true }
    },
    {
        path: '/Admin/Blogs',
        name: 'AdminBlogs',
        component: () => import('../views/Admin/AdminBlogs.vue'),
        meta: { requiresAuth: true, hideFooter: true }
    },
    {
        path: '/Admin/Blogs/Post/:id',
        name: 'AdminBlogPost',
        component: () => import('../views/Admin/AdminBlogPost.vue'),
        meta: { requiresAuth: true, hideFooter: true }
    },
    {
        path: '/Admin/Faqs',
        name: 'AdminFaqs',
        component: () => import('../views/Admin/AdminFaqs.vue'),
        meta: { requiresAuth: true, hideFooter: true }
    },
    {
        path: '/Admin/Coupons',
        name: 'AdminCoupons',
        component: () => import('../views/Admin/AdminCoupons.vue'),
        meta: { requiresAuth: true, hideFooter: true }
    },
    {
        path: '/Admin/SiteContent',
        name: 'AdminSiteContent',
        component: () => import('../views/Admin/AdminSiteContent.vue'),
        meta: { requiresAuth: true, hideFooter: true }
    },

    // Error pages
    { path: '/NotFound', name: 'NotFound', component: () => import('../views/NotFound.vue') },
    { path: '/:pathMatch(.*)*', redirect: '/NotFound' }
]

const router = createRouter({
    history: createWebHistory(),
    routes,
    scrollBehavior(to, from, savedPosition) {
        return { top: 0 }
    }
})

// Navigation guard for auth
router.beforeEach((to, from, next) => {
    if (to.meta.requiresAuth && !authHelper.isAuthenticated()) {
        next('/Login')
    } else {
        next()
    }
})

export default router
