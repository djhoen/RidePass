<template>
    <v-container class="fill-height" fluid>
        <v-row align="center" justify="center">
            <v-col cols="12" sm="8" md="6">
                <v-card class="pa-4">
                    <v-card-title class="text-h5">Bootstrap RidePass</v-card-title>
                    <v-card-text>
                        <!-- Checked rather than assumed. This route has no role guard, and cannot have
                             a static one: during a genuine first run there is nobody to authenticate
                             as, so the page must stay reachable. What it must NOT do is present a form
                             that can only fail once the platform is already initialised. -->
                        <div v-if="checking" class="py-6 text-center">
                            <v-progress-circular indeterminate color="primary"></v-progress-circular>
                        </div>
                        <v-alert v-else-if="!needed" type="info" variant="tonal">
                            <div class="font-weight-medium mb-1">RidePass is already set up.</div>
                            This page only creates the very first super admin, so there is nothing to do
                            here. Sign in with an existing super admin account instead.
                            <div class="mt-3">
                                <v-btn color="primary" variant="flat" size="small" to="/Login">Go to sign in</v-btn>
                            </div>
                        </v-alert>
                        <template v-else>
                        <p class="text-body-2 text-medium-emphasis mb-4">
                            Create the first super admin. This only works during initial setup; once a super admin exists, the server rejects further attempts.
                        </p>
                        <v-form @submit.prevent="submit">
                            <v-row>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="form.firstName" label="First name" required></v-text-field>
                                </v-col>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="form.lastName" label="Last name" required></v-text-field>
                                </v-col>
                            </v-row>
                            <v-text-field v-model="form.email" type="email" label="Email" required class="mb-2"></v-text-field>
                            <v-text-field v-model="form.password" type="password" label="Password" required class="mt-4"></v-text-field>
                            <v-btn type="submit" color="primary" block size="large" :loading="loading" class="mt-4">Create Super Admin</v-btn>
                        </v-form>
                        </template>
                    </v-card-text>
                </v-card>
            </v-col>
        </v-row>
        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { SuperAdminService } from '@/services/SuperAdminService'

const router = useRouter()
const service = new SuperAdminService()

const checking = ref(true)
const needed = ref(false)

onMounted(async () => {
    try {
        needed.value = (await service.bootstrapNeeded()).data.data.needed
    } catch {
        // Can't tell: show the form rather than a wrong "already set up". The server refuses a
        // second bootstrap anyway, so the worst case is an honest error on submit.
        needed.value = true
    } finally {
        checking.value = false
    }
})

const form = ref({ email: '', password: '', firstName: '', lastName: '' })
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

async function submit() {
    try {
        loading.value = true
        await service.bootstrap(form.value)
        snackbarText.value = 'Super admin created. Sign in now.'
        snackbarColor.value = 'success'
        snackbar.value = true
        setTimeout(() => router.push('/Login'), 1200)
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Bootstrap failed.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        loading.value = false
    }
}
</script>
