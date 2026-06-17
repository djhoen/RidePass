<template>
    <!-- A layout-aware app bar (order=-1) so Vuetify stacks it ABOVE the main nav bar
         and pushes the nav + page content down, instead of overlapping them. v-app-bar
         also vertically centers its content, so the Stop button sits centered. -->
    <v-app-bar v-if="showing" color="warning" :height="44" :order="-1" flat class="impersonation-bar">
        <v-icon class="ms-4" size="small">mdi-account-switch</v-icon>
        <span class="ms-2 text-body-2">Impersonating <strong>{{ label }}</strong></span>
        <v-spacer></v-spacer>
        <v-btn variant="text" density="comfortable" class="me-2" @click="stop">Stop</v-btn>
    </v-app-bar>
</template>

<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import authHelper from '@/helpers/AuthHelper'

const router = useRouter()
const tick = ref(0)

// Force a re-read on route change (sessionStorage changes aren't reactive).
onMounted(() => {
    router.afterEach(() => tick.value++)
})

const showing = computed(() => {
    tick.value // track
    return authHelper.isImpersonating()
})

const label = computed(() => {
    tick.value
    return authHelper.getImpersonatedLabel() ?? 'another user'
})

function stop() {
    authHelper.stopImpersonation()
    tick.value++
    // Go back to the super admin dashboard.
    router.push('/SuperAdmin')
}
</script>

<style scoped>
/* Keep the impersonation text from going invisible on the warning background. */
.impersonation-bar {
    color: rgba(0, 0, 0, 0.87);
}
</style>
