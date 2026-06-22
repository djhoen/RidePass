<template>
    <v-container class="py-8" style="max-width: 720px">
        <div v-if="loading" class="d-flex justify-center py-12">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <div v-else-if="done" class="text-center py-8">
            <v-icon color="success" size="56">mdi-check-circle</v-icon>
            <h1 class="text-h5 font-weight-bold mt-2 mb-1">Registration complete</h1>
            <p class="text-body-2 text-medium-emphasis mb-4">You're all set for the gate. See you at the track!</p>
            <div class="d-flex flex-column flex-sm-row justify-center ga-2">
                <v-btn v-if="isAuthed" color="primary" to="/User/Upcoming" prepend-icon="mdi-calendar-check">My upcoming events</v-btn>
                <v-btn variant="tonal" to="/Events">Browse events</v-btn>
            </div>

            <!-- Guests: optional account creation, prefilled from what they just entered. -->
            <div v-if="!isAuthed" class="reg-signup mt-6 pa-4 text-left">
                <h2 class="text-subtitle-1 font-weight-bold mb-1">Create your free account</h2>
                <p class="text-body-2 text-medium-emphasis mb-3">Optional, but it makes your next visit faster.</p>
                <AccountSignupForm :prefill="signupPrefill" />
            </div>
        </div>

        <div v-else-if="loadError" class="text-center py-8">
            <v-alert type="error" variant="tonal">{{ loadError }}</v-alert>
        </div>

        <div v-else-if="riders.length === 0 && spectators.length === 0" class="text-center py-8">
            <v-icon color="success" size="48">mdi-check</v-icon>
            <h1 class="text-h6 font-weight-bold mt-2 mb-1">Nothing left to finish</h1>
            <p class="text-body-2 text-medium-emphasis">This registration is already complete, or the link has expired.</p>
        </div>

        <template v-else>
            <h1 class="text-h5 font-weight-bold font-display mb-1">Finish your registration</h1>
            <p class="text-body-2 text-medium-emphasis mb-4">
                {{ eventTitle }} , add rider details{{ anyWaiver ? ' and sign the waiver' : '' }} to check in at the gate.
            </p>

            <div v-for="(rider, ri) in riders" :key="'rider-' + ri" class="reg-card mb-4 pa-3">
                <div class="font-weight-medium mb-2">Rider {{ riders.length > 1 ? ri + 1 : '' }}</div>
                <v-row dense class="mt-4">
                    <v-col cols="6"><v-text-field v-model="rider.firstName" label="First name" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="6"><v-text-field v-model="rider.lastName" label="Last name" density="compact" hide-details></v-text-field></v-col>
                </v-row>
                <v-row dense class="mt-4">
                    <v-col cols="6"><v-text-field v-model="rider.birthdate" type="date" label="Date of birth" density="compact" :max="todayIso" hide-details></v-text-field></v-col>
                    <v-col cols="6"><v-text-field v-model="rider.bike" label="Bike (optional)" density="compact" hide-details></v-text-field></v-col>
                </v-row>
                <v-row v-if="branding.requireEmergencyContact" dense class="mt-4">
                    <v-col cols="6"><v-text-field v-model="rider.emergencyName" label="Emergency contact name" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="6"><v-text-field v-model="rider.emergencyPhone" type="tel" label="Emergency contact phone" density="compact" hide-details></v-text-field></v-col>
                </v-row>
                <template v-if="classAssigns.length">
                    <div class="text-caption text-medium-emphasis mt-3 mb-1">Race classes for this rider</div>
                    <div v-for="ca in classesForRider(ri)" :key="ca.ticketId" class="d-flex align-center ga-2 mb-1">
                        <div class="flex-grow-1">{{ ca.tierName }}</div>
                        <v-text-field v-model="ca.raceNumber" label="Race #" density="compact" hide-details style="max-width: 110px"></v-text-field>
                        <v-select v-if="riders.length > 1" :model-value="ca.riderIndex"
                            @update:model-value="ca.riderIndex = $event"
                            :items="riderIndexOptions" item-title="label" item-value="value"
                            label="Rider" density="compact" hide-details style="max-width: 120px"></v-select>
                    </div>
                </template>
                <template v-if="rider.needsWaiver">
                    <div class="text-caption text-medium-emphasis mt-3 mb-1">
                        {{ isMinor(rider.birthdate) ? 'Rider is under 18 — a parent/guardian must sign' : 'Signature' }}
                    </div>
                    <v-text-field v-if="isMinor(rider.birthdate)" v-model="rider.parentName" label="Parent/guardian name" density="compact" class="mb-2" hide-details></v-text-field>
                    <SignaturePad v-model="rider.signatureDataUrl" />
                </template>
            </div>

            <div v-for="(spec, si) in spectators" :key="'spec-' + si" class="reg-card mb-4 pa-3">
                <div class="font-weight-medium mb-2">Spectator {{ spectators.length > 1 ? si + 1 : '' }} — {{ spec.tierName }}</div>
                <v-row dense class="mt-4">
                    <v-col cols="6"><v-text-field v-model="spec.firstName" label="First name" density="compact" hide-details></v-text-field></v-col>
                    <v-col cols="6"><v-text-field v-model="spec.lastName" label="Last name" density="compact" hide-details></v-text-field></v-col>
                </v-row>
                <v-text-field v-model="spec.birthdate" type="date" label="Date of birth" density="compact" class="mt-4" :max="todayIso" hide-details></v-text-field>
                <div class="text-caption text-medium-emphasis mt-3 mb-1">
                    {{ isMinor(spec.birthdate) ? 'Under 18 — a parent/guardian must sign' : 'Signature' }}
                </div>
                <v-text-field v-if="isMinor(spec.birthdate)" v-model="spec.parentName" label="Parent/guardian name" density="compact" class="mb-2" hide-details></v-text-field>
                <SignaturePad v-model="spec.signatureDataUrl" />
            </div>

            <div v-if="error" class="text-error text-body-2 mb-2">{{ error }}</div>
            <v-btn block color="primary" size="large" :loading="saving" @click="finish">Finish registration</v-btn>
        </template>

        <v-snackbar v-model="snackbar" color="error" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import dayjs from 'dayjs'
import { TicketService, type RegistrationTicket } from '@/services/TicketService'
import { UserService } from '@/services/UserService'
import { branding } from '@/stores/branding'
import authHelper from '@/helpers/AuthHelper'
import SignaturePad from '@/components/SignaturePad.vue'
import AccountSignupForm from '@/components/AccountSignupForm.vue'

const route = useRoute()
const service = new TicketService()
const userService = new UserService()
const token = String(route.params.token)
const isAuthed = authHelper.isAuthenticated()

interface RiderCard {
    firstName: string
    lastName: string
    birthdate: string
    bike: string
    parentName: string
    emergencyName: string
    emergencyPhone: string
    signatureDataUrl: string | null
    gateTicketId: string | null
    needsWaiver: boolean
}
interface ClassAssign {
    ticketId: string
    tierName: string
    riderIndex: number
    raceNumber: string
}
interface SpectatorCard {
    ticketId: string
    tierName: string
    firstName: string
    lastName: string
    birthdate: string
    parentName: string
    signatureDataUrl: string | null
}

const loading = ref(true)
const saving = ref(false)
const done = ref(false)
const error = ref('')
const loadError = ref('')
const eventTitle = ref<string | null>(null)
const riders = ref<RiderCard[]>([])
const classAssigns = ref<ClassAssign[]>([])
const spectators = ref<SpectatorCard[]>([])
const snackbar = ref(false)
const snackbarText = ref('')
const todayIso = dayjs().format('YYYY-MM-DD')

const anyWaiver = computed(() => riders.value.some(r => r.needsWaiver) || spectators.value.length > 0)

// Prefill the post-registration signup from the first rider (or spectator) we just
// collected. There's no purchaser email in the registration payload, so they enter that.
const signupPrefill = computed(() => {
    const r0 = riders.value[0]
    const s0 = spectators.value[0]
    return {
        firstName: r0?.firstName || s0?.firstName || '',
        lastName: r0?.lastName || s0?.lastName || '',
        birthdate: r0?.birthdate || s0?.birthdate || '',
        emergencyName: r0?.emergencyName || '',
        emergencyPhone: r0?.emergencyPhone || '',
    }
})
const riderIndexOptions = computed(() =>
    Array.from({ length: riders.value.length }, (_, i) => ({ value: i, label: `Rider ${i + 1}` })))

function isMinor(birthdate: string): boolean {
    return !!birthdate && dayjs().diff(dayjs(birthdate), 'year') < 18
}
function classesForRider(ri: number): ClassAssign[] {
    return classAssigns.value.filter(c => c.riderIndex === ri)
}

onMounted(async () => {
    try {
        const r = await service.getRegistration(token)
        const d = (r.data as any).data
        eventTitle.value = d.eventTitle
        build((d.tickets ?? []) as RegistrationTicket[])
        await prefillEmergencyFromProfile()
    } catch (err: any) {
        riders.value = []
        spectators.value = []
        // A 404 means the link is genuinely expired / already used: show the soft
        // "nothing left to finish" state. Any other failure (500 / network) is a
        // real error the rider needs to know about before they reach the gate.
        if (err.response?.status !== 404) {
            loadError.value = err.response?.data?.error
                || 'Could not load your registration. Refresh to try again, or use the link from your confirmation email.'
        }
    } finally {
        loading.value = false
    }
})

// Group the incomplete tickets into riders (gate fee + assigned classes) and spectators.
// The original rider grouping wasn't persisted, so we infer it: a rider per gate fee
// when gate fees exist, otherwise one rider per race class (the buyer can consolidate
// via the per-class rider selector).
function build(tickets: RegistrationTicket[]) {
    const classTickets = tickets.filter(t => t.isRace)
    const riderGateTickets = tickets.filter(t => t.isRiderGate)
    const spectatorTickets = tickets.filter(t => t.isSpectatorGate && t.needsWaiver)
    const riderWaiver = [...classTickets, ...riderGateTickets].some(t => t.needsWaiver)

    const ridersNeeded = riderGateTickets.length > 0
        ? riderGateTickets.length
        : classTickets.length

    riders.value = Array.from({ length: ridersNeeded }, (_, i) => ({
        firstName: '', lastName: '', birthdate: '', bike: '', parentName: '',
        emergencyName: '', emergencyPhone: '', signatureDataUrl: null,
        gateTicketId: riderGateTickets[i]?.ticketId ?? null,
        needsWaiver: riderWaiver,
    }))
    classAssigns.value = classTickets.map((t, i) => ({
        ticketId: t.ticketId,
        tierName: t.tierName,
        riderIndex: ridersNeeded > 0 ? i % ridersNeeded : 0,
        raceNumber: '',
    }))
    spectators.value = spectatorTickets.map(t => ({
        ticketId: t.ticketId, tierName: t.tierName,
        firstName: '', lastName: '', birthdate: '', parentName: '', signatureDataUrl: null,
    }))
}

// Pre-fill the first rider's emergency contact from the logged-in buyer's profile (the buyer
// is usually the first rider). Best-effort: skipped for guests, never overwrites a typed value.
async function prefillEmergencyFromProfile() {
    if (!isAuthed || riders.value.length === 0) return
    try {
        const r = await userService.getProfile()
        const p: any = (r.data as any).data ?? r.data
        if (!riders.value[0].emergencyName) riders.value[0].emergencyName = p.emergencyContactName ?? ''
        if (!riders.value[0].emergencyPhone) riders.value[0].emergencyPhone = p.emergencyContactPhone ?? ''
    } catch { /* leave blank for them to fill */ }
}

async function finish() {
    error.value = ''
    const registrants: Array<{
        firstName: string; lastName: string; birthdate?: string | null; bike?: string | null
        parentGuardianName?: string | null
        emergencyContactName?: string | null; emergencyContactPhone?: string | null
        waiverSignatureDataUrl?: string | null
        tickets: Array<{ ticketId: string; raceNumber?: string | null }>
    }> = []

    for (let i = 0; i < riders.value.length; i++) {
        const r = riders.value[i]
        const tickets: Array<{ ticketId: string; raceNumber?: string | null }> = []
        if (r.gateTicketId) tickets.push({ ticketId: r.gateTicketId })
        for (const ca of classAssigns.value.filter(c => c.riderIndex === i)) {
            tickets.push({ ticketId: ca.ticketId, raceNumber: ca.raceNumber.trim() || null })
        }
        if (tickets.length === 0) continue
        if (!r.firstName.trim() || !r.lastName.trim()) { error.value = `Rider ${i + 1} needs a first and last name.`; return }
        if (r.needsWaiver && !r.signatureDataUrl) { error.value = `${r.firstName || `Rider ${i + 1}`} needs to sign the waiver.`; return }
        if (r.needsWaiver && isMinor(r.birthdate) && !r.parentName.trim()) { error.value = `A parent/guardian name is required for ${r.firstName || `rider ${i + 1}`}.`; return }
        if (branding.requireEmergencyContact && !r.emergencyPhone.trim()) { error.value = `An emergency contact phone is required for ${r.firstName || `rider ${i + 1}`}.`; return }
        registrants.push({
            firstName: r.firstName.trim(), lastName: r.lastName.trim(),
            birthdate: r.birthdate || null, bike: r.bike.trim() || null,
            parentGuardianName: r.needsWaiver && isMinor(r.birthdate) ? r.parentName.trim() : null,
            emergencyContactName: branding.requireEmergencyContact ? (r.emergencyName.trim() || null) : null,
            emergencyContactPhone: branding.requireEmergencyContact ? (r.emergencyPhone.trim() || null) : null,
            waiverSignatureDataUrl: r.needsWaiver ? r.signatureDataUrl : null,
            tickets,
        })
    }

    for (const s of spectators.value) {
        if (!s.firstName.trim() || !s.lastName.trim()) { error.value = 'Each spectator needs a first and last name.'; return }
        if (!s.signatureDataUrl) { error.value = `${s.firstName || 'This spectator'} needs to sign the waiver.`; return }
        if (isMinor(s.birthdate) && !s.parentName.trim()) { error.value = `A parent/guardian name is required for ${s.firstName || 'the minor spectator'}.`; return }
        registrants.push({
            firstName: s.firstName.trim(), lastName: s.lastName.trim(),
            birthdate: s.birthdate || null, bike: null,
            parentGuardianName: isMinor(s.birthdate) ? s.parentName.trim() : null,
            waiverSignatureDataUrl: s.signatureDataUrl,
            tickets: [{ ticketId: s.ticketId }],
        })
    }

    if (registrants.length === 0) { error.value = 'Add at least one rider.'; return }

    saving.value = true
    try {
        await service.completeRegistration({ registrants })
        done.value = true
    } catch (err: any) {
        snackbarText.value = err.response?.data?.error || 'Could not save registration.'
        snackbar.value = true
    } finally {
        saving.value = false
    }
}
</script>

<style scoped>
.reg-card,
.reg-signup {
    background: rgba(0, 0, 0, 0.03);
    border-radius: 8px;
}
</style>
