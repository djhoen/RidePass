<template>
    <div class="kiosk" @pointerdown="touch" @keydown="touch">
        <header class="kiosk-head">
            <div class="kiosk-venue">{{ config?.venueName || 'Check in' }}</div>
            <div v-if="config" class="kiosk-device">{{ config.kioskName }}</div>
        </header>

        <main class="kiosk-body">
            <!-- Pair this tablet: a one-time code from Admin > Kiosks. No staff login on this device. -->
            <section v-if="step === 'pair'" class="kiosk-card">
                <h1>Set up this kiosk</h1>
                <p class="kiosk-sub">Enter the pairing code shown on Admin &gt; Kiosks.</p>
                <v-text-field v-model="pairCode" label="Pairing code" variant="outlined" class="kiosk-input kiosk-code"
                    autocomplete="off" autocapitalize="characters" maxlength="9" @keyup.enter="pair"></v-text-field>
                <v-alert v-if="error" type="error" variant="tonal" class="mb-4">{{ error }}</v-alert>
                <v-btn size="x-large" color="primary" block :loading="busy" :disabled="pairCode.replace(/[^a-z0-9]/gi, '').length !== 8"
                    @click="pair">Pair kiosk</v-btn>
            </section>

            <section v-else-if="step === 'loading'" class="kiosk-card kiosk-center">
                <v-progress-circular indeterminate size="56"></v-progress-circular>
            </section>

            <section v-else-if="step === 'start'" class="kiosk-card kiosk-center">
                <h1>Welcome</h1>
                <p class="kiosk-sub">{{ welcomeLine }}</p>
                <v-btn size="x-large" color="primary" class="kiosk-big" :loading="busy" @click="begin">Start</v-btn>
                <v-alert v-if="error" type="error" variant="tonal" class="mt-6">{{ error }}</v-alert>
            </section>

            <!-- Who are you -->
            <section v-else-if="step === 'who'" class="kiosk-card">
                <h1>About you</h1>
                <p class="kiosk-sub">The rider's own details.</p>
                <div class="kiosk-grid">
                    <v-text-field v-model="form.firstName" label="First name" variant="outlined" class="kiosk-input" autocomplete="off"></v-text-field>
                    <v-text-field v-model="form.lastName" label="Last name" variant="outlined" class="kiosk-input" autocomplete="off"></v-text-field>
                </div>
                <v-text-field v-model="form.birthdate" type="date" :max="config?.today" label="Birthdate" variant="outlined" class="kiosk-input"></v-text-field>
                <v-text-field v-model="form.email" type="email" variant="outlined" class="kiosk-input" autocomplete="off"
                    :label="isMinor ? 'Email (optional for under 18)' : 'Email'"></v-text-field>
                <v-text-field v-model="form.phone" type="tel" label="Phone (optional)" variant="outlined" class="kiosk-input" autocomplete="off"></v-text-field>
                <template v-if="config?.requireEmergencyContact && form.birthdate && !isMinor">
                    <div class="kiosk-label">Emergency contact</div>
                    <div class="kiosk-grid">
                        <v-text-field v-model="form.emergencyContactName" label="Contact name" variant="outlined" class="kiosk-input" autocomplete="off"></v-text-field>
                        <v-text-field v-model="form.emergencyContactPhone" type="tel" label="Contact phone" variant="outlined" class="kiosk-input" autocomplete="off"></v-text-field>
                    </div>
                </template>
                <v-alert v-if="error" type="error" variant="tonal" class="mb-4">{{ error }}</v-alert>
                <div class="kiosk-actions">
                    <v-btn size="x-large" variant="text" @click="reset">Cancel</v-btn>
                    <v-btn size="x-large" color="primary" :disabled="!whoComplete" @click="afterWho">Next</v-btn>
                </div>
            </section>

            <!-- A minor: their parent or guardian -->
            <section v-else-if="step === 'guardian'" class="kiosk-card">
                <h1>Parent or guardian</h1>
                <p class="kiosk-sub">Riders under 18 need a parent or guardian (18 or over) to sign for them.</p>
                <div class="kiosk-grid">
                    <v-text-field v-model="form.guardianFirstName" label="First name" variant="outlined" class="kiosk-input" autocomplete="off"></v-text-field>
                    <v-text-field v-model="form.guardianLastName" label="Last name" variant="outlined" class="kiosk-input" autocomplete="off"></v-text-field>
                </div>
                <v-text-field v-model="form.guardianEmail" type="email" label="Email" variant="outlined" class="kiosk-input" autocomplete="off"></v-text-field>
                <v-text-field v-model="form.guardianPhone" type="tel" label="Phone" variant="outlined" class="kiosk-input" autocomplete="off"></v-text-field>
                <v-text-field v-model="form.guardianBirthdate" type="date" :max="config?.today" label="Your birthdate" variant="outlined" class="kiosk-input"></v-text-field>
                <v-alert v-if="error" type="error" variant="tonal" class="mb-4">{{ error }}</v-alert>
                <div class="kiosk-actions">
                    <v-btn size="x-large" variant="text" @click="step = 'who'">Back</v-btn>
                    <v-btn size="x-large" color="primary" :disabled="!guardianComplete" @click="afterGuardian">Next</v-btn>
                </div>
            </section>

            <!-- The waiver -->
            <section v-else-if="step === 'waiver'" class="kiosk-card">
                <h1>{{ config?.waiverTitle || 'Waiver' }}</h1>
                <p class="kiosk-sub">
                    {{ isMinor ? `${guardianName}, please read and sign for ${form.firstName}.` : 'Please read and sign.' }}
                </p>
                <div class="kiosk-waiver"><RichTextView :html="config?.waiverBody || ''" /></div>
                <v-checkbox v-model="form.agreed" hide-details class="kiosk-check"
                    :label="isMinor ? `I am ${form.firstName}'s parent or guardian and I agree to this waiver for them` : 'I have read and agree to this waiver'"></v-checkbox>
                <div class="kiosk-label mt-2">{{ isMinor ? 'Parent or guardian signs here' : 'Sign here' }}</div>
                <SignaturePad v-model="form.signature" />
                <v-alert v-if="error" type="error" variant="tonal" class="my-4">{{ error }}</v-alert>
                <div class="kiosk-actions">
                    <v-btn size="x-large" variant="text" @click="step = isMinor ? 'guardian' : 'who'">Back</v-btn>
                    <v-btn size="x-large" color="primary" :disabled="!form.agreed || !form.signature" @click="step = 'photo'">Next</v-btn>
                </div>
            </section>

            <!-- The rider's photo -->
            <section v-else-if="step === 'photo'" class="kiosk-card kiosk-center">
                <h1>Your photo</h1>
                <p class="kiosk-sub">{{ isMinor ? `A photo of ${form.firstName}, so the gate knows them.` : 'So the gate knows you. Look at the camera.' }}</p>
                <PhotoCapture v-model="form.photo" button-label="Take photo" :max-dimension="720" class="kiosk-photo" />
                <v-alert v-if="error" type="error" variant="tonal" class="my-4">{{ error }}</v-alert>
                <div class="kiosk-actions">
                    <v-btn size="x-large" variant="text" :disabled="busy" @click="step = config?.waiverId ? 'waiver' : (isMinor ? 'guardian' : 'who')">Back</v-btn>
                    <v-btn v-if="!config?.photoRequired && !form.photo" size="x-large" variant="outlined" :loading="busy" @click="submit">Skip</v-btn>
                    <v-btn v-if="config?.photoRequired || form.photo" size="x-large" color="primary" :loading="busy" :disabled="!form.photo" @click="submit">Finish</v-btn>
                </div>
            </section>

            <!-- A step staff sent to this kiosk for one rider: first name only, then only those steps. -->
            <section v-else-if="step === 'push-intro' && push" class="kiosk-card kiosk-center">
                <v-icon size="72" color="primary">mdi-account-arrow-right</v-icon>
                <h1>Staff sent this to you</h1>
                <p class="kiosk-sub">For <strong>{{ push.firstName }}</strong>:
                    {{ push.wantWaiver && push.wantPhoto ? 'the waiver and a photo' : push.wantWaiver ? 'the waiver' : 'a photo' }}.</p>
                <v-btn size="x-large" color="primary" class="kiosk-big" :loading="busy" @click="beginPush">Begin</v-btn>
                <v-btn size="x-large" variant="text" class="mt-4" :disabled="busy" @click="declinePush">Not me</v-btn>
            </section>

            <section v-else-if="step === 'push-waiver' && push" class="kiosk-card">
                <h1>{{ pushWaiverDoc?.title || 'Waiver' }}</h1>
                <v-alert type="info" variant="tonal" density="compact" class="mb-3">
                    {{ push.signAs === 'guardian'
                        ? `You're signing for ${push.firstName} as their parent or guardian.`
                        : `Sign as yourself, ${push.firstName}.` }}
                </v-alert>
                <div class="kiosk-waiver"><RichTextView :html="pushWaiverDoc?.body || ''" /></div>
                <template v-if="push.signAs === 'guardian'">
                    <div class="kiosk-grid">
                        <v-text-field v-model="pushForm.guardianName" label="Parent or guardian name" variant="outlined" class="kiosk-input" autocomplete="off"></v-text-field>
                        <v-text-field v-model="pushForm.guardianPhone" type="tel" label="Parent or guardian phone" variant="outlined" class="kiosk-input" autocomplete="off"></v-text-field>
                    </div>
                </template>
                <v-checkbox v-model="pushForm.agreed" hide-details class="kiosk-check"
                    :label="push.signAs === 'guardian' ? `I am ${push.firstName}'s parent or guardian and I agree to this waiver for them` : 'I have read and agree to this waiver'"></v-checkbox>
                <div class="kiosk-label mt-2">{{ push.signAs === 'guardian' ? 'Parent or guardian signs here' : 'Sign here' }}</div>
                <SignaturePad v-model="pushForm.signature" />
                <v-alert v-if="error" type="error" variant="tonal" class="my-4">{{ error }}</v-alert>
                <div class="kiosk-actions">
                    <v-btn size="x-large" variant="text" :disabled="busy" @click="declinePush">Cancel</v-btn>
                    <v-btn size="x-large" color="primary" :loading="busy" :disabled="!pushWaiverComplete"
                        @click="push.wantPhoto ? (step = 'push-photo') : completePush()">{{ push.wantPhoto ? 'Next' : 'Finish' }}</v-btn>
                </div>
            </section>

            <section v-else-if="step === 'push-photo' && push" class="kiosk-card kiosk-center">
                <h1>Photo</h1>
                <p class="kiosk-sub">A photo of {{ push.firstName }}, so the gate knows them.</p>
                <PhotoCapture v-model="pushForm.photo" button-label="Take photo" :max-dimension="720" class="kiosk-photo" />
                <v-alert v-if="error" type="error" variant="tonal" class="my-4">{{ error }}</v-alert>
                <div class="kiosk-actions">
                    <v-btn size="x-large" variant="text" :disabled="busy" @click="push.wantWaiver ? (step = 'push-waiver') : declinePush()">
                        {{ push.wantWaiver ? 'Back' : 'Cancel' }}</v-btn>
                    <v-btn size="x-large" color="primary" :loading="busy" :disabled="!pushForm.photo" @click="completePush">Finish</v-btn>
                </div>
            </section>

            <section v-else-if="step === 'push-aborted'" class="kiosk-card kiosk-center">
                <v-icon size="72" color="warning">mdi-information-outline</v-icon>
                <h1>This request ended</h1>
                <p class="kiosk-sub">{{ pushAbortMessage || 'Staff cancelled it or it timed out. Please see the front desk.' }}</p>
                <v-btn size="x-large" variant="outlined" class="mt-6" @click="reset">OK</v-btn>
            </section>

            <section v-else-if="step === 'done'" class="kiosk-card kiosk-center">
                <v-icon size="96" color="success">mdi-check-circle</v-icon>
                <h1>{{ doneFromPush ? 'All done' : "You're checked in" }}</h1>
                <p class="kiosk-sub kiosk-done">{{ doneFromPush ? 'Thank you. The front desk can see it now.' : "You're checked in at the front desk. Tell the cashier your name." }}</p>
                <v-btn size="x-large" variant="outlined" class="mt-6" @click="reset">Done ({{ doneCountdown }})</v-btn>
            </section>
        </main>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import RichTextView from '@/components/RichTextView.vue'
import SignaturePad from '@/components/SignaturePad.vue'
import PhotoCapture from '@/components/PhotoCapture.vue'
import authHelper from '@/helpers/AuthHelper'
import { KioskDeviceApi, getKioskToken, saveKioskPairing, forgetKioskPairing, type KioskConfig, type KioskPushPayload, type KioskPushWaiver } from '@/services/KioskService'

type Step = 'pair' | 'loading' | 'start' | 'who' | 'guardian' | 'waiver' | 'photo' | 'done'
    | 'push-intro' | 'push-waiver' | 'push-photo' | 'push-aborted'

const IDLE_RESET_MS = 90_000      // nobody touched the screen mid-flow: start over, data cleared
const DONE_RESET_SECONDS = 12     // the done screen clears itself

const api = new KioskDeviceApi()
const step = ref<Step>('loading')
const config = ref<KioskConfig | null>(null)
const error = ref<string | null>(null)
const busy = ref(false)
const pairCode = ref('')
const doneCountdown = ref(DONE_RESET_SECONDS)

const blankForm = () => ({
    firstName: '', lastName: '', birthdate: '', email: '', phone: '',
    emergencyContactName: '', emergencyContactPhone: '',
    guardianFirstName: '', guardianLastName: '', guardianEmail: '', guardianPhone: '', guardianBirthdate: '',
    agreed: false,
    signature: null as string | null,
    photo: null as string | null,
})
const form = ref(blankForm())

/** Under 18 on the venue's date, as the gate judges it. */
function ageOn(birthdate: string, today: string): number {
    const [by, bm, bd] = birthdate.split('-').map(Number)
    const [ty, tm, td] = today.split('-').map(Number)
    let age = ty - by
    if (tm < bm || (tm === bm && td < bd)) age--
    return age
}
// Only what this venue's kiosk actually asks for: the waiver when there is one, the photo when it's required.
const welcomeLine = computed(() => {
    const steps = ['create your rider profile']
    if (config.value?.waiverId) steps.push('sign the waiver')
    if (config.value?.photoRequired) steps.push('take your photo')
    const list = steps.length === 1 ? steps[0] : `${steps.slice(0, -1).join(', ')} and ${steps[steps.length - 1]}`
    return `${list.charAt(0).toUpperCase()}${list.slice(1)}. It takes about two minutes.`
})
const isMinor = computed(() => !!form.value.birthdate && !!config.value
    && ageOn(form.value.birthdate, config.value.today) < 18)
const guardianName = computed(() => `${form.value.guardianFirstName} ${form.value.guardianLastName}`.trim())
const emailOk = (e: string) => /^[^@\s]+@[^@\s]+$/.test(e.trim())
const digits = (p: string) => p.replace(/\D/g, '').length
const whoComplete = computed(() => {
    const f = form.value
    if (!f.firstName.trim() || !f.lastName.trim() || !f.birthdate) return false
    if (!isMinor.value && !emailOk(f.email)) return false
    if (isMinor.value && f.email.trim() && !emailOk(f.email)) return false
    if (config.value?.requireEmergencyContact && !isMinor.value
        && (!f.emergencyContactName.trim() || digits(f.emergencyContactPhone) < 7)) return false
    return true
})
const guardianComplete = computed(() => {
    const f = form.value
    return !!f.guardianFirstName.trim() && !!f.guardianLastName.trim() && emailOk(f.guardianEmail)
        && digits(f.guardianPhone) >= 7 && !!f.guardianBirthdate
})

// ── Idle and done timers: a kiosk never keeps someone's details on screen ──────────────────
let idleTimer: ReturnType<typeof setTimeout> | null = null
let doneTimer: ReturnType<typeof setInterval> | null = null
function touch() {
    if (idleTimer) clearTimeout(idleTimer)
    if (['who', 'guardian', 'waiver', 'photo'].includes(step.value))
        idleTimer = setTimeout(() => reset(), IDLE_RESET_MS)
    // A pushed step left untouched is declined, so it doesn't come back over the next walk-up.
    else if (['push-intro', 'push-waiver', 'push-photo'].includes(step.value))
        idleTimer = setTimeout(() => declinePush(), IDLE_RESET_MS)
}
function clearTimers() {
    if (idleTimer) { clearTimeout(idleTimer); idleTimer = null }
    if (doneTimer) { clearInterval(doneTimer); doneTimer = null }
}

function reset() {
    clearTimers()
    form.value = blankForm()
    push.value = null
    pushWaiverDoc.value = null
    pushAbortMessage.value = null
    pushForm.value = blankPushForm()
    doneFromPush.value = false
    error.value = null
    busy.value = false
    step.value = getKioskToken() ? 'start' : 'pair'
    window.scrollTo(0, 0)
    // The venue's settings and waiver may have changed since: read them again for the next person.
    if (getKioskToken()) void refreshConfig()
}

/** Re-reads the kiosk settings (waiver text, photo rule, the venue's date) without leaving the screen. */
async function refreshConfig(): Promise<boolean> {
    try {
        config.value = (await api.config()).data.data
        return true
    } catch (err: any) {
        if (err.response?.status === 401) {
            forgetKioskPairing()
            config.value = null
            step.value = 'pair'
            error.value = err.response?.data?.error ?? null
        }
        return false
    }
}

async function begin() {
    busy.value = true
    error.value = null
    // Every sign-up starts from the venue's current waiver and settings.
    const ok = await refreshConfig()
    busy.value = false
    if (!ok) {
        if (step.value !== 'pair') error.value = 'The kiosk can’t reach the venue right now. Please see the front desk.'
        return
    }
    form.value = blankForm()
    step.value = 'who'
    touch()
}

function afterWho() {
    error.value = null
    step.value = isMinor.value ? 'guardian' : config.value?.waiverId ? 'waiver' : 'photo'
    touch()
}
function afterGuardian() {
    error.value = null
    if (ageOn(form.value.guardianBirthdate, config.value!.today) < 18) {
        error.value = 'A parent or guardian must be 18 or over.'
        return
    }
    step.value = config.value?.waiverId ? 'waiver' : 'photo'
    touch()
}

async function loadConfig() {
    step.value = 'loading'
    try {
        config.value = (await api.config()).data.data
        step.value = 'start'
    } catch (err: any) {
        if (err.response?.status === 401) {
            // Revoked or never paired: forget the secret and ask for a new pairing code.
            forgetKioskPairing()
            config.value = null
            step.value = 'pair'
            error.value = err.response?.data?.error ?? null
        } else {
            step.value = 'start'
            error.value = 'The kiosk can’t reach the venue right now. Please see the front desk.'
        }
    }
}

async function pair() {
    busy.value = true
    error.value = null
    try {
        const r = await api.pair(pairCode.value)
        saveKioskPairing(r.data.data.token, r.data.data.kioskName)
        // This device is a kiosk now: no staff session stays on it.
        authHelper.logout()
        pairCode.value = ''
        await loadConfig()
    } catch (err: any) {
        error.value = err.response?.data?.error || 'That pairing code didn’t work. Ask staff for a new one.'
    } finally {
        busy.value = false
    }
}

async function submit() {
    if (!config.value) return
    const f = form.value
    busy.value = true
    error.value = null
    try {
        await api.signup({
            firstName: f.firstName.trim(), lastName: f.lastName.trim(),
            email: f.email.trim() || null, phone: f.phone.trim() || null,
            birthdate: f.birthdate,
            guardianFirstName: isMinor.value ? f.guardianFirstName.trim() : null,
            guardianLastName: isMinor.value ? f.guardianLastName.trim() : null,
            guardianEmail: isMinor.value ? f.guardianEmail.trim() : null,
            guardianPhone: isMinor.value ? f.guardianPhone.trim() : null,
            guardianBirthdate: isMinor.value ? f.guardianBirthdate : null,
            emergencyContactName: !isMinor.value ? f.emergencyContactName.trim() || null : null,
            emergencyContactPhone: !isMinor.value ? f.emergencyContactPhone.trim() || null : null,
            waiverId: config.value.waiverId,
            agreedToWaiver: f.agreed,
            signatureDataUrl: config.value.waiverId ? f.signature : null,
            photoDataUrl: f.photo,
        })
        // Done: nothing they typed stays on the page.
        form.value = blankForm()
        showDone(false)
    } catch (err: any) {
        if (err.response?.status === 401) { forgetKioskPairing(); reset(); return }
        error.value = err.response?.data?.error || 'That didn’t go through. Please try again or see the front desk.'
    } finally {
        busy.value = false
    }
}

function showDone(fromPush: boolean) {
    clearTimers()
    doneFromPush.value = fromPush
    step.value = 'done'
    doneCountdown.value = DONE_RESET_SECONDS
    doneTimer = setInterval(() => {
        doneCountdown.value--
        if (doneCountdown.value <= 0) reset()
    }, 1000)
}

// ── Steps staff pushed to this kiosk ────────────────────────────────────────────────────────
// Asked for only while idle on the start screen, so a walk-up mid sign-up is never interrupted. While a
// pushed step is on screen the kiosk keeps asking, and aborts cleanly if staff cancel it or it expires.
const PUSH_POLL_MS = 3000
const push = ref<KioskPushPayload | null>(null)
/** The waiver text, fetched once when the step goes on screen (never on every poll). */
const pushWaiverDoc = ref<KioskPushWaiver | null>(null)
const pushAbortMessage = ref<string | null>(null)
const doneFromPush = ref(false)
const blankPushForm = () => ({ guardianName: '', guardianPhone: '', agreed: false, signature: null as string | null, photo: null as string | null })
const pushForm = ref(blankPushForm())
const pushWaiverComplete = computed(() => {
    const f = pushForm.value
    if (!f.agreed || !f.signature) return false
    return push.value?.signAs !== 'guardian' || (!!f.guardianName.trim() && digits(f.guardianPhone) >= 7)
})
let pushTimer: ReturnType<typeof setInterval> | null = null
let polling = false

async function pollPush() {
    if (polling || !getKioskToken()) return
    const onPush = ['push-intro', 'push-waiver', 'push-photo'].includes(step.value)
    if (step.value !== 'start' && !onPush) return
    polling = true
    try {
        const p = (await api.pollPush()).data.data
        if (onPush) {
            // The request on screen was cancelled or expired: stop, clear what was entered.
            if (!busy.value && (!p || p.pushId !== push.value?.pushId)) abortPush(null)
        } else if (p && step.value === 'start') {
            push.value = p
            pushForm.value = blankPushForm()
            error.value = null
            step.value = 'push-intro'
            touch()
            try { await api.startPush(p.pushId) } catch { /* shown anyway; staff see "waiting" a moment longer */ }
        }
    } catch (err: any) {
        if (err.response?.status === 401) { forgetKioskPairing(); reset() }
    } finally {
        polling = false
    }
}

function abortPush(message: string | null) {
    clearTimers()
    push.value = null
    pushWaiverDoc.value = null
    pushForm.value = blankPushForm()
    pushAbortMessage.value = message
    step.value = 'push-aborted'
}

/** "Not me", Cancel, or left untouched: the request ends (staff see "Declined at kiosk"). */
function declinePush() {
    const id = push.value?.pushId
    if (id) void api.declinePush(id).catch(() => { /* it already ended */ })
    reset()
}

async function beginPush() {
    const p = push.value
    if (!p) return
    if (p.wantWaiver && pushWaiverDoc.value?.waiverId !== p.waiverId) {
        busy.value = true
        try {
            pushWaiverDoc.value = (await api.pushWaiver(p.pushId)).data.data
        } catch (err: any) {
            if (err.response?.status === 401) { forgetKioskPairing(); reset(); return }
            abortPush(err.response?.data?.error ?? null)
            return
        } finally {
            busy.value = false
        }
    }
    step.value = p.wantWaiver ? 'push-waiver' : 'push-photo'
    touch()
}

async function completePush() {
    const p = push.value
    if (!p) return
    busy.value = true
    error.value = null
    try {
        const f = pushForm.value
        await api.completePush(p.pushId, {
            waiverId: p.wantWaiver ? p.waiverId : null,
            agreedToWaiver: f.agreed,
            signatureDataUrl: p.wantWaiver ? f.signature : null,
            guardianName: p.signAs === 'guardian' ? f.guardianName.trim() : null,
            guardianPhone: p.signAs === 'guardian' ? f.guardianPhone.trim() : null,
            photoDataUrl: p.wantPhoto ? f.photo : null,
        })
        push.value = null
        pushForm.value = blankPushForm()
        showDone(true)
    } catch (err: any) {
        if (err.response?.status === 401) { forgetKioskPairing(); reset(); return }
        // Ended (the waiver or who signs changed) or no longer active: say why when the server did.
        if (err.response?.status === 409) { abortPush(err.response?.data?.error ?? null); return }
        error.value = err.response?.data?.error || 'That didn’t go through. Please try again or see the front desk.'
    } finally {
        busy.value = false
    }
}

onMounted(async () => {
    pushTimer = setInterval(() => { void pollPush() }, PUSH_POLL_MS)
    if (getKioskToken()) {
        // A paired kiosk never carries a staff session.
        if (authHelper.isAuthenticated()) authHelper.logout()
        await loadConfig()
    } else {
        step.value = 'pair'
    }
})
onBeforeUnmount(() => {
    clearTimers()
    if (pushTimer) clearInterval(pushTimer)
})
</script>

<style scoped>
.kiosk {
    min-height: 100vh;
    background: var(--rp-bg, var(--v-theme-background));
    color: var(--rp-ink, inherit);
    display: flex;
    flex-direction: column;
}
.kiosk-head {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 16px 24px;
    border-bottom: 1px solid var(--rp-line);
}
.kiosk-venue { font-size: 1.4rem; font-weight: 700; }
.kiosk-device { font-size: 0.9rem; color: var(--rp-muted, inherit); opacity: 0.8; }
.kiosk-body {
    flex: 1;
    display: flex;
    justify-content: center;
    align-items: flex-start;
    padding: 24px 16px 48px;
}
.kiosk-card {
    width: 100%;
    max-width: 760px;
    background: var(--rp-card, transparent);
    border: 1px solid var(--rp-line);
    border-radius: 16px;
    padding: 28px 24px;
}
.kiosk-card h1 { font-size: 2rem; line-height: 1.2; margin: 0 0 8px; }
.kiosk-sub { font-size: 1.15rem; opacity: 0.85; margin: 0 0 20px; }
.kiosk-done { font-size: 1.4rem; }
.kiosk-center { text-align: center; }
.kiosk-big { min-width: 260px; min-height: 72px; font-size: 1.4rem; }
.kiosk-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
@media (max-width: 600px) { .kiosk-grid { grid-template-columns: 1fr; gap: 0; } }
.kiosk-input :deep(input) { font-size: 1.25rem; min-height: 56px; }
.kiosk-code :deep(input) { letter-spacing: 0.2em; text-transform: uppercase; font-size: 1.6rem; }
.kiosk-label { font-weight: 600; font-size: 1.05rem; margin: 4px 0 8px; }
.kiosk-check :deep(label) { font-size: 1.1rem; }
.kiosk-waiver {
    max-height: 40vh;
    overflow-y: auto;
    border: 1px solid var(--rp-line);
    border-radius: 10px;
    padding: 12px 16px;
    background: var(--rp-panel2, transparent);
    margin-bottom: 12px;
}
.kiosk-actions {
    display: flex;
    justify-content: space-between;
    gap: 12px;
    margin-top: 20px;
    flex-wrap: wrap;
}
.kiosk-actions :deep(.v-btn) { min-height: 64px; min-width: 140px; font-size: 1.15rem; }
.kiosk-photo { display: flex; flex-direction: column; align-items: center; }
.kiosk-photo :deep(img) { max-width: 100%; max-height: 50vh; border-radius: 12px; }
.kiosk-photo :deep(.v-btn) { min-height: 72px; font-size: 1.3rem; }
</style>
