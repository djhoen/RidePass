<template>
    <!-- A small swatch that mirrors the nav bar with the chosen background +
         foreground color so admins can preview their choices without leaving
         the settings page. -->
    <div class="navbar-preview" :style="style" role="img" aria-label="Nav bar preview">
        <span class="navbar-preview-title">{{ label || 'Preview' }}</span>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
    // Hex like #1A1F2B. Empty / null falls back to a neutral placeholder.
    color: string | null
    // Hex like #FFFFFF. Empty / null falls back to white.
    textColor?: string | null
    label?: string
}>()

function pickHex(hex: string | null | undefined, fallback: string): string {
    const m = /^#?([0-9a-f]{6})$/i.exec(hex?.trim() ?? '')
    return m ? `#${m[1].toUpperCase()}` : fallback
}

const style = computed(() => ({
    backgroundColor: pickHex(props.color, '#7a7a82'),
    color: pickHex(props.textColor, '#FFFFFF'),
}))
</script>

<style scoped>
.navbar-preview {
    height: 56px;
    display: flex;
    align-items: center;
    padding: 0 16px;
    border-radius: 6px;
    border: 1px solid rgba(0, 0, 0, 0.1);
}
.navbar-preview-title {
    font-weight: 600;
    color: inherit;
    /* Drop the dark text shadow so the chosen foreground color reads cleanly
       even on light backgrounds. */
}
</style>
