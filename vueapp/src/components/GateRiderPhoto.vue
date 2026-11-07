<template>
    <!-- A rider's photo on file at the gate. The venue's own photos (source "rider") are private:
         fetched with the staff session through the authorized endpoint, never by a public URL. -->
    <v-avatar :size="size" rounded="lg" style="background: var(--rp-raise)">
        <v-img v-if="src" :src="src" cover :alt="alt"></v-img>
        <v-progress-circular v-else-if="loading" indeterminate size="24"></v-progress-circular>
        <v-icon v-else :size="Math.round(size / 2)" class="text-muted2">mdi-account</v-icon>
    </v-avatar>
</template>

<script setup lang="ts">
import { ref, watch, onBeforeUnmount } from 'vue'
import { TicketService } from '@/services/TicketService'
import { absoluteUrl } from '@/helpers/ImageUrl'

const props = withDefaults(defineProps<{
    source: 'rider' | 'account' | 'pass' | null
    riderPhotoId?: string | null
    photoUrl?: string | null
    /** The pass's own photo (data URL), for source "pass". */
    passPhotoDataUrl?: string | null
    size?: number
    alt?: string
}>(), { riderPhotoId: null, photoUrl: null, passPhotoDataUrl: null, size: 72, alt: 'Rider photo' })

const service = new TicketService()
const src = ref<string | null>(null)
const loading = ref(false)
let objectUrl: string | null = null

function release() {
    if (objectUrl) URL.revokeObjectURL(objectUrl)
    objectUrl = null
}

async function load() {
    release()
    src.value = null
    if (props.source === 'pass') { src.value = props.passPhotoDataUrl ?? null; return }
    if (props.source === 'account') { src.value = absoluteUrl(props.photoUrl); return }
    if (props.source !== 'rider' || !props.riderPhotoId) return
    loading.value = true
    const id = props.riderPhotoId
    try {
        const r = await service.riderPhotoBlob(id)
        if (id !== props.riderPhotoId) return   // a newer photo was asked for meanwhile
        objectUrl = URL.createObjectURL(r.data)
        src.value = objectUrl
    } catch {
        src.value = null
    } finally {
        loading.value = false
    }
}

watch(() => [props.source, props.riderPhotoId, props.photoUrl, props.passPhotoDataUrl], load, { immediate: true })
onBeforeUnmount(release)
</script>
