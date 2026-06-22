<template>
    <v-container>
        <h1 class="text-h4 mb-2">Features</h1>
        <p class="text-body-2 text-medium-emphasis mb-6">
            Plan features (marked <strong>Included</strong>) are enabled for your track by RidePass , contact us to
            add or remove one. The remaining settings are policies you control.
        </p>

        <v-card>
            <v-list>
                <template v-for="(f, idx) in visibleFeatures" :key="f.key">
                    <v-divider v-if="idx > 0"></v-divider>
                    <v-list-item :disabled="!!savingKey">
                        <template #prepend>
                            <v-icon :icon="f.icon" :color="f.enabled ? 'primary' : undefined" class="mr-2"></v-icon>
                        </template>
                        <v-list-item-title>{{ f.title }}</v-list-item-title>
                        <v-list-item-subtitle style="white-space: normal">
                            {{ f.description }}
                        </v-list-item-subtitle>
                        <template #append>
                            <div class="d-flex align-center ga-2">
                                <v-btn v-if="f.configureTo && f.enabled"
                                    variant="text" size="small" :to="f.configureTo">
                                    Configure
                                </v-btn>
                                <!-- Platform features are super-admin controlled: shown as a
                                     locked "Included" badge, never a tenant-settable toggle. -->
                                <v-chip v-if="isPlatformFeature(f.key)" size="small" color="success"
                                    variant="tonal" prepend-icon="mdi-lock-check">Included</v-chip>
                                <v-switch v-else
                                    :model-value="f.enabled"
                                    @update:model-value="(v: boolean | null) => toggle(f, !!v)"
                                    color="primary"
                                    :loading="savingKey === f.key"
                                    :disabled="savingKey !== null && savingKey !== f.key"
                                    hide-details inset></v-switch>
                            </div>
                        </template>
                    </v-list-item>

                    <!-- Inline waitlist config: only shown when the toggle is on. -->
                    <v-expand-transition v-if="f.key === 'waitlist'">
                        <div v-if="f.enabled" class="px-4 pb-4 pl-14">
                            <v-text-field v-model.number="waitlistConfirmMinutes"
                                type="number" min="5" max="240" density="compact"
                                label="Confirm window (minutes)"
                                hint="When a spot opens, the next alternate has this long to pay before it rolls to the next person. Default 20."
                                persistent-hint
                                :disabled="!!savingKey"
                                style="max-width: 360px">
                                <template #append>
                                    <v-btn size="small" color="primary" variant="tonal"
                                        :loading="savingKey === 'waitlistWindow'"
                                        :disabled="!waitlistWindowDirty || waitlistConfirmMinutes < 5 || waitlistConfirmMinutes > 240"
                                        @click="saveWaitlistWindow">
                                        Save
                                    </v-btn>
                                </template>
                            </v-text-field>
                        </div>
                    </v-expand-transition>

                    <!-- Inline gift-card config: min/max denominations a buyer can pick. -->
                    <v-expand-transition v-if="f.key === 'giftCards'">
                        <div v-if="f.enabled" class="px-4 pb-4 pl-14">
                            <p class="text-caption text-medium-emphasis mb-2" style="max-width: 540px">
                                Set the smallest and largest amount a buyer can pick when purchasing a gift card.
                            </p>
                            <div class="d-flex ga-3 flex-wrap" style="max-width: 540px">
                                <v-text-field v-model.number="giftCardMin"
                                    type="number" min="1" max="10000" density="compact" prefix="$"
                                    label="Minimum" :disabled="!!savingKey"
                                    :error-messages="giftCardMinError ? [giftCardMinError] : []"
                                    style="flex: 1 1 200px"></v-text-field>
                                <v-text-field v-model.number="giftCardMax"
                                    type="number" min="1" max="10000" density="compact" prefix="$"
                                    label="Maximum" :disabled="!!savingKey"
                                    :error-messages="giftCardMaxError ? [giftCardMaxError] : []"
                                    style="flex: 1 1 200px"></v-text-field>
                            </div>
                            <v-btn size="small" color="primary" variant="tonal" class="mt-2"
                                :loading="savingKey === 'giftCardLimits'"
                                :disabled="!giftCardDirty || !!giftCardMinError || !!giftCardMaxError"
                                @click="saveGiftCardLimits">
                                Save
                            </v-btn>
                        </div>
                    </v-expand-transition>
                </template>
            </v-list>
        </v-card>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000" location="top">
            {{ snackbarText }}
        </v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { TenantService } from '@/services/TenantService'
import { MembershipService } from '@/services/MembershipService'
import { branding, loadBranding } from '@/stores/branding'

const tenantService = new TenantService()
const membershipService = new MembershipService()

interface Feature {
    key: string
    title: string
    description: string
    icon: string
    enabled: boolean
    configureTo?: string
    apply: (next: boolean) => Promise<void>
}

// Master switches read live from the branding store and write to whichever
// existing endpoint owns that bit. Detail config (prices, limits, names) stays
// on the per-feature settings pages — this page only flips the on/off bit.
//
// updateSettings (timezone + three booleans) must be called with all four
// fields together — passing the others through unchanged keeps it idempotent.
const features = computed<Feature[]>(() => [
    {
        key: 'extras',
        title: 'Add-ons',
        description: 'Sell camping, parking, pit-vehicle passes, merch, and other event extras.',
        icon: 'mdi-tag-plus',
        enabled: branding.extrasEnabled,
        configureTo: '/Admin/Extras',
        apply: async (next) => {
            await tenantService.updateExtrasEnabled({ enabled: next })
        },
    },
    {
        key: 'membership',
        title: 'Memberships',
        description: 'Sell yearly or one-time memberships.',
        icon: 'mdi-card-account-details',
        enabled: branding.membershipEnabled,
        configureTo: '/Admin/Settings/Membership',
        apply: async (next) => {
            await membershipService.updateSettings({
                enabled: next,
                name: branding.membershipName,
                priceCents: branding.membershipPriceCents,
                durationKind: branding.membershipDurationKind,
                requiredForRiders: branding.membershipRequiredForRiders,
                requiredForSpectators: branding.membershipRequiredForSpectators,
            })
        },
    },
    {
        key: 'giftCards',
        title: 'Gift Cards',
        description: 'Riders can buy and redeem digital gift cards delivered by email.',
        icon: 'mdi-gift',
        enabled: branding.giftCardsEnabled,
        apply: async (next) => {
            await tenantService.updateGiftCardSettings({
                enabled: next,
                minCents: branding.giftCardMinCents,
                maxCents: branding.giftCardMaxCents,
            })
        },
    },
    {
        key: 'rentals',
        title: 'Rentals',
        description: 'Rent gear (bikes, helmets, pads) per session with deposit + insurance support.',
        icon: 'mdi-bike-fast',
        enabled: branding.rentalsEnabled,
        configureTo: '/Admin/Rentals',
        apply: async (next) => {
            await tenantService.updateRentalsEnabled({ enabled: next })
        },
    },
    {
        key: 'seasonPasses',
        title: 'Season Passes',
        description: 'Sell season-long passes that cover entry to qualifying events.',
        icon: 'mdi-ticket-percent',
        enabled: branding.seasonPassesEnabled,
        configureTo: '/Admin/SeasonPasses',
        apply: async (next) => {
            await tenantService.updateSeasonPassesEnabled({ enabled: next })
        },
    },
    {
        key: 'concessions',
        title: 'Concessions',
        description: 'Sell food, drink, and swag from the mobile tap-to-pay app, separate from events.',
        icon: 'mdi-storefront',
        enabled: branding.concessionsEnabled,
        configureTo: '/Admin/Concessions',
        apply: async (next) => {
            await tenantService.updateConcessionsEnabled({ enabled: next })
        },
    },
    {
        key: 'blog',
        title: 'Blog',
        description: 'Publish posts with photos and feature one on your home page. Adds a Blog link to your public nav.',
        icon: 'mdi-post',
        enabled: branding.blogEnabled,
        configureTo: '/Admin/Blog',
        apply: async (next) => {
            await tenantService.updateBlogEnabled({ enabled: next })
        },
    },
    {
        key: 'allowSelfCancel',
        title: 'Self-cancel purchases',
        description: 'Riders cancel their own purchases from My Passes (refund honors the service-charge rule).',
        icon: 'mdi-cancel',
        enabled: branding.allowSelfCancel,
        apply: async (next) => {
            await tenantService.updateCancellationPolicy({
                allowSelfCancel: next,
                waitlistEnabled: branding.waitlistEnabled,
                waitlistConfirmWindowMinutes: branding.waitlistConfirmWindowMinutes,
            })
        },
    },
    {
        key: 'waitlist',
        title: 'Waitlist',
        description: 'Sold-out events and tiers offer a waitlist; alternates get texted when a spot opens.',
        icon: 'mdi-account-clock',
        enabled: branding.waitlistEnabled,
        apply: async (next) => {
            await tenantService.updateCancellationPolicy({
                allowSelfCancel: branding.allowSelfCancel,
                waitlistEnabled: next,
                waitlistConfirmWindowMinutes: branding.waitlistConfirmWindowMinutes,
            })
        },
    },
    {
        key: 'requireReservationForPasses',
        title: 'Require reservation for passes',
        description: 'Riders must reserve a spot at an event before buying a pass — no walk-up sales.',
        icon: 'mdi-calendar-check',
        enabled: branding.requireReservationForPasses,
        apply: async (next) => {
            await tenantService.updateSettings({
                timezone: branding.timezone,
                requireReservationForPasses: next,
                requireEmergencyContact: branding.requireEmergencyContact,
                allowEventSubscriptions: branding.allowEventSubscriptions,
                requireIdAtCheckin: branding.requireIdAtCheckin,
            })
        },
    },
    {
        key: 'requireEmergencyContact',
        title: 'Require emergency contact',
        description: 'Riders must add an emergency contact on their profile before any purchase.',
        icon: 'mdi-phone-alert',
        enabled: branding.requireEmergencyContact,
        apply: async (next) => {
            await tenantService.updateSettings({
                timezone: branding.timezone,
                requireReservationForPasses: branding.requireReservationForPasses,
                requireEmergencyContact: next,
                allowEventSubscriptions: branding.allowEventSubscriptions,
                requireIdAtCheckin: branding.requireIdAtCheckin,
            })
        },
    },
    {
        key: 'allowEventSubscriptions',
        title: 'Event subscriptions',
        description: 'Riders can subscribe to be notified when new events get scheduled.',
        icon: 'mdi-email-multiple',
        enabled: branding.allowEventSubscriptions,
        apply: async (next) => {
            await tenantService.updateSettings({
                timezone: branding.timezone,
                requireReservationForPasses: branding.requireReservationForPasses,
                requireEmergencyContact: branding.requireEmergencyContact,
                allowEventSubscriptions: next,
                requireIdAtCheckin: branding.requireIdAtCheckin,
            })
        },
    },
    {
        key: 'requireIdAtCheckin',
        title: 'Require ID at check-in',
        description: 'Gate staff must confirm they checked the rider\'s photo ID against the purchaser name before redeeming. One QR scan still pulls up the rider\'s whole order for the event.',
        icon: 'mdi-card-account-details-outline',
        enabled: branding.requireIdAtCheckin,
        apply: async (next) => {
            await tenantService.updateSettings({
                timezone: branding.timezone,
                requireReservationForPasses: branding.requireReservationForPasses,
                requireEmergencyContact: branding.requireEmergencyContact,
                allowEventSubscriptions: branding.allowEventSubscriptions,
                requireIdAtCheckin: next,
            })
        },
    },
])

// Super-admin-gated platform features: the tenant can't flip these on/off (that's a
// plan decision made in Super Admin → tenant settings). They appear here only when
// enabled, as a read-only "Included" badge plus any inline config. The remaining
// entries are tenant-controlled policies that keep their toggles.
const PLATFORM_FEATURE_KEYS = new Set([
    'extras', 'membership', 'giftCards', 'rentals', 'seasonPasses',
    'concessions', 'blog', 'allowSelfCancel', 'waitlist',
])
function isPlatformFeature(key: string): boolean {
    return PLATFORM_FEATURE_KEYS.has(key)
}
const visibleFeatures = computed(() =>
    features.value.filter(f => isPlatformFeature(f.key) ? f.enabled : true))

const savingKey = ref<string | null>(null)
const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

// Inline waitlist confirm-window: edits live in this ref, saved on demand so a
// toggle-on doesn't blow away an in-flight edit. Reset whenever branding reloads.
const waitlistConfirmMinutes = ref<number>(branding.waitlistConfirmWindowMinutes)
const waitlistWindowDirty = computed(() =>
    waitlistConfirmMinutes.value !== branding.waitlistConfirmWindowMinutes)

async function saveWaitlistWindow() {
    if (savingKey.value) return
    if (waitlistConfirmMinutes.value < 5 || waitlistConfirmMinutes.value > 240) return
    savingKey.value = 'waitlistWindow'
    try {
        await tenantService.updateCancellationPolicy({
            allowSelfCancel: branding.allowSelfCancel,
            waitlistEnabled: branding.waitlistEnabled,
            waitlistConfirmWindowMinutes: waitlistConfirmMinutes.value,
        })
        await loadBranding()
        flash('Waitlist window saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        savingKey.value = null
    }
}

// Inline gift-card limits — same pattern, save on demand so toggling on doesn't
// nuke the in-flight edit. Limits are stored in cents on the tenant; we surface
// them as whole dollars for the admin since the UI only ever takes round amounts.
const giftCardMin = ref<number>(Math.round(branding.giftCardMinCents / 100))
const giftCardMax = ref<number>(Math.round(branding.giftCardMaxCents / 100))
const giftCardMinError = computed(() => {
    if (!Number.isFinite(giftCardMin.value) || giftCardMin.value < 1) return 'Minimum must be at least $1.'
    return ''
})
const giftCardMaxError = computed(() => {
    if (!Number.isFinite(giftCardMax.value) || giftCardMax.value < giftCardMin.value) return 'Maximum must be ≥ minimum.'
    if (giftCardMax.value > 10000) return 'Maximum can\'t exceed $10,000.'
    return ''
})
const giftCardDirty = computed(() =>
    giftCardMin.value !== Math.round(branding.giftCardMinCents / 100)
    || giftCardMax.value !== Math.round(branding.giftCardMaxCents / 100))

async function saveGiftCardLimits() {
    if (savingKey.value) return
    if (giftCardMinError.value || giftCardMaxError.value) return
    savingKey.value = 'giftCardLimits'
    try {
        await tenantService.updateGiftCardSettings({
            enabled: branding.giftCardsEnabled,
            minCents: Math.round(giftCardMin.value * 100),
            maxCents: Math.round(giftCardMax.value * 100),
        })
        await loadBranding()
        flash('Gift card limits saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        savingKey.value = null
    }
}

async function toggle(f: Feature, next: boolean) {
    if (savingKey.value) return
    savingKey.value = f.key
    try {
        await f.apply(next)
        await loadBranding()
        // Re-sync any inline editors so they don't show stale local state.
        waitlistConfirmMinutes.value = branding.waitlistConfirmWindowMinutes
        giftCardMin.value = Math.round(branding.giftCardMinCents / 100)
        giftCardMax.value = Math.round(branding.giftCardMaxCents / 100)
        flash(`${f.title} ${next ? 'enabled' : 'disabled'}.`, 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        savingKey.value = null
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onMounted(async () => {
    if (!branding.loaded) await loadBranding()
})
</script>
