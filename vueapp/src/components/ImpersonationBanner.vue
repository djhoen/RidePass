<template>
    <v-banner v-if="showing" color="warning" density="compact" class="impersonation-banner">
        <template #prepend>
            <v-icon color="warning">mdi-account-switch</v-icon>
        </template>
        <v-banner-text>
            Impersonating <strong>{{ label }}</strong>
        </v-banner-text>
        <template #actions>
            <v-btn variant="text" density="compact" @click="stop">Stop</v-btn>
        </template>
    </v-banner>
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
.impersonation-banner {
    position: sticky;
    top: 0;
    z-index: 1100;
}
</style>
