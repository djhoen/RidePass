<template>
    <v-container style="max-width: 880px">
        <div class="rp-pagehead">
            <div>
                <div class="rp-pagehead-title">Counter Sale</div>
                <div class="rp-pagehead-sub">For walk-ins without a device. Look up the customer, build their cart, capture waiver and payment.</div>
            </div>
        </div>

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
                            style="max-width: 560px; border: 1px solid var(--rp-line2); border-radius: 10px">
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
                            <v-alert v-if="kioskChild" type="info" variant="tonal" density="compact" class="mt-3">
                                From the kiosk: <strong>{{ kioskChild.name }}</strong> (under 18), with
                                {{ customer.firstName }} as parent or guardian. Add their day pass with
                                "For {{ kioskChild.firstName }}" under Day passes.
                            </v-alert>
                        </div>
                        <KioskSignupsPanel v-if="!customer" selectable class="mt-4" @select="selectKioskSignup" />
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

                            <!-- Day passes / lift tickets (gate-fee tiers), for the customer or another rider -->
                            <v-expansion-panel value="daypass">
                                <v-expansion-panel-title>
                                    <div class="d-flex align-center ga-2">
                                        <v-icon>mdi-ticket-confirmation</v-icon>
                                        <span>Day passes</span>
                                        <v-chip v-if="dayPassCount > 0" size="x-small" color="primary" class="ml-1">
                                            {{ dayPassCount }}
                                        </v-chip>
                                    </div>
                                </v-expansion-panel-title>
                                <v-expansion-panel-text>
                                    <div v-if="loadingDayEvents" class="text-center py-4">
                                        <v-progress-circular indeterminate></v-progress-circular>
                                    </div>
                                    <div v-else-if="dayEvents.length === 0" class="text-medium-emphasis">
                                        No riding days in the next two weeks. Schedule one on
                                        <router-link to="/Admin/Events">Manage Events</router-link>.
                                    </div>
                                    <template v-else>
                                        <v-select :model-value="selectedDayEventId"
                                            @update:model-value="onDayEventSelected"
                                            :items="dayEventOptions" item-title="title" item-value="value"
                                            label="Riding day" density="compact" hide-details></v-select>
                                        <div v-if="loadingDayTiers" class="text-center py-4">
                                            <v-progress-circular indeterminate size="24"></v-progress-circular>
                                        </div>
                                        <template v-else-if="selectedDayEventId">
                                            <div v-if="dayTiers.length === 0" class="text-caption text-medium-emphasis mt-3">
                                                This day has no gate tickets set up.
                                            </div>
                                            <div v-for="t in dayTiers" :key="t.id" class="d-flex align-center flex-wrap ga-2 py-1 mt-1">
                                                <div class="flex-grow-1" style="min-width: 160px">
                                                    {{ t.name }}
                                                    <span class="text-medium-emphasis">— ${{ (t.priceCents / 100).toFixed(2) }}</span>
                                                    <v-chip v-if="t.audience === 'spectator'" size="x-small" variant="tonal" class="ml-1">Spectator</v-chip>
                                                </div>
                                                <v-btn size="small" variant="tonal" color="primary" prepend-icon="mdi-plus"
                                                    @click="addDayPass(t)">For {{ customer?.firstName || 'customer' }}</v-btn>
                                                <v-btn v-if="kioskChild" size="small" color="primary" variant="tonal" prepend-icon="mdi-tablet-cellphone"
                                                    :loading="kioskChildBusy" @click="addKioskChild(t)">For {{ kioskChild.firstName }}</v-btn>
                                                <v-btn size="small" variant="outlined" prepend-icon="mdi-account-plus"
                                                    @click="openOtherRider(t)">Another rider</v-btn>
                                            </div>
                                            <p class="text-caption text-medium-emphasis mt-2">
                                                “Another rider” is a friend or family member on this customer’s payment. Each
                                                rider gets their own profile and signs their own waiver (a parent or guardian
                                                for anyone under 18).
                                            </p>
                                        </template>
                                    </template>
                                </v-expansion-panel-text>
                            </v-expansion-panel>

                            <!-- Lesson + optional bike -->
                            <v-expansion-panel value="lesson">
                                <v-expansion-panel-title>
                                    <div class="d-flex align-center ga-2">
                                        <v-icon>mdi-whistle</v-icon>
                                        <span>Lesson</span>
                                        <v-chip v-if="lessonCount > 0"
                                            size="x-small" color="primary" class="ml-1">
                                            {{ lessonCount }}
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
                             style="border: 1px dashed var(--rp-line); border-radius: 6px">
                            Cart is empty.
                        </div>
                        <div v-else class="cart-summary">
                            <div v-for="(c, i) in cart" :key="i" class="cart-line d-flex align-center py-2 ga-2">
                                <div class="flex-grow-1" style="min-width: 0">
                                    <div class="text-body-2">
                                        <strong>{{ c.displayName }}</strong>
                                        <span class="text-medium-emphasis"> × {{ c.quantity }}</span>
                                    </div>
                                    <div v-if="c.other" class="text-caption text-medium-emphasis">
                                        <v-icon size="14">mdi-account-arrow-right</v-icon>
                                        For {{ c.other.name }}{{ c.other.isMinor ? ` (under 18, ${c.other.guardianName ?? 'guardian'} signs)` : '' }}
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
                        <v-alert v-if="otherRiderLines.length > 0" type="info" variant="tonal" density="compact" class="mb-3">
                            <strong>{{ otherRiderLines.length }} other rider{{ otherRiderLines.length === 1 ? '' : 's' }}</strong>
                            ({{ otherRiderLines.map(l => l.other!.name).join(', ') }}):
                            {{ otherRiderLines.length === 1 ? 'their waiver is' : 'each waiver is' }} signed after payment
                            (by a parent or guardian for anyone under 18). Send a link, or sign on this device on the
                            receipt screen. The gate won’t admit them until it’s signed.
                            <span v-if="!payerHasOwnLines">{{ customer?.firstName }} has no ticket of their own in this cart, so there is nothing to sign here.</span>
                        </v-alert>
                        <div v-if="!activeWaiver" class="text-medium-emphasis">
                            No active waiver configured for this tenant — proceed to payment.
                        </div>
                        <div v-else-if="!cartRequiresWaiver" class="text-medium-emphasis">
                            {{ otherRiderLines.length > 0 ? 'Nothing else in this cart needs a signature now' : 'None of the items in this cart require a waiver' }} — proceed to payment.
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
                                    style="max-width: 100%; border: 1px solid var(--rp-line); border-radius: 10px; padding: 6px; background: #fff" />
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

                        <!-- Other riders on this payment: each signs their own waiver (a guardian for a minor). -->
                        <v-card v-if="receiptRiderLines.length > 0" variant="outlined" class="pa-3 mb-4">
                            <div class="d-flex align-center mb-2">
                                <div class="text-subtitle-2">Other riders’ waivers</div>
                                <v-spacer></v-spacer>
                                <v-btn size="small" variant="text" prepend-icon="mdi-refresh" :loading="refreshingWaivers"
                                    @click="refreshRiderWaivers">Refresh</v-btn>
                            </div>
                            <p class="text-caption text-medium-emphasis mb-2">
                                The sale is complete; the gate won’t admit a rider until their waiver is signed. This
                                updates by itself while the screen is open.
                            </p>
                            <div v-for="li in receiptRiderLines" :key="li.purchaseId" class="rider-waiver-line py-2">
                                <div class="d-flex align-center flex-wrap ga-2">
                                    <div class="flex-grow-1" style="min-width: 160px">
                                        <strong>{{ li.riderName }}</strong>
                                        <span class="text-caption text-medium-emphasis">
                                            · {{ li.riderIsMinor ? 'under 18: parent or guardian signs' : 'signs for themselves' }}
                                        </span>
                                    </div>
                                    <v-chip size="small" :color="waiverChip(li.purchaseId).color" variant="tonal">
                                        {{ waiverChip(li.purchaseId).label }}
                                    </v-chip>
                                </div>
                                <div v-if="riderStatus[li.purchaseId]?.status === 'signed'" class="text-caption text-success mt-1">
                                    Signed{{ riderStatus[li.purchaseId]?.signedByGuardian && riderStatus[li.purchaseId]?.guardianName
                                        ? ` by ${riderStatus[li.purchaseId]?.guardianName} (guardian)`
                                        : riderStatus[li.purchaseId]?.signerName ? ` by ${riderStatus[li.purchaseId]?.signerName}` : '' }}.
                                </div>
                                <template v-else-if="riderStatus[li.purchaseId]?.status !== 'not_required'">
                                    <div class="d-flex flex-wrap ga-2 mt-2">
                                        <v-btn size="small" color="primary" variant="tonal" prepend-icon="mdi-send"
                                            :loading="linkBusy[li.purchaseId] === 'send'" @click="sendRiderLink(li, true)">
                                            {{ linkDelivered[li.purchaseId] ? 'Send again' : 'Send link' }}
                                        </v-btn>
                                        <v-btn size="small" variant="outlined" prepend-icon="mdi-draw"
                                            :loading="linkBusy[li.purchaseId] === 'device'" @click="signOnDevice(li)">
                                            Sign on this device
                                        </v-btn>
                                        <v-btn size="small" variant="text" prepend-icon="mdi-tablet-cellphone"
                                            @click="sendRiderToKiosk(li)">Send to kiosk</v-btn>
                                    </div>
                                    <div v-if="riderLinks[li.purchaseId]" class="d-flex align-start ga-3 mt-2">
                                        <QrCode v-if="riderLinks[li.purchaseId]!.link" :value="riderLinks[li.purchaseId]!.link!" :size="96" />
                                        <div class="text-caption" style="min-width: 0">
                                            <div>
                                                {{ riderLinks[li.purchaseId]!.signAs === 'guardian' ? 'Guardian link for' : 'Link for' }}
                                                <strong>{{ riderLinks[li.purchaseId]!.recipientName }}</strong>
                                                &lt;{{ riderLinks[li.purchaseId]!.recipientEmail }}&gt;
                                            </div>
                                            <div v-if="riderLinks[li.purchaseId]!.emailSent">Emailed.</div>
                                            <div v-else-if="linkDelivered[li.purchaseId]" class="text-warning">
                                                Email isn’t set up or didn’t send: have them scan the QR.
                                            </div>
                                            <div v-if="riderLinks[li.purchaseId]!.smsSent">Texted to {{ riderLinks[li.purchaseId]!.recipientPhone }}.</div>
                                            <div v-else-if="!riderLinks[li.purchaseId]!.smsAvailable" class="text-medium-emphasis">
                                                Texting isn’t set up for this venue yet: they can scan the QR on their phone.
                                            </div>
                                            <div v-else-if="linkDelivered[li.purchaseId] && !riderLinks[li.purchaseId]!.recipientPhone" class="text-medium-emphasis">
                                                No phone on file to text.
                                            </div>
                                            <div class="text-medium-emphasis" style="word-break: break-all">{{ riderLinks[li.purchaseId]!.link }}</div>
                                        </div>
                                    </div>
                                </template>
                            </div>
                        </v-card>

                        <div v-for="li in lineItems" :key="li.purchaseId" class="mb-4 d-flex align-center ga-3">
                            <QrCode v-if="hasQr(li)" :value="redeemUrl(li.redemptionToken)" :size="120" />
                            <div v-else class="d-flex align-center justify-center"
                                style="width: 120px; height: 120px; background: var(--rp-panel2); border-radius: 10px">
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

        <!-- Another rider: a friend or family member on this customer's payment. -->
        <v-dialog v-model="otherDialog" max-width="560" scrollable>
            <v-card>
                <v-card-title>Another rider</v-card-title>
                <v-card-subtitle v-if="otherTier">{{ otherTier.name }} — ${{ (otherTier.priceCents / 100).toFixed(2) }}</v-card-subtitle>
                <v-card-text>
                    <p class="text-caption text-medium-emphasis mb-3">
                        Nothing is saved until the sale: new profiles (and a guardian's) are created when you take payment.
                    </p>
                    <div v-if="payerDependents.length > 0" class="mb-3">
                        <div class="text-caption text-medium-emphasis mb-1">{{ customer?.firstName }}’s kids on file</div>
                        <div class="d-flex flex-wrap ga-2">
                            <v-chip v-for="d in payerDependents" :key="d.id" size="small"
                                :color="otherForm.existingDependentId === d.id ? 'primary' : undefined"
                                @click="pickDependent(d)">{{ d.firstName }} {{ d.lastName }}</v-chip>
                        </div>
                    </div>

                    <div class="d-flex ga-2 align-end mb-1">
                        <v-text-field v-model="otherSearch" label="Find an existing profile (name, email, phone)"
                            density="compact" hide-details @keyup.enter="searchOtherRider"></v-text-field>
                        <v-btn :loading="otherSearching" @click="searchOtherRider">Find</v-btn>
                    </div>
                    <v-list v-if="otherCandidates.length > 0" density="compact" class="mb-2"
                        style="border: 1px solid var(--rp-line2); border-radius: 10px">
                        <v-list-item v-for="c in otherCandidates" :key="c.id" @click="pickOtherCandidate(c)"
                            :active="otherForm.existingUserId === c.id">
                            <v-list-item-title>{{ c.firstName }} {{ c.lastName }}</v-list-item-title>
                            <v-list-item-subtitle>{{ c.email }}<span v-if="c.phone"> · {{ c.phone }}</span></v-list-item-subtitle>
                        </v-list-item>
                    </v-list>

                    <v-alert v-if="otherPickedLabel" type="success" variant="tonal" density="compact" class="mb-3">
                        {{ otherPickedLabel }}
                        <v-btn size="x-small" variant="text" class="ml-1" @click="clearOtherPick">Change</v-btn>
                    </v-alert>

                    <template v-if="!otherForm.existingUserId && !otherForm.existingDependentId">
                        <div class="text-caption text-medium-emphasis mt-2 mb-1">Or a new rider</div>
                        <v-row dense>
                            <v-col cols="12" sm="6"><v-text-field v-model="otherForm.firstName" label="First name" density="compact"></v-text-field></v-col>
                            <v-col cols="12" sm="6"><v-text-field v-model="otherForm.lastName" label="Last name" density="compact"></v-text-field></v-col>
                        </v-row>
                    </template>
                    <v-text-field v-if="!otherForm.existingDependentId" v-model="otherForm.birthdate" type="date" :max="todayIso"
                        :label="otherForm.existingUserId ? 'Birthdate (if not on their profile)' : 'Birthdate'"
                        density="compact"></v-text-field>
                    <template v-if="!otherForm.existingUserId && !otherForm.existingDependentId">
                        <v-row dense>
                            <v-col cols="12" sm="6">
                                <v-text-field v-model="otherForm.email" type="email" density="compact"
                                    :label="otherIsMinor ? 'Rider email (optional)' : 'Rider email'"></v-text-field>
                            </v-col>
                            <v-col cols="12" sm="6">
                                <PhoneField v-model="otherForm.phone" label="Rider phone (optional)" density="compact" />
                            </v-col>
                        </v-row>
                    </template>

                    <template v-if="otherNeedsGuardian">
                        <v-divider class="my-2"></v-divider>
                        <div class="text-subtitle-2 mb-1">Parent or guardian</div>
                        <p class="text-caption text-medium-emphasis mb-2">
                            Under 18: a parent or guardian signs. Their profile is created from these details,
                            or their existing account is used (matched by email, no login needed).
                        </p>
                        <v-radio-group v-model="otherForm.guardianMode" inline hide-details density="compact" class="mb-2">
                            <v-radio :label="`${customer?.firstName ?? 'The customer'} is the parent / guardian`" value="payer"></v-radio>
                            <v-radio label="Someone else" value="other"></v-radio>
                        </v-radio-group>
                        <template v-if="otherForm.guardianMode === 'other'">
                            <v-row dense>
                                <v-col cols="12" sm="6"><v-text-field v-model="otherForm.guardianFirstName" label="Guardian first name" density="compact"></v-text-field></v-col>
                                <v-col cols="12" sm="6"><v-text-field v-model="otherForm.guardianLastName" label="Guardian last name" density="compact"></v-text-field></v-col>
                                <v-col cols="12" sm="6"><v-text-field v-model="otherForm.guardianEmail" type="email" label="Guardian email" density="compact"></v-text-field></v-col>
                                <v-col cols="12" sm="6"><PhoneField v-model="otherForm.guardianPhone" label="Guardian phone" density="compact" /></v-col>
                            </v-row>
                        </template>
                        <v-text-field v-if="otherForm.guardianMode === 'other' || otherGuardianDobAsked"
                            v-model="otherForm.guardianBirthdate" type="date" :max="todayIso" density="compact"
                            :label="otherForm.guardianMode === 'other' ? 'Guardian birthdate (must be 18 or over)' : 'Guardian birthdate (not on their profile)'"></v-text-field>
                    </template>
                    <template v-if="branding.requireEmergencyContact">
                        <v-divider class="my-2"></v-divider>
                        <div class="text-subtitle-2 mb-1">Emergency contact</div>
                        <p class="text-caption text-medium-emphasis mb-2">
                            This track requires one.
                            {{ otherNeedsGuardian || otherForm.existingDependentId ? 'Leave blank to use the parent or guardian.' : 'Leave blank if it is on their profile already.' }}
                        </p>
                        <v-row dense>
                            <v-col cols="12" sm="6"><v-text-field v-model="otherForm.emergencyContactName" label="Contact name" density="compact"></v-text-field></v-col>
                            <v-col cols="12" sm="6"><PhoneField v-model="otherForm.emergencyContactPhone" label="Contact phone" density="compact" /></v-col>
                        </v-row>
                    </template>
                    <v-alert v-if="otherError" type="error" variant="tonal" density="compact" class="mt-2">{{ otherError }}</v-alert>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="otherDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="otherSaving" @click="saveOtherRider">Add to cart</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <SendToKioskDialog v-model="kioskPushOpen" :target="kioskPushTarget" :rider-name="kioskPushRider"
            @sent="id => kioskWatch.track(id, true)" @done="onKioskDialogDone" />

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, nextTick, watch } from 'vue'
import dayjs from 'dayjs'
import { formatTenantDateTime } from '@/helpers/TenantTime'
import { CounterService, type CounterRider, type CounterDependent, type RiderWaiverLink, type RiderWaiverStatusItem, type OtherRiderInput } from '@/services/CounterService'
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
import KioskSignupsPanel from '@/components/KioskSignupsPanel.vue'
import SendToKioskDialog from '@/components/SendToKioskDialog.vue'
import type { KioskSignupItem, KioskPushTarget } from '@/services/KioskService'
import { useKioskPushWatch } from '@/composables/useKioskPushWatch'

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
    // "lesson" (Lesson panel) or "daypass" (Day passes panel) for event tickets, so each panel counts its own.
    group?: 'lesson' | 'daypass'
    // Set when the line is a ticket for another rider on this payment (their own profile and waiver).
    other?: OtherLine
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
        group: 'lesson',
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

// ── Day passes: gate-fee tiers of upcoming riding days, for the customer or another rider ──────
interface OtherLine {
    // What the cashier entered: sent with the sale, which checks it again and creates / links the
    // profiles. Nothing exists for a new rider or guardian until then.
    input: OtherRiderInput
    // The matched profiles (for spotting the same rider twice) and what the cart shows.
    userId: string | null
    dependentId: string | null
    key: string
    name: string
    isMinor: boolean
    guardianName: string | null
}

const dayEvents = ref<EventDto[]>([])
const loadingDayEvents = ref(false)
const selectedDayEventId = ref<string | null>(null)
const dayTiers = ref<TicketTier[]>([])
const loadingDayTiers = ref(false)
const dayEventOptions = computed(() => dayEvents.value.map(e => ({
    value: e.id,
    title: `${e.title} — ${formatTenantDateTime(e.startsAtUtc, 'ddd MMM D')}`,
})))
const selectedDayEvent = computed(() => dayEvents.value.find(e => e.id === selectedDayEventId.value) ?? null)
const dayPassCount = computed(() => cart.value.filter(c => c.group === 'daypass').reduce((s, c) => s + c.quantity, 0))
const lessonCount = computed(() => cart.value
    .filter(c => (c.kind === 'event_ticket' && c.group !== 'daypass') || c.kind === 'rental')
    .reduce((s, c) => s + c.quantity, 0))
const otherRiderLines = computed(() => cart.value.filter(c => !!c.other))
const payerHasOwnLines = computed(() => cart.value.some(c => !c.other))

async function loadDayEvents() {
    loadingDayEvents.value = true
    try {
        // Riding days still open now, and the next two weeks (a lesson has its own panel).
        const from = new Date(Date.now() - 24 * 3600 * 1000).toISOString()
        const to = new Date(Date.now() + 14 * 24 * 3600 * 1000).toISOString()
        const r = await eventService.list(from, to)
        dayEvents.value = ((r.data as any).data as EventDto[])
            .filter(e => e.eventTypeCode !== 'lesson' && e.status === 'scheduled'
                && new Date(e.endsAtUtc).getTime() > Date.now())
            .sort((a, b) => new Date(a.startsAtUtc).getTime() - new Date(b.startsAtUtc).getTime())
        if (!selectedDayEventId.value && dayEvents.value.length > 0) await onDayEventSelected(dayEvents.value[0].id)
    } catch (err: any) {
        // Not silent: an empty panel would read as "nothing to sell today".
        dayEvents.value = []
        flash(err.response?.data?.error || 'Couldn’t load riding days. Reopen the Day passes section.', 'error')
    } finally {
        loadingDayEvents.value = false
    }
}

async function onDayEventSelected(id: string | null) {
    selectedDayEventId.value = id
    dayTiers.value = []
    if (!id) return
    loadingDayTiers.value = true
    try {
        const r = await ticketService.listActiveTiers(id)
        dayTiers.value = ((r.data as any).data as TicketTier[]).filter(t => t.isActive && t.kind === 'gate_fee')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Couldn’t load this day’s tickets.', 'error')
    } finally {
        loadingDayTiers.value = false
    }
}

function dayPassLine(t: TicketTier, other?: OtherLine): CartLine {
    const ev = selectedDayEvent.value
    return {
        kind: 'event_ticket',
        itemId: t.id,
        displayName: `${ev?.title ?? 'Day pass'} — ${t.name}`,
        unitPriceCents: t.priceCents,
        quantity: 1,
        // The customer's waiver step covers only their own lines (as the server does); another rider
        // signs their own after payment.
        requiresWaiver: !other && !!ev?.requiresRiderWaiver,
        riderPaidServiceChargeBps: t.riderPaidServiceChargeBps ?? 10000,
        eventId: ev?.id ?? null,
        group: 'daypass',
        other,
    }
}

function addDayPass(t: TicketTier) {
    const existing = cart.value.find(c => c.kind === 'event_ticket' && c.itemId === t.id && !c.other)
    if (existing) { existing.quantity += 1; return }
    cart.value.push(dayPassLine(t))
}

// ── Another rider dialog ─────────────────────────────────────────────────────────────────────
const otherDialog = ref(false)
const otherTier = ref<TicketTier | null>(null)
const otherSearch = ref('')
const otherSearching = ref(false)
const otherSaving = ref(false)
const otherError = ref<string | null>(null)
const otherCandidates = ref<{ id: string; email: string; firstName: string; lastName: string; phone: string | null }[]>([])
const otherPickedLabel = ref<string | null>(null)
const payerDependents = ref<CounterDependent[]>([])
// The server said this picked profile is a minor: ask for their guardian.
const otherForcedGuardian = ref(false)
// The server needs the guardian's birthdate (not on their account).
const otherGuardianDobAsked = ref(false)
const blankOtherForm = () => ({
    existingUserId: null as string | null,
    existingDependentId: null as string | null,
    firstName: '', lastName: '', birthdate: '', email: '', phone: '',
    // Usually a parent buying for their child: the payer signs unless staff say otherwise.
    guardianMode: 'payer' as 'payer' | 'other',
    guardianFirstName: '', guardianLastName: '', guardianEmail: '', guardianPhone: '', guardianBirthdate: '',
    emergencyContactName: '', emergencyContactPhone: '',
})
const otherForm = ref(blankOtherForm())

/** Under 18 on today's date by the typed birthdate (the server decides on the venue's date). */
const otherIsMinor = computed(() => {
    const b = otherForm.value.birthdate
    if (!b) return false
    return dayjs().diff(dayjs(b), 'year') < 18
})
const otherNeedsGuardian = computed(() =>
    !otherForm.value.existingDependentId && (otherIsMinor.value || otherForcedGuardian.value))

async function openOtherRider(t: TicketTier) {
    if (!customer.value) return
    otherTier.value = t
    otherForm.value = blankOtherForm()
    otherSearch.value = ''
    otherCandidates.value = []
    otherPickedLabel.value = null
    otherError.value = null
    otherForcedGuardian.value = false
    otherGuardianDobAsked.value = false
    otherDialog.value = true
    try {
        const r = await counter.listDependents(customer.value.id)
        payerDependents.value = (r.data as any).data.filter((d: CounterDependent) => d.isMinor)
    } catch {
        payerDependents.value = []   // a convenience only: the form still works
    }
}

async function searchOtherRider() {
    const q = otherSearch.value.trim()
    if (q.length < 2) return
    otherSearching.value = true
    try {
        const r = await counter.searchRiders(q)
        otherCandidates.value = ((r.data as any).data as typeof otherCandidates.value)
            .filter(c => c.id !== customer.value?.id)
        if (otherCandidates.value.length === 0) otherError.value = `No profile found for "${q}". Enter them as a new rider.`
        else otherError.value = null
    } catch (err: any) {
        otherError.value = err.response?.data?.error || 'Search failed. Try again before entering a new rider.'
    } finally {
        otherSearching.value = false
    }
}

function pickOtherCandidate(c: { id: string; email: string; firstName: string; lastName: string }) {
    otherForm.value.existingUserId = c.id
    otherForm.value.existingDependentId = null
    otherPickedLabel.value = `${c.firstName} ${c.lastName} <${c.email}>: their existing profile`
    otherCandidates.value = []
    otherForcedGuardian.value = false
}

function pickDependent(d: CounterDependent) {
    otherForm.value.existingDependentId = d.id
    otherForm.value.existingUserId = null
    otherPickedLabel.value = `${d.firstName} ${d.lastName}, with ${customer.value?.firstName ?? 'the customer'} as guardian`
    otherCandidates.value = []
}

function clearOtherPick() {
    otherForm.value.existingUserId = null
    otherForm.value.existingDependentId = null
    otherPickedLabel.value = null
    otherForcedGuardian.value = false
}

async function saveOtherRider() {
    if (!customer.value || !otherTier.value) return
    const f = otherForm.value
    otherSaving.value = true
    otherError.value = null
    try {
        const guardianIsPayer = otherNeedsGuardian.value && f.guardianMode === 'payer'
        const sendGuardian = otherNeedsGuardian.value && f.guardianMode === 'other'
        const input: OtherRiderInput = {
            existingUserId: f.existingUserId,
            existingDependentId: f.existingDependentId,
            firstName: f.firstName.trim() || null,
            lastName: f.lastName.trim() || null,
            birthdate: f.birthdate || null,
            email: f.email.trim() || null,
            phone: f.phone.trim() || null,
            guardianIsPayer,
            guardianFirstName: sendGuardian ? f.guardianFirstName.trim() : null,
            guardianLastName: sendGuardian ? f.guardianLastName.trim() : null,
            guardianEmail: sendGuardian ? f.guardianEmail.trim() : null,
            guardianPhone: sendGuardian ? f.guardianPhone.trim() || null : null,
            guardianBirthdate: otherNeedsGuardian.value ? f.guardianBirthdate || null : null,
            emergencyContactName: f.emergencyContactName.trim() || null,
            emergencyContactPhone: f.emergencyContactPhone.trim() || null,
        }
        // A check only: the draft says who this is and which profiles exist; the sale creates the rest.
        const r = await counter.resolveOtherRider({ payerUserId: customer.value.id, ...input })
        const p = r.data.data
        const other: OtherLine = {
            input,
            userId: p.riderUserId,
            dependentId: p.riderDependentId,
            key: p.riderDependentId ?? p.riderUserId
                ?? `${p.firstName} ${p.lastName}|${p.guardianEmail ?? p.email ?? ''}`.toLowerCase(),
            name: `${p.firstName} ${p.lastName}`,
            isMinor: p.isMinor,
            guardianName: p.guardianName,
        }
        const dup = cart.value.some(c => c.itemId === otherTier.value!.id && c.other?.key === other.key)
        if (dup) { otherError.value = `${other.name} already has this ticket in the cart.`; return }
        cart.value.push(dayPassLine(otherTier.value, other))
        otherDialog.value = false
        // The payer always "matches" as guardian, so only a separate guardian's account is worth a mention.
        const riderPart = p.matchedExistingRider
            ? 'their existing profile will be used'
            : 'their profile is created with the sale'
        const guardianPart = sendGuardian && p.matchedExistingGuardian
            ? ` ${p.guardianName ?? 'The guardian'}'s existing account signs for them.`
            : ''
        flash(`Added ${other.name}: ${riderPart}.${guardianPart}`, 'success')
    } catch (err: any) {
        const msg: string = err.response?.data?.error || 'Couldn’t add that rider.'
        otherError.value = msg
        // A picked profile turned out to be a minor: ask for their guardian, then try again.
        if (/parent or guardian/i.test(msg)) otherForcedGuardian.value = true
        // The guardian's age isn't on file: show the birthdate box.
        if (/birthdate/i.test(msg) && otherNeedsGuardian.value) otherGuardianDobAsked.value = true
    } finally {
        otherSaving.value = false
    }
}

// ── Kiosk sign-ups (Script0310): pick one as the customer ────────────────────────────────────
// An adult becomes the customer. A minor's guardian becomes the customer and the child is offered as
// an other-rider day pass ("For <child>"), carrying the waiver the guardian signed at the kiosk.
const kioskSignupIds = ref<string[]>([])
const kioskChild = ref<{ signupId: string; dependentId: string; name: string; firstName: string } | null>(null)
const kioskChildBusy = ref(false)

async function selectKioskSignup(s: KioskSignupItem) {
    const email = s.isMinor ? s.guardianEmail : s.riderEmail
    if (!email) { flash('That sign-up has no profile to open. Search for them instead.', 'error'); return }
    findingCustomer.value = true
    try {
        await resolveByEmail(email)
        kioskSignupIds.value = [s.id]
        kioskChild.value = s.isMinor && s.dependentId
            ? { signupId: s.id, dependentId: s.dependentId, name: `${s.firstName} ${s.lastName}`, firstName: s.firstName }
            : null
        catalogPanel.value = 'daypass'
        flash(s.isMinor ? `${customer.value?.firstName} is the customer; add ${s.firstName}'s day pass.`
            : `${s.firstName} ${s.lastName} is the customer.`, 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Couldn’t open that sign-up. Search for them instead.', 'error')
    } finally {
        findingCustomer.value = false
    }
}

async function addKioskChild(t: TicketTier) {
    if (!customer.value || !kioskChild.value) return
    const child = kioskChild.value
    if (cart.value.some(c => c.itemId === t.id && c.other?.key === child.dependentId)) {
        flash(`${child.name} already has this ticket in the cart.`, 'error')
        return
    }
    kioskChildBusy.value = true
    try {
        const input: OtherRiderInput = { existingDependentId: child.dependentId, kioskSignupId: child.signupId }
        const p = (await counter.resolveOtherRider({ payerUserId: customer.value.id, ...input })).data.data
        cart.value.push(dayPassLine(t, {
            input, userId: p.riderUserId, dependentId: p.riderDependentId, key: child.dependentId,
            name: `${p.firstName} ${p.lastName}`, isMinor: p.isMinor, guardianName: p.guardianName,
        }))
    } catch (err: any) {
        flash(err.response?.data?.error || `Couldn't add ${child.name}.`, 'error')
    } finally {
        kioskChildBusy.value = false
    }
}

function clearKiosk() {
    kioskSignupIds.value = []
    kioskChild.value = null
}

// ── Receipt: other riders' waivers, live ─────────────────────────────────────────────────────
const riderStatus = ref<Record<string, RiderWaiverStatusItem>>({})
const riderLinks = ref<Record<string, RiderWaiverLink>>({})
const linkBusy = ref<Record<string, 'send' | 'device' | undefined>>({})
const linkDelivered = ref<Record<string, boolean>>({})
const refreshingWaivers = ref(false)
let waiverPoll: ReturnType<typeof setInterval> | null = null

// Declared before the receipt-rider computeds: the watch below reads it during setup.
const lineItems = ref<any[]>([])
const receiptRiderLines = computed(() => (lineItems.value as any[]).filter(l => l.otherRider) as
    { purchaseId: string; riderName: string; riderIsMinor: boolean }[])
const ridersAllDone = computed(() => receiptRiderLines.value.length > 0 && receiptRiderLines.value.every(l =>
    ['signed', 'not_required'].includes(riderStatus.value[l.purchaseId]?.status ?? '')))

async function refreshRiderWaivers() {
    const ids = receiptRiderLines.value.map(l => l.purchaseId)
    if (ids.length === 0) return
    refreshingWaivers.value = true
    try {
        const r = await counter.riderWaiverStatus(ids)
        const next: Record<string, RiderWaiverStatusItem> = {}
        for (const s of r.data.data) next[s.purchaseId] = s
        riderStatus.value = next
    } catch {
        /* the next poll tries again; the chips keep their last known state */
    } finally {
        refreshingWaivers.value = false
    }
}

// Another rider can finish their waiver / photo on a front-desk kiosk instead (Script0311).
const kioskPushOpen = ref(false)
const kioskPushTarget = ref<KioskPushTarget | null>(null)
const kioskPushRider = ref<string | null>(null)
function sendRiderToKiosk(li: { purchaseId: string; riderName: string }) {
    kioskPushTarget.value = { targetKind: 'ticket', purchaseId: li.purchaseId }
    kioskPushRider.value = li.riderName
    kioskPushOpen.value = true
}
// Closing the dialog doesn't lose the result: requests sent from here are re-checked every 5 s until they end.
const kioskWatch = useKioskPushWatch<boolean>(() => { void refreshRiderWaivers() })
function onKioskDialogDone(pushId: string) {
    kioskWatch.untrack(pushId)
    void refreshRiderWaivers()
}

function stopWaiverPoll() {
    if (waiverPoll) { clearInterval(waiverPoll); waiverPoll = null }
}

// Poll while the receipt shows riders who still have to sign.
watch([step, receiptRiderLines, ridersAllDone], ([s, lines, done]) => {
    if (s === 5 && lines.length > 0 && !done) {
        if (!waiverPoll) {
            void refreshRiderWaivers()
            waiverPoll = setInterval(() => { void refreshRiderWaivers() }, 10000)
        }
    } else {
        stopWaiverPoll()
    }
})
onBeforeUnmount(stopWaiverPoll)

function resetRiderWaivers() {
    stopWaiverPoll()
    riderStatus.value = {}
    riderLinks.value = {}
    linkBusy.value = {}
    linkDelivered.value = {}
}

function waiverChip(purchaseId: string): { label: string; color: string | undefined } {
    switch (riderStatus.value[purchaseId]?.status) {
        case 'signed': return { label: 'Signed', color: 'success' }
        case 'link_sent': return { label: 'Pending link', color: 'warning' }
        case 'needs_signature': return { label: 'Not signed', color: 'error' }
        case 'not_required': return { label: 'No waiver needed', color: undefined }
        default: return { label: 'Checking…', color: undefined }
    }
}

async function sendRiderLink(li: { purchaseId: string; riderName: string }, deliver: boolean) {
    linkBusy.value[li.purchaseId] = 'send'
    try {
        const r = await counter.riderWaiverLink(li.purchaseId, deliver)
        const link = r.data.data
        if (link.status === 'signed' || link.status === 'not_required') {
            flash(`${li.riderName} is already covered.`, 'success')
        } else {
            riderLinks.value[li.purchaseId] = link
            linkDelivered.value[li.purchaseId] = deliver
            const via = [link.emailSent ? 'emailed' : null, link.smsSent ? 'texted' : null].filter(Boolean).join(' and ')
            flash(via ? `Link ${via} to ${link.recipientName}.` : `Link ready for ${link.recipientName}: have them scan the QR.`,
                via ? 'success' : 'error')
        }
        await refreshRiderWaivers()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Couldn’t create the signing link.', 'error')
    } finally {
        linkBusy.value[li.purchaseId] = undefined
    }
}

/** Opens the rider's own signing page (guardian or adult mode) on this device, in a new tab. */
async function signOnDevice(li: { purchaseId: string; riderName: string }) {
    linkBusy.value[li.purchaseId] = 'device'
    // Opened before the await so the browser doesn't treat it as an unrequested pop-up.
    const tab = window.open('', '_blank')
    try {
        const r = await counter.riderWaiverLink(li.purchaseId, false)
        const link = r.data.data
        if (!link.link) {
            tab?.close()
            flash(`${li.riderName} is already covered.`, 'success')
            return
        }
        riderLinks.value[li.purchaseId] = link
        const token = link.link.split('/').pop() ?? ''
        const url = `${window.location.origin}/SignWaiver/${token}`
        if (tab) tab.location.href = url
        // Pop-ups blocked: leaving this page would lose the receipt, so show the link and QR instead.
        else flash('The browser blocked the new tab: use the link or QR below.', 'error')
    } catch (err: any) {
        tab?.close()
        flash(err.response?.data?.error || 'Couldn’t open the signing page.', 'error')
    } finally {
        linkBusy.value[li.purchaseId] = undefined
    }
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
    void loadDayEvents()
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
    clearKiosk()
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
                otherRider: c.other?.input ?? null,
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
            kioskSignupIds: kioskSignupIds.value.length > 0 ? kioskSignupIds.value : null,
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
    resetRiderWaivers()
    clearKiosk()
}

function redeemUrl(token: string): string {
    return `${window.location.protocol}//${window.location.host}/redeem/${token}`
}

function kindLabel(kind: string): string {
    switch (kind) {
        case 'extras': return 'Add-on'
        case 'membership': return 'Membership'
        case 'season_pass': return 'Season pass'
        case 'event_ticket': return 'Day pass'
        // Anything else: "shop_rental" reads as "Shop rental".
        default: return kind.charAt(0).toUpperCase() + kind.slice(1).replace(/_/g, ' ')
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
    background: var(--rp-panel2);
}
.catalog-row + .catalog-row {
    border-top: 1px solid var(--rp-line);
}
.catalog-panels :deep(.v-expansion-panel-text__wrapper) {
    padding: 0 16px 12px;
}
.cart-summary {
    background: var(--rp-card2);
    border: 1px solid var(--rp-line);
    border-radius: 10px;
    padding: 8px 12px;
}
.cart-line + .cart-line {
    border-top: 1px solid var(--rp-line);
}
.rider-waiver-line + .rider-waiver-line {
    border-top: 1px solid var(--rp-line);
}
</style>
