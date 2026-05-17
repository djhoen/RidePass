<template>
    <div class="signature-pad" :class="{ disabled }">
        <div class="canvas-wrap" :class="{ active: isDrawing, empty: isEmpty, disabled }">
            <canvas ref="canvasRef"
                @pointerdown="startStroke" @pointermove="continueStroke"
                @pointerup="endStroke" @pointercancel="endStroke" @pointerleave="endStroke"></canvas>
            <div v-if="isEmpty" class="placeholder">{{ disabled ? (disabledPlaceholder ?? 'Sign here') : 'Sign here' }}</div>
        </div>
        <div class="d-flex align-center mt-2">
            <span class="text-caption text-medium-emphasis">{{ caption }}</span>
            <v-spacer></v-spacer>
            <v-btn size="small" variant="text" prepend-icon="mdi-eraser" :disabled="isEmpty || disabled" @click="clear">
                Clear
            </v-btn>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount, watch, nextTick } from 'vue'

const props = defineProps<{
    modelValue?: string | null
    caption?: string
    height?: number
    disabled?: boolean
    disabledPlaceholder?: string
}>()
const emit = defineEmits<{
    (e: 'update:modelValue', value: string | null): void
}>()

const canvasRef = ref<HTMLCanvasElement | null>(null)
const isDrawing = ref(false)
const isEmpty = ref(true)
const caption = props.caption ?? 'Use mouse, finger, or stylus to sign'
let ctx: CanvasRenderingContext2D | null = null
let last = { x: 0, y: 0 }
let dpr = 1

function setupCanvas() {
    const c = canvasRef.value
    if (!c) return
    dpr = window.devicePixelRatio || 1
    const wrap = c.parentElement as HTMLElement
    const cssWidth = wrap.clientWidth
    const cssHeight = props.height ?? 180
    c.style.width = cssWidth + 'px'
    c.style.height = cssHeight + 'px'
    c.width = Math.floor(cssWidth * dpr)
    c.height = Math.floor(cssHeight * dpr)
    ctx = c.getContext('2d')
    if (!ctx) return
    ctx.scale(dpr, dpr)
    ctx.lineCap = 'round'
    ctx.lineJoin = 'round'
    ctx.lineWidth = 2
    ctx.strokeStyle = '#1f2937'
}

function pointAt(e: PointerEvent): { x: number; y: number } {
    const c = canvasRef.value!
    const rect = c.getBoundingClientRect()
    return { x: e.clientX - rect.left, y: e.clientY - rect.top }
}

function startStroke(e: PointerEvent) {
    if (props.disabled || !ctx) return
    e.preventDefault()
    canvasRef.value?.setPointerCapture(e.pointerId)
    isDrawing.value = true
    last = pointAt(e)
}

function continueStroke(e: PointerEvent) {
    if (props.disabled || !isDrawing.value || !ctx) return
    const p = pointAt(e)
    ctx.beginPath()
    ctx.moveTo(last.x, last.y)
    ctx.lineTo(p.x, p.y)
    ctx.stroke()
    last = p
    if (isEmpty.value) isEmpty.value = false
}

function endStroke(e: PointerEvent) {
    if (!isDrawing.value) return
    canvasRef.value?.releasePointerCapture(e.pointerId)
    isDrawing.value = false
    emit('update:modelValue', isEmpty.value ? null : canvasRef.value!.toDataURL('image/png'))
}

function clear() {
    if (!ctx || !canvasRef.value) return
    ctx.clearRect(0, 0, canvasRef.value.width, canvasRef.value.height)
    isEmpty.value = true
    emit('update:modelValue', null)
}

function handleResize() {
    // Re-setup preserves blank state; we don't try to redraw a captured stroke after a resize
    // because once they sign we keep the data URL anyway.
    if (isEmpty.value) setupCanvas()
}

onMounted(async () => {
    setupCanvas()
    window.addEventListener('resize', handleResize)
    // The first setupCanvas can run while our parent has clientWidth=0 (e.g.,
    // mounted inside a stepper-window-item that hadn't activated yet, or before
    // CSS layout settled). That gives us a 0x0 drawing buffer — pointer events
    // fire but every stroke lands outside the buffer, so nothing renders. Wait
    // one tick and re-init so the canvas matches its now-real CSS size.
    await nextTick()
    setupCanvas()
})

onBeforeUnmount(() => {
    window.removeEventListener('resize', handleResize)
})

watch(() => props.modelValue, (v) => {
    if (!v && !isEmpty.value) clear()
})

// Same fix on disabled → enabled: the pad was likely sized while inside a
// hidden / not-yet-laid-out container. Re-run setupCanvas after the new layout
// pass so the drawing buffer matches the visible canvas.
watch(() => props.disabled, async (now, prev) => {
    if (prev && !now && isEmpty.value) {
        await nextTick()
        setupCanvas()
    }
})
</script>

<style scoped>
.canvas-wrap {
    position: relative;
    border: 2px dashed rgba(0, 0, 0, 0.25);
    border-radius: 6px;
    background: #fff;
    transition: border-color 0.15s;
}
.canvas-wrap.active {
    border-color: rgb(var(--v-theme-primary));
    border-style: solid;
}
.canvas-wrap.empty:not(.active) .placeholder {
    position: absolute;
    inset: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    color: rgba(0, 0, 0, 0.35);
    pointer-events: none;
    font-style: italic;
}
canvas {
    display: block;
    touch-action: none;
    cursor: crosshair;
}
.canvas-wrap.disabled {
    background: rgba(0, 0, 0, 0.04);
    border-color: rgba(0, 0, 0, 0.15);
}
.canvas-wrap.disabled canvas { cursor: not-allowed; }
</style>
