<template>
    <div>
        <div class="d-flex flex-wrap ga-2">
            <v-btn v-if="canNativeShare" prepend-icon="mdi-share-variant" size="small" variant="tonal"
                @click="nativeShare">
                Share…
            </v-btn>
            <v-btn v-if="show('facebook')" prepend-icon="mdi-facebook" size="small" variant="tonal"
                @click="shareFacebook">Facebook</v-btn>
            <v-btn v-if="show('twitter')" prepend-icon="mdi-twitter" size="small" variant="tonal"
                @click="shareTwitter">X / Twitter</v-btn>
            <v-btn v-if="show('linkedin')" prepend-icon="mdi-linkedin" size="small" variant="tonal"
                @click="shareLinkedIn">LinkedIn</v-btn>
            <v-btn v-if="show('whatsapp')" prepend-icon="mdi-whatsapp" size="small" variant="tonal"
                @click="shareWhatsApp">WhatsApp</v-btn>
            <v-btn v-if="show('reddit')" prepend-icon="mdi-reddit" size="small" variant="tonal"
                @click="shareReddit">Reddit</v-btn>
            <v-btn v-if="show('email')" prepend-icon="mdi-email-outline" size="small" variant="tonal"
                @click="shareEmail">Email</v-btn>
            <!-- Instagram / TikTok have no share-intent URLs, so the realistic UX
                 is "copy a caption with the link, then paste in the app". -->
            <v-btn v-if="show('instagram')" prepend-icon="mdi-instagram" size="small" variant="tonal"
                @click="copyForInstagram">Instagram</v-btn>
            <v-btn v-if="show('tiktok')" prepend-icon="mdi-music-note" size="small" variant="tonal"
                @click="copyForTiktok">TikTok</v-btn>
            <v-btn prepend-icon="mdi-link-variant" size="small" variant="text" @click="copyLink">Copy link</v-btn>
        </div>
        <v-snackbar v-model="snackbar" :timeout="2500">{{ snackbarText }}</v-snackbar>
    </div>
</template>

<script lang="ts">
export type SocialSharePlatform =
    | 'facebook' | 'twitter' | 'linkedin' | 'whatsapp' | 'reddit' | 'email' | 'instagram' | 'tiktok'
</script>

<script setup lang="ts">
import { ref, computed } from 'vue'

type Platform = SocialSharePlatform

const props = defineProps<{
    url: string
    title: string
    text?: string
    /**
     * Filter which platforms to show. Pass undefined to show every supported
     * platform (good default for self-share). Pass an explicit list to limit
     * — for tenant sharing we restrict to whichever socials they've registered.
     */
    platforms?: Platform[]
}>()

const snackbar = ref(false)
const snackbarText = ref('')

const canNativeShare = computed(() =>
    typeof navigator !== 'undefined' && typeof (navigator as any).share === 'function')

function show(p: Platform) {
    if (!props.platforms) return true
    return props.platforms.includes(p)
}

async function nativeShare() {
    try {
        await (navigator as any).share({ title: props.title, text: props.text, url: props.url })
    } catch {
        // User cancelled — no-op.
    }
}

function popup(href: string) {
    window.open(href, '_blank', 'noopener,noreferrer,width=600,height=640')
}

const u = () => encodeURIComponent(props.url)
const t = () => encodeURIComponent(props.text ?? props.title)
const titleE = () => encodeURIComponent(props.title)

function shareFacebook() { popup(`https://www.facebook.com/sharer/sharer.php?u=${u()}`) }
function shareTwitter() { popup(`https://twitter.com/intent/tweet?url=${u()}&text=${t()}`) }
function shareLinkedIn() { popup(`https://www.linkedin.com/sharing/share-offsite/?url=${u()}`) }
function shareReddit() { popup(`https://reddit.com/submit?url=${u()}&title=${titleE()}`) }
function shareWhatsApp() {
    const text = `${props.text ?? props.title} ${props.url}`
    popup(`https://wa.me/?text=${encodeURIComponent(text)}`)
}
function shareEmail() {
    const body = `${props.text ?? ''}\n\n${props.url}`
    location.href = `mailto:?subject=${titleE()}&body=${encodeURIComponent(body)}`
}

async function copyLink() {
    await copyText(props.url, 'Link copied.')
}
async function copyForInstagram() {
    await copyText(`${props.text ?? props.title}\n${props.url}`,
        'Caption copied. Open Instagram and paste it in a new post or your story.')
}
async function copyForTiktok() {
    await copyText(`${props.text ?? props.title}\n${props.url}`,
        'Caption copied. Open TikTok and paste it in your video description.')
}
async function copyText(text: string, msg: string) {
    try {
        await navigator.clipboard.writeText(text)
    } catch {
        // Clipboard API unavailable (insecure context / older browser): show the text in
        // the snackbar to copy manually rather than a native prompt.
        snackbarText.value = `Couldn't copy automatically. Copy this manually: ${text}`
        snackbar.value = true
        return
    }
    snackbarText.value = msg
    snackbar.value = true
}
</script>
