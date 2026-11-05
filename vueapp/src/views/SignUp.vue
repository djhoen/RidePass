<template>
    <v-container class="py-10 signup-page">
        <div class="signup-card">
            <h1 class="signup-title font-display mb-1">Create your account</h1>
            <p class="text-body-2 text-medium-emphasis mb-5">
                Join {{ branding.displayName }} to manage your entries and check in faster at the gate.
            </p>
            <AccountSignupForm :prefill="prefill" />
            <div class="text-center text-body-2 text-medium-emphasis mt-4">
                Already have an account?
                <router-link to="/Login" class="signup-link">Sign in</router-link>
            </div>
        </div>
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
/* Centered card, same "opcard" treatment as Login. A little wider than the 400px
   sign-in card because the form has side-by-side name fields. */
.signup-page {
    max-width: 520px;
    display: flex;
    justify-content: center;
}
.signup-card {
    width: 100%;
    max-width: 460px;
    background: var(--rp-panel);
    border: 1px solid var(--rp-line2);
    border-radius: 18px;
    padding: 30px 26px;
}
.signup-title { font-size: 2rem; }
.signup-link {
    color: var(--rp-ink);
    text-decoration: none;
    font-weight: 700;
}
.signup-link:hover {
    text-decoration: underline;
}
</style>
