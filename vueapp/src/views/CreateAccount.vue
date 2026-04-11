<template>
    <v-container class="fill-height" fluid>
        <v-row align="center" justify="center">
            <v-col cols="12" sm="8" md="5">
                <v-card class="pa-4">
                    <v-card-title class="text-h5 text-center">Create Account</v-card-title>
                    <v-card-text>
                        <v-form @submit.prevent="createAccount">
                            <v-row>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="form.firstName" label="First Name" required></v-text-field>
                                </v-col>
                                <v-col cols="12" sm="6">
                                    <v-text-field v-model="form.lastName" label="Last Name" required></v-text-field>
                                </v-col>
                            </v-row>
                            <v-text-field v-model="form.email" label="Email" type="email" required
                                class="mb-2"></v-text-field>
                            <v-text-field v-model="form.password" label="Password" type="password" required
                                class="mb-2"></v-text-field>
                            <v-text-field v-model="form.confirmPassword" label="Confirm Password" type="password"
                                required class="mb-4"></v-text-field>
                            <v-btn type="submit" color="primary" block size="large"
                                :loading="loading">Create Account</v-btn>
                        </v-form>
                    </v-card-text>
                    <v-card-actions class="justify-center">
                        <router-link to="/Login">Already have an account? Login</router-link>
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

const router = useRouter()
const userService = new UserService()

const loading = ref(false)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref('error')

const form = ref({
    firstName: '',
    lastName: '',
    email: '',
    password: '',
    confirmPassword: ''
})

async function createAccount() {
    if (form.value.password !== form.value.confirmPassword) {
        snackbarText.value = 'Passwords do not match.'
        snackbarColor.value = 'error'
        snackbar.value = true
        return
    }
    try {
        loading.value = true
        await userService.createAccount(form.value)
        snackbarText.value = 'Account created successfully!'
        snackbarColor.value = 'success'
        snackbar.value = true
        setTimeout(() => router.push('/Login'), 1500)
    } catch (error: any) {
        snackbarText.value = error.response?.data?.message || 'Failed to create account.'
        snackbarColor.value = 'error'
        snackbar.value = true
    } finally {
        loading.value = false
    }
}
</script>
