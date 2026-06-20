<template>
    <div>
        <div class="d-flex align-center justify-space-between mb-2">
            <strong>{{ label }}</strong>
            <v-btn v-if="url" variant="text" size="small" color="error" :loading="removing" @click="remove">Remove</v-btn>
        </div>
        <v-img v-if="url" :src="url" max-height="120" contain class="mb-2 border"></v-img>
        <v-file-input :label="`Upload ${label}`" accept="image/png,image/jpeg,image/webp,image/svg+xml,image/x-icon"
            density="compact" prepend-icon="mdi-upload" hide-details :loading="uploading" @update:model-value="onFile"></v-file-input>
        <v-alert v-if="error" type="error" variant="tonal" density="compact" class="mt-2">{{ error }}</v-alert>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { TenantService } from '@/services/TenantService'

type ImageKind = 'logo' | 'logoWhite' | 'favicon' | 'hero' | 'secondaryHero' | 'benefits'

const props = defineProps<{
    label: string
    kind: ImageKind
    url: string | null
}>()

const emit = defineEmits<{
    (e: 'uploaded'): void
    (e: 'removed'): void
}>()

const tenantService = new TenantService()
const uploading = ref(false)
const removing = ref(false)
const error = ref<string | null>(null)

async function onFile(file: File | File[] | null) {
    const single = Array.isArray(file) ? file[0] : file
    if (!single) return
    try {
        uploading.value = true
        error.value = null
        await tenantService.uploadBrandingImage(props.kind, single)
        emit('uploaded')
    } catch (err: any) {
        error.value = err.response?.data?.error || `Couldn’t upload the ${props.label.toLowerCase()}. Check the file and try again.`
    } finally {
        uploading.value = false
    }
}

async function remove() {
    try {
        removing.value = true
        error.value = null
        await tenantService.deleteBrandingImage(props.kind)
        emit('removed')
    } catch (err: any) {
        error.value = err.response?.data?.error || `Couldn’t remove the ${props.label.toLowerCase()}. Try again.`
    } finally {
        removing.value = false
    }
}
</script>

<style scoped>
.border {
    border: 1px solid rgba(0, 0, 0, 0.1);
    border-radius: 4px;
}
</style>
