<template>
    <canvas ref="canvasEl" class="qr-canvas"></canvas>
</template>

<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import QRCode from 'qrcode'

const props = withDefaults(defineProps<{ value: string; size?: number }>(), {
    size: 240,
})

const canvasEl = ref<HTMLCanvasElement | null>(null)

async function render() {
    if (!canvasEl.value || !props.value) return
    try {
        await QRCode.toCanvas(canvasEl.value, props.value, {
            width: props.size,
            margin: 2,
            errorCorrectionLevel: 'M',
        })
    } catch (err) {
        console.error('QR render failed', err)
    }
}

onMounted(render)
watch(() => props.value, render)
watch(() => props.size, render)
</script>

<style scoped>
.qr-canvas {
    display: block;
    image-rendering: pixelated;
    background: white;
    border: 1px solid rgba(0, 0, 0, 0.1);
    border-radius: 4px;
}
</style>
