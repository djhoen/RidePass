<template>
    <v-dialog
        :model-value="state.open"
        max-width="480"
        persistent
        @keydown.esc="cancel">
        <v-card>
            <v-card-title v-if="state.title">{{ state.title }}</v-card-title>
            <v-card-text class="text-body-1" style="white-space: pre-wrap">{{ state.message }}</v-card-text>
            <v-card-actions class="px-4 pb-3">
                <v-spacer></v-spacer>
                <v-btn variant="text" @click="cancel">{{ state.cancelText }}</v-btn>
                <v-btn :color="state.confirmColor" variant="flat" @click="ok">{{ state.confirmText }}</v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
// Mounted once globally in App.vue. Reads from the singleton state in
// useConfirm.ts and resolves the pending promise via _resolveConfirm.
import { confirmState, _resolveConfirm } from '@/composables/useConfirm'

const state = confirmState

function ok() { _resolveConfirm(true) }
function cancel() { _resolveConfirm(false) }
</script>
