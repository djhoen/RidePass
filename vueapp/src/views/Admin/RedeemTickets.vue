<template>
    <v-container style="max-width: 760px">
        <h1 class="text-h4 mb-4">Redeem Tickets</h1>

        <v-card class="mb-4 pa-4">
            <v-card-title>Scan QR</v-card-title>
            <v-card-text>
                <div id="qr-reader" class="reader-surface mb-3"></div>
                <div class="d-flex ga-2">
                    <v-btn v-if="!scanning" color="primary" @click="startScan">Start Camera</v-btn>
                    <v-btn v-else color="error" @click="stopScan">Stop Camera</v-btn>
                </div>
                <v-divider class="my-4"></v-divider>

                <!-- No QR? Find them by name or email. This is the everyday fallback at a gate:
                     dead phone, lost email, or a ticket a parent bought for a kid. -->
                <p class="text-caption text-medium-emphasis mb-2">No QR code? Look them up:</p>
                <v-text-field v-model="searchInput" label="Rider or buyer name, or email"
                    prepend-inner-icon="mdi-account-search" density="compact" hide-details clearable
                    :loading="searching" @keyup.enter="runSearch" @click:clear="clearSearch"></v-text-field>
                <div class="text-caption text-medium-emphasis mt-1">
                    Searches today's events only. Type at least 3 characters.
                </div>

                <div v-if="searchResults.length > 0" class="mt-3">
                    <div v-for="r in searchResults" :key="r.anchorToken"
                         class="search-row d-flex align-center ga-3 py-2 px-2"
                         role="button" tabindex="0"
                         @click="openResult(r)" @keyup.enter="openResult(r)">
                        <v-icon icon="mdi-account" size="20" class="text-medium-emphasis"></v-icon>
                        <div class="flex-grow-1" style="min-width: 0">
                            <div class="text-body-2 font-weight-medium">{{ r.purchaserName }}</div>
                            <div class="text-caption text-medium-emphasis text-truncate">{{ r.purchaserEmail }}</div>
                            <div v-if="r.riderNames" class="text-caption text-medium-emphasis text-truncate">
                                Riders: {{ r.riderNames }}
                            </div>
                            <div class="text-caption text-medium-emphasis">{{ r.eventTitle }}</div>
                        </div>
                        <div class="text-right flex-shrink-0">
                            <v-chip v-if="r.redeemedCount >= r.itemCount" size="x-small" color="primary">
                                All checked in
                            </v-chip>
                            <v-chip v-else-if="r.redeemedCount > 0" size="x-small" color="warning">
                                {{ r.redeemedCount }}/{{ r.itemCount }} in
                            </v-chip>
                            <v-chip v-else size="x-small" variant="tonal">
                                {{ r.itemCount }} {{ r.itemCount === 1 ? 'item' : 'items' }}
                            </v-chip>
                        </div>
                    </div>
                </div>
                <p v-else-if="searchedFor" class="text-caption text-medium-emphasis mt-3">
                    No one matching “{{ searchedFor }}” is checked in for today's events.
                </p>

                <v-divider class="my-4"></v-divider>
                <p class="text-caption text-medium-emphasis mb-2">Or paste a token / redeem URL:</p>
                <div class="d-flex ga-2">
                    <v-text-field v-model="manualInput" label="Token or URL" density="compact" hide-details></v-text-field>
                    <v-btn color="primary" :loading="loading" @click="lookupManual">Look Up</v-btn>
                </div>

                <template v-if="branding.wristbandsEnabled">
                    <v-divider class="my-4"></v-divider>
                    <p class="text-caption text-medium-emphasis mb-2">Or scan / type a wristband:</p>
                    <div class="d-flex ga-2">
                        <v-text-field v-model="bandLookupInput" label="Wristband code or #" density="compact"
                            hide-details prepend-inner-icon="mdi-watch"
                            @keyup.enter="lookupBand"></v-text-field>
                        <v-btn color="primary" :loading="bandLookupBusy" @click="lookupBand">Find</v-btn>
                    </div>
                </template>
            </v-card-text>
        </v-card>

        <v-card v-if="order" class="mb-4">
            <v-card-title class="d-flex align-center flex-wrap ga-2 pt-4">
                Order
                <v-chip v-if="redeemableCount > 0" size="small" color="success">
                    {{ redeemableCount }} redeemable
                </v-chip>
                <v-spacer></v-spacer>
                <span class="text-body-2 text-medium-emphasis">Total {{ money(order.totalAmountCents) }}</span>
            </v-card-title>

            <div class="px-4 pb-2">
                <div class="text-body-2"><strong>{{ order.purchaserName }}</strong></div>
                <div class="text-body-2 text-medium-emphasis">{{ order.purchaserEmail }}</div>
            </div>

            <!-- Waiver alarm. An unsigned attendee must be impossible to miss, whichever tab
                 staff are looking at, so it sits above the tabs and names the people. -->
            <div class="px-4">
                <v-alert v-if="order.waiverMissingCount > 0" type="error" variant="flat"
                    border="start" icon="mdi-alert-octagon" class="mb-3">
                    <div class="text-subtitle-1 font-weight-bold">
                        {{ order.waiverMissingCount }} of {{ order.waiverRequiredCount }}
                        {{ order.waiverRequiredCount === 1 ? 'attendee has' : 'attendees have' }}
                        NOT signed the required waiver
                    </div>
                    <div class="text-body-2 mt-1">{{ missingNames.join(', ') }}</div>
                    <v-btn size="small" variant="outlined" class="mt-2" @click="tab = 'waivers'">
                        See waivers
                    </v-btn>
                </v-alert>
                <v-alert v-else-if="order.waiverRequiredCount > 0" type="success" variant="tonal"
                    density="compact" icon="mdi-shield-check" class="mb-3">
                    All {{ order.waiverRequiredCount }}
                    {{ order.waiverRequiredCount === 1 ? 'required waiver is' : 'required waivers are' }} signed.
                </v-alert>
            </div>

            <v-tabs v-model="tab" color="primary">
                <v-tab value="items">Items ({{ order.items.length }})</v-tab>
                <v-tab value="waivers">
                    Waivers
                    <v-chip v-if="order.waiverMissingCount > 0" color="error" size="x-small" class="ml-2">
                        {{ order.waiverMissingCount }} unsigned
                    </v-chip>
                    <v-chip v-else-if="order.waivers.length > 0" color="success" size="x-small" class="ml-2">
                        {{ order.waivers.length }}
                    </v-chip>
                </v-tab>
            </v-tabs>
            <v-divider></v-divider>

            <v-card-text>
                <v-window v-model="tab">
                    <!-- Everything the purchaser paid for: entries, gate fees, add-ons. -->
                    <v-window-item value="items">
                        <p v-if="order.items.length === 0" class="text-medium-emphasis">No items found.</p>

                        <div v-for="item in order.items" :key="item.purchaseId"
                             class="order-row d-flex align-start py-2 ga-3">
                            <v-checkbox v-model="selectedIds" :value="item.purchaseId"
                                :disabled="!item.isRedeemableToday"
                                hide-details density="compact" class="mt-0"></v-checkbox>
                            <div class="flex-grow-1" style="min-width: 0">
                                <div class="text-body-1">
                                    <strong>{{ item.itemName }}</strong>
                                    <span v-if="item.quantity > 1" class="font-weight-bold ml-1">x{{ item.quantity }}</span>
                                    <span class="text-medium-emphasis ml-2">{{ money(item.amountCents) }}</span>
                                    <v-chip size="x-small" class="ml-2" :color="statusColor(item.status)">{{ item.status }}</v-chip>
                                    <v-chip size="x-small" class="ml-1" variant="tonal">{{ kindLabel(item) }}</v-chip>
                                </div>
                                <div v-if="item.variantLabel" class="text-caption text-medium-emphasis">
                                    {{ item.variantLabel }}
                                </div>
                                <div v-if="item.attendeeName" class="text-caption text-medium-emphasis">
                                    Rider: {{ item.attendeeName }}
                                </div>
                                <div v-if="branding.wristbandsEnabled && item.kind === 'event_ticket'
                                        && (item.status === 'paid' || item.status === 'redeemed')"
                                     class="d-flex align-center ga-2 mt-1">
                                    <v-chip v-if="bandsByTicket[item.purchaseId]" size="x-small" color="indigo"
                                        prepend-icon="mdi-watch" closable
                                        @click:close="unlinkBand(item.purchaseId)">
                                        Band {{ bandsByTicket[item.purchaseId] }}
                                    </v-chip>
                                    <v-btn v-else size="x-small" variant="tonal" prepend-icon="mdi-watch"
                                        @click="openLinkBand(item)">Link band</v-btn>
                                </div>
                                <div v-if="item.signedByParent" class="text-caption d-flex align-center ga-1" style="color: rgb(var(--v-theme-info))">
                                    <v-icon icon="mdi-shield-account" size="14"></v-icon>
                                    Minor, waiver signed by guardian{{ item.guardianName ? `: ${item.guardianName}` : '' }}
                                </div>
                                <div v-if="item.redeemedAtUtc" class="text-caption text-medium-emphasis">
                                    Redeemed {{ formatInTenant(item.redeemedAtUtc) }}
                                    <span v-if="item.redeemedByName"> by {{ item.redeemedByName }}</span>
                                </div>
                                <div v-else-if="!item.isRedeemableToday && item.notRedeemableReason"
                                     class="text-caption text-warning d-flex align-center ga-1">
                                    <v-icon icon="mdi-alert-circle-outline" size="14"></v-icon>
                                    {{ item.notRedeemableReason }}
                                </div>
                            </div>
                        </div>
                    </v-window-item>

                    <!-- Who is walking in, and has each of them signed what this event requires. -->
                    <v-window-item value="waivers">
                        <p v-if="order.waivers.length === 0" class="text-medium-emphasis">
                            No attendees on this order yet.
                        </p>

                        <div v-for="a in order.waivers" :key="a.attendeeKey"
                             class="attendee pa-3 mb-2"
                             :class="{ 'attendee-blocked': !!a.blockReason }">
                            <div class="d-flex align-center flex-wrap ga-2">
                                <v-icon :icon="attendeeIcon(a)" :color="attendeeColor(a)"></v-icon>
                                <span class="text-body-1 font-weight-bold">
                                    {{ a.name || 'Name not provided' }}
                                </span>
                                <v-chip size="x-small" variant="tonal">
                                    {{ a.audience === 'rider' ? 'Rider' : 'Spectator' }}
                                </v-chip>
                                <v-chip v-if="a.isMinor" size="x-small" color="info">Minor</v-chip>
                                <v-spacer></v-spacer>
                                <v-chip v-if="!a.waiverRequired" size="small" variant="tonal">Not required</v-chip>
                                <v-chip v-else-if="a.waiverSigned" size="small" color="success">Signed</v-chip>
                                <v-chip v-else size="small" color="error" variant="flat" class="font-weight-bold">
                                    NOT SIGNED
                                </v-chip>
                            </div>

                            <div class="text-caption mt-1">
                                <span v-if="a.age !== null" class="text-medium-emphasis">
                                    Age {{ a.age }}<span v-if="a.birthdate"> (DOB {{ formatDate(a.birthdate) }})</span>
                                </span>
                                <span v-else class="text-warning d-inline-flex align-center ga-1">
                                    <v-icon icon="mdi-alert-outline" size="14"></v-icon>
                                    No date of birth on file
                                </span>
                            </div>

                            <div v-if="a.items.length" class="text-caption text-medium-emphasis">
                                {{ a.items.join(' / ') }}
                            </div>

                            <div v-if="a.waiverSigned" class="text-caption text-medium-emphasis mt-1">
                                Signed<span v-if="a.signedAtUtc"> {{ formatInTenant(a.signedAtUtc) }}</span>
                                <span v-if="a.waiverName"> ({{ a.waiverName }})</span>
                                <span v-if="a.signerName && a.signerName !== a.name"> by {{ a.signerName }}</span>
                            </div>
                            <div v-if="a.signedByParent" class="text-caption d-flex align-center ga-1"
                                 style="color: rgb(var(--v-theme-info))">
                                <v-icon icon="mdi-shield-account" size="14"></v-icon>
                                Signed by guardian{{ a.guardianName ? `: ${a.guardianName}` : '' }}
                            </div>

                            <div v-if="a.blockReason"
                                 class="text-body-2 font-weight-medium text-error d-flex align-center ga-1 mt-2">
                                <v-icon icon="mdi-alert-circle" size="16"></v-icon>
                                {{ a.blockReason }}
                            </div>

                            <v-btn v-if="a.hasSignatureImage" size="small" variant="text" class="mt-1 px-0"
                                prepend-icon="mdi-draw" :loading="signatureLoadingFor === a.attendeeKey"
                                @click="viewSignature(a)">
                                View signature
                            </v-btn>
                        </div>
                    </v-window-item>
                </v-window>

                <!-- Photo-ID attestation: shown only when the tenant requires it. Gate
                     staff confirm the rider's ID matches the purchaser name before redeeming. -->
                <div v-if="order.requireIdAtCheckin" class="id-verify mt-4 pa-3">
                    <div class="text-body-2 font-weight-medium d-flex align-center ga-2 mb-1">
                        <v-icon icon="mdi-card-account-details-outline" size="18" color="warning"></v-icon>
                        Photo ID required at check-in
                    </div>
                    <v-checkbox v-model="idVerified" hide-details density="compact" color="warning"
                        :label="`I checked a photo ID and it matches “${order.purchaserName}”.`"></v-checkbox>
                </div>

                <div class="d-flex align-center mt-4 ga-2 flex-wrap">
                    <v-btn v-if="redeemableCount > 0" variant="text" size="small" @click="selectAllRedeemable">
                        Select all redeemable
                    </v-btn>
                    <v-btn v-if="selectedIds.length > 0" variant="text" size="small" @click="selectedIds = []">
                        Clear
                    </v-btn>
                    <v-spacer></v-spacer>
                    <v-btn color="success" :loading="redeeming"
                        :disabled="selectedIds.length === 0 || (order.requireIdAtCheckin && !idVerified)"
                        @click="redeemSelected">
                        Redeem {{ selectedIds.length }} {{ selectedIds.length === 1 ? 'item' : 'items' }}
                    </v-btn>
                </div>
            </v-card-text>
        </v-card>

        <!-- Season pass card: a scanned pass QR isn't an order — it's a person holding an
             entitlement. Walk-up admission happens here; credits passes burn one ride. -->
        <v-card v-if="pass" class="mb-4">
            <v-card-title class="d-flex align-center flex-wrap ga-2 pt-4">
                Season Pass
                <v-chip size="small" variant="tonal" color="primary">{{ passKindLabel }}</v-chip>
                <v-spacer></v-spacer>
                <v-chip v-if="pass.status !== 'paid'" color="error" size="small">{{ pass.status }}</v-chip>
            </v-card-title>
            <v-card-text>
                <div class="d-flex ga-4">
                    <!-- The photo is the whole point of pass registration: staff verify the face. -->
                    <v-avatar v-if="pass.photoDataUrl" size="96" rounded="lg">
                        <v-img :src="pass.photoDataUrl" cover></v-img>
                    </v-avatar>
                    <v-avatar v-else size="96" rounded="lg" color="grey-lighten-3">
                        <v-icon size="48" color="grey">mdi-account</v-icon>
                    </v-avatar>
                    <div class="flex-grow-1" style="min-width: 0">
                        <div class="text-h6">{{ pass.holderName || pass.purchaserName }}</div>
                        <div v-if="!pass.holderName" class="text-caption text-medium-emphasis">
                            Buyer's name — this pass hasn't been registered to a holder yet.
                        </div>
                        <div class="text-body-2 text-medium-emphasis">{{ pass.productName }}</div>
                        <div class="text-body-2 text-medium-emphasis">
                            Valid {{ dayjs(pass.validFromDate).format('MMM D, YYYY') }}
                            to {{ dayjs(pass.validToDate).format('MMM D, YYYY') }}
                        </div>
                        <div v-if="pass.productKind === 'credits'" class="text-subtitle-1 font-weight-bold mt-1">
                            {{ pass.creditsRemaining ?? 0 }}<template v-if="pass.productTotalCredits">
                                of {{ pass.productTotalCredits }}</template>
                            {{ (pass.creditsRemaining ?? 0) === 1 ? 'ride' : 'rides' }} left
                        </div>
                    </div>
                </div>

                <!-- ── The two gate checks, at a glance ─────────────────────────────────
                     Deliberately the most prominent thing after the face and the name: this is
                     what the worker is actually deciding on, and it has to be readable across a
                     counter without being clicked into. -->
                <div v-if="pass.requireIdForWristband" class="d-flex align-center ga-2 flex-wrap mt-3">
                    <v-chip :color="pass.waiverSigned ? 'success' : 'error'" variant="flat" size="small"
                        :prepend-icon="pass.waiverSigned ? 'mdi-check-circle' : 'mdi-alert-circle'">
                        Waiver {{ pass.waiverSigned ? 'signed' : 'not signed' }}
                    </v-chip>

                    <v-tooltip :text="idChipTooltip" location="top">
                        <template #activator="{ props }">
                            <v-chip v-bind="props" :color="pass.idVerified ? 'success' : 'error'" variant="flat"
                                size="small"
                                :prepend-icon="pass.idVerified ? 'mdi-check-circle' : 'mdi-alert-circle'">
                                ID &amp; age
                                {{ pass.idVerified
                                    ? (pass.idVerifiedAge != null ? `verified (${pass.idVerifiedAge})` : 'verified')
                                    : 'not verified' }}
                            </v-chip>
                        </template>
                    </v-tooltip>

                    <v-btn v-if="!pass.idVerified" size="small" color="primary" variant="tonal"
                        prepend-icon="mdi-card-account-details-outline" @click="openVerifyId">
                        Verify ID
                    </v-btn>

                    <v-chip v-if="bandReady" color="success" size="small" variant="tonal"
                        prepend-icon="mdi-check-all">Clear for a wristband</v-chip>
                </div>
                <p v-if="pass.requireIdForWristband && !bandReady"
                    class="text-caption text-error mt-1 mb-0">
                    {{ bandBlockReason }}
                </p>

                <v-alert v-if="!pass.registrationComplete" type="warning" variant="tonal"
                    density="compact" class="mt-3">
                    Not registered yet — the buyer must finish registration (holder details, photo,
                    and any required waiver) before this pass can be used at the gate.
                </v-alert>
                <v-alert v-else-if="passWindowBlock" type="warning" variant="tonal" density="compact" class="mt-3">
                    {{ passWindowBlock }}
                </v-alert>

                <template v-if="pass.todaysReservations.length > 0">
                    <div class="text-caption text-medium-emphasis mt-4 mb-1">Today</div>
                    <div v-for="r in pass.todaysReservations" :key="r.id" class="d-flex align-center ga-2 py-1">
                        <v-icon size="18" :color="r.status === 'checked_in' ? 'success' : 'medium-emphasis'">
                            {{ r.status === 'checked_in' ? 'mdi-check-circle' : 'mdi-clock-outline' }}
                        </v-icon>
                        <span class="text-body-2">{{ r.eventTitle }}</span>
                        <v-chip size="x-small" :color="r.status === 'checked_in' ? 'success' : undefined" variant="tonal">
                            {{ r.status === 'checked_in'
                                ? `Checked in${r.checkedInAtUtc ? ' ' + formatInTenant(r.checkedInAtUtc) : ''}`
                                : 'Reserved' }}
                        </v-chip>
                    </div>
                </template>

                <v-divider class="my-3"></v-divider>

                <!-- Nothing on the calendar today. A sign-up track has no admission path without an
                     event; a walk-up track admits against the operating day itself. -->
                <template v-if="pass.todaysEvents.length === 0">
                    <v-alert v-if="branding.seasonPassAdmissionTypeId === 1"
                        type="info" variant="tonal" density="compact">
                        No event is running today. This track requires event sign-up, so passes can
                        only be checked in for a scheduled event.
                    </v-alert>
                    <div v-else-if="walkUpAlreadyCheckedIn" class="d-flex align-center ga-2">
                        <v-icon size="18" color="success">mdi-check-circle</v-icon>
                        <span class="text-body-2">Already admitted today</span>
                        <v-chip v-if="walkUpAlreadyCheckedIn.checkedInAtUtc" size="x-small"
                            color="success" variant="tonal">
                            {{ formatInTenant(walkUpAlreadyCheckedIn.checkedInAtUtc) }}
                        </v-chip>
                    </div>
                    <template v-else>
                        <div class="text-body-2 text-medium-emphasis mb-2">
                            <v-icon size="16" class="mr-1">mdi-information-outline</v-icon>
                            No event today: walk-up admission
                        </div>
                        <div class="d-flex align-center ga-2">
                            <v-spacer></v-spacer>
                            <span v-if="passAdmitBlock" class="text-caption text-medium-emphasis">{{ passAdmitBlock }}</span>
                            <v-btn color="success" :loading="admitting" :disabled="!!passAdmitBlock"
                                @click="admitPass">
                                {{ pass.productKind === 'credits'
                                    ? `Admit — uses 1 ride credit (${pass.creditsRemaining ?? 0} left)`
                                    : 'Admit' }}
                            </v-btn>
                        </div>
                    </template>
                </template>
                <template v-else>
                    <v-radio-group v-if="pass.todaysEvents.length > 1" v-model="passEventId"
                        density="compact" hide-details class="mb-2">
                        <v-radio v-for="e in pass.todaysEvents" :key="e.id" :value="e.id"
                            :label="`${e.title} (${formatInTenant(e.startsAtUtc)})`"></v-radio>
                    </v-radio-group>
                    <div v-else class="text-body-2 text-medium-emphasis mb-2">
                        {{ pass.todaysEvents[0].title }}
                    </div>
                    <div class="d-flex align-center ga-2">
                        <!-- Sign-up track: warn before the click that this event has no reservation
                             on file. The server enforces it either way. -->
                        <v-chip v-if="branding.seasonPassAdmissionTypeId === 1 && passEventId && !selectedEventReserved"
                            size="x-small" color="warning" variant="tonal" prepend-icon="mdi-calendar-alert">
                            Sign-up required
                        </v-chip>
                        <v-spacer></v-spacer>
                        <span v-if="passAdmitBlock" class="text-caption text-medium-emphasis">{{ passAdmitBlock }}</span>
                        <v-btn color="success" :loading="admitting" :disabled="!!passAdmitBlock || !passEventId"
                            @click="admitPass">
                            {{ pass.productKind === 'credits'
                                ? `Admit — uses 1 ride credit (${pass.creditsRemaining ?? 0} left)`
                                : 'Admit' }}
                        </v-btn>
                    </div>
                </template>
            </v-card-text>
        </v-card>

        <!-- ── Verify ID / age ─────────────────────────────────────────────── -->
        <v-dialog v-model="verifyIdOpen" max-width="440">
            <v-card v-if="pass">
                <v-card-title class="d-flex align-center">
                    <span>Verify ID and age</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="verifyingId"
                        @click="verifyIdOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-1">
                        Check the photo ID against the person in front of you.
                    </p>
                    <div class="text-h6">{{ pass.holderName || pass.purchaserName }}</div>
                    <p class="text-caption text-medium-emphasis mb-0">
                        This is recorded against the rider, so they won't be asked again on later scans.
                    </p>

                    <v-text-field v-model="verifyDob" type="date" label="Date of birth on the ID"
                        density="compact" class="mt-4"
                        :hint="verifyDobHint" persistent-hint
                        :error-messages="verifyDobError ? [verifyDobError] : []"></v-text-field>

                    <p v-if="verifyAge != null" class="text-body-2 mt-2 mb-0">
                        That makes them <strong>{{ verifyAge }}</strong> today.
                    </p>

                    <div v-if="verifyIdError" class="text-error text-body-2 mt-3">{{ verifyIdError }}</div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="verifyingId" @click="verifyIdOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="verifyingId" :disabled="!verifyDob || !!verifyDobError"
                        @click="submitVerifyId">Record verification</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-dialog v-model="signatureOpen" max-width="520">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span class="text-truncate">Signature</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="signatureOpen = false"></v-btn>
                </v-card-title>
                <v-divider></v-divider>
                <v-card-text v-if="signature">
                    <div class="text-body-1 font-weight-bold">{{ signature.attendeeName || 'Attendee' }}</div>
                    <div class="text-caption text-medium-emphasis">
                        <span v-if="signature.waiverName">{{ signature.waiverName }}</span>
                        <span v-if="signature.signedAtUtc"> signed {{ formatInTenant(signature.signedAtUtc) }}</span>
                    </div>
                    <div v-if="signature.signedByParent" class="text-caption mt-1" style="color: rgb(var(--v-theme-info))">
                        Signed by guardian{{ signature.guardianName ? `: ${signature.guardianName}` : '' }}
                    </div>
                    <div v-if="signature.signerEmail" class="text-caption text-medium-emphasis">
                        {{ signature.signerEmail }}
                    </div>
                    <v-img v-if="signature.signatureDataUrl" :src="signature.signatureDataUrl"
                        class="signature-image mt-3" contain></v-img>
                    <p v-else class="text-caption text-medium-emphasis mt-3">
                        This waiver was accepted without a drawn signature.
                    </p>
                </v-card-text>
            </v-card>
        </v-dialog>

        <v-dialog v-model="bandDialogOpen" max-width="400">
            <v-card v-if="bandTarget">
                <v-card-title class="d-flex align-center">
                    <span>Link wristband</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="bandDialogOpen = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 text-medium-emphasis mb-3">
                        {{ bandTarget.attendeeName || bandTarget.itemName }} — scan the band's QR into the field,
                        or type its printed number.
                    </p>
                    <v-text-field v-model="bandCodeInput" label="Band code or #" density="compact" autofocus
                        hide-details @keyup.enter="saveBandLink"></v-text-field>
                    <div v-if="bandDialogError" class="text-error text-body-2 mt-2">{{ bandDialogError }}</div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="bandDialogBusy" @click="bandDialogOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="bandDialogBusy" @click="saveBandLink">Link</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, watch, onBeforeUnmount } from 'vue'
import dayjs from 'dayjs'
import { Html5Qrcode } from 'html5-qrcode'
import { TicketService, type OrderLookup, type OrderItem, type OrderWaiverAttendee, type OrderSignature,
    type GateSearchResult } from '@/services/TicketService'
import { branding } from '@/stores/branding'
import { WristbandService } from '@/services/WristbandService'
import { SeasonPassService, type PassLookup } from '@/services/SeasonPassService'

const service = new TicketService()
const wristbands = new WristbandService()
const seasonPasses = new SeasonPassService()

const manualInput = ref('')
const searchInput = ref('')
const searchResults = ref<GateSearchResult[]>([])
const searchedFor = ref('')          // the term the current results belong to
const searching = ref(false)
let searchTimer: ReturnType<typeof setTimeout> | null = null
const order = ref<OrderLookup | null>(null)
const orderToken = ref<string | null>(null)        // the originally-scanned token
const selectedIds = ref<string[]>([])
const idVerified = ref(false)
const loading = ref(false)
const redeeming = ref(false)
const scanning = ref(false)
const tab = ref<'items' | 'waivers'>('items')

// Season pass (walk-up gate redemption): populated when a scanned token turns out to be a
// pass rather than an order. Mutually exclusive with `order`.
const pass = ref<PassLookup | null>(null)
const passToken = ref<string | null>(null)
const passEventId = ref<string | null>(null)
const admitting = ref(false)

const signatureOpen = ref(false)
const signature = ref<OrderSignature | null>(null)
const signatureLoadingFor = ref<string | null>(null)

let scanner: Html5Qrcode | null = null

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error' | 'warning'>('success')

const redeemableCount = computed(() =>
    order.value?.items.filter(i => i.isRedeemableToday).length ?? 0)

// Names on the alarm banner, so staff can call the unsigned riders over without opening the tab.
const missingNames = computed(() =>
    (order.value?.waivers ?? [])
        .filter(a => a.waiverRequired && !a.waiverSigned)
        .map(a => a.name || 'Name not provided'))

async function startScan() {
    try {
        scanner = new Html5Qrcode('qr-reader')
        await scanner.start(
            { facingMode: 'environment' },
            { fps: 10, qrbox: { width: 260, height: 260 } },
            onDecoded,
            () => {},
        )
        scanning.value = true
    } catch (err: any) {
        flash(err?.message || 'Failed to start camera.', 'error')
    }
}

async function stopScan() {
    if (!scanner) return
    try { await scanner.stop(); await scanner.clear() } catch {}
    scanner = null
    scanning.value = false
}

async function onDecoded(decodedText: string) {
    const token = extractToken(decodedText)
    if (!token) return
    await stopScan()
    await loadOrder(token)
}

function extractToken(raw: string): string | null {
    const direct = raw.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i)
    return direct ? direct[0] : null
}

async function lookupManual() {
    const token = extractToken(manualInput.value)
    if (!token) { flash('No token found in input.', 'error'); return }
    await loadOrder(token)
}

// Debounced as the operator types: a gate queue is not the place to make someone press a button.
// Under 3 characters the server rejects the query, so don't even ask.
watch(searchInput, () => {
    if (searchTimer) clearTimeout(searchTimer)
    const term = (searchInput.value ?? '').trim()
    if (term.length < 3) {
        searchResults.value = []
        searchedFor.value = ''
        return
    }
    searchTimer = setTimeout(runSearch, 300)
})

async function runSearch() {
    const term = (searchInput.value ?? '').trim()
    if (term.length < 3) return
    searching.value = true
    try {
        const r = await service.gateSearch(term)
        searchResults.value = (r.data as any).data ?? []
        searchedFor.value = term
    } catch (err: any) {
        // A failed lookup must never look like "this rider doesn't exist" — that turns away someone
        // who paid. Say the search failed, and leave the previous results alone.
        flash(err.response?.data?.error
            || `Couldn’t search for “${term}”. Check the connection and try again.`, 'error')
    } finally {
        searching.value = false
    }
}

function clearSearch() {
    if (searchTimer) clearTimeout(searchTimer)
    searchInput.value = ''
    searchResults.value = []
    searchedFor.value = ''
}

// Picking a result is the same as scanning that rider's QR: its token anchors the whole order.
async function openResult(r: GateSearchResult) {
    await loadOrder(r.anchorToken)
}

// ── Wristbands (tenant feature): link a serialized band to an entrant, find riders by band. ──
const bandLookupInput = ref('')
const bandLookupBusy = ref(false)
const bandsByTicket = ref<Record<string, string>>({})
const bandDialogOpen = ref(false)
const bandDialogBusy = ref(false)
const bandDialogError = ref('')
const bandCodeInput = ref('')
const bandTarget = ref<OrderItem | null>(null)

async function lookupBand() {
    const code = bandLookupInput.value.trim()
    if (!code) return
    bandLookupBusy.value = true
    try {
        const r = await wristbands.resolve(code)
        bandLookupInput.value = ''
        await loadOrder(r.data.data.redemptionToken)
    } catch (err: any) {
        flash(err.response?.status === 404
            ? (err.response?.data?.error || 'No entrant is linked to that band.')
            : (err.response?.data?.error || 'Could not look up that band. Check the connection and try again.'), 'error')
    } finally { bandLookupBusy.value = false }
}

async function loadBands() {
    bandsByTicket.value = {}
    if (!branding.wristbandsEnabled) return
    const ids = order.value?.items.filter(i => i.kind === 'event_ticket').map(i => i.purchaseId) ?? []
    if (ids.length === 0) return
    try {
        const r = await wristbands.codes(ids)
        const map: Record<string, string> = {}
        for (const row of r.data.data.tickets) map[row.ticketId] = row.code
        bandsByTicket.value = map
    } catch { /* band chips are decoration on this screen; the order itself already loaded */ }
}

function openLinkBand(item: OrderItem) {
    bandTarget.value = item
    bandCodeInput.value = ''
    bandDialogError.value = ''
    bandDialogOpen.value = true
}

async function saveBandLink() {
    if (!bandTarget.value) return
    const code = bandCodeInput.value.trim()
    if (!code) { bandDialogError.value = 'Scan or type the band code first.'; return }
    bandDialogBusy.value = true
    bandDialogError.value = ''
    try {
        await wristbands.link(bandTarget.value.purchaseId, code)
        bandDialogOpen.value = false
        flash(`Band ${code} linked.`, 'success')
        await loadBands()
    } catch (err: any) {
        bandDialogError.value = err.response?.data?.error || 'Could not link the band. Please try again.'
    } finally { bandDialogBusy.value = false }
}

async function unlinkBand(ticketId: string) {
    try {
        await wristbands.unlink(ticketId)
        flash('Band unlinked.', 'success')
        await loadBands()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not unlink the band.', 'error')
    }
}

async function loadOrder(token: string) {
    try {
        loading.value = true
        const r = await service.orderLookup(token)
        order.value = (r.data as any).data
        orderToken.value = token
        pass.value = null
        passToken.value = null
        idVerified.value = false   // re-attest per scan
        // Land on Waivers when someone still owes one: that's the thing staff must act on.
        tab.value = (order.value?.waiverMissingCount ?? 0) > 0 ? 'waivers' : 'items'
        // Auto-select everything redeemable so the staff can just click Redeem.
        selectedIds.value = order.value?.items
            .filter(i => i.isRedeemableToday)
            .map(i => i.purchaseId) ?? []
        await loadBands()
    } catch (err: any) {
        // Only a real 404 means the QR/token is invalid — and even then it may be a SEASON PASS
        // token, which lives in a different subsystem than orders. Try that before giving up.
        // A network blip or server error must NOT read as "not found" — that would turn away a
        // paying customer holding a valid ticket.
        const status = err.response?.status
        if (status === 404 && await tryLoadPass(token)) return
        const msg = status === 404
            ? (err.response?.data?.error || 'Order not found. Double-check the code and rescan.')
            : (err.response?.data?.error || 'Couldn’t look up the order. Check the connection and rescan.')
        flash(msg, 'error')
        order.value = null
        orderToken.value = null
        selectedIds.value = []
    } finally {
        loading.value = false
    }
}

// Returns true when the token resolved to a season pass (card shown) OR the lookup failed in a
// way we already surfaced — i.e. whenever the caller should NOT show its own "not found" message.
async function tryLoadPass(token: string): Promise<boolean> {
    try {
        const r = await seasonPasses.lookupByToken(token)
        pass.value = (r.data as any).data
        passToken.value = token
        order.value = null
        orderToken.value = null
        selectedIds.value = []
        passEventId.value = pass.value?.todaysEvents.length === 1 ? pass.value.todaysEvents[0].id : null
        return true
    } catch (err: any) {
        if (err.response?.status === 404) return false      // genuinely unknown token
        flash(err.response?.data?.error || 'Couldn’t look up the pass. Check the connection and rescan.', 'error')
        return true
    }
}

const passKindLabel = computed(() => {
    switch (pass.value?.productKind) {
        case 'credits': return 'Ride credits'
        case 'days_of_week': return 'Select days'
        case 'unlimited': return 'Unlimited'
        default: return pass.value?.productKind ?? ''
    }
})

// Season-window problems staff should see before they hit the Admit button.
const passWindowBlock = computed(() => {
    if (!pass.value) return null
    if (dayjs().isBefore(dayjs(pass.value.validFromDate), 'day'))
        return `This pass isn't valid yet — its season starts ${dayjs(pass.value.validFromDate).format('MMM D, YYYY')}.`
    if (dayjs().isAfter(dayjs(pass.value.validToDate), 'day'))
        return `This pass's season ended ${dayjs(pass.value.validToDate).format('MMM D, YYYY')}.`
    return null
})

// Why Admit is disabled, or null when it's allowed. The server re-validates all of this;
// surfacing it up front just saves the gate line a failed round-trip.
// On a no-event day, has this pass already been walked in today? A walk-up admission carries no
// eventId, which is exactly what distinguishes it from an event-anchored row.
const walkUpAlreadyCheckedIn = computed(() =>
    pass.value?.todaysReservations.find(r => r.eventId === null && r.status === 'checked_in') ?? null)

// Does the selected event already have a reservation on this pass? Drives the sign-up warning.
const selectedEventReserved = computed(() =>
    pass.value?.todaysReservations.some(r => r.eventId === passEventId.value) ?? false)

const passAdmitBlock = computed(() => {
    if (!pass.value) return null
    if (pass.value.status !== 'paid') return 'Pass is not active.'
    if (!pass.value.registrationComplete) return 'Registration incomplete.'
    if (passWindowBlock.value) return 'Outside the pass season.'
    if (pass.value.productKind === 'credits' && (pass.value.creditsRemaining ?? 0) <= 0)
        return 'No ride credits left.'
    if (pass.value.todaysEvents.length === 0 && walkUpAlreadyCheckedIn.value)
        return 'Already admitted today.'
    return null
})

// ── Waiver + ID gate for a wristband ────────────────────────────────────────
// Mirrors what WristbandController.Link enforces, so the screen never invites a click the
// server is going to refuse.
const bandReady = computed(() =>
    !!pass.value && (!pass.value.requireIdForWristband
        || (pass.value.waiverSigned && pass.value.idVerified)))

const bandBlockReason = computed(() => {
    const p = pass.value
    if (!p || bandReady.value) return ''
    if (!p.waiverSigned && !p.idVerified)
        return 'No wristband yet: this rider still needs to sign the waiver and have their ID verified.'
    if (!p.waiverSigned)
        return p.waiverBlockReason
            || 'No wristband yet: this rider still needs to sign the waiver.'
    return 'No wristband yet: this rider still needs their ID and age verified.'
})

const idChipTooltip = computed(() => {
    const p = pass.value
    if (!p) return ''
    if (!p.idVerified) return 'No ID check on file for this rider.'
    const when = p.idVerifiedAtUtc ? formatInTenant(p.idVerifiedAtUtc) : 'earlier'
    const who = p.idVerifiedByName ? ` by ${p.idVerifiedByName}` : ''
    const scope = p.idVerifiedScope === 'rider'
        ? 'Recorded against their account, so it carries to anything else they buy.'
        : 'Recorded against this pass only, because the holder has no account of their own.'
    return `Verified ${when}${who}. ${scope}`
})

// ── Verify ID dialog ────────────────────────────────────────────────────────
const verifyIdOpen = ref(false)
const verifyingId = ref(false)
const verifyIdError = ref('')
const verifyDob = ref('')

function openVerifyId() {
    // Seed from what the rider gave at registration: the common case is the document simply
    // confirming it, so staff only change it when the two disagree.
    verifyDob.value = pass.value?.holderBirthdate
        ? dayjs(pass.value.holderBirthdate).format('YYYY-MM-DD')
        : ''
    verifyIdError.value = ''
    verifyIdOpen.value = true
}

const verifyAge = computed(() => {
    if (!verifyDob.value) return null
    const d = dayjs(verifyDob.value)
    return d.isValid() ? dayjs().diff(d, 'year') : null
})

const verifyDobError = computed(() => {
    if (!verifyDob.value) return ''
    const d = dayjs(verifyDob.value)
    if (!d.isValid()) return 'That date isn\'t valid.'
    if (d.isAfter(dayjs(), 'day')) return 'That date of birth is in the future.'
    return ''
})

const verifyDobHint = computed(() =>
    pass.value?.holderBirthdate
        ? 'Pre-filled from what the rider gave at registration. Change it if the ID says otherwise.'
        : 'No birthdate on file, so read it off the ID.')

async function submitVerifyId() {
    if (!passToken.value || !verifyDob.value || verifyDobError.value) return
    verifyingId.value = true
    verifyIdError.value = ''
    try {
        await seasonPasses.verifyPassHolderId(passToken.value, verifyDob.value)
        verifyIdOpen.value = false
        flash('ID and age verified.', 'success')
        // Re-read rather than patching locally: the server decides whether this stuck to the
        // rider's account or only to this pass, and the chip's wording depends on which.
        await tryLoadPass(passToken.value)
    } catch (err: any) {
        verifyIdError.value = err.response?.data?.error
            || 'Couldn\'t record the ID verification. Check the connection and try again.'
    } finally {
        verifyingId.value = false
    }
}

async function admitPass() {
    if (!pass.value || !passToken.value) return
    // With events running, staff must still pick one. With none, the walk-up path sends null.
    if (pass.value.todaysEvents.length > 0 && !passEventId.value) return
    admitting.value = true
    try {
        const r = await seasonPasses.redeemAtGate(passToken.value, passEventId.value)
        const data = (r.data as any).data
        if (data.alreadyAdmitted) {
            flash(`Already admitted today${data.checkedInAtUtc ? ' at ' + formatInTenant(data.checkedInAtUtc) : ''}.`, 'warning')
        } else if (pass.value.productKind === 'credits') {
            const left = data.creditsRemaining ?? 0
            flash(`Admitted — ${left} ${left === 1 ? 'ride' : 'rides'} left on this pass.`, 'success')
        } else {
            flash('Admitted.', 'success')
        }
        await tryLoadPass(passToken.value)   // refresh credits + today's check-in state
    } catch (err: any) {
        flash(err.response?.data?.error || 'Couldn’t admit this pass. Check the connection and try again.', 'error')
    } finally {
        admitting.value = false
    }
}

function selectAllRedeemable() {
    selectedIds.value = order.value?.items
        .filter(i => i.isRedeemableToday)
        .map(i => i.purchaseId) ?? []
}

async function redeemSelected() {
    if (!order.value || !orderToken.value || selectedIds.value.length === 0) return
    redeeming.value = true
    try {
        const items = order.value.items
            .filter(i => selectedIds.value.includes(i.purchaseId))
            .map(i => ({ kind: i.kind, purchaseId: i.purchaseId }))
        const r = await service.redeemBulk({ orderToken: orderToken.value, items, idVerified: idVerified.value })
        const data = (r.data as any).data
        const n = data.redeemedCount ?? 0
        const errs: string[] = data.errors ?? []
        if (errs.length && n > 0) flash(`Redeemed ${n}; ${errs.length} skipped: ${errs.join(' ')}`, 'warning')
        else if (errs.length) flash(errs.join(' '), 'error')
        else flash(`Redeemed ${n}.`, 'success')
        // Refresh the order so the redeemed rows now show as redeemed.
        await loadOrder(orderToken.value)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Redeem failed.', 'error')
    } finally {
        redeeming.value = false
    }
}

async function viewSignature(a: OrderWaiverAttendee) {
    if (!orderToken.value || !a.signaturePurchaseId) return
    signatureLoadingFor.value = a.attendeeKey
    try {
        const r = await service.orderSignature(orderToken.value, a.signaturePurchaseId)
        signature.value = (r.data as any).data
        signatureOpen.value = true
    } catch (err: any) {
        const who = a.name || 'this attendee'
        flash(err.response?.data?.error || `Couldn’t load the signature for ${who}. Check the connection and try again.`, 'error')
    } finally {
        signatureLoadingFor.value = null
    }
}

function attendeeIcon(a: OrderWaiverAttendee): string {
    if (a.blockReason) return 'mdi-alert-octagon'
    if (a.waiverRequired && a.waiverSigned) return 'mdi-shield-check'
    return 'mdi-account'
}

function attendeeColor(a: OrderWaiverAttendee): string {
    if (a.blockReason) return 'error'
    if (a.waiverRequired && a.waiverSigned) return 'success'
    return 'medium-emphasis'
}

// What the line actually admits. An event ticket is a race entry, a rider gate fee, or a
// spectator gate fee, and labeling all three "Race Entry" misleads whoever is working the gate.
function kindLabel(item: OrderItem): string {
    switch (item.kind) {
        case 'pass': return 'Pass'
        case 'extras': return 'Add-on'
        case 'membership': return 'Membership'
        case 'event_ticket':
            if (item.ticketKind === 'race_entry') return 'Race Entry'
            if (item.audience === 'spectator' || item.ticketKind === 'spectator_pass') return 'Spectator'
            if (item.ticketKind === 'gate_fee') return 'Rider Gate Fee'
            return 'Admission'
        default: return item.kind
    }
}

function money(cents: number): string {
    return `$${(cents / 100).toFixed(2)}`
}

function formatInTenant(utc: string): string {
    return dayjs.utc(utc).tz(branding.timezone || 'UTC').format('YYYY-MM-DD HH:mm')
}

// Birthdates are calendar dates, not instants: render them as stored so a timezone
// shift can't move someone's DOB (and their age) by a day.
function formatDate(utc: string): string {
    return dayjs.utc(utc).format('YYYY-MM-DD')
}

function statusColor(status: string): string {
    switch (status) {
        case 'paid': return 'success'
        case 'pending': return 'warning'
        case 'failed': return 'error'
        case 'refunded': return 'grey'
        case 'redeemed': return 'primary'
        default: return 'default'
    }
}

function flash(text: string, color: 'success' | 'error' | 'warning') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

onBeforeUnmount(() => {
    if (scanner) stopScan()
    if (searchTimer) clearTimeout(searchTimer)
})
</script>

<style scoped>
.reader-surface {
    width: 100%;
    max-width: 420px;
    min-height: 260px;
    border: 1px dashed rgba(0, 0, 0, 0.2);
    border-radius: 6px;
    margin: 0 auto;
    background: #f5f5f5;
}
.order-row + .order-row {
    border-top: 1px solid rgba(0, 0, 0, 0.06);
}
.search-row {
    border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
    border-radius: 6px;
    cursor: pointer;
}
.search-row + .search-row {
    margin-top: 6px;
}
.search-row:hover,
.search-row:focus-visible {
    background: rgba(var(--v-theme-primary), 0.06);
}
.id-verify {
    border: 1px solid rgb(var(--v-theme-warning));
    border-radius: 6px;
    background: rgba(var(--v-theme-warning), 0.06);
}
.attendee {
    border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
    border-radius: 6px;
}
/* An attendee who can't be admitted reads as a problem at a glance, not as another row. */
.attendee-blocked {
    border: 2px solid rgb(var(--v-theme-error));
    background: rgba(var(--v-theme-error), 0.06);
}
.signature-image {
    border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
    border-radius: 6px;
    background: #fff;
    max-height: 220px;
}
</style>
