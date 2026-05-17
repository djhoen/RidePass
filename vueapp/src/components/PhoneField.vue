<template>
    <v-text-field
        :model-value="modelValue ?? ''"
        @update:model-value="onInput"
        type="tel"
        inputmode="tel"
        :placeholder="placeholder ?? '(555) 555-5555'"
        v-bind="$attrs"
    />
</template>

<script setup lang="ts">
import { watch } from 'vue'

// Drop-in replacement for `<v-text-field type="tel">` that keeps every phone
// number in `(XXX) XXX-XXXX` format. Digits-only validators (e.g.
// `phone.replace(/\D/g, '').length >= 7`) still work because we just
// reshuffle the same digits.
defineOptions({ inheritAttrs: false })

const props = defineProps<{
    modelValue: string | null | undefined
    placeholder?: string
}>()

const emit = defineEmits<{
    (e: 'update:modelValue', v: string): void
}>()

function formatPhone(raw: string): string {
    const digits = raw.replace(/\D/g, '').slice(0, 10)
    if (digits.length === 0) return ''
    if (digits.length < 4) return digits
    if (digits.length < 7) return `(${digits.slice(0, 3)}) ${digits.slice(3)}`
    return `(${digits.slice(0, 3)}) ${digits.slice(3, 6)}-${digits.slice(6)}`
}

function onInput(v: string | null) {
    emit('update:modelValue', formatPhone(String(v ?? '')))
}

// Re-emit formatted whenever the parent sets a raw value (loaded from server,
// pasted, programmatically set). Skip the no-op case to avoid an update loop.
watch(() => props.modelValue, (next) => {
    const raw = String(next ?? '')
    const formatted = formatPhone(raw)
    if (formatted !== raw) emit('update:modelValue', formatted)
}, { immediate: true })
</script>
