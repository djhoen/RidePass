<template>
    <v-container class="py-10 signup-page">
        <h1 class="text-h5 font-weight-bold mb-1">Create your account</h1>
        <p class="text-body-2 text-medium-emphasis mb-5">
            Join {{ branding.displayName }} to manage your entries and check in faster at the gate.
        </p>
        <v-card variant="flat" class="signup-card pa-5">
            <AccountSignupForm :prefill="prefill" />
            <div class="text-center text-body-2 text-medium-emphasis mt-4">
                Already have an account?
                <router-link to="/Login" class="signup-link">Sign in</router-link>
            </div>
        </v-card>
    </v-container>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import AccountSignupForm from '@/components/AccountSignupForm.vue'
import { branding } from '@/stores/branding'

// Standalone signup page — the destination for the "create an account" link in the guest
// confirmation email. Prefills name/email from the query string when present.
const route = useRoute()
const prefill = computed(() => {
    const name = ((route.query.name as string) || '').trim()
    const parts = name.split(/\s+/).filter(Boolean)
    return {
        email: (route.query.email as string) || '',
        firstName: parts[0] ?? '',
        lastName: parts.slice(1).join(' '),
    }
})
</script>

<style scoped>
.signup-page {
    max-width: 520px;
}
.signup-card {
    border: 1px solid rgba(0, 0, 0, 0.08);
    border-radius: 12px;
}
.signup-link {
    color: rgb(var(--v-theme-primary));
    text-decoration: none;
    font-weight: 600;
}
.signup-link:hover {
    text-decoration: underline;
}
</style>
