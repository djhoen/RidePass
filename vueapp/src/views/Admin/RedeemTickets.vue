<template>
    <v-container style="max-width: 760px">
        <div class="rp-pagehead">
            <div>
                <div class="rp-pagehead-title">Redeem Tickets</div>
            </div>
        </div>

        <!-- Today's kiosk sign-ups (Script0310): who just checked in at the front desk, with photo and waiver. -->
        <KioskSignupsPanel compact :limit="5" class="mb-4" />

        <v-card class="mb-4 pa-4">
            <v-card-title>Scan QR</v-card-title>
            <v-card-text>
                <div id="qr-reader" class="reader-surface mb-3"></div>
                <div class="d-flex ga-2">
                    <v-btn v-if="!scanning" color="primary" @click="startScan">Start Camera</v-btn>
                    <v-btn v-else color="error" @click="stopScan">Stop Camera</v-btn>
                </div>
                <v-divider class="my-4"></v-divider>

                <!-- No QR? Find them by name, email or phone. This is the everyday fallback at a gate:
                     dead phone, lost email, a ticket a parent bought for a kid, or a season pass
                     card left at home. A USB scanner also works here (and with nothing focused). -->
                <p class="text-caption text-medium-emphasis mb-2">No QR code? Look them up:</p>
                <v-text-field ref="searchField" v-model="searchInput" data-gate-scan="search"
                    label="Rider, pass holder or buyer: name, email or phone"
                    prepend-inner-icon="mdi-account-search" density="compact" hide-details clearable
                    :loading="searching" @keydown.enter="onSearchEnter" @click:clear="clearSearch"></v-text-field>
                <div class="text-caption text-medium-emphasis mt-1">
                    Searches today's events and season passes valid today. Type at least 3 characters.
                </div>

                <div v-if="searchResults.length > 0" class="mt-3">
                    <div v-for="r in searchResults" :key="r.kind + r.anchorToken"
                         class="search-row d-flex align-center ga-3 py-2 px-2"
                         role="button" tabindex="0"
                         @click="openResult(r)" @keydown.enter="openResult(r)">
                        <v-icon :icon="r.kind === 'season_pass' ? 'mdi-card-account-details' : 'mdi-ticket-confirmation'"
                            size="20" class="text-medium-emphasis"></v-icon>
                        <!-- Same layout for both kinds: who is at the gate (riders / pass holder),
                             who bought it when that's someone else, the emails, and what they hold. -->
                        <div class="flex-grow-1" style="min-width: 0">
                            <div class="text-body-2 font-weight-medium">{{ r.displayName }}</div>
                            <div v-if="r.purchaserName && r.purchaserName !== r.displayName"
                                 class="text-caption text-medium-emphasis text-truncate">
                                Bought by {{ r.purchaserName }}
                            </div>
                            <div class="text-caption text-medium-emphasis text-truncate">
                                {{ r.purchaserEmail }}<template v-if="r.accountEmail"> · account: {{ r.accountEmail }}</template>
                            </div>
                            <div class="text-caption text-medium-emphasis">
                                {{ r.kind === 'season_pass' ? passResultSummary(r) : r.holds }}
                            </div>
                        </div>
                        <div class="d-flex flex-column align-end ga-1 flex-shrink-0">
                            <v-chip size="x-small" variant="outlined">
                                {{ r.kind === 'season_pass' ? 'Season pass' : 'Ticket' }}
                            </v-chip>
                            <v-chip v-if="r.kind === 'season_pass'" size="x-small"
                                :color="r.redeemedCount > 0 ? 'primary' : undefined"
                                :variant="r.redeemedCount > 0 ? 'flat' : 'tonal'">
                                {{ r.redeemedCount > 0 ? 'In today' : 'Not in yet' }}
                            </v-chip>
                            <v-chip v-else-if="r.redeemedCount >= r.itemCount" size="x-small" color="primary">
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
                    No ticket or season pass matching “{{ searchedFor }}” for today.
                </p>

                <v-divider class="my-4"></v-divider>
                <p class="text-caption text-medium-emphasis mb-2">
                    Or scan / paste a token, redeem URL<template v-if="branding.wristbandsEnabled"> or wristband code</template>:
                </p>
                <div class="d-flex ga-2">
                    <v-text-field v-model="manualInput" data-gate-scan="codes"
                        :label="branding.wristbandsEnabled ? 'Token, URL or wristband' : 'Token or URL'"
                        density="compact" hide-details @keydown.enter="onManualEnter"></v-text-field>
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
                                <v-chip v-else size="small" color="error" variant="tonal" class="font-weight-bold">
                                    NOT SIGNED
                                </v-chip>
                            </div>

                            <div class="text-caption mt-1">
                                <span v-if="a.age !== null" class="text-medium-emphasis">
                                    Age {{ a.age }}<span v-if="a.birthdate"> (DOB {{ formatDate(a.birthdate) }})</span>
                                </span>
                                <span v-else class="text-warning d-inline-flex align-center ga-1">
                                    <v-icon icon="mdi-alert-outline" size="14"></v-icon>
                                    No date of birth on file<template v-if="a.birthdateUnknown">: check their ID
                                        to confirm they're 18 or over (a minor needs a guardian's signature)</template>
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
                            <!-- A minor's own signature: on file, but it doesn't count. Say who signed. -->
                            <div v-else-if="a.waiverRequired && a.signedAtUtc && a.isMinor && !a.signedByParent"
                                 class="text-caption text-medium-emphasis mt-1">
                                Signed {{ formatInTenant(a.signedAtUtc) }}
                                by {{ a.signerName || a.name || 'the rider' }} themselves, not by a parent or guardian
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
                            <v-btn v-if="a.canRequestSignature && a.purchaseIds.length" size="small" variant="tonal"
                                color="primary" class="mt-2" prepend-icon="mdi-email-fast-outline"
                                @click="openSignRequest('ticket', a.purchaseIds[0], a.name, a.adultWaiverNeeded ? a.riderEmail : a.guardianEmail, a.isMinor, a.adultWaiverNeeded)">
                                Send signing link{{ a.adultWaiverNeeded ? ' to the rider' : a.isMinor ? ' to a guardian' : '' }}
                            </v-btn>
                            <v-btn v-if="(a.canRequestSignature && a.purchaseIds.length) || (a.photoPurchaseId && a.riderPhotoMode !== 'off' && !a.hasPhoto)"
                                size="small" variant="text" class="mt-2 ml-1" prepend-icon="mdi-tablet-cellphone"
                                @click="openSendToKiosk('ticket', (a.canRequestSignature ? a.purchaseIds[0] : a.photoPurchaseId)!, a.name,
                                    !!a.canRequestSignature, !!a.photoPurchaseId && a.riderPhotoMode !== 'off', a.photoPurchaseId)">
                                Send to kiosk
                            </v-btn>
                            <!-- Yearly birthdate check: due (prompt) or blocking (required). -->
                            <v-alert v-if="a.birthdateCheckNeeded" density="compact" variant="tonal"
                                :type="a.birthdateBlocked ? 'error' : 'warning'" class="mt-2"
                                icon="mdi-card-account-details-outline">
                                <div class="d-flex align-center flex-wrap ga-2">
                                    <span>{{ birthdateCheckText(a.idVerifiedLocalDate) }}</span>
                                    <v-spacer></v-spacer>
                                    <v-btn v-if="a.verifyPurchaseId" size="small" color="primary" variant="flat"
                                        @click="openVerifyRider(a)">Verify ID</v-btn>
                                </div>
                            </v-alert>
                            <!-- Rider photo on file (tenant setting): the photo, or "No photo on file". -->
                            <div v-if="a.riderPhotoMode !== 'off' && a.audience === 'rider'"
                                 class="d-flex align-center flex-wrap ga-3 mt-2">
                                <GateRiderPhoto v-if="a.hasPhoto" :source="a.photoSource" :rider-photo-id="a.riderPhotoId"
                                    :photo-url="a.photoUrl" :version="a.photoTakenAtUtc" :size="72"
                                    :alt="`Photo of ${a.name || 'the rider'}`" />
                                <v-alert v-else density="compact" variant="tonal" class="flex-grow-1"
                                    :type="a.photoBlocked ? 'error' : 'warning'" icon="mdi-account-box-outline">
                                    No photo on file{{ a.photoBlocked ? ': take one to check them in.' : '.' }}
                                </v-alert>
                                <v-btn v-if="a.photoPurchaseId" size="small" prepend-icon="mdi-camera"
                                    :color="a.hasPhoto ? undefined : 'primary'" :variant="a.hasPhoto ? 'text' : 'flat'"
                                    @click="openTakePhoto('ticket', a.photoPurchaseId, a.name)">
                                    {{ a.hasPhoto ? 'Replace photo' : 'Take photo' }}
                                </v-btn>
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
                    <GateRiderPhoto v-if="pass.hasPhoto && pass.photoSource !== 'pass'" :source="pass.photoSource"
                        :rider-photo-id="pass.riderPhotoId" :photo-url="pass.photoUrl" :version="pass.photoTakenAtUtc"
                        :size="96" />
                    <v-avatar v-else-if="pass.photoDataUrl" size="96" rounded="lg">
                        <v-img :src="pass.photoDataUrl" cover></v-img>
                    </v-avatar>
                    <v-avatar v-else size="96" rounded="lg" style="background: var(--rp-raise)">
                        <v-icon size="48" class="text-muted2">mdi-account</v-icon>
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
                    <v-chip :color="pass.waiverSigned ? 'success' : 'error'" variant="tonal" size="small"
                        :prepend-icon="pass.waiverSigned ? 'mdi-check-circle' : 'mdi-alert-circle'">
                        Waiver {{ pass.waiverSigned ? 'signed' : 'not signed' }}
                    </v-chip>

                    <v-tooltip :text="idChipTooltip" location="top">
                        <template #activator="{ props }">
                            <v-chip v-bind="props" :color="pass.idVerified ? 'success' : 'error'" variant="tonal"
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

                <!-- Waiver verdict for the holder: age today, who signed, and why it doesn't count
                     (a minor's own signature). Same decision admission makes. -->
                <div v-if="pass.holderAge != null || pass.birthdateUnknown || pass.waiverSignedByGuardian"
                     class="text-caption text-medium-emphasis mt-2 d-flex align-center flex-wrap ga-2">
                    <span v-if="pass.holderAge != null">Age {{ pass.holderAge }}</span>
                    <v-chip v-if="pass.holderIsMinor" size="x-small" color="info">Minor</v-chip>
                    <span v-if="pass.waiverSignedByGuardian" class="d-inline-flex align-center ga-1"
                          style="color: rgb(var(--v-theme-info))">
                        <v-icon icon="mdi-shield-account" size="14"></v-icon>
                        Signed by guardian{{ pass.waiverGuardianName ? `: ${pass.waiverGuardianName}` : '' }}
                    </span>
                    <span v-else-if="pass.holderIsMinor && pass.waiverSignerName">
                        Signed by {{ pass.waiverSignerName }} themselves
                    </span>
                    <span v-if="pass.birthdateUnknown" class="text-warning d-inline-flex align-center ga-1">
                        <v-icon icon="mdi-alert-outline" size="14"></v-icon>
                        No date of birth on file: check their ID to confirm they're 18 or over
                    </span>
                </div>
                <!-- Yearly birthdate check: due (prompt) or blocking (required). -->
                <!-- Rider photo on file: none yet (prompt mode admits), or replace the one on file. -->
                <v-alert v-if="pass.photoNeeded && !pass.photoBlocked" type="warning" variant="tonal"
                    density="compact" class="mt-3" icon="mdi-account-box-outline">
                    <div class="d-flex align-center flex-wrap ga-2">
                        <span>No photo on file.</span>
                        <v-spacer></v-spacer>
                        <v-btn size="small" variant="text" prepend-icon="mdi-tablet-cellphone"
                            @click="openSendToKiosk('season_pass', pass.id, pass.holderName || pass.purchaserName, false, true)">Send to kiosk</v-btn>
                        <v-btn size="small" color="primary" variant="flat" prepend-icon="mdi-camera"
                            @click="openTakePhoto('season_pass', pass.id, pass.holderName || pass.purchaserName)">Take photo</v-btn>
                    </div>
                </v-alert>
                <v-btn v-else-if="pass.riderPhotoMode !== 'off' && pass.hasPhoto" size="small" variant="text"
                    class="mt-2 px-0" prepend-icon="mdi-camera"
                    @click="openTakePhoto('season_pass', pass.id, pass.holderName || pass.purchaserName)">
                    Replace photo
                </v-btn>
                <v-alert v-if="pass.birthdateCheckNeeded && !pass.birthdateBlocked" type="warning" variant="tonal"
                    density="compact" class="mt-3" icon="mdi-card-account-details-outline">
                    <div class="d-flex align-center flex-wrap ga-2">
                        <span>{{ birthdateCheckText(pass.idVerifiedLocalDate) }}</span>
                        <v-spacer></v-spacer>
                        <v-btn size="small" color="primary" variant="flat" @click="openVerifyId">Verify ID</v-btn>
                    </div>
                </v-alert>
                <v-alert v-if="pass.waiverBlockReason && pass.birthdateBlocked" type="error" variant="tonal"
                    density="compact" class="mt-3" icon="mdi-card-account-details-outline">
                    <div class="d-flex align-center flex-wrap ga-2">
                        <span>{{ pass.waiverBlockReason }}</span>
                        <v-spacer></v-spacer>
                        <v-btn size="small" color="primary" variant="flat" @click="openVerifyId">Verify ID</v-btn>
                    </div>
                </v-alert>
                <v-alert v-else-if="pass.waiverBlockReason && pass.photoBlocked" type="error" variant="tonal"
                    density="compact" class="mt-3" icon="mdi-account-box-outline">
                    <div class="d-flex align-center flex-wrap ga-2">
                        <span>{{ pass.waiverBlockReason }}</span>
                        <v-spacer></v-spacer>
                        <v-btn size="small" color="primary" variant="flat" prepend-icon="mdi-camera"
                            @click="openTakePhoto('season_pass', pass.id, pass.holderName || pass.purchaserName)">Take photo</v-btn>
                    </div>
                </v-alert>
                <v-alert v-else-if="pass.waiverBlockReason" type="error" variant="tonal" density="compact" class="mt-3">
                    {{ pass.waiverBlockReason }}
                    <div class="mt-2">
                        <v-btn size="small" variant="tonal" color="primary" prepend-icon="mdi-email-fast-outline"
                            @click="openSignRequest('season_pass', pass.id, pass.holderName || pass.purchaserName, pass.adultWaiverNeeded ? pass.riderEmail : pass.guardianEmail, !!pass.holderIsMinor, pass.adultWaiverNeeded)">
                            Send signing link{{ pass.adultWaiverNeeded ? ' to the rider' : pass.holderIsMinor ? ' to a guardian' : '' }}
                        </v-btn>
                        <v-btn size="small" variant="text" class="ml-1" prepend-icon="mdi-tablet-cellphone"
                            @click="openSendToKiosk('season_pass', pass.id, pass.holderName || pass.purchaserName, true, pass.riderPhotoMode !== 'off')">
                            Send to kiosk
                        </v-btn>
                    </div>
                </v-alert>

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

                <!-- ── Buddy passes ────────────────────────────────────────────
                     Only rendered when the product grants them. The holder is standing
                     here (we scanned their pass), which is the whole precondition. -->
                <template v-if="pass.buddyPass">
                    <v-divider class="my-3"></v-divider>
                    <div class="d-flex align-center ga-2 flex-wrap">
                        <v-icon size="18" color="primary">mdi-account-multiple-plus</v-icon>
                        <span class="text-body-2">
                            <strong>{{ pass.buddyPass.remaining }}</strong> of
                            {{ pass.buddyPass.total }} buddy passes left
                        </span>
                        <v-chip v-if="pass.buddyPass.goodFor.length" size="x-small" variant="tonal">
                            <v-tooltip activator="parent" location="top">
                                Good for: {{ pass.buddyPass.goodFor.join(', ') }}
                            </v-tooltip>
                            {{ pass.buddyPass.goodFor.join(', ') }}
                        </v-chip>
                        <v-spacer></v-spacer>
                        <v-btn size="small" variant="tonal" color="primary"
                            :disabled="!!buddyBlock" @click="openBuddy">
                            <v-tooltip v-if="buddyBlock" activator="parent" location="top">
                                {{ buddyBlock }}
                            </v-tooltip>
                            Bring a buddy
                        </v-btn>
                    </div>
                    <p v-if="buddyBlock" class="text-caption text-medium-emphasis mt-1 mb-0">
                        {{ buddyBlock }}
                    </p>
                </template>
            </v-card-text>
        </v-card>

        <!-- ── Bring a buddy ───────────────────────────────────────────────── -->
        <v-dialog v-model="buddyOpen" max-width="520">
            <v-card v-if="pass">
                <v-card-title class="d-flex align-center">
                    <span>Bring a buddy</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="buddyBusy"
                        @click="buddyOpen = false"></v-btn>
                </v-card-title>
                <v-divider></v-divider>
                <v-card-text>
                    <v-alert v-if="buddyError" type="error" variant="tonal" density="compact" class="mb-3">
                        {{ buddyError }}
                    </v-alert>

                    <p class="text-body-2 mb-1">
                        Using one of <strong>{{ pass.holderName || pass.purchaserName }}</strong>'s
                        buddy passes ({{ pass.buddyPass?.remaining }} left).
                    </p>
                    <p class="text-caption text-medium-emphasis mb-3">
                        The guest needs an account. Look them up, or create one here.
                    </p>

                    <!-- Step 1: the guest -->
                    <div v-if="!buddyRider" class="d-flex ga-2 align-start">
                        <v-text-field v-model="buddyEmail" label="Guest email" density="compact"
                            hide-details style="flex: 1" @keyup.enter="findBuddy"></v-text-field>
                        <v-btn :loading="buddyFinding" :disabled="!buddyEmail.trim()"
                            @click="findBuddy">Find</v-btn>
                    </div>
                    <div v-else class="d-flex align-center ga-2 mb-2">
                        <v-icon size="18" color="success">mdi-account-check</v-icon>
                        <span class="text-body-2">{{ buddyRider.firstName }} {{ buddyRider.lastName }}</span>
                        <span class="text-caption text-medium-emphasis">{{ buddyRider.email }}</span>
                        <v-spacer></v-spacer>
                        <v-btn variant="text" size="small" @click="clearBuddyRider">Change</v-btn>
                    </div>

                    <!-- Step 1b: no account yet, so make one at the window -->
                    <template v-if="buddyNotFound && !buddyRider">
                        <v-alert type="info" variant="tonal" density="compact" class="mt-3 mb-2">
                            No account for that email. Create one to admit them.
                        </v-alert>
                        <div class="d-flex ga-2">
                            <v-text-field v-model="buddyFirst" label="First name" density="compact"
                                hide-details style="flex: 1"></v-text-field>
                            <v-text-field v-model="buddyLast" label="Last name" density="compact"
                                hide-details style="flex: 1"></v-text-field>
                        </div>
                        <v-text-field v-model="buddyDob" type="date" label="Date of birth"
                            density="compact" hide-details class="mt-4"></v-text-field>
                        <div class="d-flex ga-2 mt-4">
                            <v-text-field v-model="buddyEcName" label="Emergency contact"
                                density="compact" hide-details style="flex: 1"></v-text-field>
                            <v-text-field v-model="buddyEcPhone" label="Their phone"
                                density="compact" hide-details style="flex: 1"></v-text-field>
                        </div>
                        <v-btn class="mt-3" color="primary" size="small" :loading="buddyCreating"
                            :disabled="!canCreateBuddy" @click="createBuddy">Create account</v-btn>
                    </template>

                    <!-- Step 2: what admits them -->
                    <template v-if="buddyRider">
                        <v-divider class="my-3"></v-divider>
                        <div v-if="buddyEligibleEvents.length === 0" class="text-body-2 text-medium-emphasis">
                            Nothing running today that this buddy pass is good for.
                            <span v-if="pass.buddyPass?.goodFor.length">
                                It covers: {{ pass.buddyPass.goodFor.join(', ') }}.
                            </span>
                        </div>
                        <template v-else>
                            <v-select v-model="buddyEventId" :items="buddyEventItems" label="Event"
                                density="compact" hide-details></v-select>
                            <v-select v-model="buddyTierId" :items="buddyTierItems" label="Entry option"
                                density="compact" hide-details class="mt-4"
                                :loading="buddyTiersLoading" :disabled="!buddyEventId"></v-select>
                            <p class="text-caption text-medium-emphasis mt-2 mb-0">
                                They'll still need to sign the waiver and add their details before the
                                gate will pass them.
                            </p>
                        </template>
                    </template>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" :disabled="buddyBusy" @click="buddyOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="buddyRedeeming"
                        :disabled="!buddyRider || !buddyTierId" @click="redeemBuddy">
                        Use a buddy pass
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- ── Verify ID / age ─────────────────────────────────────────────── -->
        <v-dialog v-model="verifyIdOpen" max-width="440">
            <v-card v-if="verifyTarget">
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
                    <div class="text-h6">{{ verifyTarget.name }}</div>
                    <p class="text-caption text-medium-emphasis mb-0">
                        This is recorded against the rider, so they won't be asked again on later scans
                        <template v-if="branding.birthdateVerifyMode !== 'off'">
                            for {{ branding.birthdateVerifyValidDays }} days</template>.
                    </p>

                    <v-text-field v-model="verifyDob" type="date" label="Date of birth on the ID"
                        density="compact" class="mt-4"
                        :hint="verifyDobHint" persistent-hint
                        :error-messages="verifyDobError ? [verifyDobError] : []"></v-text-field>

                    <p v-if="verifyAge != null" class="text-body-2 mt-2 mb-0">
                        That makes them <strong>{{ verifyAge }}</strong> today.
                        <template v-if="verifyAge < 18">A parent or guardian has to sign their waiver.</template>
                    </p>
                    <v-alert v-if="verifyDobMismatch" type="warning" variant="tonal" density="compact" class="mt-3">
                        This differs from the birthdate on file
                        ({{ calendarText(verifyTarget.recordedBirthdate!) }}). The date on the ID
                        is what counts from now on.
                    </v-alert>

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

        <!-- Take / replace a rider photo: saved for the rider, so later visits need none. -->
        <v-dialog v-model="takePhotoOpen" max-width="420">
            <v-card v-if="takePhotoTarget">
                <v-card-title>{{ takePhotoTarget.name || 'Rider' }}: photo</v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-3">
                        Take a clear photo of the rider's face (camera on a tablet or phone, or pick a file).
                        It is kept for this rider, so later visits and events need none.
                    </p>
                    <PhotoCapture v-model="takePhotoData" button-label="Take photo" />
                    <div v-if="takePhotoError" class="text-error text-body-2 mt-3">{{ takePhotoError }}</div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="takingPhoto" @click="takePhotoOpen = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="takingPhoto" :disabled="!takePhotoData" @click="submitTakePhoto">
                        Save photo
                    </v-btn>
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

        <!-- Waiver fix-up: email the waiver signing link (to a parent / guardian for a minor). Once
             signed, the signature is linked to this ticket / pass and a re-scan admits them. -->
        <SendToKioskDialog v-model="kioskOpen" :target="kioskTarget" :rider-name="kioskRider"
            :allow-waiver="kioskAllowWaiver" :allow-photo="kioskAllowPhoto"
            @sent="id => kioskWatch.track(id, kioskKind)" @done="onKioskDialogDone" />

        <v-dialog v-model="signRequestOpen" max-width="460">
            <v-card>
                <v-card-title>Send a waiver signing link</v-card-title>
                <v-card-text>
                    <p class="text-body-2 text-medium-emphasis mb-3">
                        <template v-if="signRequestAdult">
                            {{ signRequestName || 'This rider' }} has turned 18 since their waiver was signed, so they
                            have to sign it themselves as an adult. Send the link to them, not a parent.
                        </template>
                        <template v-else-if="signRequestForMinor">
                            {{ signRequestName || 'This rider' }} is under 18, so a parent or guardian has to
                            sign. The link opens the waiver for {{ signRequestName || 'the rider' }}; the guardian
                            signs on their behalf.
                        </template>
                        <template v-else>
                            The link opens the waiver for {{ signRequestName || 'the rider' }} to sign.
                        </template>
                    </p>
                    <v-text-field v-model="signRequestEmail" type="email" density="compact" autofocus
                        :label="signRequestAdult ? 'Rider\'s email' : signRequestForMinor ? 'Parent / guardian email' : 'Email'"
                        :hint="!signRequestSuggested ? '' : signRequestAdult ? 'The rider\'s own email on file.'
                            : 'The buyer on file. Change it if the guardian is someone else.'"
                        persistent-hint @keyup.enter="sendSignRequest"></v-text-field>
                    <div v-if="signRequestError" class="text-error text-body-2 mt-2">{{ signRequestError }}</div>
                    <div v-if="signRequestResult" class="mt-3">
                        <div class="text-body-2">
                            {{ signRequestResult.sent
                                ? `Sent to ${signRequestResult.recipientEmail}.`
                                : signRequestAdult
                                    ? 'Email isn’t available here, so open this link on the rider’s phone or this device:'
                                    : 'Email isn’t available here, so open this link on the guardian’s phone or this device:' }}
                        </div>
                        <div class="d-flex align-center ga-2 mt-1">
                            <div class="text-caption flex-grow-1" style="word-break: break-all">{{ signRequestResult.link }}</div>
                            <v-btn size="small" variant="tonal" prepend-icon="mdi-content-copy"
                                @click="copyLink(signRequestResult.link)">{{ linkCopied ? 'Copied' : 'Copy link' }}</v-btn>
                        </div>
                        <div class="text-caption text-medium-emphasis mt-2">
                            Once it’s signed, look them up again to check them in.
                        </div>
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn :disabled="signRequestBusy" @click="signRequestOpen = false">
                        {{ signRequestResult ? 'Done' : 'Cancel' }}
                    </v-btn>
                    <v-btn v-if="!signRequestResult" color="primary" :loading="signRequestBusy"
                        @click="sendSignRequest">Send</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick, onBeforeUnmount } from 'vue'
import dayjs from 'dayjs'
import { Html5Qrcode } from 'html5-qrcode'
import { TicketService, type OrderLookup, type OrderItem, type OrderWaiverAttendee, type OrderSignature,
    type GateSearchResult, type GateWaiverSignRequestResult } from '@/services/TicketService'
import { branding } from '@/stores/branding'
import { WristbandService } from '@/services/WristbandService'
import { SeasonPassService, type PassLookup } from '@/services/SeasonPassService'
import { CounterService, type CounterRider } from '@/services/CounterService'
import PhotoCapture from '@/components/PhotoCapture.vue'
import GateRiderPhoto from '@/components/GateRiderPhoto.vue'
import KioskSignupsPanel from '@/components/KioskSignupsPanel.vue'
import SendToKioskDialog from '@/components/SendToKioskDialog.vue'
import type { KioskPushTarget } from '@/services/KioskService'
import { useKioskPushWatch } from '@/composables/useKioskPushWatch'
import { useCopyFeedback } from '@/composables/useCopyFeedback'
import { useGateScanner, parseGateScan, extractGateToken, isPlainWords, type GateScan,
    type GateScanSource } from '@/composables/useGateScanner'

const service = new TicketService()
const wristbands = new WristbandService()
const seasonPasses = new SeasonPassService()
const counter = new CounterService()

const manualInput = ref('')
const searchField = ref<{ focus: () => void } | null>(null)
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

// ── Buddy passes ─────────────────────────────────────────────────────────────
// The holder is present by construction: we only get here from scanning their pass.
const buddyOpen = ref(false)
const buddyEmail = ref('')
const buddyRider = ref<CounterRider | null>(null)
const buddyNotFound = ref(false)
const buddyFinding = ref(false)
const buddyCreating = ref(false)
const buddyRedeeming = ref(false)
const buddyError = ref<string | null>(null)
const buddyFirst = ref('')
const buddyLast = ref('')
const buddyDob = ref('')
const buddyEcName = ref('')
const buddyEcPhone = ref('')
const buddyEventId = ref<string | null>(null)
const buddyTierId = ref<string | null>(null)
const buddyTiers = ref<Array<{ id: string; name: string; priceCents: number }>>([])
const buddyTiersLoading = ref(false)

const buddyBusy = computed(() => buddyFinding.value || buddyCreating.value || buddyRedeeming.value)

// Why the button is unavailable, said once so the tooltip and the caption agree.
const buddyBlock = computed(() => {
    const b = pass.value?.buddyPass
    if (!b) return null
    if (b.remaining <= 0) return `All ${b.total} buddy passes have been used this season.`
    // Phase 1 redeems the free perk end to end; a partial discount has to be priced through
    // the counter's ticket path, so the server refuses it and the button says so up front.
    if (!b.isFree) return "Discounted buddy passes can't be redeemed here yet."
    if (b.goodFor.length === 0) return 'These buddy passes are not set up for any event type yet.'
    return null
})

// Only today's events whose type the entitlement covers: offering the rest would mean a
// confident click followed by a server refusal.
const buddyEligibleEvents = computed(() => {
    const ids = pass.value?.buddyPass?.eventTypeIds ?? []
    return (pass.value?.todaysEvents ?? []).filter(e => ids.includes(e.eventTypeId))
})
const buddyEventItems = computed(() =>
    buddyEligibleEvents.value.map(e => ({ value: e.id, title: `${e.title} (${formatInTenant(e.startsAtUtc)})` })))
const buddyTierItems = computed(() =>
    buddyTiers.value.map(t => ({ value: t.id, title: t.name })))

const canCreateBuddy = computed(() =>
    buddyEmail.value.trim().length > 3 && buddyFirst.value.trim().length > 0
    && buddyLast.value.trim().length > 0 && !!buddyDob.value)

function resetBuddy() {
    buddyEmail.value = ''
    buddyRider.value = null
    buddyNotFound.value = false
    buddyError.value = null
    buddyFirst.value = ''
    buddyLast.value = ''
    buddyDob.value = ''
    buddyEcName.value = ''
    buddyEcPhone.value = ''
    buddyEventId.value = null
    buddyTierId.value = null
    buddyTiers.value = []
}

function openBuddy() {
    resetBuddy()
    // One eligible event is the common case, so skip a pointless choice.
    if (buddyEligibleEvents.value.length === 1) buddyEventId.value = buddyEligibleEvents.value[0].id
    buddyOpen.value = true
}

function clearBuddyRider() {
    buddyRider.value = null
    buddyNotFound.value = false
    buddyError.value = null
}

async function findBuddy() {
    const email = buddyEmail.value.trim()
    if (!email) return
    buddyFinding.value = true
    buddyError.value = null
    buddyNotFound.value = false
    try {
        const r = await counter.findRider(email)
        buddyRider.value = r.data.data
    } catch (e: any) {
        // A 404 is the ordinary "new guest" path, not an error worth alarming staff about.
        if (e.response?.status === 404) {
            buddyNotFound.value = true
        } else {
            buddyError.value = e.response?.data?.error
                ?? 'Could not look that guest up. Check the email and try again.'
        }
    } finally {
        buddyFinding.value = false
    }
}

async function createBuddy() {
    buddyCreating.value = true
    buddyError.value = null
    try {
        const r = await counter.createRider({
            email: buddyEmail.value.trim(),
            firstName: buddyFirst.value.trim(),
            lastName: buddyLast.value.trim(),
            birthdate: buddyDob.value,
            emergencyContactName: buddyEcName.value.trim() || undefined,
            emergencyContactPhone: buddyEcPhone.value.trim() || undefined,
        })
        buddyRider.value = r.data.data
        buddyNotFound.value = false
    } catch (e: any) {
        buddyError.value = e.response?.data?.error
            ?? 'Could not create that account. Nothing was saved; check the details and try again.'
    } finally {
        buddyCreating.value = false
    }
}

async function loadBuddyTiers(eventId: string) {
    buddyTiersLoading.value = true
    buddyTierId.value = null
    buddyTiers.value = []
    try {
        const r = await service.listActiveTiers(eventId)
        buddyTiers.value = r.data.data.map((t: any) => ({ id: t.id, name: t.name, priceCents: t.priceCents }))
        if (buddyTiers.value.length === 1) buddyTierId.value = buddyTiers.value[0].id
    } catch (e: any) {
        buddyError.value = e.response?.data?.error
            ?? 'Could not load the entry options for that event. Pick it again to retry.'
    } finally {
        buddyTiersLoading.value = false
    }
}

watch(buddyEventId, id => { if (id) loadBuddyTiers(id) })

async function redeemBuddy() {
    if (!pass.value || !buddyRider.value || !buddyTierId.value) return
    buddyRedeeming.value = true
    buddyError.value = null
    try {
        const r = await seasonPasses.redeemBuddyPass(
            pass.value.id, buddyRider.value.id, buddyTierId.value)
        // Reflect the spend without a full re-scan; the server is authoritative and returns it.
        if (pass.value.buddyPass) {
            pass.value.buddyPass.remaining = r.data.data.remaining
            pass.value.buddyPass.used = pass.value.buddyPass.total - r.data.data.remaining
        }
        buddyOpen.value = false
        flash(`Buddy pass used for ${buddyRider.value.firstName}. They still need to register.`, 'success')
    } catch (e: any) {
        buddyError.value = e.response?.data?.error
            ?? 'Could not use the buddy pass. Nothing was spent; try again.'
    } finally {
        buddyRedeeming.value = false
    }
}
const passToken = ref<string | null>(null)
const passEventId = ref<string | null>(null)
// The event the pass card's verdict (waiver, photo, registration) was judged against. When staff pick
// a different event, the card is looked up again for it, so the card and admission always agree.
const passJudgedEventId = ref<string | null>(null)
let passEventSeq = 0
watch(passEventId, async (id) => {
    if (!pass.value || !passToken.value || !id || id === passJudgedEventId.value) return
    const token = passToken.value
    const seq = ++passEventSeq
    try {
        const r = await seasonPasses.lookupByToken(token, id)
        if (seq !== passEventSeq || passToken.value !== token || passEventId.value !== id) return
        pass.value = (r.data as any).data
        passJudgedEventId.value = pass.value?.judgedEventId ?? id
    } catch (err: any) {
        flash(err.response?.data?.error || 'Couldn\'t check the pass for that event. Pick it again.', 'error')
    }
})
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
    const token = extractGateToken(decodedText)
    if (!token) return
    await stopScan()
    openedResultKey.value = null
    await loadOrder(token)
}

// ── Keyboard-wedge scanners ────────────────────────────────────────────────────────────────
// A scan lands whether nothing is focused, a button is, or the search / paste box is (both carry
// data-gate-scan). Other fields, like the band link or buddy email boxes, keep their keystrokes.
// Paused while a dialog is up so a stray scan can't swap the order out from under it.
useGateScanner({
    onScan: (scan, raw, source) => { void handleScan(scan, raw, source) },
    wristbandsEnabled: () => !!branding.wristbandsEnabled,
    enabled: () => !buddyOpen.value && !verifyIdOpen.value && !signatureOpen.value && !bandDialogOpen.value
        && !signRequestOpen.value && !takePhotoOpen.value,
})

// A scan that arrives mid-lookup or mid-check-in waits; only the latest one is kept.
let queuedScan: { scan: GateScan; raw: string; source: GateScanSource } | null = null

// One entry point for scans and pastes: a ticket/pass token, a /redeem/{token} link, or (when the
// tenant uses them) a wristband code.
// - search box: the scan's characters landed in it, so it goes back to the term its results
//   belong to; a "band code" that resolves to nobody was really a number typed fast, so search it.
// - paste box: its text stays until the lookup succeeds, so a typo can be fixed.
async function handleScan(scan: GateScan, raw: string, source: GateScanSource) {
    if (busy.value) { queuedScan = { scan, raw, source }; return }
    if (scanning.value) await stopScan()
    if (source === 'codes') manualInput.value = raw.trim()

    let ok: boolean
    if (scan.kind === 'token') {
        if (source === 'search') restoreSearchBox()
        ok = await loadOrder(scan.token)
    } else {
        const res = await resolveBand(scan.code, { quietNotFound: source === 'search' })
        if (res === 'not_found' && source === 'search') {
            searchInput.value = raw.trim()      // the watcher runs it as a search
            return
        }
        if (source === 'search') restoreSearchBox()
        ok = res === 'ok'
    }
    if (!ok) return
    if (source === 'codes') manualInput.value = ''
    // A scanned pass that is also in the result list gets its row marked on admission.
    openedResultKey.value = null
    if (scan.kind === 'token' && pass.value) {
        const row = searchResults.value.find(r => r.kind === 'season_pass' && r.anchorToken === scan.token)
        if (row) openedResultKey.value = resultKey(row)
    }
}

function restoreSearchBox() {
    if (searchTimer) clearTimeout(searchTimer)
    searchInput.value = searchedFor.value
}

// Enter in the paste box with text that isn't a code (codes are caught by the scanner first).
function onManualEnter(e: KeyboardEvent) {
    if (e.defaultPrevented) return
    void lookupManual()
}

async function lookupManual() {
    const raw = manualInput.value
    const scan = parseGateScan(raw, !!branding.wristbandsEnabled)
    if (!scan) {
        flash(branding.wristbandsEnabled ? 'No token, link or wristband code found in input.' : 'No token found in input.', 'error')
        return
    }
    await handleScan(scan, raw, 'codes')
}

// Enter in the search box: tokens and links were already taken by the scanner, so this is a name.
function onSearchEnter(e: KeyboardEvent) {
    if (e.defaultPrevented) return
    void runSearch()
}

// After a successful check-in: the paste box empties, the result list stays (its row now shows the
// check-in) and the search box is focused with its text selected, so typing or scanning the next
// person replaces it. Focus only where a physical keyboard is likely (a fine pointer): on a phone or
// tablet it would pop the on-screen keyboard over the card, and the page-level capture works
// without focus anyway.
async function readyForNextScan() {
    manualInput.value = ''
    await nextTick()
    if (window.matchMedia?.('(pointer: fine)').matches) {
        searchField.value?.focus()
        const el = document.activeElement
        if (el instanceof HTMLInputElement) el.select()
    } else {
        (document.activeElement as HTMLElement | null)?.blur?.()
    }
}

// Text that is (or is on its way to being) a token or link rather than a name or phone number.
function looksLikeCode(term: string): boolean {
    return !!extractGateToken(term) || /^[0-9a-f]{8}-/i.test(term) || /^https?:\/\//i.test(term)
        || term.includes('/redeem')
}

// Debounced as the operator types: a gate queue is not the place to make someone press a button.
// Under 3 characters the server rejects the query, so don't even ask. A token or link arriving in
// the box is not a name: don't search it and don't wipe the results it is about to be scanned over.
watch(searchInput, () => {
    if (searchTimer) clearTimeout(searchTimer)
    const term = (searchInput.value ?? '').trim()
    if (looksLikeCode(term) && !isPlainWords(term)) return
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

// The result row whose order / pass is open, so a check-in can mark that row in place.
const openedResultKey = ref<string | null>(null)
function resultKey(r: Pick<GateSearchResult, 'kind' | 'anchorToken'>): string {
    return `${r.kind}:${r.anchorToken}`
}
function markOpenedResult(update: (r: GateSearchResult) => void) {
    const row = searchResults.value.find(x => resultKey(x) === openedResultKey.value)
    if (row) update(row)
}

// Picking a result is the same as scanning that rider's QR: a ticket's token anchors the whole
// order, a season pass's token opens the pass card.
async function openResult(r: GateSearchResult) {
    if (busy.value) return
    let ok: boolean
    if (r.kind === 'season_pass') {
        const seq = ++lookupSeq
        loading.value = true
        try {
            const res = await tryLoadPass(r.anchorToken, seq)
            if (res === 'not_found') flash('That pass could not be found. Search again.', 'error')
            ok = res === 'ok'
        } finally { if (seq === lookupSeq) loading.value = false }
    } else {
        ok = await loadOrder(r.anchorToken)
    }
    if (ok) openedResultKey.value = resultKey(r)
}

function passResultSummary(r: GateSearchResult): string {
    const parts = [r.holds]
    if (r.productKind === 'credits' && r.creditsRemaining != null)
        parts.push(`${r.creditsRemaining} ${r.creditsRemaining === 1 ? 'ride' : 'rides'} left`)
    if (r.validToDate) parts.push(`valid to ${dayjs(r.validToDate).format('MMM D, YYYY')}`)
    return parts.join(' · ')
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

// Anything in flight that a new scan must not race: a lookup, a redeem, an admission, a band lookup.
// (Declared here, after the last flag it reads.) A scan that arrived meanwhile runs when it clears.
const busy = computed(() => loading.value || redeeming.value || admitting.value || bandLookupBusy.value)
watch(busy, isBusy => {
    if (isBusy || !queuedScan) return
    const next = queuedScan
    queuedScan = null
    void handleScan(next.scan, next.raw, next.source)
})

async function lookupBand() {
    const code = bandLookupInput.value.trim()
    if (!code) return
    if (await resolveBand(code) === 'ok') bandLookupInput.value = ''
}

// Band code → the wearer's order or pass (loadOrder falls through to the pass card for a pass
// token). Shared by the band box and scanner input. quietNotFound leaves the "no such band" case to
// the caller (the search box treats it as a search instead).
async function resolveBand(code: string, opts: { quietNotFound?: boolean } = {}): Promise<'ok' | 'not_found' | 'failed'> {
    bandLookupBusy.value = true
    try {
        const r = await wristbands.resolve(code)
        return await loadOrder(r.data.data.redemptionToken) ? 'ok' : 'failed'
    } catch (err: any) {
        const notFound = err.response?.status === 404
        if (notFound && opts.quietNotFound) return 'not_found'
        flash(notFound
            ? (err.response?.data?.error || 'No entrant is linked to that band.')
            : (err.response?.data?.error || 'Could not look up that band. Check the connection and try again.'), 'error')
        return notFound ? 'not_found' : 'failed'
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

// Every order / pass lookup takes a sequence number; a response that comes back after a newer
// lookup started is dropped, so a slow answer for the previous person never overwrites the card
// for the one standing at the gate now.
let lookupSeq = 0

// True when an order (or, for a pass token, the pass card) is now showing.
async function loadOrder(token: string): Promise<boolean> {
    const seq = ++lookupSeq
    try {
        loading.value = true
        const r = await service.orderLookup(token)
        if (seq !== lookupSeq) return false
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
        return true
    } catch (err: any) {
        if (seq !== lookupSeq) return false
        // Only a real 404 means the QR/token is invalid — and even then it may be a SEASON PASS
        // token, which lives in a different subsystem than orders. Try that before giving up.
        // A network blip or server error must NOT read as "not found" — that would turn away a
        // paying customer holding a valid ticket.
        const status = err.response?.status
        if (status === 404) {
            const res = await tryLoadPass(token, seq)
            if (res === 'ok') return true
            if (res !== 'not_found') return false     // already surfaced, or superseded
        }
        const msg = status === 404
            ? (err.response?.data?.error || 'Order not found. Double-check the code and rescan.')
            : (err.response?.data?.error || 'Couldn’t look up the order. Check the connection and rescan.')
        flash(msg, 'error')
        order.value = null
        orderToken.value = null
        selectedIds.value = []
        return false
    } finally {
        if (seq === lookupSeq) loading.value = false
    }
}

// 'ok': the pass card is showing. 'not_found': no pass has this token (the caller words that).
// 'error': the lookup failed and was already surfaced. 'stale': a newer lookup took over.
// keepEventId: a re-read of the same pass (after an ID check or an admission) stays on the event staff
// picked and is judged against it; a fresh scan passes none.
async function tryLoadPass(token: string, seq = ++lookupSeq, keepEventId: string | null = null): Promise<'ok' | 'not_found' | 'error' | 'stale'> {
    try {
        const r = await seasonPasses.lookupByToken(token, keepEventId)
        if (seq !== lookupSeq) return 'stale'
        pass.value = (r.data as any).data
        passToken.value = token
        order.value = null
        orderToken.value = null
        selectedIds.value = []
        const events = pass.value?.todaysEvents ?? []
        passEventId.value = keepEventId && events.some(e => e.id === keepEventId) ? keepEventId
            : events.length === 1 ? events[0].id : null
        // The event the server judged the card against (the one sent, else the event the holder was
        // admitted to today, else the single event running today).
        passJudgedEventId.value = pass.value?.judgedEventId ?? null
        return 'ok'
    } catch (err: any) {
        if (seq !== lookupSeq) return 'stale'
        if (err.response?.status === 404) return 'not_found'      // genuinely unknown token
        flash(err.response?.data?.error || 'Couldn’t look up the pass. Check the connection and rescan.', 'error')
        return 'error'
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
    // The waiver verdict (incl. a minor's own signature) the server will enforce on admission.
    if (pass.value.waiverBlockReason) return pass.value.waiverBlockReason
    return null
})

// ── Waiver signing link (gate fix-up for a waiver block) ─────────────────────
const signRequestOpen = ref(false)
const signRequestBusy = ref(false)
const signRequestError = ref('')
const signRequestEmail = ref('')
const signRequestSuggested = ref(false)
const signRequestName = ref<string | null>(null)
const signRequestForMinor = ref(false)
// Turned 18 since their waiver: the link goes to the RIDER, who signs as an adult.
const signRequestAdult = ref(false)
// Copy a signing link (shown when email isn't available) so staff can paste it into a text message.
const { copied: linkCopied, copy: copyLink, reset: resetLinkCopied } = useCopyFeedback()
const signRequestTarget = ref<{ kind: 'ticket' | 'season_pass'; purchaseId: string } | null>(null)
const signRequestResult = ref<GateWaiverSignRequestResult | null>(null)

function openSignRequest(kind: 'ticket' | 'season_pass', purchaseId: string, name: string | null,
    guardianEmail: string | null, forMinor: boolean, adult = false) {
    signRequestTarget.value = { kind, purchaseId }
    signRequestName.value = name
    signRequestForMinor.value = forMinor && !adult
    signRequestAdult.value = adult
    signRequestEmail.value = guardianEmail ?? ''
    signRequestSuggested.value = !!guardianEmail
    signRequestError.value = ''
    signRequestResult.value = null
    resetLinkCopied()
    signRequestOpen.value = true
}

async function sendSignRequest() {
    if (!signRequestTarget.value || signRequestResult.value) return
    const email = signRequestEmail.value.trim()
    if (!email.includes('@')) { signRequestError.value = 'Enter an email address.'; return }
    signRequestBusy.value = true
    signRequestError.value = ''
    try {
        // For a pass, the event the card's verdict was judged against (whatever the server judged it
        // against, e.g. the event the holder was admitted to today), so the link asks for the document
        // that is actually missing.
        const eventId = signRequestTarget.value.kind === 'season_pass' ? passJudgedEventId.value : null
        const r = await service.gateWaiverSignRequest({ ...signRequestTarget.value, email, eventId })
        resetLinkCopied()
        signRequestResult.value = r.data.data
    } catch (err: any) {
        signRequestError.value = err.response?.data?.error || 'Couldn’t send the link. Check the connection and try again.'
    } finally { signRequestBusy.value = false }
}

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

// Who the dialog is verifying: the pass holder on the pass card, or one rider in the order's
// waiver panel (recorded against their rider ticket, the rest of their tickets and their account).
const verifyTarget = ref<{ kind: 'pass' | 'ticket'; name: string; recordedBirthdate: string | null;
    purchaseId: string | null } | null>(null)

function openVerifyDialog(target: NonNullable<typeof verifyTarget.value>) {
    verifyTarget.value = target
    // Seed from what the rider gave at registration: the common case is the document simply
    // confirming it, so staff only change it when the two disagree.
    verifyDob.value = target.recordedBirthdate ? calendarDate(target.recordedBirthdate) : ''
    verifyIdError.value = ''
    verifyIdOpen.value = true
}

function openVerifyId() {
    if (!pass.value) return
    openVerifyDialog({ kind: 'pass', name: pass.value.holderName || pass.value.purchaserName,
        recordedBirthdate: pass.value.holderBirthdate, purchaseId: null })
}

// Take / replace a rider photo (ticket rider or pass holder). Re-read afterwards: the gate decides.
const takePhotoOpen = ref(false)
const takingPhoto = ref(false)
const takePhotoData = ref<string | null>(null)
const takePhotoError = ref('')
const takePhotoTarget = ref<{ kind: 'ticket' | 'season_pass'; purchaseId: string; name: string | null } | null>(null)

// Send a waiver / photo step for this rider to a front-desk kiosk (Script0311); refresh when it's done.
const kioskOpen = ref(false)
const kioskTarget = ref<KioskPushTarget | null>(null)
const kioskRider = ref<string | null>(null)
const kioskAllowWaiver = ref(true)
const kioskAllowPhoto = ref(true)
const kioskKind = ref<'ticket' | 'season_pass'>('ticket')
// A registrant's waiver is fixed on their first ticket and their photo checked on their rider ticket: one
// request carries both (purchaseId for the waiver, photoPurchaseId for the photo).
function openSendToKiosk(kind: 'ticket' | 'season_pass', purchaseId: string, name: string | null,
    allowWaiver: boolean, allowPhoto: boolean, photoPurchaseId: string | null = null) {
    kioskKind.value = kind
    kioskTarget.value = kind === 'season_pass'
        ? { targetKind: 'season_pass', purchaseId, eventId: passEventId.value }
        : { targetKind: 'ticket', purchaseId, photoPurchaseId }
    kioskRider.value = name
    kioskAllowWaiver.value = allowWaiver
    kioskAllowPhoto.value = allowPhoto
    kioskOpen.value = true
}
// Closing the dialog doesn't lose the result: requests sent from here are re-checked every 5 s until they end.
const kioskWatch = useKioskPushWatch<'ticket' | 'season_pass'>(kind => { void onKioskDone(kind) })
function onKioskDialogDone(pushId: string) {
    kioskWatch.untrack(pushId)
    void onKioskDone(kioskKind.value)
}
async function onKioskDone(kind: 'ticket' | 'season_pass') {
    flash('The kiosk finished: the gate is updated.', 'success')
    await refreshAfterPhoto(kind)
}

function openTakePhoto(kind: 'ticket' | 'season_pass', purchaseId: string, name: string | null) {
    takePhotoTarget.value = { kind, purchaseId, name }
    takePhotoData.value = null
    takePhotoError.value = ''
    takePhotoOpen.value = true
}

async function submitTakePhoto() {
    const t = takePhotoTarget.value
    if (!t || !takePhotoData.value) return
    takingPhoto.value = true
    takePhotoError.value = ''
    try {
        await service.takeRiderPhoto(t.kind, t.purchaseId, takePhotoData.value)
        takePhotoOpen.value = false
        flash('Photo saved.', 'success')
        await refreshAfterPhoto(t.kind)
    } catch (err: any) {
        takePhotoError.value = err.response?.data?.error || 'Couldn\'t save the photo.'
    } finally {
        takingPhoto.value = false
    }
}

// Re-read what the gate now says after a photo, WITHOUT a fresh scan's resets: the ID attestation,
// the selected items, the open tab and the chosen pass event all stay as staff left them.
async function refreshAfterPhoto(kind: 'ticket' | 'season_pass') {
    try {
        if (kind === 'season_pass' && passToken.value) {
            const token = passToken.value
            const keepEvent = passEventId.value
            const r = await seasonPasses.lookupByToken(token, keepEvent)
            if (passToken.value !== token) return
            pass.value = (r.data as any).data
            passJudgedEventId.value = pass.value?.judgedEventId ?? keepEvent
            if (keepEvent && !pass.value?.todaysEvents.some(e => e.id === keepEvent))
                passEventId.value = pass.value?.todaysEvents.length === 1 ? pass.value.todaysEvents[0].id : null
        } else if (orderToken.value) {
            const token = orderToken.value
            const r = await service.orderLookup(token)
            if (orderToken.value !== token) return
            const fresh = (r.data as any).data as OrderLookup
            const stillThere = new Set(fresh.items.map(i => i.purchaseId))
            order.value = fresh
            selectedIds.value = selectedIds.value.filter(id => stillThere.has(id))
        }
    } catch (err: any) {
        flash(err.response?.data?.error || 'Photo saved, but the screen couldn\'t refresh. Rescan to see it.', 'warning')
    }
}

function openVerifyRider(a: OrderWaiverAttendee) {
    if (!a.verifyPurchaseId) return
    openVerifyDialog({ kind: 'ticket', name: a.name || 'This rider',
        recordedBirthdate: a.recordedBirthdate, purchaseId: a.verifyPurchaseId })
}

// The ID says something other than what the rider gave: the ID wins, and staff should see it.
const verifyDobMismatch = computed(() =>
    !!verifyTarget.value?.recordedBirthdate && !!verifyDob.value && !verifyDobError.value
    && calendarDate(verifyTarget.value.recordedBirthdate) !== verifyDob.value)

// Birthdates and "checked on" days are CALENDAR dates (the API sends yyyy-MM-dd), never instants:
// read as plain text so no browser time zone can move them a day.
function calendarDate(value: string): string {
    return value.slice(0, 10)
}
function calendarText(value: string): string {
    const [y, m, d] = calendarDate(value).split('-').map(Number)
    return dayjs(new Date(y, m - 1, d)).format('MMM D, YYYY')
}

// "Check ID: not verified since <tenant-local day> / never", for the gate banners. The day is the
// one the server's block message uses.
function birthdateCheckText(verifiedLocalDate: string | null): string {
    return verifiedLocalDate
        ? `Check ID: birthdate not verified since ${calendarText(verifiedLocalDate)}.`
        : 'Check ID: birthdate never verified.'
}

// No age (and so no "a parent or guardian has to sign") while the date is invalid or in the future.
const verifyAge = computed(() => {
    if (!verifyDob.value || verifyDobError.value) return null
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
    verifyTarget.value?.recordedBirthdate
        ? 'Pre-filled from what the rider gave at registration. Change it if the ID says otherwise.'
        : 'No birthdate on file, so read it off the ID.')

async function submitVerifyId() {
    const target = verifyTarget.value
    if (!target || !verifyDob.value || verifyDobError.value) return
    if (target.kind === 'pass' && !passToken.value) return
    if (target.kind === 'ticket' && (!orderToken.value || !target.purchaseId)) return
    verifyingId.value = true
    verifyIdError.value = ''
    try {
        if (target.kind === 'pass') await seasonPasses.verifyPassHolderId(passToken.value!, verifyDob.value)
        else await service.verifyOrderRiderId(orderToken.value!, target.purchaseId!, verifyDob.value)
        verifyIdOpen.value = false
        flash('ID and age verified.', 'success')
        // Re-read rather than patching locally: the server decides whether this stuck to the
        // rider's account, and a verified DOB that makes the rider a minor brings the guardian
        // rule into the verdict, which only the gate can say.
        if (target.kind === 'pass') await tryLoadPass(passToken.value!, ++lookupSeq, passEventId.value)
        else await loadOrder(orderToken.value!)
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
        markOpenedResult(row => { row.redeemedCount = 1 })   // the search row now reads "In today"
        await tryLoadPass(passToken.value, ++lookupSeq, passEventId.value)   // refresh credits + today's check-in state
        await readyForNextScan()
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
        const ticketIdsSent = items.filter(i => i.kind === 'event_ticket').map(i => i.purchaseId)
        await loadOrder(orderToken.value)
        if (n > 0) {
            // Mark progress on the search row this order was opened from: tickets sent that are no
            // longer redeemable today went through; nothing redeemable left means all are in.
            const stillOpen = new Set(order.value?.items.filter(i => i.isRedeemableToday).map(i => i.purchaseId) ?? [])
            const nowIn = ticketIdsSent.filter(id => !stillOpen.has(id)).length
            markOpenedResult(row => {
                row.redeemedCount = redeemableCount.value === 0
                    ? row.itemCount
                    : Math.min(row.itemCount, row.redeemedCount + nowIn)
            })
            await readyForNextScan()
        }
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
    border: 1px dashed var(--rp-line2);
    border-radius: 6px;
    margin: 0 auto;
    background: var(--rp-panel2);
}
.order-row + .order-row {
    border-top: 1px solid var(--rp-line);
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
