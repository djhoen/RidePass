<template>
    <div>
        <div class="d-flex align-center ga-2 mb-2">
            <div class="text-subtitle-2">{{ title }}</div>
            <v-chip v-if="photos.length" size="x-small" variant="tonal">{{ photos.length }} / {{ maxPhotos }}</v-chip>
            <v-spacer></v-spacer>
            <v-btn size="small" variant="tonal" prepend-icon="mdi-camera-plus"
                :loading="uploading" :disabled="photos.length >= maxPhotos" @click="pick">
                Add photo
            </v-btn>
        </div>
        <p v-if="hint" class="text-caption text-medium-emphasis mb-2">{{ hint }}</p>

        <input ref="fileInput" type="file" accept="image/png,image/jpeg,image/webp"
            class="d-none" multiple @change="onFilesPicked" />

        <div v-if="loading" class="text-center py-3">
            <v-progress-circular indeterminate size="24" color="primary"></v-progress-circular>
        </div>
        <div v-else-if="photos.length === 0" class="text-caption text-medium-emphasis py-2">
            No photos yet. {{ emptyHint }}
        </div>
        <div v-else class="photo-grid">
            <div v-for="p in photos" :key="p.id" class="photo-tile">
                <v-img :src="p.imageUrl" :alt="p.caption || 'Condition photo'" aspect-ratio="1" cover
                    class="rounded" style="cursor: zoom-in" @click="viewing = p"></v-img>
                <v-btn icon="mdi-close" size="x-small" variant="flat" color="surface"
                    class="photo-remove" :loading="deletingId === p.id" @click.stop="remove(p)"></v-btn>
                <div v-if="p.caption" class="text-caption text-truncate mt-1">{{ p.caption }}</div>
            </div>
        </div>

        <div v-if="error" class="text-error text-caption mt-2">{{ error }}</div>

        <!-- Full-size view: a thumbnail is no use when you're arguing about a scratch. -->
        <v-dialog :model-value="viewing !== null" max-width="900" @update:model-value="viewing = null">
            <v-card v-if="viewing">
                <v-card-title class="d-flex align-center">
                    <span class="text-body-1">{{ viewing.caption || title }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="viewing = null"></v-btn>
                </v-card-title>
                <v-img :src="viewing.imageUrl" max-height="70vh" contain></v-img>
                <v-card-text class="text-caption text-medium-emphasis">
                    Taken {{ formatWhen(viewing.createdAt) }}
                </v-card-text>
            </v-card>
        </v-dialog>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import dayjs from 'dayjs'
import { BikeShopService, type ShopConditionPhoto } from '@/services/BikeShopService'
import { useConfirm } from '@/composables/useConfirm'

// Owner is a work order OR a rental, never both. Stage separates the two ends of a rental
// ('intake' when it goes out, 'return' when it comes back) so a damage claim has a before
// and an after to compare.
const props = defineProps<{
    workOrderId?: string | null
    rentalId?: string | null
    stage?: 'intake' | 'return' | 'progress'
    title?: string
    hint?: string
}>()

const service = new BikeShopService()
const confirm = useConfirm()
const maxPhotos = 12          // matches the server cap, per stage

const photos = ref<ShopConditionPhoto[]>([])
const loading = ref(false)
const uploading = ref(false)
const deletingId = ref<string | null>(null)
const error = ref('')
const viewing = ref<ShopConditionPhoto | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)

const stage = computed(() => props.stage ?? 'intake')
const title = computed(() => props.title ?? 'Condition photos')
const emptyHint = computed(() =>
    stage.value === 'return'
        ? 'Photograph any damage before capturing part of the deposit.'
        : 'Photograph existing damage so it can’t be disputed later.')

function pick() { fileInput.value?.click() }

async function load() {
    const owner = props.workOrderId ?? props.rentalId
    if (!owner) { photos.value = []; return }
    loading.value = true
    error.value = ''
    // The component is reused as the bound record changes; drop the old photos so a failed load
    // can't leave a previous record's photos showing under the new one.
    photos.value = []
    try {
        const r = props.workOrderId
            ? await service.listWorkOrderPhotos(props.workOrderId)
            : await service.listRentalPhotos(props.rentalId!)
        photos.value = ((r.data as any).data as ShopConditionPhoto[])
            .filter(p => p.stage === stage.value)
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not load the photos. Reopen this to try again.'
    } finally {
        loading.value = false
    }
}

async function onFilesPicked(ev: Event) {
    const input = ev.target as HTMLInputElement
    const files = Array.from(input.files ?? [])
    input.value = ''   // let the same file be picked again after a delete
    if (files.length === 0) return

    uploading.value = true
    error.value = ''
    let uploaded = 0
    try {
        for (const f of files) {
            if (photos.value.length + uploaded >= maxPhotos) {
                error.value = `Only ${maxPhotos} photos are kept here, so the rest were skipped.`
                break
            }
            if (props.workOrderId) await service.uploadWorkOrderPhoto(props.workOrderId, f, stage.value)
            else await service.uploadRentalPhoto(props.rentalId!, f, stage.value)
            uploaded++
        }
        if (uploaded > 0) await load()
    } catch (e: any) {
        // Some may have landed before the failure, so refresh either way rather than
        // leaving the grid out of step with the server.
        error.value = e.response?.data?.error || 'That photo could not be uploaded. Try again.'
        await load()
    } finally {
        uploading.value = false
    }
}

async function remove(p: ShopConditionPhoto) {
    const ok = await confirm({
        title: 'Delete this photo?',
        message: 'It will be removed from the record permanently.',
        confirmText: 'Delete',
        confirmColor: 'error',
    })
    if (!ok) return
    deletingId.value = p.id
    error.value = ''
    try {
        await service.deleteConditionPhoto(p.id)
        photos.value = photos.value.filter(x => x.id !== p.id)
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not delete that photo. Try again.'
    } finally {
        deletingId.value = null
    }
}

function formatWhen(iso: string): string { return dayjs(iso).format('MMM D, YYYY h:mm A') }

watch(() => [props.workOrderId, props.rentalId, props.stage], load, { immediate: true })
defineExpose({ reload: load })
</script>

<style scoped>
.photo-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(110px, 1fr));
    gap: 8px;
}
.photo-tile { position: relative; }
.photo-remove {
    position: absolute;
    top: 2px;
    right: 2px;
    opacity: 0.85;
}
</style>
