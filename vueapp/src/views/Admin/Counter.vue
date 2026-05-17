<template>
    <v-container style="max-width: 880px">
        <h1 class="text-h4 mb-4">Counter Sale</h1>
        <p class="text-body-2 text-medium-emphasis mb-4">
            For walk-ins without a device. Look up the customer, build their cart, capture waiver and payment.
        </p>

        <v-stepper v-model="step" :items="stepperItems" hide-actions>
        <v-stepper-window>
            <!-- Step 1: Customer -->
            <v-stepper-window-item :value="1">
                <v-card class="mb-4 pa-4">
                    <v-card-title>Customer</v-card-title>
                    <v-card-text>
                        <div v-if="!customer" class="d-flex ga-2 align-end">
                            <v-text-field v-model="customerEmail" type="email" label="Email"
                                density="compact" hide-details style="max-width: 360px"
                                @keyup.enter="findCustomer"></v-text-field>
                            <v-btn :loading="findingCustomer" @click="findCustomer">Find</v-btn>
                        </div>

                        <v-alert v-if="lookupError" type="info" variant="tonal" class="mt-3">
                            {{ lookupError }}
                            <div class="mt-2">
                                <v-btn size="small" color="primary" @click="showCreate = true">Create new customer</v-btn>
                            </div>
                        </v-alert>

                        <v-card v-if="showCreate && !customer" class="mt-3 pa-3" variant="outlined">
                            <div class="text-subtitle-2 mb-2">New customer</div>
                            <v-row>
                                <v-col cols="12" md="6">
                                    <v-text-field v-model="newCustomer.firstName" label="First name" density="compact"></v-text-field>
                                </v-col>
                                <v-col cols="12" md="6">
                                    <v-text-field v-model="newCustomer.lastName" label="Last name" density="compact"></v-text-field>
                                </v-col>
                            </v-row>
                            <v-text-field v-model="newCustomer.email" type="email" label="Email" density="compact"></v-text-field>
                            <v-text-field v-model="newCustomer.birthdate" type="date" :max="todayIso"
                                label="Birthdate" density="compact"></v-text-field>
                            <v-row>
                                <v-col cols="12" md="6">
                                    <v-text-field v-model="newCustomer.emergencyContactName" label="Emergency contact name" density="compact"></v-text-field>
                                </v-col>
                                <v-col cols="12" md="6">
                                    <PhoneField v-model="newCustomer.emergencyContactPhone" label="Emergency contact phone" density="compact" />
                                </v-col>
                            </v-row>
                            <v-btn color="primary" :loading="creatingCustomer" :disabled="!canCreateCustomer" @click="createCustomer">
                                Create customer
                            </v-btn>
                            <p class="text-caption text-medium-emphasis mt-2">
                                Account is created without a password. Customer can claim it later via password reset.
                            </p>
                        </v-card>

                        <div v-if="customer" class="mt-2">
                            <v-alert type="success" variant="tonal">
                                <strong>{{ customer.firstName }} {{ customer.lastName }}</strong> &lt;{{ customer.email }}&gt;
                                <span v-if="customer.hasSignedCurrentWaiver" class="text-caption ml-2">
                                    — waiver signed
                                </span>
                                <span v-else class="text-caption ml-2">— waiver not signed yet</span>
                                <div v-if="customer.emergencyContactName" class="text-caption mt-1">
                                    Emergency: <strong>{{ customer.emergencyContactName }}</strong> · {{ customer.emergencyContactPhone }}
                                </div>
                                <div v-else class="text-caption text-warning mt-1">
                                    No emergency contact on file.
                                </div>
                            </v-alert>
                            <div class="mt-3">
                                <v-btn variant="text" size="small" @click="resetCustomer">Pick a different customer</v-btn>
                                <v-btn color="primary" class="ml-2" @click="step = 2">Continue to cart</v-btn>
                            </div>
                        </div>
                    </v-card-text>
                </v-card>
            </v-stepper-window-item>

            <!-- Step 2: Cart -->
            <v-stepper-window-item :value="2">
                <v-card class="mb-4 pa-4">
                    <v-card-title>Cart</v-card-title>
                    <v-card-text>
                        <v-expansion-panels v-model="catalogPanel" class="mb-3 catalog-panels">
                            <!-- Passes -->
                            <v-expansion-panel value="passes">
                                <v-expansion-panel-title>
                                    <div class="d-flex align-left ga-2">
                                        <v-icon>mdi-ticket-confirmation-outline</v-icon>
                                        <span>Passes</span>
                                        <v-chip v-if="cartCount('pass') > 0" size="x-small" color="primary" class="ml-1">
                                            {{ cartCount('pass') }}
                                        </v-chip>
                                    </div>
                                </v-expansion-panel-title>
                                <v-expansion-panel-text>
                                    <div v-if="loadingProducts" class="text-center py-4">
                                        <v-progress-circular indeterminate></v-progress-circular>
                                    </div>
                                    <div v-else-if="products.length === 0" class="text-medium-emphasis">
                                        No pass products. Add some on the Passes admin page.
                                    </div>
                                    <div v-else>
                                        <div v-for="p in products" :key="p.id"
                                            class="d-flex align-left py-3 ga-3 catalog-row">
                                            <div class="flex-grow-1" style="min-width: 0">
                                                <div class="text-body-1"><strong>{{ p.name }}</strong></div>
                                                <div v-if="p.description" class="text-caption text-medium-emphasis">{{ p.description }}</div>
                                                <div class="text-caption text-medium-emphasis">${{ (p.priceCents / 100).toFixed(2) }}</div>
                                            </div>
                                            <div class="d-flex align-center ga-1" style="flex: 0 0 auto">
                                                <v-btn size="small" icon variant="outlined" :disabled="qtyOf('pass', p.id) === 0"
                                                    @click="addToCart('pass', p.id, p.name, p.priceCents, p.requiresWaiver, p.riderPaidServiceChargeBps, -1)">
                                                    <v-icon>mdi-minus</v-icon>
                                                </v-btn>
                                                <div style="min-width: 32px; text-align: center"><strong>{{ qtyOf('pass', p.id) }}</strong></div>
                                                <v-btn size="small" icon variant="outlined"
                                                    @click="addToCart('pass', p.id, p.name, p.priceCents, p.requiresWaiver, p.riderPaidServiceChargeBps, 1)">
                                                    <v-icon>mdi-plus</v-icon>
                                                </v-btn>
                                            </div>
                                        </div>
                                    </div>
                                </v-expansion-panel-text>
                            </v-expansion-panel>

                            <!-- Add-ons (tenant-wide merch — no event attachment) -->
                            <v-expansion-panel v-if="branding.extrasEnabled" value="extras">
                                <v-expansion-panel-title>
                                    <div class="d-flex align-center ga-2">
                                        <v-icon>mdi-package-variant</v-icon>
                                        <span>Add-ons</span>
                                        <v-chip v-if="cartCount('extras') > 0" size="x-small" color="primary" class="ml-1">
                                            {{ cartCount('extras') }}
                                        </v-chip>
                                    </div>
                                </v-expansion-panel-title>
                                <v-expansion-panel-text>
                                    <div v-if="loadingExtras" class="text-center py-4">
                                        <v-progress-circular indeterminate></v-progress-circular>
                                    </div>
                                    <div v-else-if="extrasAsEligible.length === 0" class="text-medium-emphasis">
                                        No active add-ons. Create some on the Add-ons admin page.
                                    </div>
                                    <ExtrasPicker v-else
                                        :extras="extrasAsEligible"
                                        :model-value="extrasSelection"
                                        @update:model-value="onExtrasSelectionChanged" />
                                </v-expansion-panel-text>
                            </v-expansion-panel>

                            <!-- Membership -->
                            <v-expansion-panel v-if="membershipOffered" value="membership">
                                <v-expansion-panel-title>
                                    <div class="d-flex align-left ga-2">
                                        <v-icon>mdi-card-account-details</v-icon>
                                        <span>Membership</span>
                                        <v-chip v-if="cartCount('membership') > 0" size="x-small" color="primary" class="ml-1">
                                            {{ cartCount('membership') }}
                                        </v-chip>
                                    </div>
                                </v-expansion-panel-title>
                                <v-expansion-panel-text>
                                    <v-card variant="outlined" class="pa-4 d-flex align-left ga-3">
                                        <v-icon size="40" color="primary">mdi-card-account-details</v-icon>
                                        <div class="flex-grow-1">
                                            <div class="text-body-1"><strong>{{ branding.membershipName }}</strong></div>
                                            <div class="text-caption text-medium-emphasis">
                                                ${{ (branding.membershipPriceCents / 100).toFixed(2) }} ·
                                                {{ branding.membershipDurationKind === 'yearly' ? 'Annual' : 'One-time' }}
                                            </div>
                                        </div>
                                        <div class="d-flex align-center ga-1">
                                            <v-btn size="small" icon variant="outlined"
                                                :disabled="qtyOf('membership', MEMBERSHIP_ITEM_ID) === 0"
                                                @click="addMembershipToCart(-1)">
                                                <v-icon>mdi-minus</v-icon>
                                            </v-btn>
                                            <div style="min-width: 32px; text-align: center">
                                                <strong>{{ qtyOf('membership', MEMBERSHIP_ITEM_ID) }}</strong>
                                            </div>
                                            <v-btn size="small" icon variant="outlined"
                                                :disabled="qtyOf('membership', MEMBERSHIP_ITEM_ID) >= 1"
                                                @click="addMembershipToCart(1)">
                                                <v-icon>mdi-plus</v-icon>
                                            </v-btn>
                                        </div>
                                    </v-card>
                                    <p class="text-caption text-medium-emphasis mt-2">
                                        Memberships are sold one per customer. Adding a second is blocked.
                                    </p>
                                </v-expansion-panel-text>
                            </v-expansion-panel>
                        </v-expansion-panels>

                        <v-divider class="my-4"></v-divider>

                        <v-select v-if="availableVouchers.length > 0"
                            v-model="selectedVoucherId"
                            :items="voucherOptions"
                            item-title="title" item-value="value"
                            label="Apply customer voucher (optional)" density="compact"
                            clearable hide-details class="mb-3"
                            hint="Voucher applies to one ticket, or to a single-quantity pass line."
                            persistent-hint></v-select>

                        <div class="text-subtitle-2 mb-2">Cart</div>
                        <div v-if="cart.length === 0" class="text-medium-emphasis pa-3 text-center"
                             style="border: 1px dashed rgba(0,0,0,0.12); border-radius: 6px">
                            Cart is empty.
                        </div>
                        <div v-else class="cart-summary">
                            <div v-for="(c, i) in cart" :key="i" class="cart-line d-flex align-center py-2 ga-2">
                                <div class="flex-grow-1" style="min-width: 0">
                                    <div class="text-body-2">
                                        <strong>{{ c.displayName }}</strong>
                                        <span class="text-medium-emphasis"> × {{ c.quantity }}</span>
                                    </div>
                                </div>
                                <div class="text-right" style="flex: 0 0 auto">
                                    ${{ ((c.unitPriceCents * c.quantity) / 100).toFixed(2) }}
                                </div>
                                <v-btn size="x-small" icon="mdi-close" variant="text"
                                    @click="removeLine(i)" aria-label="Remove line"></v-btn>
                            </div>
                            <v-divider class="my-2"></v-divider>
                            <div v-if="cartServiceChargeCents > 0" class="d-flex justify-space-between text-caption text-medium-emphasis">
                                <div>Subtotal</div>
                                <div>${{ (cartSubtotalCents / 100).toFixed(2) }}</div>
                            </div>
                            <div v-if="cartServiceChargeCents > 0" class="d-flex justify-space-between text-caption text-medium-emphasis">
                                <div>Service charge</div>
                                <div>${{ (cartServiceChargeCents / 100).toFixed(2) }}</div>
                            </div>
                            <div class="d-flex justify-space-between text-body-1 mt-1">
                                <strong>Total</strong>
                                <strong>${{ totalDollars }}</strong>
                            </div>
                        </div>

                        <div class="d-flex mt-4 ga-2">
                            <v-btn variant="text" @click="step = 1">Back</v-btn>
                            <v-spacer></v-spacer>
                            <v-btn color="primary" :disabled="cart.length === 0" @click="advanceFromCart">Continue</v-btn>
                        </div>
                    </v-card-text>
                </v-card>
            </v-stepper-window-item>

            <!-- Step 3: Waiver -->
            <v-stepper-window-item :value="3">
                <v-card class="mb-4 pa-4">
                    <v-card-title>Waiver</v-card-title>
                    <v-card-text>
                        <div v-if="!activeWaiver" class="text-medium-emphasis">
                            No active waiver configured for this tenant — proceed to payment.
                        </div>
                        <div v-else-if="!cartRequiresWaiver" class="text-medium-emphasis">
                            None of the items in this cart require a waiver — proceed to payment.
                        </div>
                        <template v-else-if="customer?.hasSignedCurrentWaiver">
                            <v-alert type="success" variant="tonal" class="mb-3">
                                <div class="d-flex align-center">
                                    <v-icon class="mr-2">mdi-file-sign</v-icon>
                                    <div>
                                        <div><strong>{{ customer.firstName }} {{ customer.lastName }}</strong> already signed this waiver.</div>
                                        <div v-if="customer.waiverSignedAtUtc" class="text-caption">
                                            Signed {{ formatSignedAt(customer.waiverSignedAtUtc) }}
                                        </div>
                                    </div>
                                </div>
                            </v-alert>
                            <div v-if="customer.waiverSignatureDataUrl" class="mb-2">
                                <div class="text-caption text-medium-emphasis mb-1">Their signature on file:</div>
                                <img :src="customer.waiverSignatureDataUrl" alt="Customer signature"
                                    style="max-width: 100%; border: 1px solid rgba(0,0,0,0.12); border-radius: 6px; background: #fff" />
                            </div>
                        </template>
                        <template v-else>
                            <p class="text-body-2 text-medium-emphasis mb-2">
                                Hand the device to the customer. The customer must read &amp; agree to:
                            </p>
                            <v-card variant="outlined" class="pa-3 waiver-body mb-3">
                                <div v-if="hasBody(activeWaiver.body)"><RichTextView :html="activeWaiver.body" /></div>
                                <div v-else class="text-medium-emphasis">
                                    (Tenant has not filled in waiver text yet.)
                                </div>
                            </v-card>
                            <v-alert v-if="customer?.isMinor" type="info" variant="tonal" density="compact" class="mt-2 mb-2">
                                Customer is under 18 — a parent or guardian must sign and provide their info.
                            </v-alert>
                            <v-checkbox v-model="customerAcknowledged" hide-details
                                :label="customer?.isMinor
                                    ? 'The parent / guardian has read and agrees to this waiver on the customer\'s behalf'
                                    : 'The customer has read and agrees to this waiver'"></v-checkbox>
                            <div v-if="customerAcknowledged" class="mt-3">
                                <v-row v-if="customer?.isMinor" class="mb-1">
                                    <v-col cols="12" md="6">
                                        <v-text-field v-model="parentName" label="Parent / guardian name" density="compact"></v-text-field>
                                    </v-col>
                                    <v-col cols="12" md="6">
                                        <PhoneField v-model="parentPhone" label="Parent / guardian phone" density="compact" />
                                    </v-col>
                                </v-row>
                                <div class="text-subtitle-2 mb-1">{{ customer?.isMinor ? 'Parent signs below' : 'Sign below' }}</div>
                                <SignaturePad v-model="customerSignatureDataUrl" />
                            </div>
                        </template>
                        <div class="d-flex mt-4 ga-2">
                            <v-btn variant="text" @click="step = 2">Back</v-btn>
                            <v-spacer></v-spacer>
                            <v-btn color="primary" :disabled="!canAdvanceFromWaiver" @click="advanceFromWaiver">Continue</v-btn>
                        </div>
                    </v-card-text>
                </v-card>
            </v-stepper-window-item>

            <!-- Step 4: Payment -->
            <v-stepper-window-item :value="4">
                <v-card class="mb-4 pa-4">
                    <v-card-title>Payment</v-card-title>
                    <v-card-text>
                        <div class="mb-3">
                            Total to collect: <strong>${{ totalDollars }}</strong>
                        </div>

                        <div v-if="!clientSecret && !cashSubmitted">
                            <v-tabs v-model="paymentMethod" density="compact" class="mb-3">
                                <v-tab value="card" prepend-icon="mdi-credit-card">Card</v-tab>
                                <v-tab value="cash" prepend-icon="mdi-cash">Cash</v-tab>
                            </v-tabs>

                            <div v-if="paymentMethod === 'card'">
                                <p v-if="!branding.stripePublishableKey" class="text-error">
                                    Stripe publishable key is not configured for this tenant.
                                </p>
                                <p v-else class="text-caption text-medium-emphasis mb-3">
                                    Hand the customer the device to enter card details.
                                </p>
                            </div>
                            <div v-else>
                                <p class="text-caption text-medium-emphasis mb-3">
                                    Collect cash from the customer, then click below to record the sale.
                                    The platform service charge will be deducted from your next payout.
                                </p>
                            </div>

                            <div class="d-flex mt-2 ga-2">
                                <v-btn variant="text" @click="step = 3">Back</v-btn>
                                <v-spacer></v-spacer>
                                <v-btn v-if="paymentMethod === 'card'" color="primary"
                                    :loading="creatingSale" :disabled="!branding.stripePublishableKey" @click="submitCard">
                                    Prepare card payment
                                </v-btn>
                                <v-btn v-else color="primary" :loading="creatingSale" @click="submitCash">
                                    Confirm ${{ totalDollars }} cash received
                                </v-btn>
                            </div>
                        </div>

                        <div v-else-if="clientSecret">
                            <div id="payment-element" class="mb-4"></div>
                            <v-btn color="primary" :loading="paying" :disabled="!stripeReady" @click="pay">
                                Charge ${{ totalDollars }}
                            </v-btn>
                            <div v-if="paymentError" class="text-error mt-3">{{ paymentError }}</div>
                        </div>
                    </v-card-text>
                </v-card>
            </v-stepper-window-item>

            <!-- Step 5: Receipt -->
            <v-stepper-window-item :value="5">
                <v-card class="mb-4 pa-4">
                    <v-card-title>Sale complete</v-card-title>
                    <v-card-text>
                        <v-alert type="success" variant="tonal" class="mb-3">
                            <span v-if="cashSubmitted">Cash sale recorded — ${{ totalDollars }} collected.</span>
                            <span v-else-if="totalAmountCents === 0">Voucher applied — no charge.</span>
                            <span v-else>Charged ${{ totalDollars }}.</span>
                            Each line item below has its own QR for redemption.
                        </v-alert>
                        <div v-for="li in lineItems" :key="li.purchaseId" class="mb-4 d-flex align-center ga-3">
                            <QrCode v-if="hasQr(li)" :value="redeemUrl(li.redemptionToken)" :size="120" />
                            <div v-else class="d-flex align-center justify-center"
                                style="width: 120px; height: 120px; background: rgba(0,0,0,0.05); border-radius: 6px">
                                <v-icon size="40" color="primary">mdi-card-account-details</v-icon>
                            </div>
                            <div>
                                <div><strong>{{ li.displayName }}</strong> ×{{ li.quantity }}</div>
                                <div class="text-caption text-medium-emphasis">
                                    ${{ (li.lineAmountCents / 100).toFixed(2) }} · {{ kindLabel(li.kind) }}
                                </div>
                                <div v-if="hasQr(li)" class="text-caption">
                                    <code>{{ li.redemptionToken }}</code>
                                </div>
                                <div v-else class="text-caption text-medium-emphasis">
                                    No QR — membership is now active on the customer's account.
                                </div>
                            </div>
                        </div>
                        <v-btn color="primary" class="mt-3" @click="reset">New sale</v-btn>
                    </v-card-text>
                </v-card>
            </v-stepper-window-item>
        </v-stepper-window>
        </v-stepper>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import dayjs from 'dayjs'
import { CounterService, type CounterRider } from '@/services/CounterService'
import { PassService, type PassProduct, type WaiverDto } from '@/services/PassService'
import type { EligibleExtra, EligibleExtraVariant } from '@/services/EventService'
import { ExtraService, type ExtraProduct } from '@/services/ExtraService'
import { RewardService, type RiderRewardRedemption } from '@/services/RewardService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'
import RichTextView from '@/components/RichTextView.vue'
import QrCode from '@/components/QrCode.vue'
import SignaturePad from '@/components/SignaturePad.vue'
import ExtrasPicker, { type ExtraSelection } from '@/components/ExtrasPicker.vue'
import PhoneField from '@/components/PhoneField.vue'

type Customer = CounterRider
type CartKind = 'pass' | 'extras' | 'membership'

interface CartLine {
    kind: CartKind
    itemId: string
    displayName: string
    unitPriceCents: number
    quantity: number
    requiresWaiver: boolean
    riderPaidServiceChargeBps: number
    variantId?: string | null
}

const MEMBERSHIP_ITEM_ID = '00000000-0000-0000-0000-000000000001'

const counter = new CounterService()
const passService = new PassService()
const extraService = new ExtraService()
const rewardService = new RewardService()

const stepLabels = ['Customer', 'Cart', 'Waiver', 'Payment', 'Receipt']
const step = ref(1)

// Make earlier step headers clickable so the cashier can jump back. Once payment
// has actually been initiated (clientSecret) or the sale is complete (step 5),
// lock editability so the cart can't be silently changed under a live PaymentIntent.
const stepperItems = computed(() => stepLabels.map((title, i) => {
    const stepNumber = i + 1
    const locked = step.value === 5 || !!clientSecret.value || cashSubmitted.value
    return {
        title,
        value: stepNumber,
        // Allow clicking to jump back to any step at or before the current one,
        // but don't let the user click forward past where they actually are.
        editable: !locked && stepNumber <= step.value,
    }
}))

// Customer step
const customerEmail = ref('')
const findingCustomer = ref(false)
const lookupError = ref<string | null>(null)
const showCreate = ref(false)
const creatingCustomer = ref(false)
const newCustomer = ref({ firstName: '', lastName: '', email: '', birthdate: '', emergencyContactName: '', emergencyContactPhone: '' })
const customer = ref<Customer | null>(null)
const todayIso = new Date().toISOString().slice(0, 10)
const canCreateCustomer = computed(() =>
    !!newCustomer.value.firstName && !!newCustomer.value.lastName
    && /\S+@\S+/.test(newCustomer.value.email)
    && !!newCustomer.value.birthdate && newCustomer.value.birthdate < todayIso
    && !!newCustomer.value.emergencyContactName.trim()
    && newCustomer.value.emergencyContactPhone.replace(/\D/g, '').length >= 7)

// Cart step — accordion: only one section open at a time, default to passes.
const catalogPanel = ref<string | undefined>('passes')
const products = ref<PassProduct[]>([])
const loadingProducts = ref(false)
const extras = ref<ExtraProduct[]>([])
const loadingExtras = ref(false)
const cart = ref<CartLine[]>([])

const availableVouchers = ref<RiderRewardRedemption[]>([])
const selectedVoucherId = ref<string | null>(null)
const voucherOptions = computed(() => availableVouchers.value.map(v => ({
    value: v.id,
    title: `${v.programName} — ${v.rewardPercentOff === 100 ? 'Free' : v.rewardPercentOff + '% off'}`,
})))

// Map ExtraProduct → EligibleExtra so the existing ExtrasPicker UI works unchanged.
// "Tenant-wide" means no event eligibility row; per-event inventory doesn't apply.
// Variant remaining is still tenant-wide on the variant itself.
const extrasAsEligible = computed<EligibleExtra[]>(() =>
    extras.value
        .filter(p => p.isActive)
        .map(p => ({
            productId: p.id,
            name: p.name,
            kind: p.kind,
            priceCents: p.priceCents,
            imageUrl: p.imageUrl,
            inventory: null,
            sold: 0,
            remaining: -1,
            requiresWaiver: p.requiresWaiver,
            variants: (p.variants ?? [])
                .filter(v => v.isActive)
                .map<EligibleExtraVariant>(v => ({
                    id: v.id,
                    size: v.size,
                    color: v.color,
                    gender: v.gender,
                    priceCents: v.priceCents ?? p.priceCents,
                    imageUrl: v.imageUrl ?? p.imageUrl,
                    inventory: v.inventory,
                    sold: v.sold,
                    remaining: v.remaining,
                })),
        })))

const extrasSelection = computed<ExtraSelection[]>(() =>
    cart.value
        .filter(c => c.kind === 'extras')
        .map(c => ({ productId: c.itemId, variantId: c.variantId ?? null, quantity: c.quantity })))

const membershipOffered = computed(() =>
    branding.membershipEnabled && branding.membershipPriceCents > 0)

// Waiver step
const activeWaiver = ref<WaiverDto | null>(null)
const customerAcknowledged = ref(false)
const customerSignatureDataUrl = ref<string | null>(null)
const parentName = ref('')
const parentPhone = ref('')
const cartRequiresWaiver = computed(() => cart.value.some(c => c.requiresWaiver))
const willSignWaiver = computed(() =>
    activeWaiver.value !== null
    && customer.value?.hasSignedCurrentWaiver === false
    && cartRequiresWaiver.value)
const canAdvanceFromWaiver = computed(() => {
    if (!willSignWaiver.value) return true
    if (!customerAcknowledged.value || !customerSignatureDataUrl.value) return false
    if (customer.value?.isMinor) {
        if (!parentName.value.trim()) return false
        if (parentPhone.value.replace(/\D/g, '').length < 7) return false
    }
    return true
})

// Payment step
const paymentMethod = ref<'card' | 'cash'>('card')
const cashSubmitted = ref(false)
const creatingSale = ref(false)
const clientSecret = ref<string | null>(null)
const totalAmountCents = ref(0)
const lineItems = ref<any[]>([])
const stripeReady = ref(false)
const paying = ref(false)
const paymentError = ref<string | null>(null)
let stripe: any = null
let elements: any = null

const cartSubtotalCents = computed(() => cart.value.reduce((sum, c) => sum + c.unitPriceCents * c.quantity, 0))
const cartServiceChargeCents = computed(() => {
    const bps = branding.serviceChargeBps ?? 0
    return cart.value.reduce((sum, c) => {
        const perUnitCharge = Math.floor((c.unitPriceCents * bps) / 10000)
        const customerPerUnit = Math.floor((perUnitCharge * c.riderPaidServiceChargeBps) / 10000)
        return sum + customerPerUnit * c.quantity
    }, 0)
})
const cartTotalCents = computed(() => cartSubtotalCents.value + cartServiceChargeCents.value)
const totalDollars = computed(() => {
    const cents = clientSecret.value ? totalAmountCents.value : cartTotalCents.value
    return (cents / 100).toFixed(2)
})

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(async () => {
    loadingProducts.value = true
    loadingExtras.value = true
    try {
        const [p, w, x] = await Promise.all([
            passService.listActive(),
            passService.getWaiver().catch(() => ({ data: { data: null } })),
            branding.extrasEnabled
                ? extraService.listForAdmin().catch(() => ({ data: { data: [] as ExtraProduct[] } }))
                : Promise.resolve({ data: { data: [] as ExtraProduct[] } }),
        ])
        products.value = (p.data as any).data
        activeWaiver.value = (w.data as any).data ?? null
        extras.value = ((x.data as any).data as ExtraProduct[])
            .filter(e => e.isActive)
            .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name))
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load catalog.', 'error')
    } finally {
        loadingProducts.value = false
        loadingExtras.value = false
    }
})

async function findCustomer() {
    if (!customerEmail.value.trim()) return
    findingCustomer.value = true
    lookupError.value = null
    try {
        const r = await counter.findRider(customerEmail.value.trim())
        customer.value = (r.data as any).data
        showCreate.value = false
        try {
            const v = await rewardService.listRiderRedemptions(customer.value!.id)
            availableVouchers.value = ((v.data as any).data as RiderRewardRedemption[]).filter(x => !x.redeemedAtUtc)
        } catch { availableVouchers.value = [] }
    } catch (err: any) {
        if (err.response?.status === 404) {
            lookupError.value = `No customer found for "${customerEmail.value.trim()}".`
            newCustomer.value = { firstName: '', lastName: '', email: customerEmail.value.trim(), birthdate: '', emergencyContactName: '', emergencyContactPhone: '' }
        } else {
            flash(err.response?.data?.error || 'Lookup failed.', 'error')
        }
    } finally {
        findingCustomer.value = false
    }
}

async function createCustomer() {
    creatingCustomer.value = true
    try {
        const r = await counter.createRider({
            email: newCustomer.value.email.trim(),
            firstName: newCustomer.value.firstName.trim(),
            lastName: newCustomer.value.lastName.trim(),
            birthdate: newCustomer.value.birthdate,
            emergencyContactName: newCustomer.value.emergencyContactName.trim(),
            emergencyContactPhone: newCustomer.value.emergencyContactPhone.trim(),
        })
        customer.value = { ...(r.data as any).data, hasSignedCurrentWaiver: false }
        showCreate.value = false
        lookupError.value = null
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not create customer.', 'error')
    } finally {
        creatingCustomer.value = false
    }
}

function resetCustomer() {
    customer.value = null
    customerEmail.value = ''
    lookupError.value = null
    showCreate.value = false
    cart.value = []
}

function qtyOf(kind: CartKind, itemId: string): number {
    return cart.value
        .filter(c => c.kind === kind && c.itemId === itemId)
        .reduce((sum, c) => sum + c.quantity, 0)
}

function cartCount(kind: CartKind): number {
    return cart.value
        .filter(c => c.kind === kind)
        .reduce((sum, c) => sum + c.quantity, 0)
}

function addToCart(kind: 'pass', itemId: string, displayName: string, unitPriceCents: number, requiresWaiver: boolean, riderPaidServiceChargeBps: number, delta: number) {
    const existing = cart.value.find(c => c.kind === kind && c.itemId === itemId)
    if (existing) {
        existing.quantity += delta
        if (existing.quantity <= 0) {
            cart.value = cart.value.filter(c => c !== existing)
        }
    } else if (delta > 0) {
        cart.value.push({ kind, itemId, displayName, unitPriceCents, quantity: delta, requiresWaiver, riderPaidServiceChargeBps })
    }
}

function addMembershipToCart(delta: number) {
    if (!membershipOffered.value) return
    const existing = cart.value.find(c => c.kind === 'membership')
    if (existing) {
        existing.quantity += delta
        if (existing.quantity <= 0) {
            cart.value = cart.value.filter(c => c !== existing)
        }
    } else if (delta > 0) {
        cart.value.push({
            kind: 'membership',
            itemId: MEMBERSHIP_ITEM_ID,
            displayName: branding.membershipName,
            unitPriceCents: branding.membershipPriceCents,
            quantity: 1,
            requiresWaiver: false,
            riderPaidServiceChargeBps: 0,
        })
    }
}

function onExtrasSelectionChanged(next: ExtraSelection[]) {
    const others = cart.value.filter(c => c.kind !== 'extras')
    const newLines: CartLine[] = []
    for (const sel of next) {
        if (sel.quantity <= 0) continue
        const product = extras.value.find(p => p.id === sel.productId)
        if (!product) continue
        const variant = sel.variantId
            ? product.variants.find(v => v.id === sel.variantId) ?? null
            : null
        const unitPriceCents = variant?.priceCents ?? product.priceCents
        const variantAttrs = variant
            ? [variant.size, variant.color, variant.gender].filter(s => !!s)
            : []
        const displayName = variantAttrs.length > 0
            ? `${product.name} (${variantAttrs.join(' / ')})`
            : product.name
        newLines.push({
            kind: 'extras',
            itemId: product.id,
            displayName,
            unitPriceCents,
            quantity: sel.quantity,
            requiresWaiver: product.requiresWaiver,
            riderPaidServiceChargeBps: product.riderPaidServiceChargeBps,
            variantId: sel.variantId,
        })
    }
    cart.value = [...others, ...newLines]
}

function removeLine(idx: number) {
    cart.value = cart.value.filter((_, i) => i !== idx)
}

function advanceFromCart() {
    step.value = 3
}

function advanceFromWaiver() {
    step.value = 4
}

async function submitCard() {
    creatingSale.value = true
    try { await createSale('stripe') } finally { creatingSale.value = false }
}

async function submitCash() {
    creatingSale.value = true
    try { await createSale('cash') } finally { creatingSale.value = false }
}

async function createSale(method: 'stripe' | 'cash') {
    if (!customer.value) return
    paymentError.value = null
    try {
        const signingForMinor = willSignWaiver.value && customer.value?.isMinor === true
        const r = await counter.createSale({
            riderId: customer.value.id,
            items: cart.value.map(c => ({
                kind: c.kind,
                itemId: c.itemId,
                quantity: c.quantity,
                eventId: null,
                variantId: c.variantId ?? null,
            })),
            signWaiver: willSignWaiver.value && customerAcknowledged.value,
            signatureDataUrl: willSignWaiver.value ? customerSignatureDataUrl.value : null,
            parentName: signingForMinor ? parentName.value.trim() : null,
            parentPhone: signingForMinor ? parentPhone.value.trim() : null,
            rewardRedemptionId: selectedVoucherId.value,
            paymentMethod: method,
        })
        const data = (r.data as any).data
        clientSecret.value = data.clientSecret
        totalAmountCents.value = data.totalAmountCents
        lineItems.value = data.lineItems

        if (!clientSecret.value) {
            cashSubmitted.value = method === 'cash'
            step.value = 5
            flash(method === 'cash' ? 'Cash sale recorded.' : 'Voucher applied — sale complete!', 'success')
            return
        }

        await nextTick()
        await mountPaymentElement()
    } catch (err: any) {
        paymentError.value = err.response?.data?.error || 'Could not start payment.'
        flash(paymentError.value!, 'error')
    }
}

async function mountPaymentElement() {
    if (!clientSecret.value) return
    stripe = await getStripe(branding.stripePublishableKey)
    if (!stripe) { paymentError.value = 'Stripe not available.'; return }
    elements = stripe.elements({ clientSecret: clientSecret.value })
    const pe = elements.create('payment')
    pe.mount('#payment-element')
    stripeReady.value = true
}

async function pay() {
    if (!stripe || !elements) return
    paying.value = true
    paymentError.value = null
    try {
        const { error } = await stripe.confirmPayment({
            elements,
            confirmParams: { return_url: window.location.href },
            redirect: 'if_required',
        })
        if (error) {
            paymentError.value = error.message || 'Payment failed.'
        } else {
            step.value = 5
        }
    } catch (err: any) {
        paymentError.value = err?.message || 'Payment failed.'
    } finally {
        paying.value = false
    }
}

function reset() {
    step.value = 1
    customer.value = null
    customerEmail.value = ''
    cart.value = []
    clientSecret.value = null
    totalAmountCents.value = 0
    lineItems.value = []
    stripeReady.value = false
    customerAcknowledged.value = false
    customerSignatureDataUrl.value = null
    parentName.value = ''
    parentPhone.value = ''
    paymentError.value = null
    paymentMethod.value = 'card'
    cashSubmitted.value = false
    selectedVoucherId.value = null
    availableVouchers.value = []
    catalogPanel.value = 'passes'
}

function redeemUrl(token: string): string {
    return `${window.location.protocol}//${window.location.host}/redeem/${token}`
}

function kindLabel(kind: string): string {
    switch (kind) {
        case 'pass': return 'Pass'
        case 'extras': return 'Add-on'
        case 'membership': return 'Membership'
        default: return kind
    }
}

const ZERO_GUID = '00000000-0000-0000-0000-000000000000'
function hasQr(li: { kind: string; redemptionToken: string }): boolean {
    if (li.kind === 'membership') return false
    return !!li.redemptionToken && li.redemptionToken !== ZERO_GUID
}

function formatSignedAt(utc: string): string {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('MMM D, YYYY [at] h:mm A')
}

function hasBody(body: string | null | undefined): boolean {
    if (!body) return false
    return body.replace(/<[^>]+>/g, '').trim().length > 0
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>

<style scoped>
.waiver-body {
    max-height: 280px;
    overflow-y: auto;
    background: rgba(0, 0, 0, 0.03);
}
.catalog-row + .catalog-row {
    border-top: 1px solid rgba(0, 0, 0, 0.06);
}
.catalog-panels :deep(.v-expansion-panel-text__wrapper) {
    padding: 0 16px 12px;
}
.cart-summary {
    background: rgba(0, 0, 0, 0.02);
    border: 1px solid rgba(0, 0, 0, 0.06);
    border-radius: 6px;
    padding: 8px 12px;
}
.cart-line + .cart-line {
    border-top: 1px solid rgba(0, 0, 0, 0.05);
}
</style>
