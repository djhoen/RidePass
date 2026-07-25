<!--
    Taking rented gear back: photograph the condition, decide how much of the authorized deposit to
    keep for damage, release the rest. The deposit hold is manual-capture, so "keep nothing" is the
    normal path and the renter is never charged for it.

    Extracted from the Rentals page so the Rental Board can close out a rental from a clicked bar
    without forking the damage-capture flow.
-->
<template>
    <v-dialog :model-value="modelValue" max-width="440"
        @update:model-value="v => !busy && emit('update:modelValue', v)">
        <v-card v-if="rental">
            <v-card-title class="d-flex align-center">
                <span>Return rental</span>
                <v-spacer></v-spacer>
                <v-btn icon="mdi-close" variant="text" size="small" :disabled="busy" @click="close"></v-btn>
            </v-card-title>
            <v-card-text>
                <!-- Covered vs simply-no-deposit are opposite conversations to have with a renter
                     standing at the counter, and both used to render as a bare $0.00. The waiver
                     fee is what distinguishes them; a zero deposit on its own does not. -->
                <v-alert v-if="covered" type="info" variant="tonal" density="compact" class="mb-3">
                    <strong>{{ rental.insuranceLabelSnapshot || 'Damage Protection' }}</strong> was
                    bought on this rental ({{ money(rental.insuranceCents ?? 0) }}), so no deposit was
                    held and there is nothing to charge damage against. Still photograph anything
                    broken, then complete the return.
                </v-alert>
                <p v-else-if="rental.depositCents === 0" class="text-body-2 text-medium-emphasis mb-3">
                    No deposit was held on this rental, so there's nothing to keep against damage.
                    Photograph anything broken and settle it separately.
                </p>
                <p v-else class="text-body-2 text-medium-emphasis mb-3">
                    Deposit authorized: <strong>{{ money(rental.depositCents) }}</strong>. Enter any damage
                    to keep; the rest is released to the renter's card automatically.
                </p>
                <!-- Photograph the damage BEFORE keeping any of the deposit: this is the
                     evidence if the renter disputes the charge. -->
                <ConditionPhotos :rental-id="rental.id" stage="return"
                    title="Return photos"
                    hint="Photograph any damage you're charging for." />
                <PhotoQrPanel kind="rental" :id="rental.id" class="mb-4" />
                <v-text-field v-if="rental.depositCents > 0"
                    v-model.number="damageDollars" type="number" min="0" step="0.01"
                    :max="rental.depositCents / 100" label="Damage to keep" prefix="$" density="compact"
                    persistent-hint :hint="`Up to the ${money(rental.depositCents)} authorized`"></v-text-field>
                <v-text-field v-model="conditionNotes" label="Condition notes" density="compact" class="mt-4" hide-details></v-text-field>
                <div v-if="error" class="text-error text-body-2 mt-2">{{ error }}</div>
            </v-card-text>
            <v-card-actions>
                <v-spacer></v-spacer>
                <v-btn :disabled="busy" @click="close">Cancel</v-btn>
                <v-btn color="primary" :loading="busy" @click="doReturn">Complete return</v-btn>
            </v-card-actions>
        </v-card>
    </v-dialog>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { BikeShopService } from '@/services/BikeShopService'
import ConditionPhotos from '@/components/bikeshop/ConditionPhotos.vue'
import PhotoQrPanel from '@/components/bikeshop/PhotoQrPanel.vue'

/** Only what the return needs; both hosts pass a richer rental object, which is fine. */
export interface ReturnableRental {
    id: string
    depositCents: number
    /** Greater than zero means the renter bought the damage waiver. */
    insuranceCents?: number
    insuranceLabelSnapshot?: string | null
}

const props = defineProps<{
    modelValue: boolean
    rental: ReturnableRental | null
}>()
const emit = defineEmits<{
    (e: 'update:modelValue', v: boolean): void
    (e: 'returned', capturedCents: number): void
}>()

const service = new BikeShopService()

/** The waiver fee is the evidence the renter is covered, not the zero deposit it produced. */
const covered = computed(() => (props.rental?.insuranceCents ?? 0) > 0)

const busy = ref(false)
const error = ref('')
const damageDollars = ref<number | null>(0)
const conditionNotes = ref('')

function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
function close() { emit('update:modelValue', false) }

watch(() => props.modelValue, open => {
    if (!open) return
    damageDollars.value = 0
    conditionNotes.value = ''
    error.value = ''
})

async function doReturn() {
    if (!props.rental) return
    error.value = ''
    busy.value = true
    try {
        // Clamp to the authorized deposit (the server clamps too, but don't let the field submit
        // a figure it visibly can't honor).
        const captured = Math.min(props.rental.depositCents, Math.max(0, Math.round((damageDollars.value ?? 0) * 100)))
        await service.returnRental(props.rental.id, {
            depositCapturedCents: captured,
            conditionNotes: conditionNotes.value.trim() || null,
        })
        close()
        emit('returned', captured)
    } catch (e: any) {
        error.value = e.response?.data?.error || 'Could not complete the return. Please try again.'
    } finally { busy.value = false }
}
</script>
