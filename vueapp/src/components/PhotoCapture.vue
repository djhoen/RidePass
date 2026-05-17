<template>
    <div>
        <input ref="fileInput" type="file" accept="image/*" capture="user" hidden @change="onFile" />
        <div v-if="modelValue" class="photo-preview">
            <img :src="modelValue" alt="Captured photo" />
            <v-btn size="small" variant="tonal" prepend-icon="mdi-camera-retake" class="mt-2" @click="trigger">
                Retake
            </v-btn>
        </div>
        <v-btn v-else color="primary" prepend-icon="mdi-camera" @click="trigger">
            {{ buttonLabel }}
        </v-btn>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'

const props = defineProps<{
    modelValue: string | null
    buttonLabel?: string
    maxDimension?: number
}>()
const emit = defineEmits<{
    (e: 'update:modelValue', value: string | null): void
}>()

const fileInput = ref<HTMLInputElement | null>(null)
const buttonLabel = props.buttonLabel ?? 'Take photo'
const maxDimension = props.maxDimension ?? 600

function trigger() { fileInput.value?.click() }

async function onFile(e: Event) {
    const target = e.target as HTMLInputElement
    const file = target.files?.[0]
    target.value = ''
    if (!file) return
    const dataUrl = await fileToScaledDataUrl(file, maxDimension)
    emit('update:modelValue', dataUrl)
}

function fileToScaledDataUrl(file: File, max: number): Promise<string> {
    return new Promise((resolve, reject) => {
        const reader = new FileReader()
        reader.onload = () => {
            const img = new Image()
            img.onload = () => {
                const scale = Math.min(1, max / Math.max(img.width, img.height))
                const w = Math.round(img.width * scale)
                const h = Math.round(img.height * scale)
                const canvas = document.createElement('canvas')
                canvas.width = w
                canvas.height = h
                const ctx = canvas.getContext('2d')
                if (!ctx) { reject(new Error('Canvas unavailable')); return }
                ctx.drawImage(img, 0, 0, w, h)
                resolve(canvas.toDataURL('image/jpeg', 0.85))
            }
            img.onerror = () => reject(new Error('Image load failed'))
            img.src = reader.result as string
        }
        reader.onerror = () => reject(reader.error ?? new Error('Read failed'))
        reader.readAsDataURL(file)
    })
}
</script>

<style scoped>
.photo-preview img {
    max-width: 240px;
    max-height: 240px;
    border-radius: 8px;
    border: 1px solid rgba(0, 0, 0, 0.12);
    object-fit: cover;
}
</style>
