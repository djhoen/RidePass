<template>
    <v-menu v-model="open" :close-on-content-click="false" location="bottom start">
        <template #activator="{ props }">
            <v-btn v-bind="props" variant="outlined" size="small" class="px-2"
                :aria-label="`Color: ${modelValue}`">
                <span class="swatch mr-2" :style="{ background: modelValue }"></span>
                <v-icon size="small">mdi-menu-down</v-icon>
            </v-btn>
        </template>

        <v-card min-width="248">
            <v-card-text class="pb-2">
                <div class="text-caption text-medium-emphasis mb-2">Recommended</div>
                <div class="d-flex flex-wrap ga-2">
                    <button v-for="hex in swatches" :key="hex" type="button" class="swatch-btn"
                        :class="{ 'swatch-btn--on': sameColor(hex, modelValue) }"
                        :style="{ background: hex }" :title="takenBy(hex) ? `Used by ${takenBy(hex)}` : hex"
                        @click="choose(hex)">
                        <v-icon v-if="sameColor(hex, modelValue)" size="16"
                            :color="inkOn(hex)">mdi-check</v-icon>
                        <span v-else-if="takenBy(hex)" class="taken-dot"
                            :style="{ background: inkOn(hex) }"></span>
                    </button>
                </div>
                <div v-if="takenBy(modelValue)" class="text-caption text-warning mt-2">
                    Also used by {{ takenBy(modelValue) }}. Two centers in the same color can't be
                    told apart on a chart.
                </div>
                <div v-if="sameColor(modelValue, totalSeriesColor)" class="text-caption text-warning mt-2">
                    This blue is what charts use for total revenue, so this center would look like
                    the total line.
                </div>
            </v-card-text>

            <v-divider></v-divider>
            <v-card-text class="pt-2">
                <div class="text-caption text-medium-emphasis mb-2">Custom</div>
                <v-text-field v-model="draft" density="compact" variant="outlined"
                    hide-details placeholder="#eb6834" maxlength="7"
                    :error="draft.length > 0 && !isHex(draft)"
                    @update:model-value="onCustom">
                    <template #prepend-inner>
                        <span class="swatch" :style="{ background: modelValue }"></span>
                    </template>
                </v-text-field>
            </v-card-text>

            <v-card-actions>
                <v-spacer></v-spacer>
                <v-btn size="small" variant="text" @click="open = false">Done</v-btn>
            </v-card-actions>
        </v-card>
    </v-menu>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { inkOn } from '../helpers/profitCenterColor'

const props = defineProps<{
    modelValue: string
    swatches: string[]
    /** Blue is reserved for the total series; picking it gets a warning, not a block. */
    totalSeriesColor: string
    /** Other centers' colors, so a duplicate pick is flagged before it reaches a chart. */
    usedBy?: { name: string; color: string }[]
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()

const open = ref(false)

// The custom field edits its OWN draft rather than the bound value: a half-typed "#eb6" is not a
// color, so pushing every keystroke up would either store garbage or (binding one-way and
// refusing to emit) snap the field back mid-word and make it untypeable.
const draft = ref(props.modelValue)
watch(() => props.modelValue, v => { draft.value = v })

function isHex(v: string): boolean {
    return /^#[0-9a-fA-F]{6}$/.test((v || '').trim())
}

function sameColor(a: string, b: string): boolean {
    return (a || '').trim().toLowerCase() === (b || '').trim().toLowerCase()
}

/** The other center already wearing this color, if any. */
function takenBy(hex: string): string | null {
    return props.usedBy?.find(u => sameColor(u.color, hex))?.name ?? null
}

function choose(hex: string) {
    emit('update:modelValue', hex)
}

// Only a complete hex is pushed up; until then the swatch keeps showing the last good color and
// the field flags itself as invalid.
function onCustom(v: string) {
    const value = (v || '').trim()
    if (isHex(value)) emit('update:modelValue', value.toLowerCase())
}
</script>

<style scoped>
.swatch {
    display: inline-block;
    width: 16px;
    height: 16px;
    border-radius: 4px;
    /* Hairline ring so a light swatch still has an edge against the surface. */
    box-shadow: inset 0 0 0 1px rgba(var(--v-theme-on-surface), 0.2);
}

.swatch-btn {
    width: 30px;
    height: 30px;
    border-radius: 6px;
    border: none;
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    box-shadow: inset 0 0 0 1px rgba(var(--v-theme-on-surface), 0.2);
}

.swatch-btn--on {
    box-shadow: 0 0 0 2px rgb(var(--v-theme-surface)), 0 0 0 4px rgba(var(--v-theme-on-surface), 0.5);
}

.taken-dot {
    width: 5px;
    height: 5px;
    border-radius: 50%;
    opacity: 0.85;
}
</style>
