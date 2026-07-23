<template>
    <!-- Compact nav-bar affordance replacing the old full-width warning banner:
         a red pill left of the notification bell, with the details + the Stop
         action in a dropdown. Renders nothing when not impersonating. -->
    <v-menu v-if="showing" location="bottom end">
        <template #activator="{ props }">
            <v-btn v-bind="props" color="error" variant="flat" size="small" rounded="pill"
                :class="['impersonation-btn', 'me-1', { 'impersonation-float': floating }]"
                prepend-icon="mdi-account-switch">
                Impersonating
            </v-btn>
        </template>
        <v-card min-width="300" max-width="380">
            <v-card-text class="pb-2">
                <div class="text-caption text-medium-emphasis">Impersonating</div>
                <div class="text-body-2 font-weight-medium">{{ label }}</div>
                <div class="text-caption text-medium-emphasis mt-3">Tenant</div>
                <div class="text-body-2">{{ tenantLabel }}</div>
            </v-card-text>
            <v-divider></v-divider>
            <v-card-actions>
                <v-spacer></v-spacer>
                <v-btn color="error" variant="text" prepend-icon="mdi-account-off" @click="stop">
                    Stop impersonating
                </v-btn>
            </v-card-actions>
        </v-card>
    </v-menu>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import authHelper, { authState, isImpersonating } from '@/helpers/AuthHelper'
import { branding } from '@/stores/branding'
import tenantHelper from '@/helpers/TenantHelper'

// floating: pin the pill fixed to the top-right of the viewport, for chromeless
// (hideNav) pages where there is no nav bar to sit in.
withDefaults(defineProps<{ floating?: boolean }>(), { floating: false })

const router = useRouter()

const showing = isImpersonating
const label = computed(() => authState.impersonatedLabel ?? 'another user')

// On a tenant subdomain the impersonated session belongs to that tenant, whose
// name the branding store already carries. On the apex there is no tenant
// (impersonating a global rider / platform-wide user).
const tenantLabel = computed(() =>
    tenantHelper.getSubdomain() ? branding.displayName : 'None (platform-wide user)')

function stop() {
    if (authHelper.canRestoreLocally()) {
        // Same-origin (e.g. a rider on the apex): restore the stashed super-admin session.
        authHelper.stopImpersonation()
        router.push('/SuperAdmin')
    } else {
        // Cross-origin tenant-admin preview: the super-admin session lives on the apex,
        // not this subdomain. Clear the impersonation token and bounce back to the apex.
        authHelper.logout()
        const rootDomain = import.meta.env.VITE_ROOT_DOMAIN ?? 'ridepass.local'
        const port = window.location.port ? `:${window.location.port}` : ''
        window.location.href = `${window.location.protocol}//${rootDomain}${port}/SuperAdmin`
    }
}
</script>

<style scoped>
/* Chromeless pages: float above the page content in the top-right. Below the
   branding splash (z 10000) but above ordinary page content. */
.impersonation-float {
    position: fixed;
    top: 12px;
    right: 12px;
    z-index: 2000;
}
</style>
