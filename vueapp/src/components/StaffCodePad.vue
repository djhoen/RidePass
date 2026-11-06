<template>
    <!-- Numeric keypad for staff codes at the register. Big buttons for a tablet; a USB keypad or
         keyboard works too. The code is never shown, only counted. -->
    <v-dialog :model-value="modelValue" max-width="340" persistent @update:model-value="emit('update:modelValue', $event)">
        <v-card>
            <v-card-title class="d-flex align-center">
                {{ title }}
                <v-spacer />
                <v-btn icon="mdi-close" variant="text" size="small" :disabled="loading" @click="cancel" />
            </v-card-title>
            <v-card-text class="pt-0">
                <p v-if="subtitle" class="text-caption text-medium-emphasis mb-3">{{ subtitle }}</p>

                <!-- Keyboard sink. Visually hidden; one-time-code keeps password managers and autofill
                     out of it. Focused only on pointer devices so a tablet never pops its keyboard. -->
                <input ref="hiddenEl" class="pad__hidden" type="password" inputmode="numeric" autocomplete="one-time-code"
                    tabindex="-1" aria-label="Staff code" :value="code" :disabled="loading"
                    @input="onHiddenInput" @keydown.enter.prevent="submit" @keydown.esc.prevent="cancel" />

                <div class="pad__dots" aria-live="polite" :aria-label="`${code.length} digits entered`">
                    <span v-for="i in Math.max(minDigits, code.length)" :key="i" class="pad__dot" :class="{ 'pad__dot--on': i <= code.length }" />
                </div>
                <div class="pad__error text-caption text-center" :class="{ 'pad__error--on': !!error }">{{ error || ' ' }}</div>

                <div class="pad__grid">
                    <v-btn v-for="d in DIGITS" :key="d" class="pad__key" variant="tonal" height="56" :disabled="loading" @click="push(d)">{{ d }}</v-btn>
                    <v-btn class="pad__key" variant="text" height="56" :disabled="loading || !code" aria-label="Clear" @click="clear">C</v-btn>
                    <v-btn class="pad__key" variant="tonal" height="56" :disabled="loading" @click="push('0')">0</v-btn>
                    <v-btn class="pad__key" variant="text" height="56" icon="mdi-backspace-outline" :disabled="loading || !code" aria-label="Backspace" @click="backspace" />
                </div>
            </v-card-text>
            <v-card-actions>
                <v-btn variant="text" :disabled="loading" @click="cancel">Cancel</v-btn>
                <v-spacer />
                <v-btn color="primary" variant="flat" :loading="loading" :disabled="code.length < minDigits" @click="submit">{{ submitLabel }}</v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, watch, onUnmounted, nextTick } from 'vue'

const props = withDefaults(defineProps<{
    modelValue: boolean
    title?: string
    subtitle?: string
    submitLabel?: string
    loading?: boolean
    error?: string          // server message; clears the typed code so the person starts over
    minDigits?: number
    maxDigits?: number
}>(), { title: 'Enter your staff code', subtitle: '', submitLabel: 'Enter', loading: false, error: '', minDigits: 4, maxDigits: 8 })

const emit = defineEmits<{
    (e: 'update:modelValue', open: boolean): void
    (e: 'submit', code: string): void
    (e: 'cancel'): void
}>()

const DIGITS = ['1', '2', '3', '4', '5', '6', '7', '8', '9']
const code = ref('')
const hiddenEl = ref<HTMLInputElement | null>(null)

function push(d: string) {
    if (props.loading || code.value.length >= props.maxDigits) return
    code.value += d
}
function backspace() { if (!props.loading) code.value = code.value.slice(0, -1) }
function clear() { if (!props.loading) code.value = '' }
function submit() {
    if (props.loading || code.value.length < props.minDigits) return
    emit('submit', code.value)
}
function cancel() {
    if (props.loading) return
    code.value = ''
    emit('cancel')
    emit('update:modelValue', false)
}
// Typed straight into the hidden input (desktop): keep digits only, capped.
function onHiddenInput(e: Event) {
    const raw = (e.target as HTMLInputElement).value
    code.value = raw.replace(/\D/g, '').slice(0, props.maxDigits)
}
// Hardware keys when the hidden input is not focused (tablet with a USB keypad).
function onKey(e: KeyboardEvent) {
    if (!props.modelValue || e.target === hiddenEl.value) return
    if (/^\d$/.test(e.key)) { push(e.key); e.preventDefault() }
    else if (e.key === 'Backspace') { backspace(); e.preventDefault() }
    else if (e.key === 'Enter') { submit(); e.preventDefault() }
    else if (e.key === 'Escape') { cancel(); e.preventDefault() }
}

watch(() => props.modelValue, async (open) => {
    code.value = ''
    if (open) {
        window.addEventListener('keydown', onKey)
        await nextTick()
        // A mouse/trackpad device gets the keyboard sink focused; a touch tablet keeps its keyboard down.
        if (window.matchMedia?.('(pointer: fine)').matches) hiddenEl.value?.focus()
    } else {
        window.removeEventListener('keydown', onKey)
    }
}, { immediate: true })
// A rejected code is cleared so the next attempt starts fresh rather than appending to the old one.
watch(() => props.error, (msg) => { if (msg) code.value = '' })
onUnmounted(() => window.removeEventListener('keydown', onKey))
</script>

<style scoped>
.pad__hidden { position: absolute; width: 1px; height: 1px; opacity: 0; pointer-events: none; }
.pad__dots { display: flex; justify-content: center; gap: 12px; min-height: 18px; margin-top: 4px; }
.pad__dot { width: 14px; height: 14px; border-radius: 50%; border: 2px solid var(--rp-line3); transition: background-color 0.1s ease; }
.pad__dot--on { background: var(--rp-ink); border-color: var(--rp-ink); }
.pad__error { min-height: 20px; margin: 8px 0 4px; color: rgb(var(--v-theme-error)); visibility: hidden; }
.pad__error--on { visibility: visible; }
.pad__grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; }
.pad__key { font-size: 1.35rem; font-weight: 600; font-variant-numeric: tabular-nums; }
</style>
