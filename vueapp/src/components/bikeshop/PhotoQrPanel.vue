<template>
    <div>
        <v-btn size="small" variant="text" prepend-icon="mdi-qrcode-scan" @click="open = true">
            Take photos on your phone
        </v-btn>

        <v-dialog v-model="open" max-width="380">
            <v-card class="text-center pa-4">
                <v-card-title class="d-flex align-center">
                    <span class="text-body-1">Scan to add photos</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="open = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <QrCode :value="captureUrl" :size="220" />
                    <p class="text-caption text-medium-emphasis mt-3 mb-0">
                        Scan with your phone camera to open this {{ label }} and add photos.
                        You'll sign in once on that phone; after that scanning opens it straight away.
                    </p>
                    <v-btn variant="text" size="small" class="mt-2" prepend-icon="mdi-content-copy"
                        @click="copyLink">{{ copied ? 'Link copied' : 'Copy link instead' }}</v-btn>
                </v-card-text>
            </v-card>
        </v-dialog>
    </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import QrCode from '@/components/QrCode.vue'

// The QR carries a plain deep link to the authenticated capture page, NOT an upload token:
// the phone signs in once and the session persists, so there is no unauthenticated write path
// and nothing sensitive is encoded in a code that might sit on a bench.
const props = defineProps<{ kind: 'work-order' | 'rental'; id: string }>()

const open = ref(false)
const copied = ref(false)

const label = computed(() => props.kind === 'work-order' ? 'work order' : 'rental')
// Absolute, because the phone scanning it has no idea what host the counter screen was on.
const captureUrl = computed(() =>
    `${window.location.origin}/Admin/BikeShop/Photos/${props.kind}/${props.id}`)

async function copyLink() {
    try {
        await navigator.clipboard.writeText(captureUrl.value)
        copied.value = true
        setTimeout(() => { copied.value = false }, 2000)
    } catch {
        // Clipboard is blocked in some embedded/insecure contexts; the QR still works, so
        // this is a convenience that can fail quietly without costing the user anything.
    }
}
</script>
