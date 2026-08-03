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
                            <v-text-field v-model="customerEmail" label="Name, email, or phone"
                                density="compact" hide-details style="max-width: 360px"
                                hint="Type at least 2 characters" persistent-hint
                                @keyup.enter="findCustomer"></v-text-field>
                            <v-btn :loading="findingCustomer" @click="findCustomer">Find</v-btn>
                            <!-- Walk-ins are this screen's whole purpose, so creating one must not be
                                 hidden behind a failed search. -->
                            <v-btn variant="text" @click="startCreate">New customer</v-btn>
                        </div>

                        <!-- More than one match: let the operator pick rather than guess. Phone and
                             email are both shown because two riders sharing a name is exactly the
                             case this list exists to resolve. -->
                        <v-list v-if="!customer && candidates.length > 1" density="compact" class="mt-2"
                            style="max-width: 560px; border: 1px solid rgba(128,128,128,0.3); border-radius: 6px">
                            <v-list-item v-for="c in candidates" :key="c.id" @click="pickCandidate(c)">
                                <v-list-item-title>{{ c.firstName }} {{ c.lastName }}</v-list-item-title>
                                <v-list-item-subtitle>
                                    {{ c.email }}<span v-if="c.phone"> · {{ c.phone }}</span>
                                </v-list-item-subtitle>
                            </v-list-item>
                        </v-list>

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
                            <v-text-field v-model="newCustomer.email" type="email" label="Email" density="compact" class="mt-4"></v-text-field>
                            <v-text-field v-model="newCustomer.birthdate" type="date" :max="todayIso"
                                label="Birthdate" density="compact" class="mt-4"></v-text-field>
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

                            <!-- Season passes -->
                            <v-expansion-panel v-if="branding.seasonPassesEnabled && seasonPasses.length > 0"
                                value="season_pass">
                                <v-expansion-panel-title>
                                    <div class="d-flex align-center ga-2">
                                        <v-icon>mdi-ticket-account</v-icon>
                                        <span>Season passes</span>
                                        <v-chip v-if="cartCount('season_pass') > 0" size="x-small" color="primary" class="ml-1">
                                            {{ cartCount('season_pass') }}
                                        </v-chip>
                                    </div>
                                </v-expansion-panel-title>
                                <v-expansion-panel-text>
                                    <v-card v-for="p in seasonPasses" :key="p.id"
                                        variant="outlined" class="pa-4 d-flex align-center ga-3 mb-2">
                                        <v-icon size="40" color="primary">mdi-ticket-account</v-icon>
                                        <div class="flex-grow-1" style="min-width: 0">
                                            <div class="text-body-1"><strong>{{ p.name }}</strong></div>
                                            <div class="text-caption text-medium-emphasis">
                                                ${{ (p.priceCents / 100).toFixed(2) }} ·
                                                {{ p.kind === 'credits'
                                                    ? `${p.totalCredits} visit${p.totalCredits === 1 ? '' : 's'}`
                                                    : p.kind === 'days_of_week' ? 'Selected days' : 'Unlimited' }}
                                            </div>
                                        </div>
                                        <div class="d-flex align-center ga-1">
                                            <v-btn size="small" icon variant="outlined"
                                                :disabled="qtyOf('season_pass', p.id) === 0"
                                                @click="addSeasonPassToCart(p, -1)">
                                                <v-icon>mdi-minus</v-icon>
                                            </v-btn>
                                            <div style="min-width: 32px; text-align: center">
                                                <strong>{{ qtyOf('season_pass', p.id) }}</strong>
                                            </div>
                                            <v-btn size="small" icon variant="outlined"
                                                @click="addSeasonPassToCart(p, 1)">
                                                <v-icon>mdi-plus</v-icon>
                                            </v-btn>
                                        </div>
                                    </v-card>
                                    <v-alert v-if="cartCount('season_pass') > 0" type="info" variant="tonal"
                                        density="compact" class="mt-2">
                                        <strong>This pass won’t scan yet.</strong> The rider has to add a photo
                                        (and sign, if the pass needs a waiver) under My Passes in their account
                                        before the gate will admit them. Tell them before they walk away.
                                    </v-alert>
                                </v-expansion-panel-text>
                            </v-expansion-panel>

                            <!-- Lesson + optional bike -->
                            <v-expansion-panel value="lesson">
                                <v-expansion-panel-title>
                                    <div class="d-flex align-center ga-2">
                                        <v-icon>mdi-whistle</v-icon>
                                        <span>Lesson</span>
                                        <v-chip v-if="cartCount('event_ticket') + cartCount('rental') > 0"
                                            size="x-small" color="primary" class="ml-1">
                                            {{ cartCount('event_ticket') + cartCount('rental') }}
                                        </v-chip>
                                    </div>
                                </v-expansion-panel-title>
                                <v-expansion-panel-text>
                                    <div v-if="loadingLessons" class="text-center py-4">
                                        <v-progress-circular indeterminate></v-progress-circular>
                                    </div>
                                    <div v-else-if="lessonEvents.length === 0" class="text-medium-emphasis">
                                        No upcoming lessons. Schedule one on
                                        <router-link to="/Admin/Events">Manage Events</router-link>
                                        (event type “Lesson”).
                                    </div>
                                    <template v-else>
                                        <v-select :model-value="selectedLessonId"
                                            @update:model-value="onLessonSelected"
                                            :items="lessonOptions" item-title="title" item-value="value"
                                            label="Choose a lesson" density="compact" clearable hide-details></v-select>

                                        <div v-if="loadingLessonDetail" class="text-center py-4">
                                            <v-progress-circular indeterminate size="24"></v-progress-circular>
                                        </div>

                                        <template v-else-if="selectedLessonId">
                                            <!-- Lesson ticket tiers -->
                                            <div class="text-subtitle-2 mt-4 mb-1">Lesson ticket</div>
                                            <div v-if="activeLessonTiers.length === 0" class="text-caption text-medium-emphasis">
                                                This lesson has no ticket set up yet. Add one on Manage Events.
                                            </div>
                                            <div v-for="t in activeLessonTiers" :key="t.id"
                                                class="d-flex align-center ga-2 py-1">
                                                <div class="flex-grow-1">
                                                    {{ t.name }}
                                                    <span class="text-medium-emphasis">— ${{ (t.priceCents / 100).toFixed(2) }}</span>
                                                </div>
                                                <v-btn size="small" variant="tonal" color="primary"
                                                    prepend-icon="mdi-plus" @click="addLessonTicket(t)">Add</v-btn>
                                            </div>

                                            <!-- Bikes for this lesson -->
                                            <div class="text-subtitle-2 mt-4 mb-1">Add a bike (optional)</div>
                                            <div v-if="lessonBikes.length === 0" class="text-caption text-medium-emphasis">
                                                No bikes offered with this lesson.
                                            </div>
                                            <div v-for="b in lessonBikes" :key="b.variantId"
                                                class="d-flex align-center ga-2 py-1">
                                                <div class="flex-grow-1">
                                                    {{ b.name }}
                                                    <span class="text-medium-emphasis">— ${{ (b.priceCents / 100).toFixed(2) }}</span>
                                                    <span v-if="b.depositCents > 0" class="text-caption text-medium-emphasis">
                                                        + ${{ (b.depositCents / 100).toFixed(2) }} deposit at pickup
                                                    </span>
                                                    <v-chip v-if="b.available <= 0" size="x-small" color="error" variant="tonal" class="ml-1">
                                                        Fully booked
                                                    </v-chip>
                                                    <v-chip v-else-if="b.available <= 3" size="x-small" color="warning" variant="tonal" class="ml-1">
                                                        {{ b.available }} left
                                                    </v-chip>
                                                </div>
                                                <v-btn size="small"
                                                    :variant="lessonBikeInCart()?.itemId === b.variantId ? 'flat' : 'tonal'"
                                                    :color="lessonBikeInCart()?.itemId === b.variantId ? 'success' : 'primary'"
                                                    :disabled="b.available <= 0 && lessonBikeInCart()?.itemId !== b.variantId"
                                                    @click="toggleLessonBike(b)">
                                                    {{ lessonBikeInCart()?.itemId === b.variantId ? 'Added' : 'Add' }}
                                                </v-btn>
                                            </div>
                                        </template>
                                    </template>
                                </v-expansion-panel-text>
                            </v-expansion-panel>
                        </v-expansion-panels>

                        <v-divider class="my-4"></v-divider>

                        <v-select v-if="discountOptions.length > 0"
                            v-model="selectedDiscountId"
                            :items="discountOptions"
                            item-title="title" item-value="value"
                            label="Apply discount (optional)" density="compact"
                            clearable hide-details class="mb-3"></v-select>
                        <v-text-field v-if="selectedDiscount?.requiresManager"
                            v-model="discountManagerPin"
                            label="Manager PIN" type="password" density="compact"
                            inputmode="numeric" autocomplete="off"
                            class="mb-3"
                            :hint="'A manager PIN is required to apply ' + selectedDiscount.name + '.'"
                            persistent-hint></v-text-field>

                        <v-alert v-if="!branding.extrasEnabled && !membershipOffered" type="info" variant="tonal" class="mb-3">
                            Nothing is set up to sell at the counter yet. Enable add-ons or configure a membership in Settings.
                        </v-alert>
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
                            <div v-if="discountEstimateCents > 0"
                                 class="d-flex justify-space-between text-caption text-success">
                                <div>{{ selectedDiscount?.name }}</div>
                                <div>-${{ (discountEstimateCents / 100).toFixed(2) }}</div>
                            </div>
                            <div class="d-flex justify-space-between text-body-1 mt-1">
                                <strong>Total</strong>
                                <strong>${{ totalDollars }}</strong>
                            </div>
                            <div v-if="cartDepositCents > 0" class="text-caption text-medium-emphasis mt-1">
                                A ${{ (cartDepositCents / 100).toFixed(2) }} refundable deposit is due separately when the bike is picked up.
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
                            <span v-if="creditEstimate > 0" class="text-success">
                                ({{ moneyCents(creditEstimate) }} store credit, {{ moneyCents(dueEstimateCents) }} due)
                            </span>
                        </div>

                        <div v-if="!clientSecret && !cashSubmitted">
                            <CreditLookupField v-model="creditAccount" class="mb-3" style="max-width: 440px" />
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
                                    Confirm {{ moneyCents(dueEstimateCents) }} cash received
                                </v-btn>
                            </div>
                        </div>

                        <div v-else-if="clientSecret">
                            <div id="payment-element" class="mb-4"></div>
                            <div class="d-flex align-center ga-2">
                                <v-btn variant="text" :disabled="paying" @click="cancelCardPayment">Start over</v-btn>
                                <v-spacer></v-spacer>
                                <v-btn color="primary" :loading="paying" :disabled="!stripeReady" @click="pay">
                                    Charge ${{ totalDollars }}
                                </v-btn>
                            </div>
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
                            <span v-else>Charged ${{ totalDollars }}.</span>
                            Each line item below has its own QR for redemption.
                        </v-alert>
                        <v-alert v-if="lineItems.some(l => l.kind === 'season_pass')"
                            type="warning" variant="tonal" density="compact" class="mb-4">
                            <strong>The season pass isn’t usable yet.</strong> The rider must add a photo
                            (and sign, if the pass requires a waiver) under My Passes before the gate will
                            admit them. The QR below won’t scan until they do.
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
import { ref, computed, onMounted, nextTick, watch } from 'vue'
import dayjs from 'dayjs'
import { formatTenantDateTime } from '@/helpers/TenantTime'
import { CounterService, type CounterRider } from '@/services/CounterService'
import { PassService, type WaiverDto } from '@/services/PassService'
import type { EligibleExtra, EligibleExtraVariant, EventDto, EligibleRental } from '@/services/EventService'
import { EventService } from '@/services/EventService'
import { TicketService, type TicketTier } from '@/services/TicketService'
import { ExtraService, type ExtraProduct } from '@/services/ExtraService'
import { DiscountService, type DiscountPreset, type DiscountSurface } from '@/services/DiscountService'
import { SeasonPassService, type SeasonPassProduct } from '@/services/SeasonPassService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'
import RichTextView from '@/components/RichTextView.vue'
import QrCode from '@/components/QrCode.vue'
import { useConfirm } from '@/composables/useConfirm'
import SignaturePad from '@/components/SignaturePad.vue'
import ExtrasPicker, { type ExtraSelection } from '@/components/ExtrasPicker.vue'
import CreditLookupField from '@/components/CreditLookupField.vue'
import { type CreditLookupResult } from '@/services/CreditService'
import PhoneField from '@/components/PhoneField.vue'

type Customer = CounterRider
type CartKind = 'extras' | 'membership' | 'event_ticket' | 'rental' | 'season_pass'

interface CartLine {
    kind: CartKind
    itemId: string
    displayName: string
    unitPriceCents: number
    quantity: number
    requiresWaiver: boolean
    riderPaidServiceChargeBps: number
    variantId?: string | null
    // Set for lesson ticket + bike lines: the lesson event the sale is attached to.
    eventId?: string | null
    // Refundable bike deposit (rental lines only). Added to the charged total but held, not
    // earned — returned to the customer at bike return.
    depositCents?: number
}

const MEMBERSHIP_ITEM_ID = '00000000-0000-0000-0000-000000000001'

const counter = new CounterService()
const passService = new PassService()
const extraService = new ExtraService()
const eventService = new EventService()
const ticketService = new TicketService()

const stepLabels = ['Customer', 'Cart', 'Waiver', 'Payment', 'Receipt']
const confirm = useConfirm()
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

// Cart step accordion: only one section open at a time; defaults to add-ons. The cart
// sells add-ons and memberships (event tickets/passes are not sold at this counter).
const catalogPanel = ref<string | undefined>('extras')
const extras = ref<ExtraProduct[]>([])
const loadingExtras = ref(false)
const cart = ref<CartLine[]>([])

// Tenant-defined staff discounts ("Military 10%", "VMBA member"). The server is the authority on
// what actually comes off; everything here is to let the cashier pick one and see roughly what it
// is worth before charging.
const discountService = new DiscountService()
const discountPresets = ref<DiscountPreset[]>([])
const selectedDiscountId = ref<string | null>(null)
const discountManagerPin = ref('')

/** Cart kinds and discount surfaces share names except rentals, which bill as shop_rental. */
function surfaceForKind(kind: CartKind): DiscountSurface {
    return kind === 'rental' ? 'shop_rental' : kind
}

/** Only discounts that touch something currently in the cart — a cashier should not be offered
 *  "10% off food" while ringing up a race entry. */
const applicableDiscounts = computed(() => {
    const inCart = new Set(cart.value.map(c => surfaceForKind(c.kind)))
    return discountPresets.value.filter(p => p.surfaces.some(s => inCart.has(s)))
})
const discountOptions = computed(() => applicableDiscounts.value.map(p => ({
    value: p.id,
    title: `${p.name} — ${p.label}${p.requiresManager ? ' (manager)' : ''}`,
})))
const selectedDiscount = computed(() =>
    applicableDiscounts.value.find(p => p.id === selectedDiscountId.value) ?? null)

/** Mirrors DiscountPreset.DiscountFor on the server: percent is basis points, amount is cents, and
 *  either way it can never exceed the goods it applies to. */
/** The counter sells four things, each its own discount surface, so the list is the union of all
 *  four. A cashier without settings.manage cannot read the full list, hence the per-surface calls. */
async function loadDiscounts() {
    const surfaces: DiscountSurface[] = ['event_ticket', 'extras', 'shop_rental', 'membership', 'season_pass']
    try {
        const responses = await Promise.all(surfaces.map(s => discountService.forSurface(s)))
        const byId = new Map<string, DiscountPreset>()
        for (const r of responses) {
            for (const p of r.data.data) byId.set(p.id, p)
        }
        discountPresets.value = [...byId.values()].sort((a, b) =>
            a.sortOrder - b.sortOrder || a.name.localeCompare(b.name))
    } catch (err: any) {
        // Never render a load failure as "this track has no discounts" — the cashier would charge
        // full price and the customer would be denied a rate they are entitled to.
        discountPresets.value = []
        flash(err.response?.data?.error
            || 'Couldn’t load this track’s discounts. Reload before charging if one is owed.', 'error')
    }
}

// A discount only survives while something it applies to is still in the cart. Dropping the last
// eligible line would otherwise leave a stale selection that the server rejects at charge time.
watch(applicableDiscounts, list => {
    if (selectedDiscountId.value && !list.some(p => p.id === selectedDiscountId.value)) {
        selectedDiscountId.value = null
        discountManagerPin.value = ''
    }
})

// ── Season passes ───────────────────────────────────────────────────────────
// GET /SeasonPass/Products is public and already filters to active, non-employee products for this
// tenant, so counter staff can read it without a new endpoint or permission.
const seasonPassService = new SeasonPassService()
const seasonPasses = ref<SeasonPassProduct[]>([])
async function loadSeasonPasses() {
    if (!branding.seasonPassesEnabled) return
    try {
        const r = await seasonPassService.listActive()
        seasonPasses.value = r.data.data
    } catch (err: any) {
        // Not silent: an empty panel would read as "this track sells no passes" and the counter
        // would turn away a paying customer.
        seasonPasses.value = []
        flash(err.response?.data?.error
            || 'Couldn’t load season passes. Reload before selling one.', 'error')
    }
}

function addSeasonPassToCart(p: SeasonPassProduct, delta: number) {
    const existing = cart.value.find(c => c.kind === 'season_pass' && c.itemId === p.id)
    if (existing) {
        existing.quantity += delta
        if (existing.quantity <= 0) cart.value.splice(cart.value.indexOf(existing), 1)
    } else if (delta > 0) {
        cart.value.push({
            kind: 'season_pass',
            itemId: p.id,
            displayName: p.name,
            unitPriceCents: p.priceCents,
            quantity: delta,
            // Deliberately false even when the product requires a waiver: the pass's waiver belongs
            // to its HOLDER (often not the buyer) and is captured with the photo during
            // registration. The counter's waiver step signs the PURCHASER's account waiver, which
            // would be the wrong person on the pass.
            requiresWaiver: false,
            riderPaidServiceChargeBps: p.riderPaidServiceChargeBps,
        })
    }
}

const discountEstimateCents = computed(() => {
    const p = selectedDiscount.value
    if (!p) return 0
    const base = cart.value
        .filter(c => p.surfaces.includes(surfaceForKind(c.kind)))
        .reduce((sum, c) => sum + c.unitPriceCents * c.quantity, 0)
    if (base <= 0) return 0
    const raw = p.kind === 'percent' ? Math.floor((base * p.value) / 10000) : p.value
    return Math.min(Math.max(raw, 0), base)
})

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
            // Carried from the product, not defaulted: this decides whether the buyer or the
            // track funds the service charge on the sale, so a stand-in value would mis-charge.
            riderPaidServiceChargeBps: p.riderPaidServiceChargeBps,
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

// ── Lessons: sell a lesson (ticket) + optional bike at the counter ───────────
const lessonEvents = ref<EventDto[]>([])
const loadingLessons = ref(false)
const selectedLessonId = ref<string | null>(null)
const lessonDetail = ref<EventDto | null>(null)      // hydrated single event (tiers + bikes + availability)
const lessonTiers = ref<TicketTier[]>([])
const loadingLessonDetail = ref(false)
const lessonOptions = computed(() => lessonEvents.value.map(e => ({
    value: e.id,
    title: `${e.title} — ${formatTenantDateTime(e.startsAtUtc, 'MMM D, YYYY, h:mm A')}`,
})))
const lessonBikes = computed<EligibleRental[]>(() => lessonDetail.value?.eligibleRentals ?? [])
const activeLessonTiers = computed(() => lessonTiers.value.filter(t => t.isActive))

async function loadLessons() {
    loadingLessons.value = true
    try {
        // Upcoming lesson-type events over the next 120 days.
        const from = new Date().toISOString()
        const to = new Date(Date.now() + 120 * 24 * 3600 * 1000).toISOString()
        const r = await eventService.list(from, to)
        lessonEvents.value = ((r.data as any).data as EventDto[])
            .filter(e => e.eventTypeCode === 'lesson' && e.status === 'scheduled'
                && new Date(e.endsAtUtc).getTime() > Date.now())
    } catch (err: any) {
        lessonEvents.value = []
        paymentError.value = err.response?.data?.error || 'Could not load lessons. Try reopening the section.'
    } finally {
        loadingLessons.value = false
    }
}

async function onLessonSelected(id: string | null) {
    selectedLessonId.value = id
    lessonDetail.value = null
    lessonTiers.value = []
    if (!id) return
    loadingLessonDetail.value = true
    try {
        const [ev, tiers] = await Promise.all([
            eventService.getPublic(id),
            ticketService.listActiveTiers(id),
        ])
        lessonDetail.value = (ev.data as any).data
        lessonTiers.value = (tiers.data as any).data
    } catch (err: any) {
        paymentError.value = err.response?.data?.error || 'Could not load this lesson’s tickets and bikes.'
    } finally {
        loadingLessonDetail.value = false
    }
}

function lessonBikeInCart(): CartLine | undefined {
    return cart.value.find(c => c.kind === 'rental' && c.eventId === selectedLessonId.value)
}

function addLessonTicket(tier: TicketTier) {
    if (!selectedLessonId.value) return
    // One lesson-ticket line per tier click; the counter allows multiple (e.g. two riders).
    const existing = cart.value.find(c => c.kind === 'event_ticket' && c.itemId === tier.id)
    if (existing) { existing.quantity += 1; return }
    cart.value.push({
        kind: 'event_ticket',
        itemId: tier.id,
        displayName: `${lessonDetail.value?.title ?? 'Lesson'} — ${tier.name}`,
        unitPriceCents: tier.priceCents,
        quantity: 1,
        requiresWaiver: !!lessonDetail.value?.requiresRiderWaiver,
        riderPaidServiceChargeBps: tier.riderPaidServiceChargeBps ?? 10000,
        eventId: selectedLessonId.value,
    })
}

function toggleLessonBike(bike: EligibleRental) {
    if (!selectedLessonId.value) return
    const existing = lessonBikeInCart()
    if (existing && existing.itemId === bike.variantId) {
        cart.value = cart.value.filter(c => c !== existing)   // clicking the selected bike removes it
        return
    }
    // One bike per lesson: replace any existing bike line for this lesson.
    cart.value = cart.value.filter(c => !(c.kind === 'rental' && c.eventId === selectedLessonId.value))
    cart.value.push({
        kind: 'rental',
        itemId: bike.variantId,
        displayName: `${bike.name} (bike)`,
        unitPriceCents: bike.priceCents,
        quantity: 1,
        requiresWaiver: false,
        // All-in pricing on the shop catalog: the bike fee carries no rider service charge.
        riderPaidServiceChargeBps: 0,
        eventId: selectedLessonId.value,
        depositCents: bike.depositCents,
    })
}

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
// Refundable bike deposits: NOT charged with the sale. Recorded on the rental and handled
// at the shop when the bike goes out (hold or cash, staff's call).
const cartDepositCents = computed(() => cart.value.reduce((sum, c) => sum + (c.depositCents ?? 0), 0))
// Net of any staff discount so the credit and amount-due estimates below agree with what the
// customer is about to be charged. Still an estimate either way: like the service charge above it,
// this figure excludes admission tax, which only the server computes.
const cartTotalCents = computed(() => Math.max(0,
    cartSubtotalCents.value + cartServiceChargeCents.value - discountEstimateCents.value))
const totalDollars = computed(() => {
    const cents = clientSecret.value ? totalAmountCents.value : cartTotalCents.value
    return (cents / 100).toFixed(2)
})

// Store credit tender (client estimate for display; the server re-verifies and caps).
const creditAccount = ref<CreditLookupResult | null>(null)
const creditEstimate = computed(() =>
    creditAccount.value ? Math.min(creditAccount.value.balanceCents, cartTotalCents.value) : 0)
const dueEstimateCents = computed(() => Math.max(0, cartTotalCents.value - creditEstimate.value))
function moneyCents(cents: number): string { return `$${(cents / 100).toFixed(2)}` }

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(async () => {
    // Return from a redirect-based payment method: restore the stashed receipt and jump to
    // step 5 so the operator sees the confirmation + QR codes instead of a reset stepper.
    const params = new URLSearchParams(window.location.search)
    const pi = params.get('payment_intent')
    const redirectStatus = params.get('redirect_status')
    if (pi && redirectStatus) {
        if (redirectStatus === 'succeeded') {
            try {
                const saved = sessionStorage.getItem(`counterReceipt:${pi}`)
                if (saved) {
                    const r = JSON.parse(saved)
                    lineItems.value = r.lineItems ?? []
                    totalAmountCents.value = r.totalAmountCents ?? 0
                }
                sessionStorage.removeItem(`counterReceipt:${pi}`)
            } catch { /* ignore a malformed/missing stash; step 5 still confirms the charge */ }
            step.value = 5
            flash('Payment received. Sale complete.', 'success')
        } else {
            flash('The payment was not completed. Start the sale again.', 'error')
        }
        history.replaceState(null, '', window.location.pathname)
    }
    void loadDiscounts()
    void loadSeasonPasses()
    loadingExtras.value = true
    try {
        const [w, x] = await Promise.all([
            passService.getWaiver().catch((e: any) => {
                flash(e.response?.data?.error ?? 'Couldn’t load the waiver. Riders may not be able to sign until you refresh.', 'error')
                return { data: { data: null } }
            }),
            branding.extrasEnabled
                ? extraService.listForAdmin().catch((e: any) => {
                    flash(e.response?.data?.error ?? 'Couldn’t load add-ons. Some items may be missing until you refresh.', 'error')
                    return { data: { data: [] as ExtraProduct[] } }
                })
                : Promise.resolve({ data: { data: [] as ExtraProduct[] } }),
        ])
        activeWaiver.value = (w.data as any).data ?? null
        extras.value = ((x.data as any).data as ExtraProduct[])
            .filter(e => e.isActive)
            .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name))
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load catalog.', 'error')
    } finally {
        loadingExtras.value = false
    }
    // Lessons load in the background so the Lesson panel is ready when the cashier opens it.
    await loadLessons()
})

// Candidates from a name/phone search. One match resolves straight through; several present a
// list; none offers to create. Only an email can be looked up directly, so a chosen candidate is
// resolved via findRider to pull the full record (waiver state, emergency contact).
const candidates = ref<{ id: string; email: string; firstName: string; lastName: string; phone: string | null }[]>([])

async function resolveByEmail(email: string) {
    const r = await counter.findRider(email)
    customer.value = (r.data as any).data
    candidates.value = []
    showCreate.value = false
    lookupError.value = null
}

async function pickCandidate(c: { email: string }) {
    findingCustomer.value = true
    try { await resolveByEmail(c.email) }
    catch (err: any) { flash(err.response?.data?.error || 'Could not open that customer. Try again.', 'error') }
    finally { findingCustomer.value = false }
}

// Prefills the new-customer form from whatever was typed: an email goes in the email box, anything
// else is treated as a name, so the operator never retypes what they already entered.
function startCreate() {
    const typed = customerEmail.value.trim()
    const looksEmail = typed.includes('@')
    const parts = looksEmail ? [] : typed.split(/\s+/).filter(Boolean)
    newCustomer.value = {
        firstName: parts[0] ?? '', lastName: parts.slice(1).join(' '),
        email: looksEmail ? typed : '', birthdate: '',
        emergencyContactName: '', emergencyContactPhone: '',
    }
    candidates.value = []
    showCreate.value = true
}

async function findCustomer() {
    const q = customerEmail.value.trim()
    if (q.length < 2) return
    findingCustomer.value = true
    lookupError.value = null
    candidates.value = []
    try {
        const r = await counter.searchRiders(q)
        const found = (r.data as any).data as typeof candidates.value
        if (found.length === 1) {
            await resolveByEmail(found[0].email)
        } else if (found.length > 1) {
            candidates.value = found
        } else {
            lookupError.value = `No customer found for "${q}".`
            startCreate()
            showCreate.value = false   // offer it via the alert's button, don't force the form open
        }
    } catch (err: any) {
        // Never render a failed search as "no customer": that is how duplicate accounts get created.
        flash(err.response?.data?.error || 'Customer search failed. Try again before creating a new one.', 'error')
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
                eventId: c.eventId ?? null,
                variantId: c.variantId ?? null,
            })),
            signWaiver: willSignWaiver.value && customerAcknowledged.value,
            signatureDataUrl: willSignWaiver.value ? customerSignatureDataUrl.value : null,
            parentName: signingForMinor ? parentName.value.trim() : null,
            parentPhone: signingForMinor ? parentPhone.value.trim() : null,
            discountPresetId: selectedDiscountId.value,
            managerPin: discountManagerPin.value || null,
            paymentMethod: method,
            creditAccountId: creditAccount.value?.id ?? null,
            creditCents: creditAccount.value?.balanceCents ?? 0,
        })
        const data = (r.data as any).data
        clientSecret.value = data.clientSecret
        totalAmountCents.value = data.totalAmountCents
        lineItems.value = data.lineItems

        if (!clientSecret.value) {
            cashSubmitted.value = true
            step.value = 5
            const credited = data.creditAppliedCents ?? 0
            flash(credited > 0
                ? `Sale complete: ${moneyCents(credited)} store credit applied, ${moneyCents(data.dueCents ?? 0)} collected.`
                // Not cash and nothing left to charge: the cart came to zero on its own (a comp or
                // a fully discounted line). It used to say "Voucher applied", which is no longer a
                // thing that can happen.
                : method === 'cash' ? 'Cash sale recorded.' : 'Sale complete — no charge due.', 'success')
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
    // Direct-charge tenants confirm the online counter charge on their own connected account.
    const stripeAccount = branding.stripeChargeMode === 'direct' ? branding.stripeConnectAccountId : null
    stripe = await getStripe(branding.stripePublishableKey, stripeAccount)
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
        // A redirect-based method (3DS / wallet) navigates away and back, remounting this
        // page with all sale state lost. Stash the receipt keyed by PaymentIntent id so the
        // mount-time return handler can restore step 5 with its QR codes.
        const piId = clientSecret.value?.split('_secret')[0] ?? ''
        if (piId) {
            try {
                sessionStorage.setItem(`counterReceipt:${piId}`, JSON.stringify({
                    lineItems: lineItems.value,
                    totalAmountCents: totalAmountCents.value,
                }))
            } catch { /* sessionStorage unavailable; a redirect return would show an empty receipt */ }
        }
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

// Escape hatch from the locked card-payment step (declined card, wrong item, customer
// changed their mind). The PaymentIntent is uncaptured, so nothing was charged; any
// leftover pending rows are cleaned up by the PendingPurchaseReconciler.
async function cancelCardPayment() {
    const ok = await confirm({
        title: 'Start over?',
        message: 'Discard this prepared payment and return to a new sale? The customer has not been charged. If they already approved it on the reader, check Purchases before re-ringing.',
        confirmText: 'Start over',
        confirmColor: 'warning',
    })
    if (!ok) return
    reset()
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
    selectedDiscountId.value = null
    discountManagerPin.value = ''
    catalogPanel.value = 'extras'
    creditAccount.value = null
}

function redeemUrl(token: string): string {
    return `${window.location.protocol}//${window.location.host}/redeem/${token}`
}

function kindLabel(kind: string): string {
    switch (kind) {
        case 'extras': return 'Add-on'
        case 'membership': return 'Membership'
        case 'season_pass': return 'Season pass'
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
