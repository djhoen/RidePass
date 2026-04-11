<template>
    <v-container>
        <v-row class="my-12" align="center" justify="center">
            <v-col cols="12" md="8" class="text-center">
                <h1 class="text-h2 font-weight-bold mb-4">Welcome to Our Platform</h1>
                <p class="text-h6 text-medium-emphasis mb-8">
                    A modern solution for all your needs. Get started today and see the difference.
                </p>
                <v-btn color="primary" size="x-large" to="/CreateAccount">Get Started</v-btn>
                <v-btn variant="outlined" size="x-large" class="ml-4" to="/BlogFeed">Learn More</v-btn>
            </v-col>
        </v-row>

        <v-row class="my-8">
            <v-col v-for="feature in features" :key="feature.title" cols="12" md="4">
                <v-card class="pa-6 text-center" elevation="2" height="100%">
                    <v-icon size="48" color="primary" class="mb-4">{{ feature.icon }}</v-icon>
                    <v-card-title class="text-h6">{{ feature.title }}</v-card-title>
                    <v-card-text>{{ feature.description }}</v-card-text>
                </v-card>
            </v-col>
        </v-row>

        <v-row class="my-12" justify="center">
            <v-col cols="12" md="8" class="text-center">
                <v-card color="primary" class="pa-8">
                    <h2 class="text-h4 font-weight-bold mb-4">Ready to get started?</h2>
                    <p class="text-body-1 mb-6">Join thousands of users who trust our platform.</p>
                    <v-btn color="white" size="large" to="/CreateAccount">Create Your Account</v-btn>
                </v-card>
            </v-col>
        </v-row>
    </v-container>
</template>

<script setup lang="ts">
const features = [
    {
        icon: 'mdi-rocket-launch',
        title: 'Fast & Reliable',
        description: 'Built with performance in mind. Lightning fast responses and 99.9% uptime.'
    },
    {
        icon: 'mdi-shield-check',
        title: 'Secure',
        description: 'Enterprise-grade security to keep your data safe and protected at all times.'
    },
    {
        icon: 'mdi-headset',
        title: 'Support',
        description: 'Dedicated support team available to help you whenever you need assistance.'
    }
]
</script>
