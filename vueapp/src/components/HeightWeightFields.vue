<template>
    <!-- Height in ft + in, weight in lb; v-model is whole inches + whole pounds (Script0317). -->
    <div class="hw-fields">
        <v-text-field :model-value="feet" @update:model-value="onFeet" type="number" min="2" max="8"
            :label="required ? 'Height (ft) *' : 'Height (ft)'" density="compact" hide-details="auto"
            :disabled="disabled" inputmode="numeric" class="hw-ft"></v-text-field>
        <v-text-field :model-value="inches" @update:model-value="onInches" type="number" min="0" max="11"
            label="in" density="compact" hide-details="auto" :disabled="disabled" inputmode="numeric"
            class="hw-in"></v-text-field>
        <v-text-field :model-value="modelValue.weightLb ?? ''" @update:model-value="onWeight" type="number"
            min="20" max="450" :label="required ? 'Weight (lb) *' : 'Weight (lb)'" density="compact"
            hide-details="auto" :disabled="disabled" inputmode="numeric" class="hw-lb"></v-text-field>
    </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { fromInches, toInches, type Measurement } from '@/helpers/RiderMeasurements'

const props = defineProps<{
    modelValue: Measurement
    required?: boolean
    disabled?: boolean
}>()
const emit = defineEmits<{ (e: 'update:modelValue', value: Measurement): void }>()

// Feet / inches are kept as typed (so "5" then "7" doesn't jump around) and re-synced only when
// the parent sets a different total (a prefill from the profile).
const feet = ref<string>('')
const inches = ref<string>('')
watch(() => props.modelValue.heightIn, total => {
    if (total === toInches(feet.value, inches.value)) return
    const parts = fromInches(total)
    feet.value = parts.feet == null ? '' : String(parts.feet)
    inches.value = parts.inches == null ? '' : String(parts.inches)
}, { immediate: true })

function emitHeight() {
    emit('update:modelValue', { ...props.modelValue, heightIn: toInches(feet.value, inches.value) })
}
function onFeet(v: string) { feet.value = v ?? ''; emitHeight() }
function onInches(v: string) { inches.value = v ?? ''; emitHeight() }
function onWeight(v: string) {
    const n = v === '' || v === null || v === undefined ? null : Number(v)
    emit('update:modelValue', { ...props.modelValue, weightLb: n === null || Number.isNaN(n) ? null : Math.round(n) })
}
</script>

<style scoped>
.hw-fields { display: flex; gap: 8px; flex-wrap: wrap; }
.hw-ft { flex: 1 1 90px; }
.hw-in { flex: 1 1 70px; }
.hw-lb { flex: 1 1 110px; }
</style>
