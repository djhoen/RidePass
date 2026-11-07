import type { Router } from 'vue-router'

/**
 * Front-desk kiosk routes (Script0310), registered on the app router at startup. Kept in their own module
 * so the kiosk feature doesn't edit the shared route table.
 * - /Kiosk: the tablet app. Public (the tablet has no staff session; it holds its own kiosk key),
 *   full screen: no site navigation or footer.
 * - /Admin/Kiosks: pairing and managing the tablets (settings.manage).
 */
export function registerKioskRoutes(router: Router) {
    router.addRoute({
        path: '/Kiosk',
        name: 'Kiosk',
        component: () => import('../views/Kiosk.vue'),
        meta: { hideNav: true, hideFooter: true },
    })
    router.addRoute({
        path: '/Admin/Kiosks',
        name: 'AdminKiosks',
        component: () => import('../views/Admin/Kiosks.vue'),
        meta: { requiresAuth: true, requiresPermission: 'settings.manage', hideFooter: true },
    })
}
