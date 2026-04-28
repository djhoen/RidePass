<template>
    <v-container class="fill-height" fluid>
        <v-row align="center" justify="center">
            <v-col cols="12" sm="8" md="4">
                <v-card class="pa-4">
                    <v-card-title class="text-h5 text-center">Login</v-card-title>
                    <v-card-text>
                        <v-form @submit.prevent="login">
                            <v-text-field v-model="form.email" label="Email" type="email"
                                prepend-inner-icon="mdi-email" required class="mb-2"></v-text-field>
                            <v-text-field v-model="form.password" label="Password" type="password"
                                prepend-inner-icon="mdi-lock" required class="mb-4"></v-text-field>
                            <v-btn type="submit" color="primary" block size="large" :loading="loading">Login</v-btn>
                        </v-form>
                    </v-card-text>
                    <v-card-actions class="justify-center flex-column">
                        <router-link to="/CreateAccount" class="mb-2">Create an account</router-link>
                        <router-link to="/ResetPassword">Forgot password?</router-link>
                    </v-card-actions>
                </v-card>
            </v-col>
        </v-row>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { UserService } from '@/services/UserService'
import authHelper from '@/helpers/AuthHelper'

const router = useRouter()
const userService = new UserService()

const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('error')

const form = ref({
    email: '',
    password: ''
})

function isTenantStaffRole(role: string): boolean {
    return ['tenant_admin', 'tenant_manager', 'tenant_cashier', 'tenant_scanner',
            'tenant_accountant', 'tenant_staff'].includes(role)
}

async function login() {
    try {
        loading.value = true
        const response = await userService.login(form.value)
        const payload = response.data.data
        authHelper.setToken(payload.token)
        authHelper.setUserId(payload.userId)
        authHelper.setRole(payload.role)
        if (payload.role === 'super_admin') {
            router.push('/SuperAdmin')
        } else if (isTenantStaffRole(payload.role)) {
            router.push('/Admin/Dashboard')
        } else {
            router.push('/')
        }
    } catch (error: any) {
        snackbarText.value = error.response?.data?.error || 'Login failed.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        loading.value = false
    }
}
</script>
