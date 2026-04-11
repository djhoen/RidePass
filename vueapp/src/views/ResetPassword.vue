<template>
    <v-container class="fill-height" fluid>
        <v-row align="center" justify="center">
            <v-col cols="12" sm="8" md="4">
                <v-card class="pa-4">
                    <v-card-title class="text-h5 text-center">Reset Password</v-card-title>
                    <v-card-text>
                        <p class="text-body-2 text-medium-emphasis mb-4">
                            Enter your email address and we'll send you instructions to reset your password.
                        </p>
                        <v-form @submit.prevent="resetPassword">
                            <v-text-field v-model="email" label="Email" type="email" prepend-inner-icon="mdi-email"
                                required class="mb-4"></v-text-field>
                            <v-btn type="submit" color="primary" block size="large"
                                :loading="loading">Send Reset Link</v-btn>
                        </v-form>
                    </v-card-text>
                    <v-card-actions class="justify-center">
                        <router-link to="/Login">Back to Login</router-link>
                    </v-card-actions>
                </v-card>
            </v-col>
        </v-row>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { UserService } from '@/services/UserService'

const userService = new UserService()

const email = ref('')
const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('success')

async function resetPassword() {
    try {
        loading.value = true
        await userService.resetPassword({ email: email.value })
        snackbarText.value = 'If that email exists, a reset link has been sent.'
        snackbarColor.value = 'success'
        snackbar.value = true
    } catch (error: any) {
        snackbarText.value = error.response?.data?.message || 'Something went wrong.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        loading.value = false
    }
}
</script>
