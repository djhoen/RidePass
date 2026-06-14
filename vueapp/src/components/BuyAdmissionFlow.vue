<template>
    <div>
        <div v-if="loading" class="d-flex justify-center py-8">
            <v-progress-circular indeterminate color="primary"></v-progress-circular>
        </div>

        <template v-else>
            <!-- Event context (description + spots-left) shown above the tier picker
                 when the parent passes the event in. The tier list itself shows per-tier
                 inventory when the tier has an explicit cap. -->
            <v-card v-if="event && (event.description || eventSpotsLeft !== null)" variant="tonal" class="mb-4">
                <v-card-text class="py-3">
                    <p v-if="event.description" class="mb-2" style="white-space: pre-wrap">{{ event.description }}</p>
                    <div v-if="eventSpotsLeft !== null" class="text-body-2">
                        <v-icon size="small" class="mr-1">mdi-account-multiple</v-icon>
                        <strong>{{ eventSpotsLeft }}</strong> of {{ event.capacity }} {{ eventSpotsLeft === 1 ? 'spot' : 'spots' }} left
                    </div>
                </v-card-text>
            </v-card>

            <!-- Race entries are tied to a rider account (waiver on file, My Passes,
                 waitlist alerts), so racers create their login (or sign in) here as the
                 first step instead of being bounced to a login wall. Spectator / mixed
                 flows keep guest checkout below. -->
            <v-card v-if="needsRacerAuth && tiers.length > 0" class="mb-4 pa-4" variant="outlined">
                <v-card-title class="px-0">
                    {{ authMode === 'create' ? 'Create your racer account' : 'Log in to continue' }}
                </v-card-title>
                <v-card-text class="px-0">
                    <p class="text-body-2 text-medium-emphasis mb-4">
                        Race entries are tied to your account so your waiver stays on file and your
                        passes show up under My Passes.
                    </p>

                    <template v-if="authMode === 'create'">
                        <v-row>
                            <v-col cols="12" sm="6">
                                <v-text-field v-model="authForm.firstName" label="First name" density="compact"></v-text-field>
                            </v-col>
                            <v-col cols="12" sm="6">
                                <v-text-field v-model="authForm.lastName" label="Last name" density="compact"></v-text-field>
                            </v-col>
                        </v-row>
                        <v-text-field v-model="authForm.email" type="email" label="Email" density="compact" class="mt-4"></v-text-field>
                        <PhoneField v-model="authForm.phone" label="Mobile phone" density="compact" class="mt-4"
                            hint="Used for waitlist and event-day alerts." persistent-hint />
                        <v-text-field v-model="authForm.birthdate" type="date" :max="todayIso" label="Birthdate"
                            density="compact" class="mt-4"
                            hint="Riders under 18 need a parent or guardian on the waiver." persistent-hint></v-text-field>
                        <v-row class="mt-0">
                            <v-col cols="12" sm="6">
                                <v-text-field v-model="authForm.emergencyContactName" label="Emergency contact name" density="compact"></v-text-field>
                            </v-col>
                            <v-col cols="12" sm="6">
                                <PhoneField v-model="authForm.emergencyContactPhone" label="Emergency contact phone" density="compact" />
                            </v-col>
                        </v-row>
                        <v-text-field v-model="authForm.password" type="password" label="Password" density="compact" class="mt-4"></v-text-field>
                        <v-text-field v-model="authForm.confirmPassword" type="password" label="Confirm password" density="compact" class="mt-4"></v-text-field>
                    </template>

                    <template v-else>
                        <v-text-field v-model="authForm.email" type="email" label="Email" density="compact"></v-text-field>
                        <v-text-field v-model="authForm.password" type="password" label="Password" density="compact" class="mt-4"></v-text-field>
                    </template>

                    <div v-if="authError" class="text-error text-caption mt-2">{{ authError }}</div>

                    <v-btn color="primary" block size="large" class="mt-4" :loading="authBusy" @click="submitAuth">
                        {{ authMode === 'create' ? 'Create account &amp; continue' : 'Log in &amp; continue' }}
                    </v-btn>

                    <div class="text-center text-body-2 mt-3">
                        <template v-if="authMode === 'create'">
                            Already have an account?
                            <a class="auth-toggle-link" @click="switchAuthMode('login')">Log in</a>
                        </template>
                        <template v-else>
                            New here?
                            <a class="auth-toggle-link" @click="switchAuthMode('create')">Create an account</a>
                        </template>
                    </div>
                </v-card-text>
            </v-card>

            <v-stepper v-if="tiers.length > 0 && !completed && !needsRacerAuth" v-model="step" color="primary" hide-actions>
                <v-stepper-header>
                    <template v-for="(item, idx) in stepperItems" :key="item.value">
                        <v-divider v-if="idx > 0"></v-divider>
                        <v-stepper-item :value="item.value" :title="item.title">
                            <template #icon>
                                <span class="text-body-2 font-weight-bold">{{ idx + 1 }}</span>
                            </template>
                        </v-stepper-item>
                    </template>
                </v-stepper-header>
                <v-stepper-window>
                    <v-stepper-window-item value="select">
                        <v-card class="mb-4 pa-4">
                            <v-card-title>Choose your class</v-card-title>
                            <v-card-text>
                                <div v-for="t in tiers" :key="t.id" class="tier-row">
                                    <div class="tier-row-info">
                                        <v-chip size="x-small" class="mr-2"
                                            :color="t.kind === 'race_entry' ? 'deep-orange' : 'primary'">
                                            {{ t.kind === 'race_entry' ? 'Race' : 'Watch' }}
                                        </v-chip>
                                        <strong>{{ t.name }}</strong>
                                        <span class="ml-1">— ${{ (t.priceCents / 100).toFixed(2) }}</span>
                                        <span v-if="t.inventory" class="text-caption text-medium-emphasis ml-1">
                                            ({{ Math.max(0, t.inventory - (t.sold ?? 0)) }} of {{ t.inventory }} left)
                                        </span>
                                        <span v-if="isSoldOut(t)" class="text-error text-caption"> — SOLD OUT</span>
                                        <div v-if="bundledLabel(t)" class="text-caption text-success mt-1">
                                            <v-icon size="small" class="mr-1">mdi-tag-multiple</v-icon>{{ bundledLabel(t) }}
                                        </div>
                                    </div>
                                    <div class="tier-row-qty d-flex align-center ga-1">
                                        <template v-if="isSoldOut(t) && isAuthenticated && branding.waitlistEnabled">
                                            <v-btn size="small" color="amber-darken-2" variant="tonal"
                                                prepend-icon="mdi-clock-outline" @click="openWaitlistDialog(t)">
                                                Join Waitlist
                                            </v-btn>
                                        </template>
                                        <template v-else>
                                            <v-btn icon="mdi-minus" size="x-small" variant="tonal"
                                                :disabled="(quantities[t.id] ?? 0) <= 0" @click="decQty(t)"></v-btn>
                                            <span class="qty-display">{{ quantities[t.id] ?? 0 }}</span>
                                            <v-btn icon="mdi-plus" size="x-small" variant="tonal"
                                                :disabled="!canIncQty(t)" @click="incQty(t)"></v-btn>
                                        </template>
                                    </div>
                                </div>

                                <!-- Guest contact info lives on the Racer Info step in race mode;
                                     keep it on Select for spectator/mixed flows so single-step purchase still works. -->
                                <template v-if="!isAuthenticated && cartUnits > 0 && !isRaceMode">
                                    <v-divider class="my-4"></v-divider>
                                    <div class="text-subtitle-2 mb-2">Your contact info</div>
                                    <p class="text-caption text-medium-emphasis mb-3">
                                        No account needed. We'll email you a receipt, and the QR codes appear here on confirmation.
                                    </p>
                                    <v-text-field v-model="guestName" label="Full name" density="compact" class="mb-2"></v-text-field>
                                    <v-text-field v-model="guestEmail" type="email" label="Email" density="compact" class="mt-4"></v-text-field>
                                </template>

                                <v-select v-if="isAuthenticated && availableVouchers.length > 0 && cartUnits > 0"
                                    v-model="selectedVoucherId" :items="voucherOptions"
                                    item-title="title" item-value="value"
                                    label="Apply a reward voucher (optional)" density="compact" clearable class="mt-3"
                                    hint="Vouchers only apply when buying a single admission."
                                    persistent-hint :hide-details="false"></v-select>

                                <v-btn color="primary" class="mt-4" :disabled="!canAdvanceFromSelect"
                                    @click="step = stepAfterSelect()">
                                    {{ isRaceMode ? 'Continue to Racer Info' : (extrasNeeded ? 'Continue to Add-ons' : 'Continue') }}
                                </v-btn>
                            </v-card-text>
                        </v-card>
                    </v-stepper-window-item>

                    <!-- Step: Racer Info — only present in Buy Race Entry mode.
                         Loads the rider's profile so they can confirm/correct everything
                         (name, phone, emergency contact) before paying. Edits auto-save on
                         Continue. Future racer-specific fields (rider number, bike, hometown)
                         will land here too. -->
                    <v-stepper-window-item v-if="isRaceMode" value="racer_info">
                        <v-card class="mb-4 pa-4">
                            <v-card-title>Racer Info</v-card-title>
                            <v-card-text>
                                <p class="text-body-2 text-medium-emphasis mb-3">
                                    Confirm everything looks right — we'll use this info for the entry.
                                </p>

                                <template v-if="!isAuthenticated">
                                    <div class="text-subtitle-2 mb-2">Your contact info</div>
                                    <v-row>
                                        <v-col cols="12" sm="6">
                                            <v-text-field v-model="guestName" label="Full name" density="compact"></v-text-field>
                                        </v-col>
                                        <v-col cols="12" sm="6">
                                            <v-text-field v-model="guestEmail" type="email" label="Email" density="compact"></v-text-field>
                                        </v-col>
                                    </v-row>
                                </template>
                                <template v-else>
                                    <div v-if="profileLoading" class="text-center py-4">
                                        <v-progress-circular indeterminate size="32"></v-progress-circular>
                                    </div>
                                    <template v-else>
                                        <v-row>
                                            <v-col cols="12" sm="6">
                                                <v-text-field v-model="profileForm.firstName" label="First name" density="compact"></v-text-field>
                                            </v-col>
                                            <v-col cols="12" sm="6">
                                                <v-text-field v-model="profileForm.lastName" label="Last name" density="compact"></v-text-field>
                                            </v-col>
                                            <v-col cols="12" sm="6">
                                                <v-text-field v-model="profileForm.email" label="Email" type="email"
                                                    density="compact" readonly persistent-hint
                                                    hint="Contact support to change your email."></v-text-field>
                                            </v-col>
                                            <v-col cols="12" sm="6">
                                                <PhoneField v-model="profileForm.phone" label="Mobile phone" density="compact"
                                                    hint="Used for waitlist alerts and event-day messages."
                                                    persistent-hint />
                                            </v-col>
                                            <v-col cols="12" sm="6">
                                                <v-text-field v-model="profileForm.birthdate"
                                                    type="date" :max="todayIso"
                                                    label="Birthdate" density="compact"
                                                    hint="Riders under 18 need a parent or guardian on the waiver."
                                                    persistent-hint></v-text-field>
                                            </v-col>
                                            <v-col v-if="localRiderIsMinor" cols="12" sm="6">
                                                <v-alert type="info" variant="tonal" density="compact">
                                                    Rider is under 18 — parent or guardian info captured on the waiver below.
                                                </v-alert>
                                            </v-col>
                                        </v-row>
                                        <div class="text-subtitle-2 mt-4 mb-1">Address</div>
                                        <v-row>
                                            <v-col cols="12" sm="8">
                                                <v-text-field v-model="profileForm.addressLine"
                                                    label="Street address" density="compact" maxlength="200"></v-text-field>
                                            </v-col>
                                            <v-col cols="12" sm="4">
                                                <v-text-field v-model="profileForm.addressLine2"
                                                    label="Apt / suite (optional)" density="compact" maxlength="200"></v-text-field>
                                            </v-col>
                                            <v-col cols="12" sm="5">
                                                <v-text-field v-model="profileForm.city"
                                                    label="City" density="compact" maxlength="120"></v-text-field>
                                            </v-col>
                                            <v-col cols="6" sm="4">
                                                <v-select v-model="profileForm.state"
                                                    :items="US_STATES" item-title="title" item-value="value"
                                                    label="State" density="compact"></v-select>
                                            </v-col>
                                            <v-col cols="6" sm="3">
                                                <v-text-field v-model="profileForm.postalCode"
                                                    label="ZIP" density="compact" maxlength="20"></v-text-field>
                                            </v-col>
                                        </v-row>
                                        <v-row>
                                            <v-col cols="12" sm="4">
                                                <v-text-field v-model="profileForm.raceNumber" label="Racer number"
                                                    placeholder="e.g. 21B" density="compact" maxlength="16"></v-text-field>
                                            </v-col>
                                            <v-col cols="12" sm="8">
                                                <v-text-field v-model="profileForm.bike" label="Bike"
                                                    placeholder="e.g. Yamaha YZ250F" density="compact" maxlength="100"></v-text-field>
                                            </v-col>
                                        </v-row>
                                        <v-row class="mt-4">
                                            <v-col cols="12" sm="6">
                                                <v-text-field v-model="profileForm.emergencyContactName" label="Emergency Contact Name" density="compact"></v-text-field>
                                            </v-col>
                                            <v-col cols="12" sm="6">
                                                <PhoneField v-model="profileForm.emergencyContactPhone" label="Emergency Contact Phone" density="compact" />
                                            </v-col>
                                        </v-row>
                                        <div v-if="profileSaveError" class="text-error text-caption mt-2">
                                            {{ profileSaveError }}
                                        </div>
                                    </template>
                                </template>

                                <!-- Inline waiver — required when the event has a waiver and this rider
                                     hasn't signed it. Skipped when already on file or no waiver applies. -->
                                <div v-if="isAuthenticated && raceWaiverNeedsSigning" class="mt-6">
                                    <v-divider class="mb-4"></v-divider>
                                    <div class="text-subtitle-2 mb-1">
                                        Waiver — {{ raceWaiver?.name || raceWaiver?.title }}
                                    </div>
                                    <p v-if="raceWaiverLoading" class="text-caption text-medium-emphasis">
                                        Loading waiver…
                                    </p>
                                    <v-alert v-else-if="!profileForm.birthdate"
                                        type="warning" variant="tonal" density="compact">
                                        Enter your birthdate above before signing the waiver — we use it to
                                        determine whether a parent or guardian needs to sign on your behalf.
                                    </v-alert>
                                    <template v-else-if="raceWaiver">
                                        <v-card variant="outlined" class="pa-3 racer-waiver-body mb-3">
                                            <RichTextView v-if="raceWaiver.body" :html="raceWaiver.body" />
                                            <div v-else class="text-medium-emphasis">(Waiver text is empty.)</div>
                                        </v-card>
                                        <v-alert v-if="raceRiderIsMinor" type="info" variant="tonal" density="compact" class="mb-2">
                                            Rider is under 18 — a parent or guardian must sign and provide their info.
                                        </v-alert>
                                        <v-checkbox v-model="waiverAcknowledged" hide-details density="compact"
                                            :label="raceRiderIsMinor
                                                ? 'I am the parent / guardian and agree to this waiver on the rider\'s behalf'
                                                : 'I have read and agree to this waiver'"></v-checkbox>
                                        <v-row v-if="raceRiderIsMinor && waiverAcknowledged" class="mt-1">
                                            <v-col cols="12" sm="6">
                                                <v-text-field v-model="waiverParentName"
                                                    label="Parent / guardian name" density="compact"></v-text-field>
                                            </v-col>
                                            <v-col cols="12" sm="6">
                                                <PhoneField v-model="waiverParentPhone"
                                                    label="Parent / guardian phone" density="compact" />
                                            </v-col>
                                        </v-row>
                                        <div v-if="waiverAcknowledged" class="mt-2">
                                            <div class="text-caption text-medium-emphasis mb-1">
                                                {{ raceRiderIsMinor ? 'Parent signs below' : 'Sign below' }}
                                            </div>
                                            <SignaturePad v-model="waiverSignatureDataUrl" />
                                        </div>
                                        <div v-if="waiverSignError" class="text-error text-caption mt-2">
                                            {{ waiverSignError }}
                                        </div>
                                    </template>
                                </div>
                                <v-alert v-else-if="isAuthenticated && raceWaiver && raceWaiverAlreadySigned"
                                    type="success" variant="tonal" density="compact" class="mt-4">
                                    Waiver "{{ raceWaiver.name || raceWaiver.title }}" is on file.
                                </v-alert>

                                <div class="d-flex align-center mt-4 ga-2 flex-wrap">
                                    <v-btn variant="text" @click="step = 'select'">Back</v-btn>
                                    <v-spacer></v-spacer>
                                    <v-btn color="primary" :loading="savingProfile || waiverSigning"
                                        :disabled="!canAdvanceFromRacerInfo"
                                        @click="continueFromRacerInfo">
                                        {{ raceWaiverNeedsSigning && waiverSignatureDataUrl
                                            ? 'Sign &amp; Continue'
                                            : profileDirty ? 'Save &amp; Continue' : (extrasNeeded ? 'Continue to Add-ons' : 'Continue') }}
                                    </v-btn>
                                </div>
                            </v-card-text>
                        </v-card>
                    </v-stepper-window-item>

                    <v-stepper-window-item v-if="extrasNeeded" value="extras">
                        <v-card class="mb-4 pa-4">
                            <v-card-title>Add-ons (optional)</v-card-title>
                            <v-card-text>
                                <p class="text-body-2 text-medium-emphasis mb-3">
                                    Add other items offered for this event.
                                </p>
                                <ExtrasPicker :extras="eligibleExtras" v-model="extraSelections" />
                                <div class="d-flex align-center mt-4 ga-2 flex-wrap">
                                    <v-btn variant="text" @click="step = isRaceMode ? 'racer_info' : 'select'">Back</v-btn>
                                    <v-spacer></v-spacer>
                                    <v-btn color="primary" @click="step = 'discounts'">Continue</v-btn>
                                </div>
                            </v-card-text>
                        </v-card>
                    </v-stepper-window-item>

                    <v-stepper-window-item value="discounts">
                        <v-card class="mb-4 pa-4">
                            <v-card-title>Promo &amp; gift codes</v-card-title>
                            <v-card-text>
                                <p class="text-body-2 text-medium-emphasis mb-3">
                                    Have a code? Enter it below — otherwise just continue.
                                </p>
                                <v-text-field v-model="couponCode" label="Promo code (optional)"
                                              placeholder="SUMMER25" density="compact"
                                              :hide-details="false"
                                              :hint="couponHint" :persistent-hint="!!couponHint"
                                              :error-messages="couponError ? [couponError] : []"></v-text-field>
                                <v-text-field v-model="giftCardCode" label="Gift card code (optional)"
                                              placeholder="GIFT-XXXXXXXX" density="compact" class="mt-3"
                                              :hide-details="false"
                                              :error-messages="giftCardError ? [giftCardError] : []"></v-text-field>

                                <div class="text-caption text-medium-emphasis mt-3">
                                    Service charge and any voucher / coupon / gift card discounts apply at the payment step.
                                </div>
                                <div class="d-flex align-center mt-4 ga-2 flex-wrap">
                                    <v-btn variant="text"
                                           @click="step = extrasNeeded ? 'extras' : (isRaceMode ? 'racer_info' : 'select')">Back</v-btn>
                                    <v-spacer></v-spacer>
                                    <v-btn color="primary" :loading="creating" @click="createIntent">
                                        Continue to Payment
                                    </v-btn>
                                </div>
                            </v-card-text>
                        </v-card>
                    </v-stepper-window-item>

                    <v-stepper-window-item value="payment">
                        <v-card class="mb-4 pa-4">
                            <v-card-title>Payment</v-card-title>
                            <v-card-text>
                                <div v-if="!branding.stripePublishableKey" class="text-error">Stripe publishable key is not configured.</div>
                                <template v-else>
                                    <v-table v-if="riderServiceChargeCents > 0 || giftCardAppliedCents > 0" density="compact" class="mb-3">
                                        <tbody>
                                            <tr><td>Subtotal</td><td class="text-right">${{ ((amountCents + giftCardAppliedCents - riderServiceChargeCents) / 100).toFixed(2) }}</td></tr>
                                            <tr v-if="riderServiceChargeCents > 0"><td>Service charge</td><td class="text-right">${{ (riderServiceChargeCents / 100).toFixed(2) }}</td></tr>
                                            <tr v-if="giftCardAppliedCents > 0"><td>Gift card applied</td><td class="text-right">−${{ (giftCardAppliedCents / 100).toFixed(2) }}</td></tr>
                                            <tr><td><strong>Total</strong></td><td class="text-right"><strong>${{ displayAmount() }}</strong></td></tr>
                                        </tbody>
                                    </v-table>
                                    <div :id="paymentElementId" class="mb-4"></div>
                                    <v-btn color="primary" :loading="paying" :disabled="!stripeReady" @click="pay">Pay ${{ displayAmount() }}</v-btn>
                                    <div v-if="paymentError" class="text-error mt-3">{{ paymentError }}</div>
                                </template>
                            </v-card-text>
                        </v-card>
                    </v-stepper-window-item>
                </v-stepper-window>
            </v-stepper>

            <!-- Order Summary sits below the stepper so the active step stays the focal
                 point. Updates live as items get added to the cart. Hidden once the
                 purchase completes (the QR card takes over). -->
            <v-card v-if="tiers.length > 0 && !completed && !needsRacerAuth" class="mt-4 pa-3" variant="outlined">
                <div class="text-overline text-medium-emphasis mb-2">Order Summary</div>
                <v-table density="compact" class="bg-transparent">
                    <tbody>
                        <tr v-if="cartLineItems.length === 0 && extrasLineItems.length === 0">
                            <td colspan="2" class="text-medium-emphasis text-caption">
                                Pick class to see your total.
                            </td>
                        </tr>
                        <tr v-for="line in cartLineItems" :key="line.tierId">
                            <td>{{ line.name }} × {{ line.quantity }}</td>
                            <td class="text-right">${{ ((line.priceCents * line.quantity) / 100).toFixed(2) }}</td>
                        </tr>
                        <tr v-for="line in extrasLineItems" :key="line.productId">
                            <td>{{ line.name }} × {{ line.quantity }}</td>
                            <td class="text-right">${{ ((line.priceCents * line.quantity) / 100).toFixed(2) }}</td>
                        </tr>
                        <tr v-if="addMembership && branding.membershipPriceCents > 0">
                            <td>{{ branding.membershipName }}</td>
                            <td class="text-right">{{ formatMoney(branding.membershipPriceCents) }}</td>
                        </tr>
                        <tr>
                            <td><strong>Total</strong></td>
                            <td class="text-right"><strong>${{ (orderTotalCents / 100).toFixed(2) }}</strong></td>
                        </tr>
                    </tbody>
                </v-table>
            </v-card>

            <div v-if="tiers.length === 0" class="text-medium-emphasis">
                This event has no admissions available.
            </div>

            <v-card v-if="completed" class="mb-4 pa-4">
                <v-card-title>Payment</v-card-title>
                <v-card-text>
                    <v-alert type="success" class="mb-4">
                        {{ purchasedTickets.length === 1 ? 'Admission purchased! Show this QR at the gate.'
                            : `${purchasedTickets.length} admissions purchased! Show each QR at the gate.` }}
                    </v-alert>
                    <div class="qr-grid">
                        <div v-for="t in purchasedTickets" :key="t.purchaseId" class="qr-cell">
                            <QrCode :value="redeemUrl(t.redemptionToken)" :size="200" />
                            <div class="text-caption text-center mt-2">{{ t.tierName }}</div>
                        </div>
                    </div>
                    <div v-if="!isAuthenticated" class="text-center text-caption text-medium-emphasis mt-3">
                        Screenshot or save these QRs — they're your tickets.
                    </div>
                    <div v-else class="text-center text-caption text-medium-emphasis mt-3">
                        Find them later on <router-link to="/User/MyPasses">My Passes</router-link>.
                    </div>
                </v-card-text>
            </v-card>
        </template>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">{{ snackbarText }}</v-snackbar>

        <!-- Waiver-required dialog: surfaces when the server rejects the purchase because
             the rider hasn't signed the active waiver. Linking to /Waiver instead of just
             flashing a toast lets them complete the gating step in one click. -->
        <v-dialog v-model="waiverDialog" max-width="520" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Sign the waiver first</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="waiverDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="mb-2">{{ waiverDialogMessage }}</p>
                    <p class="text-body-2 text-medium-emphasis">
                        It only takes a minute — read the waiver, sign, and you'll come back here to finish your purchase.
                    </p>
                </v-card-text>
                <v-card-actions>
                    <v-btn @click="waiverDialog = false">Not now</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn color="primary" @click="goToWaiver">Read &amp; sign</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="membershipGateOpen" max-width="520" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Membership required</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="membershipGateOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="mb-2">{{ membershipGateMessage }}</p>
                </v-card-text>
                <v-card-actions>
                    <v-btn @click="membershipGateOpen = false">Not now</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn class="bg-color-primary" :loading="creating" @click="addMembershipAndRetry">
                        Add to cart ({{ formatMoney(branding.membershipPriceCents) }})
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="waitlistDialog" max-width="540" persistent>
            <v-card v-if="waitlistTier">
                <v-card-title class="d-flex align-center">
                    <span>Join the {{ waitlistTier.name }} waitlist</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="waitlistDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-3">
                        We'll text you the moment a spot opens. The first alternate in line gets it,
                        with {{ branding.waitlistConfirmWindowMinutes }} minutes to confirm before the
                        spot rolls to the next person.
                    </p>
                    <v-alert v-if="waitlistPhone" type="info" variant="tonal" density="compact" class="mb-3">
                        Texts will go to <strong>{{ waitlistPhone }}</strong>.
                        <router-link to="/User/Profile" class="ml-1">Update on your profile.</router-link>
                    </v-alert>
                    <v-alert v-else type="warning" variant="tonal" density="compact" class="mb-3">
                        Add a mobile phone on your <router-link to="/User/Profile">profile</router-link> first —
                        we can't notify you of an open spot without one.
                    </v-alert>

                    <v-checkbox v-model="waitlistPrepay" hide-details density="compact"
                        :label="`Pre-pay $${(waitlistPrepayCents / 100).toFixed(2)} now to guarantee my spot (refunded if no spot opens up)`"></v-checkbox>

                    <v-textarea v-model="waitlistNotes" label="Anything else? (optional)" rows="2"
                        density="compact" maxlength="500" counter class="mt-2"></v-textarea>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="waitlistDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="waitlistJoining" :disabled="!waitlistPhone" @click="confirmJoinWaitlist">
                        {{ waitlistPrepay ? 'Continue to Payment' : 'Join Waitlist' }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="waitlistPayOpen" persistent max-width="500">
            <v-card v-if="waitlistPayInFlight">
                <v-card-title class="d-flex align-center">
                    <span>Pre-pay {{ waitlistPayInFlight.tierName }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="waitlistPayOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-3">
                        You'll be charged ${{ (waitlistPayInFlight.amountCents / 100).toFixed(2) }} now.
                        If no spot ever opens up, the full amount is refunded automatically.
                    </p>
                    <div :id="waitlistPaymentElementId" class="mb-4"></div>
                    <v-btn color="primary" :loading="waitlistPaying" :disabled="!waitlistStripeReady" @click="payWaitlist">
                        Pay ${{ (waitlistPayInFlight.amountCents / 100).toFixed(2) }}
                    </v-btn>
                    <div v-if="waitlistPaymentError" class="text-error mt-3">{{ waitlistPaymentError }}</div>
                </v-card-text>
            </v-card>
        </v-dialog>
    </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, nextTick, computed, watch } from 'vue'
import { useRouter } from 'vue-router'
import { TicketService, type TicketTier, type TicketRedemption } from '@/services/TicketService'
import { type EventDto } from '@/services/EventService'
import { RewardService, type RiderRewardRedemption } from '@/services/RewardService'
import { WaitlistService } from '@/services/WaitlistService'
import { UserService } from '@/services/UserService'
import { branding } from '@/stores/branding'
import { getStripe } from '@/helpers/StripeHelper'
import authHelper from '@/helpers/AuthHelper'
import QrCode from '@/components/QrCode.vue'
import ExtrasPicker, { type ExtraSelection } from '@/components/ExtrasPicker.vue'
import PhoneField from '@/components/PhoneField.vue'
import SignaturePad from '@/components/SignaturePad.vue'
import RichTextView from '@/components/RichTextView.vue'
import { WaiverService, type WaiverDto, type WaiverSignatureStatus } from '@/services/WaiverService'

const props = defineProps<{
    eventId: string
    kindFilter?: 'spectator_pass' | 'race_entry' | null
    // Optional context: when the parent already has the event (e.g. from a calendar
    // grid or home-page card), pass it in so we can show description + spots left.
    event?: EventDto | null
}>()

const eventSpotsLeft = computed<number | null>(() => {
    const ev = props.event
    if (!ev?.capacity) return null
    return Math.max(0, ev.capacity - (ev.spotsReserved ?? 0))
})
const emit = defineEmits<{ (e: 'completed'): void }>()

const router = useRouter()
const service = new TicketService()
const rewardService = new RewardService()
const waitlistService = new WaitlistService()
const userService = new UserService()
const waiverService = new WaiverService()

// Unique element id per mount so multiple instances (e.g. dialog re-opened for a
// different event) never clash with each other or with the route page version.
const paymentElementId = `payment-element-${Math.random().toString(36).slice(2, 10)}`

const allTiers = ref<TicketTier[]>([])
const tiers = computed<TicketTier[]>(() =>
    props.kindFilter ? allTiers.value.filter(t => t.kind === props.kindFilter) : allTiers.value
)

// Per-tier quantities. Reactive map keyed by tier id. Resets when load() runs.
const quantities = reactive<Record<string, number>>({})

const cartUnits = computed(() =>
    Object.values(quantities).reduce<number>((sum, q) => sum + (q ?? 0), 0)
)
const cartTotalCents = computed(() =>
    tiers.value.reduce<number>((sum, t) => sum + (quantities[t.id] ?? 0) * t.priceCents, 0)
)
// Per-tier rows with qty > 0, used to render the Order Summary line items.
const cartLineItems = computed(() =>
    tiers.value
        .filter(t => (quantities[t.id] ?? 0) > 0)
        .map(t => ({ tierId: t.id, name: t.name, priceCents: t.priceCents, quantity: quantities[t.id] ?? 0 }))
)

// ── Event extras (camping/parking/etc.) ─────────────────────────────────────
// Show every extra that's available outright OR has variants (variant inventory
// is checked per-variant, so the product-level remaining=0 is meaningless).
const eligibleExtras = computed(() =>
    (props.event?.eligibleExtras ?? []).filter(e => e.remaining !== 0 || e.variants.length > 0))
const extrasNeeded = computed(() => eligibleExtras.value.length > 0)
const extraSelections = ref<ExtraSelection[]>([])

function resolvedExtraPrice(s: ExtraSelection): number {
    const product = eligibleExtras.value.find(e => e.productId === s.productId)
    if (!product) return 0
    if (s.variantId) {
        const v = product.variants.find(x => x.id === s.variantId)
        if (v) return v.priceCents
    }
    return product.priceCents
}
function resolvedExtraName(s: ExtraSelection): string {
    const product = eligibleExtras.value.find(e => e.productId === s.productId)
    if (!product) return 'Add-on'
    if (s.variantId) {
        const v = product.variants.find(x => x.id === s.variantId)
        if (v) {
            const attrs = [v.size, v.color, v.gender].filter(x => !!x).join(' / ')
            return attrs ? `${product.name} (${attrs})` : product.name
        }
    }
    return product.name
}

const extrasTotalCents = computed(() =>
    extraSelections.value.reduce((sum, s) => sum + s.quantity * resolvedExtraPrice(s), 0))
const extrasLineItems = computed(() =>
    extraSelections.value
        .filter(s => s.quantity > 0)
        .map(s => ({
            productId: s.productId + (s.variantId ?? ''),
            name: resolvedExtraName(s),
            priceCents: resolvedExtraPrice(s),
            quantity: s.quantity,
        })))
const orderTotalCents = computed(() => cartTotalCents.value
    + extrasTotalCents.value
    + (addMembership.value ? (branding.membershipPriceCents ?? 0) : 0))
const purchasedTickets = ref<TicketRedemption[]>([])

const couponCode = ref('')
const couponError = ref('')
const couponHint = computed(() => couponError.value ? '' : 'Coupons can\'t be combined with reward vouchers.')
watch(couponCode, () => { couponError.value = '' })

const giftCardCode = ref('')
const giftCardError = ref('')
watch(giftCardCode, () => { giftCardError.value = '' })

function tierRemaining(t: TicketTier): number | null {
    if (t.inventory === null) return null
    return Math.max(0, t.inventory - (t.sold ?? 0))
}
function canIncQty(t: TicketTier): boolean {
    if (isSoldOut(t)) return false
    const remaining = tierRemaining(t)
    if (remaining === null) return true
    return (quantities[t.id] ?? 0) < remaining
}
function incQty(t: TicketTier) {
    if (canIncQty(t)) quantities[t.id] = (quantities[t.id] ?? 0) + 1
}
function decQty(t: TicketTier) {
    const cur = quantities[t.id] ?? 0
    if (cur > 0) quantities[t.id] = cur - 1
}

const loading = ref(true)
const creating = ref(false)

const isAuthenticated = computed(() => authHelper.isAuthenticated())
const guestName = ref('')
const guestEmail = ref('')

// ── Inline racer auth gate ───────────────────────────────────────────────────
// Race entries are account-bound, so an unauthenticated rider creates a login (or
// signs in) before the purchase stepper. Once a token is set, isAuthenticated flips
// (authState is reactive) and the normal authenticated race flow takes over.
type AuthMode = 'create' | 'login'
const authMode = ref<AuthMode>('create')
const authBusy = ref(false)
const authError = ref<string | null>(null)
const authForm = reactive({
    firstName: '', lastName: '', email: '', phone: '', birthdate: '',
    emergencyContactName: '', emergencyContactPhone: '',
    password: '', confirmPassword: '',
})

function switchAuthMode(mode: AuthMode) {
    authMode.value = mode
    authError.value = null
}

async function submitAuth() {
    authError.value = null
    const email = authForm.email.trim()
    if (!email || !/\S+@\S+/.test(email)) { authError.value = 'Enter a valid email.'; return }
    if (!authForm.password) { authError.value = 'Enter your password.'; return }

    if (authMode.value === 'create') {
        if (!authForm.firstName.trim() || !authForm.lastName.trim()) {
            authError.value = 'Enter your first and last name.'; return
        }
        if (authForm.password !== authForm.confirmPassword) {
            authError.value = 'Passwords do not match.'; return
        }
        if (!authForm.birthdate || authForm.birthdate >= todayIso) {
            authError.value = 'Enter a valid birthdate.'; return
        }
        if (authForm.phone.replace(/\D/g, '').length < 7) {
            authError.value = 'Enter a valid mobile phone — we use it for waitlist and event-day alerts.'; return
        }
        if (!authForm.emergencyContactName.trim() || authForm.emergencyContactPhone.replace(/\D/g, '').length < 7) {
            authError.value = 'Enter an emergency contact name and phone.'; return
        }
    }

    authBusy.value = true
    try {
        if (authMode.value === 'create') {
            await userService.createAccount({
                firstName: authForm.firstName.trim(),
                lastName: authForm.lastName.trim(),
                email,
                phone: authForm.phone.trim(),
                birthdate: authForm.birthdate,
                emergencyContactName: authForm.emergencyContactName.trim(),
                emergencyContactPhone: authForm.emergencyContactPhone.trim(),
                password: authForm.password,
            })
        }
        // Create-or-login both finish by signing in to obtain a token.
        const resp = await userService.login({ email, password: authForm.password })
        const payload = (resp.data as any).data
        authHelper.setToken(payload.token)
        if (payload.userId) authHelper.setUserId(payload.userId)
        if (payload.role) authHelper.setRole(payload.role)
        // Now authenticated — (re)load tiers + reward vouchers; the race stepper renders.
        await load()
    } catch (err: any) {
        authError.value = err.response?.data?.error || err.response?.data?.message
            || (authMode.value === 'create' ? 'Could not create your account.' : 'Login failed.')
    } finally {
        authBusy.value = false
    }
}

const availableVouchers = ref<RiderRewardRedemption[]>([])
const selectedVoucherId = ref<string | null>(null)
const voucherOptions = computed(() => availableVouchers.value.map(v => ({
    value: v.id,
    title: `${v.programName} — ${v.rewardPercentOff === 100 ? 'Free' : v.rewardPercentOff + '% off'}`,
})))

type AdmissionStepKey = 'select' | 'racer_info' | 'extras' | 'discounts' | 'payment'
const step = ref<AdmissionStepKey>('select')
// Buy Race Entry route hard-locks kindFilter to 'race_entry' — that's our cue to
// rename step 1 and slot in the Racer Info collection step before Add-ons.
const isRaceMode = computed(() => props.kindFilter === 'race_entry')
// Gate the race flow behind inline account creation / login when signed out.
const needsRacerAuth = computed(() => isRaceMode.value && !isAuthenticated.value)
const stepperItems = computed<{ title: string; value: AdmissionStepKey }[]>(() => {
    const items: { title: string; value: AdmissionStepKey }[] = [
        { title: isRaceMode.value ? 'Select Class' : 'Select Admissions', value: 'select' },
    ]
    if (isRaceMode.value) items.push({ title: 'Racer Info', value: 'racer_info' })
    if (extrasNeeded.value) items.push({ title: 'Add-ons', value: 'extras' })
    items.push({ title: 'Discounts', value: 'discounts' })
    items.push({ title: 'Payment', value: 'payment' })
    return items
})

// Helper: where the Continue button on the Select step should go next.
function stepAfterSelect(): AdmissionStepKey {
    if (isRaceMode.value) return 'racer_info'
    if (extrasNeeded.value) return 'extras'
    return 'discounts'
}
// Helper: where the Continue button on the Racer Info step should go next.
function stepAfterRacerInfo(): AdmissionStepKey {
    if (extrasNeeded.value) return 'extras'
    return 'discounts'
}

const purchaseId = ref<string | null>(null)
const redemptionToken = ref<string | null>(null)
const clientSecret = ref<string | null>(null)
const amountCents = ref(0)
const riderServiceChargeCents = ref(0)
const giftCardAppliedCents = ref(0)
const stripeReady = ref(false)
const paying = ref(false)
const paymentError = ref<string | null>(null)
const completed = ref(false)

let stripe: any = null
let elements: any = null

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

const waiverDialog = ref(false)
const waiverDialogMessage = ref('')

// Membership gate dialog (surfaced when the backend rejects with "Membership required").
// `addMembership` flips on when the rider opts to bundle their membership into this same
// PaymentIntent — passed back into createIntent on retry.
const membershipGateOpen = ref(false)
const membershipGateMessage = ref('')
const addMembership = ref(false)

function formatMoney(cents: number): string {
    return `$${((cents ?? 0) / 100).toFixed(2)}`
}

async function addMembershipAndRetry() {
    addMembership.value = true
    membershipGateOpen.value = false
    await createIntent()
}

// ── Waitlist (Join + pre-pay) ────────────────────────────────────────────────
const waitlistDialog = ref(false)
const waitlistTier = ref<TicketTier | null>(null)
const waitlistPhone = ref<string | null>(null)
const waitlistPrepay = ref(false)
const waitlistNotes = ref('')
const waitlistJoining = ref(false)

const waitlistPayOpen = ref(false)
const waitlistPaying = ref(false)
const waitlistStripeReady = ref(false)
const waitlistPaymentError = ref<string | null>(null)
const waitlistPaymentElementId = `waitlist-pay-${Math.random().toString(36).slice(2, 10)}`
const waitlistPayInFlight = ref<{ tierName: string; amountCents: number; clientSecret: string } | null>(null)
let waitlistStripe: any = null
let waitlistElements: any = null

const waitlistPrepayCents = computed(() => {
    if (!waitlistTier.value) return 0
    const t = waitlistTier.value
    const fee = Math.floor(t.priceCents * branding.serviceChargeBps / 10000)
    const riderPortion = Math.floor(fee * t.riderPaidServiceChargeBps / 10000)
    return t.priceCents + riderPortion
})

async function openWaitlistDialog(t: TicketTier) {
    waitlistTier.value = t
    waitlistPrepay.value = false
    waitlistNotes.value = ''
    waitlistDialog.value = true
    // Pull the rider's phone fresh — they may have just updated it in another tab.
    try {
        const r = await userService.getProfile()
        const data = (r.data as any).data ?? r.data
        waitlistPhone.value = data?.phone ?? null
    } catch { waitlistPhone.value = null }
}

async function confirmJoinWaitlist() {
    if (!waitlistTier.value || !waitlistPhone.value) return
    waitlistJoining.value = true
    try {
        const r = await waitlistService.join({
            eventId: props.eventId,
            tierId: waitlistTier.value.id,
            prepay: waitlistPrepay.value,
            notes: waitlistNotes.value.trim() || null,
        })
        const data = (r.data as any).data
        if (data.clientSecret) {
            waitlistPayInFlight.value = {
                tierName: waitlistTier.value.name,
                amountCents: data.prepayAmountCents,
                clientSecret: data.clientSecret,
            }
            waitlistDialog.value = false
            waitlistPayOpen.value = true
            await nextTick()
            await mountWaitlistPayment()
        } else {
            waitlistDialog.value = false
            flash(`You're #${data.position} on the waitlist. We'll text ${data.notifyPhone} when a spot opens.`, 'success')
        }
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not join waitlist.', 'error')
    } finally {
        waitlistJoining.value = false
    }
}

async function mountWaitlistPayment() {
    if (!waitlistPayInFlight.value) return
    waitlistStripe = await getStripe(branding.stripePublishableKey)
    if (!waitlistStripe) { waitlistPaymentError.value = 'Stripe not available.'; return }
    waitlistElements = waitlistStripe.elements({ clientSecret: waitlistPayInFlight.value.clientSecret })
    const pe = waitlistElements.create('payment')
    pe.mount(`#${waitlistPaymentElementId}`)
    waitlistStripeReady.value = true
}

async function payWaitlist() {
    if (!waitlistStripe || !waitlistElements) return
    waitlistPaying.value = true
    waitlistPaymentError.value = null
    try {
        const { error } = await waitlistStripe.confirmPayment({
            elements: waitlistElements,
            confirmParams: { return_url: window.location.origin + '/User/MyPasses' },
            redirect: 'if_required',
        })
        if (error) {
            waitlistPaymentError.value = error.message || 'Payment failed.'
        } else {
            waitlistPayOpen.value = false
            flash('Pre-paid! You\'re locked in for the next available spot.', 'success')
        }
    } catch (err: any) {
        waitlistPaymentError.value = err?.message || 'Payment failed.'
    } finally {
        waitlistPaying.value = false
    }
}

function goToWaiver() {
    waiverDialog.value = false
    // After signing they come back to the page they were on (e.g. home with the dialog).
    router.push({ path: '/Waiver', query: { next: router.currentRoute.value.fullPath } })
}

function redeemUrl(token: string): string {
    return `${window.location.protocol}//${window.location.host}/redeem/${token}`
}

function displayAmount() { return (amountCents.value / 100).toFixed(2) }

function isSoldOut(t: TicketTier): boolean {
    return t.inventory !== null && (t.sold ?? 0) >= t.inventory
}

// "Includes 4 coupons (20% off tickets)" — only when the tier carries a bundle.
function bundledLabel(t: TicketTier): string | null {
    if (!t.bundledCouponCount || t.bundledCouponCount <= 0) return null
    const value = t.bundledCouponDiscountValue ?? 0
    const discountStr = t.bundledCouponDiscountKind === 'percent'
        ? `${Math.round(value / 100)}% off`
        : `$${(value / 100).toFixed(2)} off`
    const scopeStr = t.bundledCouponScope === 'event_ticket' ? ' tickets'
        : t.bundledCouponScope === 'pass' ? ' passes'
        : t.bundledCouponScope === 'season_pass' ? ' season passes'
        : ''
    return `Includes ${t.bundledCouponCount} coupon${t.bundledCouponCount === 1 ? '' : 's'} — ${discountStr}${scopeStr}`
}

// Step-1 gate: cart non-empty. In race mode, guest contact info moves to Racer Info,
// so the Select step only needs at least one item picked. In other modes guest info
// still lives on Select and gates advancement here.
const canAdvanceFromSelect = computed(() => {
    if (cartUnits.value === 0) return false
    if (isRaceMode.value) return true
    if (!isAuthenticated.value) {
        return guestEmail.value.trim().length > 0 && guestName.value.trim().length > 0
    }
    return true
})

// Race-mode Racer Info step gate: guest contact info must be present.
const canAdvanceFromRacerInfo = computed(() => {
    if (cartUnits.value === 0) return false
    if (!isAuthenticated.value) {
        return guestEmail.value.trim().length > 0 && guestName.value.trim().length > 0
    }
    if (profileLoading.value) return false
    if (!profileForm.firstName.trim() || !profileForm.lastName.trim()) return false
    // If the event needs a waiver and the rider hasn't signed, force them to do
    // it inline before the Continue button enables. Birthdate is a hard prereq —
    // we need it to know whether a parent has to sign instead of the rider.
    if (raceWaiverNeedsSigning.value) {
        if (raceWaiverLoading.value) return false
        if (!profileForm.birthdate) return false
        if (!waiverAcknowledged.value || !waiverSignatureDataUrl.value) return false
        if (raceRiderIsMinor.value) {
            if (!waiverParentName.value.trim()) return false
            if (waiverParentPhone.value.replace(/\D/g, '').length < 7) return false
        }
    }
    return true
})

// Racer Info: signed-in users see their full profile inline so they can fix anything
// before paying. Loaded the first time they hit the step. Three separate save endpoints
// (profile metadata, phone, emergency contact) get called only when their slice is dirty.
const profileLoading = ref(false)
const profileLoaded = ref(false)
const savingProfile = ref(false)
const profileSaveError = ref<string | null>(null)
const profileForm = reactive({
    firstName: '',
    lastName: '',
    email: '',
    phone: '',
    birthdate: '',                  // ISO 'YYYY-MM-DD' format for the date input
    emergencyContactName: '',
    emergencyContactPhone: '',
    addressLine: '',
    addressLine2: '',
    city: '',
    state: '',
    postalCode: '',
    country: 'US',
    bike: '',
    raceNumber: '',
})
const profileOriginal = reactive({ ...profileForm })

const profileDirty = computed(() =>
    profileForm.firstName !== profileOriginal.firstName
    || profileForm.lastName !== profileOriginal.lastName
    || profileForm.phone !== profileOriginal.phone
    || profileForm.birthdate !== profileOriginal.birthdate
    || profileForm.emergencyContactName !== profileOriginal.emergencyContactName
    || profileForm.emergencyContactPhone !== profileOriginal.emergencyContactPhone
    || profileForm.addressLine !== profileOriginal.addressLine
    || profileForm.addressLine2 !== profileOriginal.addressLine2
    || profileForm.city !== profileOriginal.city
    || profileForm.state !== profileOriginal.state
    || profileForm.postalCode !== profileOriginal.postalCode
    || profileForm.country !== profileOriginal.country
    || profileForm.bike !== profileOriginal.bike
    || profileForm.raceNumber !== profileOriginal.raceNumber)

// US state list for the State dropdown. 50 states + DC + common territories.
const US_STATES: { value: string; title: string }[] = [
    { value: 'AL', title: 'Alabama' }, { value: 'AK', title: 'Alaska' },
    { value: 'AZ', title: 'Arizona' }, { value: 'AR', title: 'Arkansas' },
    { value: 'CA', title: 'California' }, { value: 'CO', title: 'Colorado' },
    { value: 'CT', title: 'Connecticut' }, { value: 'DE', title: 'Delaware' },
    { value: 'DC', title: 'District of Columbia' }, { value: 'FL', title: 'Florida' },
    { value: 'GA', title: 'Georgia' }, { value: 'HI', title: 'Hawaii' },
    { value: 'ID', title: 'Idaho' }, { value: 'IL', title: 'Illinois' },
    { value: 'IN', title: 'Indiana' }, { value: 'IA', title: 'Iowa' },
    { value: 'KS', title: 'Kansas' }, { value: 'KY', title: 'Kentucky' },
    { value: 'LA', title: 'Louisiana' }, { value: 'ME', title: 'Maine' },
    { value: 'MD', title: 'Maryland' }, { value: 'MA', title: 'Massachusetts' },
    { value: 'MI', title: 'Michigan' }, { value: 'MN', title: 'Minnesota' },
    { value: 'MS', title: 'Mississippi' }, { value: 'MO', title: 'Missouri' },
    { value: 'MT', title: 'Montana' }, { value: 'NE', title: 'Nebraska' },
    { value: 'NV', title: 'Nevada' }, { value: 'NH', title: 'New Hampshire' },
    { value: 'NJ', title: 'New Jersey' }, { value: 'NM', title: 'New Mexico' },
    { value: 'NY', title: 'New York' }, { value: 'NC', title: 'North Carolina' },
    { value: 'ND', title: 'North Dakota' }, { value: 'OH', title: 'Ohio' },
    { value: 'OK', title: 'Oklahoma' }, { value: 'OR', title: 'Oregon' },
    { value: 'PA', title: 'Pennsylvania' }, { value: 'RI', title: 'Rhode Island' },
    { value: 'SC', title: 'South Carolina' }, { value: 'SD', title: 'South Dakota' },
    { value: 'TN', title: 'Tennessee' }, { value: 'TX', title: 'Texas' },
    { value: 'UT', title: 'Utah' }, { value: 'VT', title: 'Vermont' },
    { value: 'VA', title: 'Virginia' }, { value: 'WA', title: 'Washington' },
    { value: 'WV', title: 'West Virginia' }, { value: 'WI', title: 'Wisconsin' },
    { value: 'WY', title: 'Wyoming' },
    { value: 'PR', title: 'Puerto Rico' },
]

// Locally-computed minor flag from the form's birthdate (so the Racer Info UI
// reacts immediately when the rider enters or fixes their DOB). The server
// independently re-derives this when the waiver gets signed, so this is just
// presentation. Returns false when the date is unparseable / empty.
const localRiderIsMinor = computed(() => {
    if (!profileForm.birthdate) return false
    const dob = new Date(profileForm.birthdate)
    if (isNaN(dob.getTime())) return false
    const eighteenYearsAgo = new Date()
    eighteenYearsAgo.setFullYear(eighteenYearsAgo.getFullYear() - 18)
    return dob > eighteenYearsAgo
})
const todayIso = new Date().toISOString().slice(0, 10)

async function loadProfile() {
    if (!isAuthenticated.value || profileLoaded.value || profileLoading.value) return
    profileLoading.value = true
    try {
        const r = await userService.getProfile()
        const data = ((r.data as any).data ?? r.data) as any
        const next = {
            firstName: data?.firstName ?? '',
            lastName: data?.lastName ?? '',
            email: data?.email ?? '',
            phone: data?.phone ?? '',
            // Server returns ISO timestamp; the date input wants YYYY-MM-DD only.
            birthdate: data?.birthdate ? String(data.birthdate).slice(0, 10) : '',
            emergencyContactName: data?.emergencyContactName ?? '',
            emergencyContactPhone: data?.emergencyContactPhone ?? '',
            addressLine: data?.addressLine ?? '',
            addressLine2: data?.addressLine2 ?? '',
            city: data?.city ?? '',
            state: data?.state ?? '',
            postalCode: data?.postalCode ?? '',
            country: data?.country ?? 'US',
            bike: data?.bike ?? '',
            raceNumber: data?.raceNumber ?? '',
        }
        Object.assign(profileForm, next)
        Object.assign(profileOriginal, next)
        profileLoaded.value = true
    } catch {
        // Non-fatal — the step still works with the existing values.
    } finally {
        profileLoading.value = false
    }
}

async function saveProfileChanges(): Promise<boolean> {
    if (!profileDirty.value) return true
    savingProfile.value = true
    profileSaveError.value = null
    try {
        const tasks: Promise<unknown>[] = []
        // Name change → updateProfile (which also wants email/phone in its body).
        if (profileForm.firstName !== profileOriginal.firstName
            || profileForm.lastName !== profileOriginal.lastName) {
            tasks.push(userService.updateProfile({
                firstName: profileForm.firstName.trim(),
                lastName: profileForm.lastName.trim(),
                email: profileForm.email,
                phone: profileForm.phone.trim(),
            }))
        }
        if (profileForm.phone !== profileOriginal.phone) {
            tasks.push(userService.updatePhone({ phone: profileForm.phone.trim() }))
        }
        if (profileForm.emergencyContactName !== profileOriginal.emergencyContactName
            || profileForm.emergencyContactPhone !== profileOriginal.emergencyContactPhone) {
            tasks.push(userService.updateEmergencyContact({
                name: profileForm.emergencyContactName.trim(),
                phone: profileForm.emergencyContactPhone.trim(),
            }))
        }
        if (profileForm.bike !== profileOriginal.bike
            || profileForm.raceNumber !== profileOriginal.raceNumber) {
            tasks.push(userService.updateRacerInfo({
                bike: profileForm.bike.trim() || null,
                raceNumber: profileForm.raceNumber.trim() || null,
            }))
        }
        if (profileForm.birthdate !== profileOriginal.birthdate && profileForm.birthdate) {
            tasks.push(userService.updateBirthdate({ birthdate: profileForm.birthdate }))
        }
        if (profileForm.addressLine !== profileOriginal.addressLine
            || profileForm.addressLine2 !== profileOriginal.addressLine2
            || profileForm.city !== profileOriginal.city
            || profileForm.state !== profileOriginal.state
            || profileForm.postalCode !== profileOriginal.postalCode
            || profileForm.country !== profileOriginal.country) {
            tasks.push(userService.updateAddress({
                addressLine: profileForm.addressLine.trim() || null,
                addressLine2: profileForm.addressLine2.trim() || null,
                city: profileForm.city.trim() || null,
                state: profileForm.state.trim() || null,
                postalCode: profileForm.postalCode.trim() || null,
                country: profileForm.country.trim() || 'US',
            }))
        }
        await Promise.all(tasks)
        // Birthdate change can flip server-computed minor status. Refetch the
        // waiver signature so the inline waiver UI knows whether to require
        // parent fields after the save lands.
        if (profileForm.birthdate !== profileOriginal.birthdate && raceWaiver.value) {
            try {
                const sig = await waiverService.getMySignatureFor(raceWaiver.value.id)
                raceWaiverSignature.value = (sig.data as any).data
            } catch { /* non-fatal */ }
        }
        Object.assign(profileOriginal, profileForm)
        return true
    } catch (err: any) {
        profileSaveError.value = err.response?.data?.error || 'Could not save your changes.'
        return false
    } finally {
        savingProfile.value = false
    }
}

async function continueFromRacerInfo() {
    const ok = await saveProfileChanges()
    if (!ok) return
    if (raceWaiverNeedsSigning.value) {
        const signed = await signWaiverIfNeeded()
        if (!signed) return
    }
    step.value = stepAfterRacerInfo()
}

// ── Inline waiver gating for the Racer Info step ─────────────────────────────
// The event may pin a specific waiver via event.waiverId. When it doesn't, we
// fall back to the tenant's default active waiver (still required if event.requiresWaiver).
const raceWaiver = ref<WaiverDto | null>(null)
const raceWaiverLoading = ref(false)
const raceWaiverSignature = ref<WaiverSignatureStatus | null>(null)
// Prefer the locally-entered birthdate so the parent fields appear immediately
// when the rider fixes their DOB. Falls back to the server-stamped flag when
// the form hasn't loaded a birthdate yet.
const raceRiderIsMinor = computed(() => {
    if (profileForm.birthdate) return localRiderIsMinor.value
    return raceWaiverSignature.value?.riderIsMinor ?? false
})
const raceWaiverAlreadySigned = computed(() => raceWaiverSignature.value?.hasSignedCurrent ?? false)
const raceWaiverNeedsSigning = computed(() => {
    if (!isRaceMode.value || !isAuthenticated.value) return false
    if (raceWaiverLoading.value) return false
    if (!raceWaiver.value) return false
    return !raceWaiverAlreadySigned.value
})

const waiverAcknowledged = ref(false)
const waiverSignatureDataUrl = ref<string | null>(null)
const waiverParentName = ref('')
const waiverParentPhone = ref('')
const waiverSigning = ref(false)
const waiverSignError = ref<string | null>(null)

async function loadEventWaiver() {
    if (!isRaceMode.value || !isAuthenticated.value) return
    if (!props.event?.requiresRiderWaiver) {
        raceWaiver.value = null
        raceWaiverSignature.value = null
        return
    }
    raceWaiverLoading.value = true
    try {
        // Race-mode buy → use the racer waiver. Falls through to the tenant default
        // active waiver when the event doesn't pin one. Spectator waiver lives on
        // the same event row but is resolved by spectator buy paths.
        const wId = props.event?.racerWaiverId
        const r = wId
            ? await waiverService.getById(wId)
            : await waiverService.getActive()
        raceWaiver.value = (r.data as any).data
        if (raceWaiver.value) {
            const sig = await waiverService.getMySignatureFor(raceWaiver.value.id)
            raceWaiverSignature.value = (sig.data as any).data
        }
    } catch {
        raceWaiver.value = null
        raceWaiverSignature.value = null
    } finally {
        raceWaiverLoading.value = false
    }
}

async function signWaiverIfNeeded(): Promise<boolean> {
    if (!raceWaiverNeedsSigning.value || !raceWaiver.value) return true
    waiverSignError.value = null
    if (!profileForm.birthdate) {
        waiverSignError.value = 'Please enter your birthdate before signing the waiver.'
        return false
    }
    if (!waiverAcknowledged.value) {
        waiverSignError.value = 'Please acknowledge the waiver before continuing.'
        return false
    }
    if (!waiverSignatureDataUrl.value) {
        waiverSignError.value = 'A handwritten signature is required.'
        return false
    }
    if (raceRiderIsMinor.value) {
        if (!waiverParentName.value.trim()) {
            waiverSignError.value = 'Parent / guardian name is required for riders under 18.'
            return false
        }
        if (waiverParentPhone.value.replace(/\D/g, '').length < 7) {
            waiverSignError.value = 'A valid parent / guardian phone is required.'
            return false
        }
    }
    waiverSigning.value = true
    try {
        const r = await waiverService.sign(raceWaiver.value.id, {
            signatureDataUrl: waiverSignatureDataUrl.value,
            parentName: raceRiderIsMinor.value ? waiverParentName.value.trim() : null,
            parentPhone: raceRiderIsMinor.value ? waiverParentPhone.value.trim() : null,
        })
        raceWaiverSignature.value = (r.data as any).data
        return true
    } catch (err: any) {
        waiverSignError.value = err.response?.data?.error || 'Could not record signature.'
        return false
    } finally {
        waiverSigning.value = false
    }
}

async function load() {
    loading.value = true
    try {
        const r = await service.listActiveTiers(props.eventId)
        allTiers.value = (r.data as any).data
        // Reset cart whenever we (re)load — covers both first mount and event-switch.
        for (const k of Object.keys(quantities)) delete quantities[k]
        extraSelections.value = []
        for (const t of allTiers.value) quantities[t.id] = 0
        if (isAuthenticated.value) {
            try {
                const v = await rewardService.listMyRedemptions()
                availableVouchers.value = ((v.data as any).data as RiderRewardRedemption[]).filter(x => !x.redeemedAtUtc)
            } catch { /* no rewards yet */ }
        }
    } finally {
        loading.value = false
    }
}

onMounted(load)
// When the parent swaps eventId (dialog reused for a different event), reload.
watch(() => props.eventId, () => {
    step.value = 'select'
    completed.value = false
    purchasedTickets.value = []
    profileLoaded.value = false
    addMembership.value = false
    load()
})

// Lazy-load the rider's profile + event waiver the first time they hit Racer Info.
watch(step, (next) => {
    if (next === 'racer_info' && isAuthenticated.value) {
        if (!profileLoaded.value) loadProfile()
        if (!raceWaiver.value && !raceWaiverLoading.value) loadEventWaiver()
    }
})

async function createIntent() {
    if (cartUnits.value === 0) return
    try {
        creating.value = true
        const items = tiers.value
            .filter(t => (quantities[t.id] ?? 0) > 0)
            .map(t => ({ tierId: t.id, quantity: quantities[t.id] }))
        const extras = extraSelections.value
            .filter(s => s.quantity > 0)
            .map(s => ({ productId: s.productId, quantity: s.quantity, variantId: s.variantId ?? null }))
        const req: {
            items: { tierId: string; quantity: number }[]
            email?: string | null
            name?: string | null
            rewardRedemptionId?: string | null
            couponCode?: string | null
            giftCardCode?: string | null
            extras?: Array<{ productId: string; quantity: number }> | null
            addMembership?: boolean
        } = {
            items,
            rewardRedemptionId: selectedVoucherId.value,
            couponCode: couponCode.value.trim().length > 0 ? couponCode.value.trim() : null,
            giftCardCode: giftCardCode.value.trim().length > 0 ? giftCardCode.value.trim() : null,
            extras: extras.length > 0 ? extras : null,
            addMembership: addMembership.value || undefined,
        }
        if (!isAuthenticated.value) {
            req.email = guestEmail.value.trim()
            req.name = guestName.value.trim()
        }
        const r = await service.createTicketPurchase(req)
        const data = (r.data as any).data
        purchaseId.value = data.purchaseId
        redemptionToken.value = data.redemptionToken
        purchasedTickets.value = data.tickets || []
        clientSecret.value = data.clientSecret
        amountCents.value = data.amountCents
        riderServiceChargeCents.value = data.riderServiceChargeCents ?? 0
        giftCardAppliedCents.value = data.giftCardAppliedCents ?? 0

        if (!clientSecret.value && amountCents.value === 0) {
            completed.value = true
            const msg = giftCardAppliedCents.value > 0
                ? 'Gift card covered the cart — your tickets are ready!'
                : 'Voucher applied — your free ticket is ready!'
            flash(msg, 'success')
            emit('completed')
            return
        }

        step.value = 'payment'
        await nextTick()
        await mountPaymentElement()
    } catch (err: any) {
        const message = err.response?.data?.error as string | undefined
        // Waiver-required → modal. Membership-required → modal. Coupon/gift errors → inline. Else toast.
        if (message && /membership/i.test(message)) {
            membershipGateMessage.value = message
            membershipGateOpen.value = true
        } else if (message && /waiver/i.test(message)) {
            waiverDialogMessage.value = message
            waiverDialog.value = true
        } else if (message && /coupon/i.test(message)) {
            couponError.value = message
        } else if (message && /gift card/i.test(message)) {
            giftCardError.value = message
        } else {
            flash(message || 'Failed to start payment.', 'error')
        }
    } finally {
        creating.value = false
    }
}

async function mountPaymentElement() {
    if (!clientSecret.value) return
    stripe = await getStripe(branding.stripePublishableKey)
    if (!stripe) { paymentError.value = 'Stripe not available.'; return }
    elements = stripe.elements({ clientSecret: clientSecret.value })
    const pe = elements.create('payment')
    pe.mount(`#${paymentElementId}`)
    stripeReady.value = true
}

async function pay() {
    if (!stripe || !elements) return
    paying.value = true
    paymentError.value = null
    try {
        const returnUrl = isAuthenticated.value
            ? window.location.origin + '/User/MyPasses'
            : window.location.href
        const { error } = await stripe.confirmPayment({
            elements,
            confirmParams: { return_url: returnUrl },
            redirect: 'if_required',
        })
        if (error) {
            paymentError.value = error.message || 'Payment failed.'
        } else {
            completed.value = true
            emit('completed')
        }
    } catch (err: any) {
        paymentError.value = err?.message || 'Payment failed.'
    } finally {
        paying.value = false
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>

<style scoped>
.tier-row {
    display: flex;
    align-items: center;
    padding: 8px 0;
    border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
    gap: 12px;
}
.tier-row:last-child { border-bottom: none; }
.tier-row-info { flex: 1; min-width: 0; }
.qty-display {
    min-width: 28px;
    text-align: center;
    font-weight: 600;
}
.racer-waiver-body {
    max-height: 280px;
    overflow-y: auto;
    background: rgba(0, 0, 0, 0.03);
}
.qr-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
    gap: 16px;
    justify-items: center;
}
.qr-cell {
    display: flex;
    flex-direction: column;
    align-items: center;
}
.auth-toggle-link {
    color: rgb(var(--v-theme-primary));
    font-weight: 600;
    cursor: pointer;
}
.auth-toggle-link:hover { text-decoration: underline; }
</style>
