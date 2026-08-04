<template>
    <v-btn size="small" variant="text" prepend-icon="mdi-tablet" @click="open = true">Display</v-btn>

    <v-dialog v-model="open" max-width="440">
        <v-card>
            <v-card-title class="d-flex align-center">
                Customer display
                <v-spacer />
                <v-btn icon="mdi-close" variant="text" size="small" @click="open = false" />
            </v-card-title>
            <v-card-text>
                <template v-if="shopDisplayPaired">
                    <p class="text-body-2 mb-0">
                        <v-icon color="success" size="small">mdi-check-circle</v-icon>
                        Paired with a customer display. It mirrors charges, and rental agreements and
                        waivers can be signed on it.
                    </p>
                </template>
                <template v-else>
                    <p class="text-caption text-medium-emphasis mb-3">
                        Open <strong>Customer Display</strong> from the Bike Shop menu on the
                        customer-facing tablet, then enter the pair code it shows here.
                    </p>
                    <v-text-field v-model="codeInput" label="Pair code" placeholder="123456"
                        density="compact" hide-details autofocus @keyup.enter="pair" />
                    <div v-if="error" class="text-error text-caption mt-2">{{ error }}</div>
                </template>
            </v-card-text>
            <v-card-actions>
                <v-btn v-if="shopDisplayPaired" variant="text" color="error" @click="unpair">Unpair</v-btn>
                <v-spacer />
                <v-btn variant="text" @click="open = false">Close</v-btn>
                <v-btn v-if="!shopDisplayPaired" color="primary" variant="flat" :loading="pairing" @click="pair">Pair</v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { shopDisplayPaired, pairShopDisplay, unpairShopDisplay } from '@/helpers/ShopDisplay'

const open = ref(false)
const codeInput = ref('')
const pairing = ref(false)
const error = ref('')

async function pair() {
    const code = codeInput.value.trim()
    if (!code) { error.value = 'Enter the pair code shown on the customer display tablet.'; return }
    pairing.value = true
    error.value = ''
    try {
        await pairShopDisplay(code)
        codeInput.value = ''
        open.value = false
    } catch (err: any) {
        error.value = err.response?.data?.error || 'Pairing failed. Check the code on the customer display tablet.'
    } finally { pairing.value = false }
}

function unpair() {
    unpairShopDisplay()
    open.value = false
}
</script>
