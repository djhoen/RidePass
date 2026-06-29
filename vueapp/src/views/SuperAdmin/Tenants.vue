<template>
    <v-container>
        <h1 class="text-h4 mb-4">Tenants</h1>

        <div class="d-flex align-center mb-3">
            <v-spacer></v-spacer>
            <v-btn variant="tonal" prepend-icon="mdi-cloud-download-outline" class="mr-2" @click="openImport">Import from stage</v-btn>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreateTenant">New Tenant</v-btn>
        </div>
        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Subdomain</th>
                        <th>Display Name</th>
                        <th style="width: 120px">Status</th>
                        <th style="width: 130px">Service charge</th>
                        <th style="width: 140px">Client type</th>
                        <th style="width: 160px">Timezone</th>
                        <th style="width: 180px">Created</th>
                        <th style="width: 160px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="t in tenants" :key="t.id">
                        <td><code>{{ t.subdomain }}</code></td>
                        <td>
                            {{ t.displayName }}
                            <v-chip v-if="!t.isPublished" size="x-small" color="warning" variant="tonal" class="ml-1">Draft</v-chip>
                        </td>
                        <td>{{ t.status }}</td>
                        <td>
                            {{ (t.serviceChargeBps / 100).toFixed(2) }}%
                            <span v-if="t.monthlyServiceChargeCapCents !== null" class="text-caption text-medium-emphasis">
                                cap ${{ (t.monthlyServiceChargeCapCents / 100).toFixed(2) }}
                            </span>
                        </td>
                        <td>
                            <v-chip size="small" variant="flat" label
                                :color="clientTypeColor(t.clientType)"
                                :prepend-icon="clientTypeIcon(t.clientType)">
                                {{ clientTypeShort(t.clientType) }}
                            </v-chip>
                        </td>
                        <td>{{ t.timezone }}</td>
                        <td>{{ formatDate(t.createdAtUtc) }}</td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" @click="openEdit(t)">Edit</v-btn>
                            <v-btn v-if="t.isPublished" variant="text" size="small"
                                :href="tenantUrl(t.subdomain)" target="_blank">Visit</v-btn>
                            <v-btn v-else variant="text" size="small" color="warning"
                                :href="previewUrl(t.subdomain)" target="_blank">Preview</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loadingTenants && tenants.length === 0">
                        <td colspan="8" class="text-center text-medium-emphasis py-8">No tenants yet.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <!-- Create tenant dialog -->
        <v-dialog v-model="createDialog" fullscreen persistent>
            <v-card class="d-flex flex-column" style="height: 100%">
                <v-card-title class="d-flex align-center">
                    <span>New Tenant</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="createDialog = false"></v-btn>
                </v-card-title>
                <v-tabs v-model="createTab" color="primary" style="flex: 0 0 auto">
                    <v-tab value="general">General</v-tab>
                    <v-tab value="features">Feature Toggles</v-tab>
                    <v-tab value="embed">Embedded Widgets</v-tab>
                </v-tabs>
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <v-window v-model="createTab">
                    <v-window-item value="general">
                        <v-row>
                            <v-col cols="12" md="6">
                                <v-text-field v-model="createForm.subdomain" label="Subdomain" density="compact"
                                    hint="lowercase, digits, hyphens" persistent-hint></v-text-field>
                            </v-col>
                            <v-col cols="12" md="6">
                                <v-autocomplete v-model="createForm.timezone" :items="timezoneOptions"
                                    item-title="title" item-value="value" label="Timezone" density="compact"></v-autocomplete>
                            </v-col>
                        </v-row>
                        <v-text-field v-model="createForm.displayName" label="Display Name" density="compact" class="mt-4"></v-text-field>
                        <v-select v-model="createForm.tenantType" :items="tenantTypeOptions"
                            label="Tenant type" density="compact"
                            hint="Drives event-type / waiver / pass-product defaults at creation. Locked after creation."
                            persistent-hint class="mt-4"></v-select>
                        <v-select v-if="createForm.tenantType === 'mountain_bike'"
                            v-model="createForm.venueCategory" :items="venueCategoryOptions"
                            item-title="title" item-value="value" label="Venue category" density="compact"
                            hint="Sets the access product + day naming: Bike park → Day Pass / Trail Day, Shuttle → Shuttle Pass / Shuttle Day, Resort → Lift Ticket / Lift Day."
                            persistent-hint class="mt-4"></v-select>
                        <v-select v-model="createForm.clientType" :items="clientTypeOptions"
                            item-title="title" item-value="value" label="Client type" density="compact" class="mt-4"
                            hint="How this track's public presence is delivered." persistent-hint></v-select>
                        <v-text-field v-if="createForm.clientType === 'custom_domain'"
                            v-model="createForm.customDomain" label="Custom domain"
                            placeholder="www.xyztrack.com" density="compact" clearable class="mt-4"></v-text-field>
                        <v-switch v-if="createForm.clientType === 'custom_domain'"
                            v-model="createForm.customDomainVerified" color="primary" inset density="compact" hide-details class="mt-2"
                            :label="createForm.customDomainVerified ? 'Domain verified — subdomain forwards to it' : 'Domain not verified (no forwarding yet)'"></v-switch>

                        <v-divider class="my-4"></v-divider>
                        <div class="text-subtitle-2 mb-1">Optional: first tenant admin</div>
                        <p class="text-caption text-medium-emphasis mb-3">
                            Leave blank to skip. A temporary password is generated and shown once.
                        </p>
                        <v-row>
                            <v-col cols="12" md="4">
                                <v-text-field v-model="createForm.adminFirstName" label="First name" density="compact"></v-text-field>
                            </v-col>
                            <v-col cols="12" md="4">
                                <v-text-field v-model="createForm.adminLastName" label="Last name" density="compact"></v-text-field>
                            </v-col>
                            <v-col cols="12" md="4">
                                <v-text-field v-model="createForm.adminEmail" type="email" label="Email" density="compact"></v-text-field>
                            </v-col>
                        </v-row>
                    </v-window-item>

                    <v-window-item value="features">
                        <p class="text-caption text-medium-emphasis mb-3">
                            Platform-level on/off for tenant features. Detailed config (gift-card limits,
                            membership price, etc.) is on the tenant's own Settings → Features page after creation.
                        </p>
                        <v-switch v-for="f in featureToggles" :key="'c-' + f.key"
                            v-model="createForm[f.key]" color="primary" inset density="compact"
                            :label="f.label" :messages="f.description"></v-switch>
                    </v-window-item>

                    <v-window-item value="embed">
                        <p class="text-caption text-medium-emphasis mb-3">
                            For tracks that keep their own website and embed RidePass widgets. You can also set this later.
                        </p>
                        <v-switch v-model="createForm.embedEnabled" color="primary" inset density="compact" hide-details
                            :label="createForm.embedEnabled ? 'Embed widgets enabled' : 'Embed widgets disabled'"></v-switch>
                        <v-combobox v-model="createForm.embedAllowedOrigins" label="Allowed embed origins"
                            placeholder="https://www.xyztrack.com" multiple chips closable-chips density="compact" class="mt-4"
                            hint="Sites allowed to embed the widgets (CSP frame-ancestors). One origin per chip. A bare domain (xyz.com) covers both xyz.com and www.xyz.com; our own properties are always allowed via global origins."
                            persistent-hint></v-combobox>
                        <v-text-field v-model="createForm.externalHomeUrl" label="External home URL"
                            placeholder="https://www.xyztrack.com" density="compact" clearable class="mt-4"
                            hint="The track's own website home. {subdomain}.ridepass.io forwards here."
                            persistent-hint></v-text-field>
                        <v-text-field v-model="createForm.externalEventsUrl" label="External events page URL"
                            placeholder="https://www.xyztrack.com/events" density="compact" clearable class="mt-4"
                            hint="Where event links on the RidePass discovery site point (falls back to the home URL)."
                            persistent-hint></v-text-field>
                        <v-select v-model="createForm.embedEventTarget" label="Apex event click goes to"
                            :items="eventTargetOptions" item-title="title" item-value="value"
                            density="compact" class="mt-4"
                            hint="Where an event click on the RidePass discovery site lands for this track."
                            persistent-hint></v-select>
                    </v-window-item>
                    </v-window>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="createDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="creating" @click="submitCreateTenant">Create</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- One-time credential reveal -->
        <v-dialog v-model="credsDialog" max-width="560" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Tenant created</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="credsDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="mb-3">
                        <strong>{{ createdResult?.displayName }}</strong>
                        (<code>{{ createdResult?.subdomain }}</code>)
                        is live.
                    </p>
                    <template v-if="createdResult?.adminTemporaryPassword">
                        <v-alert type="warning" variant="tonal" class="mb-3">
                            This is the only time the admin password is shown. Copy it now.
                        </v-alert>
                        <div class="text-body-2 mb-1"><strong>Email:</strong> {{ createdResult.adminEmail }}</div>
                        <div class="text-body-2 mb-1">
                            <strong>Temporary Password:</strong>
                            <code>{{ createdResult.adminTemporaryPassword }}</code>
                        </div>
                    </template>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn color="primary" @click="credsDialog = false">Done</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Edit tenant dialog -->
        <v-dialog v-model="editDialog" fullscreen persistent>
            <v-card v-if="editTenant" class="d-flex flex-column" style="height: 100%">
                <v-card-title class="d-flex align-center">
                    <span>
                        Edit {{ editTenant.displayName }}
                        <span class="text-medium-emphasis text-body-2">(<code>{{ editTenant.subdomain }}</code>)</span>
                    </span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="editDialog = false"></v-btn>
                </v-card-title>
                <v-tabs v-model="editTab" color="primary" style="flex: 0 0 auto">
                    <v-tab value="general">General</v-tab>
                    <v-tab value="features">Feature Toggles</v-tab>
                    <v-tab value="embed">Embedded Widgets</v-tab>
                </v-tabs>
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0">
                    <v-window v-model="editTab">
                    <v-window-item value="general">
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.displayName" label="Display name" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="3">
                            <v-select v-model="editForm.status" :items="statusOptions"
                                item-title="title" item-value="value" label="Status" density="compact"></v-select>
                        </v-col>
                        <v-col cols="12" md="3">
                            <v-autocomplete v-model="editForm.timezone" :items="timezoneOptions"
                                item-title="title" item-value="value" label="Timezone" density="compact"></v-autocomplete>
                        </v-col>
                    </v-row>

                    <v-switch v-model="editForm.isPublished" color="primary" inset density="compact" hide-details
                        :label="editForm.isPublished ? 'Published — visible in public discovery' : 'Not published — hidden from the map, featured, search, and events'"
                        class="mb-2"></v-switch>

                    <div class="text-subtitle-2 mt-2 mb-1">Billing</div>
                    <v-row class="mt-2">
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="editForm.serviceChargePct" type="number" step="0.01" min="0" max="100"
                                label="Service charge" suffix="%" density="compact"
                                hint="Flat % RidePass takes from each sale." persistent-hint></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="editForm.serviceChargeCapDollars" type="number" step="0.01" min="0"
                                label="Monthly cap (blank = none)" prefix="$" density="compact" clearable
                                hint="Once reached, 0% is taken until next UTC month." persistent-hint></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-select v-model="editForm.stripeChargeMode" :items="chargeModeItems" item-title="label"
                                item-value="value" label="Charge mode" density="compact" class="mt-4"
                                hint="Direct = charge on the track's own Stripe account (required over ~$1M/yr); our service charge rides as the Stripe application fee. The cap does not apply in direct mode."
                                persistent-hint></v-select>
                        </v-col>
                        <v-col v-if="editForm.stripeChargeMode === 'direct' && editTenant?.stripeConnectStatus !== 'active'" cols="12" md="6" class="d-flex align-center">
                            <v-alert type="warning" density="compact" variant="tonal" class="mt-4">
                                This track must connect its own Stripe account (Settings &rarr; Payments) and reach "active" before direct charges will go through.
                            </v-alert>
                        </v-col>
                        <v-col cols="12">
                            <v-btn size="small" variant="tonal" color="info" :loading="testingConnect"
                                prepend-icon="mdi-connection" @click="testStripeConnect">
                                Test Stripe connection
                            </v-btn>
                            <v-alert v-if="connectTestResult" :type="connectTestResult.ok ? 'success' : 'error'"
                                density="compact" variant="tonal" class="mt-2" closable
                                @click:close="connectTestResult = null">
                                {{ connectTestResult.message }}
                            </v-alert>
                        </v-col>
                    </v-row>

                    <div class="text-subtitle-2 mt-4 mb-3">Address</div>
                    <v-text-field v-model="editForm.addressLine" label="Address line" density="compact"></v-text-field>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.city" label="City" density="compact" class="mt-4"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="3">
                            <v-text-field v-model="editForm.region" label="State / region" density="compact" class="mt-4"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="3">
                            <v-text-field v-model="editForm.postalCode" label="Postal code" density="compact" class="mt-4"></v-text-field>
                        </v-col>
                    </v-row>
                    <v-text-field v-model="editForm.country" label="Country" density="compact" class="mt-4"></v-text-field>

                    <div class="d-flex align-center ga-2 mt-4">
                        <v-text-field v-model.number="editForm.latitude" type="number" step="0.0001"
                            label="Latitude" density="compact" hide-details></v-text-field>
                        <v-text-field v-model.number="editForm.longitude" type="number" step="0.0001"
                            label="Longitude" density="compact" hide-details></v-text-field>
                        <v-btn variant="tonal" :loading="geocoding" prepend-icon="mdi-map-search"
                            @click="lookupCoords">Look up</v-btn>
                    </div>
                    <div class="text-caption text-medium-emphasis mt-1">
                        Coordinates place the track on the apex "Tracks near you" map. "Look up" geocodes the address above.
                    </div>

                    <div class="text-subtitle-2 mt-4 mb-3">Contact</div>
                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.contactEmail" type="email" label="Contact email" density="compact"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="editForm.phone" type="tel" label="Phone" density="compact"></v-text-field>
                        </v-col>
                    </v-row>

                    <div class="text-subtitle-2 mt-4 mb-3">Deployment</div>
                    <v-select v-model="editForm.clientType" :items="clientTypeOptions"
                        item-title="title" item-value="value" label="Client type" density="compact"
                        hint="How this track's public presence is delivered." persistent-hint></v-select>
                    <v-text-field v-if="editForm.clientType === 'custom_domain'"
                        v-model="editForm.customDomain" label="Custom domain"
                        placeholder="www.xyztrack.com" density="compact" clearable class="mt-4"
                        hint="The track's own domain (host only). They point it at {subdomain}.ridepass.io via CNAME."
                        persistent-hint></v-text-field>
                    <v-switch v-if="editForm.clientType === 'custom_domain'"
                        v-model="editForm.customDomainVerified" color="primary" inset density="compact" hide-details class="mt-2"
                        :label="editForm.customDomainVerified ? 'Domain verified — subdomain forwards to it' : 'Domain not verified (no forwarding yet)'"></v-switch>
                    <p v-if="editForm.clientType === 'embedded'" class="text-caption text-medium-emphasis mt-1">
                        Configure the embed widgets and origins in the <strong>Embedded Widgets</strong> tab.
                    </p>

                    <div class="text-subtitle-2 mt-4 mb-1">LoamPassMx</div>
                    <v-text-field v-model="editForm.loampassMxDestinationId" label="LoamMx destination ID"
                        density="compact" clearable
                        hint="Set this to make the track a LoamPassMx track (riders can link their Loam Pass and redeem credits). Blank = not a LoamPassMx track."
                        persistent-hint></v-text-field>
                    </v-window-item>

                    <v-window-item value="features">
                        <p class="text-caption text-medium-emphasis mb-3">
                            Platform-level on/off for tenant features. Detailed config (gift-card limits,
                            membership price, etc.) lives on the tenant's own Settings → Features page.
                        </p>
                        <v-switch v-for="f in featureToggles" :key="'e-' + f.key"
                            v-model="editForm[f.key]" color="primary" inset density="compact"
                            :label="f.label" :messages="f.description"></v-switch>
                    </v-window-item>

                    <v-window-item value="embed">
                        <p class="text-caption text-medium-emphasis mb-3">
                            For tracks that keep their own website and embed RidePass widgets. Enable embedding,
                            list the site origins allowed to frame the widgets, then share the snippet below.
                        </p>
                        <v-switch v-model="editForm.embedEnabled" color="primary" inset density="compact" hide-details
                            :label="editForm.embedEnabled ? 'Embed widgets enabled' : 'Embed widgets disabled'"></v-switch>
                        <v-combobox v-model="editForm.embedAllowedOrigins" label="Allowed embed origins"
                            placeholder="https://www.xyztrack.com" multiple chips closable-chips density="compact"
                            class="mt-4"
                            hint="Sites allowed to embed the widgets (CSP frame-ancestors). One origin per chip. A bare domain (xyz.com) covers both xyz.com and www.xyz.com; our own properties are always allowed via global origins."
                            persistent-hint></v-combobox>
                        <v-text-field v-model="editForm.externalHomeUrl" label="External home URL"
                            placeholder="https://www.xyztrack.com" density="compact" clearable class="mt-4"
                            hint="The track's own website home. {subdomain}.ridepass.io forwards here."
                            persistent-hint></v-text-field>
                        <v-text-field v-model="editForm.externalEventsUrl" label="External events page URL"
                            placeholder="https://www.xyztrack.com/events" density="compact" clearable class="mt-4"
                            hint="Where event links on the RidePass discovery site point (falls back to the home URL)."
                            persistent-hint></v-text-field>
                        <v-select v-model="editForm.embedEventTarget" label="Apex event click goes to"
                            :items="eventTargetOptions" item-title="title" item-value="value"
                            density="compact" class="mt-4"
                            hint="Where an event click on the RidePass discovery site lands for this track."
                            persistent-hint></v-select>

                        <div class="text-subtitle-2 mt-6 mb-1">Embed snippet builder</div>
                        <p class="text-caption text-medium-emphasis mb-2">
                            Pick a widget, fill in any options, then send the track the snippet to paste on their site.
                            Embedding must be enabled (above) and their site listed in the allowed origins.
                        </p>
                        <v-select v-model="snippetWidget" :items="embedWidgetItems" item-title="title" item-value="value"
                            label="Widget" density="compact" prepend-inner-icon="mdi-puzzle"></v-select>
                        <p v-if="snippetWidgetDef" class="text-caption text-medium-emphasis mt-n2 mb-2">
                            {{ snippetWidgetDef.description }}
                        </p>
                        <v-text-field v-for="p in snippetWidgetParams" :key="p.attr"
                            v-model="snippetParams[p.attr]" :label="p.label" :placeholder="p.placeholder"
                            density="compact" clearable class="mt-2" :hint="p.hint" persistent-hint></v-text-field>
                        <v-textarea :model-value="embedSnippet" readonly variant="outlined" density="compact"
                            rows="3" auto-grow class="mt-4" style="font-family: monospace;"></v-textarea>
                        <v-btn size="small" variant="tonal" prepend-icon="mdi-content-copy"
                            @click="copyEmbedSnippet">Copy snippet</v-btn>

                        <v-divider class="my-4"></v-divider>
                        <div class="d-flex align-center">
                            <div class="text-subtitle-2">Preview</div>
                            <v-spacer></v-spacer>
                            <v-switch v-model="showPreview" color="primary" inset density="compact" hide-details
                                :disabled="!widgetPreviewUrl" label="Show preview"></v-switch>
                        </div>
                        <p v-if="!widgetPreviewUrl" class="text-caption text-medium-emphasis">
                            Fill in the required options above to preview this widget.
                        </p>
                        <p v-else class="text-caption text-medium-emphasis mb-2">
                            Live preview of the widget on <code>{{ editTenant?.subdomain }}</code>, exactly as it renders on the track's site.
                        </p>
                        <div v-if="showPreview && widgetPreviewUrl" class="mt-1">
                            <iframe ref="previewIframe" :src="widgetPreviewUrl" title="Widget preview"
                                style="width: 100%; min-height: 320px; border: 1px solid rgba(0,0,0,0.12); border-radius: 6px; display: block;"></iframe>
                        </div>
                    </v-window-item>
                    </v-window>

                    <div v-if="editError" class="text-error text-caption mt-2">{{ editError }}</div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="editDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="savingEdit" @click="saveEdit">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Import a tenant from staging -->
        <v-dialog v-model="importDialog" max-width="640">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Import tenant from stage</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="importDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <div v-if="importLoading" class="text-center py-6">
                        <v-progress-circular indeterminate color="primary"></v-progress-circular>
                    </div>
                    <template v-else>
                        <v-alert v-if="importError" type="error" variant="tonal" density="compact" class="mb-3">{{ importError }}</v-alert>

                        <!-- Step 1: pick a stage tenant -->
                        <template v-if="!preview">
                            <p class="text-caption text-medium-emphasis mb-2">
                                Unpublished tenants on staging. Pick one to promote to production (config + images only , never orders).
                            </p>
                            <v-list v-if="stageTenants.length" density="compact" class="border rounded">
                                <v-list-item v-for="t in stageTenants" :key="t.id" @click="doPreview(t)">
                                    <v-list-item-title>
                                        {{ t.displayName }} <code class="ml-1 text-medium-emphasis">{{ t.subdomain }}</code>
                                    </v-list-item-title>
                                    <template #append><v-icon icon="mdi-chevron-right"></v-icon></template>
                                </v-list-item>
                            </v-list>
                            <p v-else-if="!importError" class="text-medium-emphasis">No unpublished tenants on staging.</p>
                        </template>

                        <!-- Step 2: preview -->
                        <template v-else>
                            <div class="text-h6 font-weight-bold mb-1">
                                {{ preview.displayName }} <code class="ml-1 text-medium-emphasis">{{ preview.subdomain }}</code>
                            </div>
                            <v-alert v-if="preview.status === 'blocked'" type="error" variant="tonal" class="mt-2">
                                {{ preview.reason }}
                            </v-alert>
                            <template v-else>
                                <v-chip :color="preview.mode === 'replace' ? 'warning' : 'success'" variant="tonal" class="mb-3">
                                    {{ preview.mode === 'replace' ? 'Will REPLACE the existing prod tenant' : 'Will CREATE a new prod tenant' }}
                                </v-chip>
                                <v-alert v-if="preview.mode === 'replace'" type="warning" variant="tonal" density="compact" class="mb-3">
                                    This tenant already exists on prod (unpublished, no live orders). Promoting replaces its current config with the stage version.
                                </v-alert>
                                <div class="text-body-2">
                                    <div>Events: <strong>{{ preview.counts.events }}</strong></div>
                                    <div>Ticket tiers: <strong>{{ preview.counts.ticketTiers }}</strong></div>
                                    <div>Add-ons: <strong>{{ preview.counts.addOns }}</strong></div>
                                    <div>Season passes: <strong>{{ preview.counts.seasonPasses }}</strong></div>
                                    <div>Images: <strong>{{ preview.counts.images }}</strong></div>
                                </div>
                                <p class="text-caption text-medium-emphasis mt-3">
                                    Stripe Connect, SMS, and domain settings are not copied , reconnect those on prod, then publish when ready.
                                </p>
                            </template>
                        </template>
                    </template>
                </v-card-text>
                <v-card-actions v-if="preview && !importLoading">
                    <v-btn variant="text" @click="preview = null">Back</v-btn>
                    <v-spacer></v-spacer>
                    <v-btn v-if="preview.status !== 'blocked'"
                        :color="preview.mode === 'replace' ? 'warning' : 'primary'"
                        :loading="importing" @click="doImport">
                        {{ preview.mode === 'replace' ? 'Replace on prod' : 'Create on prod' }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, onBeforeUnmount, computed, watch } from 'vue'
import dayjs from 'dayjs'
import { SuperAdminService, type TenantSummary, type CreateTenantResult, type UpdateTenantPayload, type StageTenant, type PromotionResult } from '@/services/SuperAdminService'
import { EMBED_WIDGETS, getEmbedWidget, buildEmbedSnippet, buildEmbedPath } from '@/embed/widgets'
import tenantHelper from '@/helpers/TenantHelper'
import { geocode } from '@/helpers/Geocode'
import authHelper from '@/helpers/AuthHelper'

const service = new SuperAdminService()

const tenants = ref<TenantSummary[]>([])
const loadingTenants = ref(false)

const createDialog = ref(false)
const createTab = ref<'general' | 'features' | 'embed'>('general')
const creating = ref(false)
function blankCreateForm() {
    return {
        subdomain: '',
        displayName: '',
        tenantType: 'motocross' as 'motocross' | 'mountain_bike',
        venueCategory: null as 'bike_park' | 'shuttle' | 'resort' | null,
        timezone: 'America/New_York',
        adminFirstName: '',
        adminLastName: '',
        adminEmail: '',
        clientType: 'hosted' as 'hosted' | 'custom_domain' | 'embedded',
        customDomain: '',
        customDomainVerified: false,
        embedEnabled: false,
        embedAllowedOrigins: [] as string[],
        externalHomeUrl: '',
        externalEventsUrl: '',
        embedEventTarget: 'external' as 'external' | 'ridepass',
        // Feature defaults below are the MX baseline; switching to MTB re-applies the
        // MTB defaults via the tenantType watcher.
        giftCardsEnabled: false,
        rentalsEnabled: false,
        extrasEnabled: true,
        seasonPassesEnabled: true,
        concessionsEnabled: false,
        blogEnabled: false,
        membershipEnabled: true,
        waitlistEnabled: true,
        allowSelfCancel: false,
    }
}
const createForm = ref(blankCreateForm())

// Re-apply sensible feature defaults + venue category whenever the tenant type changes
// in the create dialog. MTB turns Rentals on (bike/gear rental is core to parks); both
// types get Add-ons / Memberships / Season passes / Waitlist on, the rest off. Manual
// toggles after a type pick stick until the type is changed again.
function applyTypeFeatureDefaults(type: 'motocross' | 'mountain_bike') {
    const f = createForm.value
    f.extrasEnabled = true
    f.membershipEnabled = true
    f.seasonPassesEnabled = true
    f.waitlistEnabled = true
    f.giftCardsEnabled = false
    f.concessionsEnabled = false
    f.blogEnabled = false
    f.allowSelfCancel = false
    f.rentalsEnabled = type === 'mountain_bike'
}
watch(() => createForm.value.tenantType, (type) => {
    createForm.value.venueCategory = type === 'mountain_bike'
        ? (createForm.value.venueCategory ?? 'bike_park')
        : null
    applyTypeFeatureDefaults(type)
})

// Platform feature switches, shown identically in the create + edit dialogs. The
// description is a one-liner so a super-admin knows what each bit turns on without
// leaving the dialog. Keys map to the matching boolean on both form shapes.
type FeatureKey =
    | 'giftCardsEnabled' | 'rentalsEnabled' | 'extrasEnabled' | 'seasonPassesEnabled'
    | 'concessionsEnabled' | 'blogEnabled' | 'membershipEnabled' | 'waitlistEnabled' | 'allowSelfCancel'
const featureToggles: { key: FeatureKey; label: string; description: string }[] = [
    { key: 'giftCardsEnabled', label: 'Gift cards', description: 'Riders buy and redeem digital gift cards delivered by email.' },
    { key: 'rentalsEnabled', label: 'Rentals', description: 'Rent gear (bikes, helmets, pads) per session, with deposit and insurance support.' },
    { key: 'extrasEnabled', label: 'Add-ons', description: 'Sell camping, parking, pit-vehicle passes, and merch alongside event entries.' },
    { key: 'seasonPassesEnabled', label: 'Season passes', description: 'Sell season-long passes that cover entry to qualifying events.' },
    { key: 'concessionsEnabled', label: 'Food & Beverage', description: 'Sell food, drink, and swag from the mobile tap-to-pay app, separate from events.' },
    { key: 'blogEnabled', label: 'Blog', description: 'Publish posts with photos and add a Blog link to the public nav.' },
    { key: 'membershipEnabled', label: 'Membership', description: 'Sell yearly or one-time memberships and gate selected purchases behind them.' },
    { key: 'waitlistEnabled', label: 'Event waitlist', description: 'Sold-out events and tiers offer a waitlist; alternates get texted when a spot opens.' },
    { key: 'allowSelfCancel', label: 'Rider self-cancel', description: 'Riders cancel their own purchases from My Passes (refund honors the service-charge rule).' },
]

const venueCategoryOptions = [
    { value: 'bike_park', title: 'Bike park' },
    { value: 'shuttle', title: 'Shuttle' },
    { value: 'resort', title: 'Resort' },
]

const tenantTypeOptions = [
    { value: 'motocross', title: 'Motocross (MX)' },
    { value: 'mountain_bike', title: 'Mountain Bike (MTB)' },
]

const credsDialog = ref(false)
const createdResult = ref<CreateTenantResult | null>(null)

const timezoneOptions = [
    { title: 'Eastern (New York)', value: 'America/New_York' },
    { title: 'Central (Chicago)', value: 'America/Chicago' },
    { title: 'Mountain (Denver)', value: 'America/Denver' },
    { title: 'Mountain — no DST (Phoenix)', value: 'America/Phoenix' },
    { title: 'Pacific (Los Angeles)', value: 'America/Los_Angeles' },
    { title: 'Alaska (Anchorage)', value: 'America/Anchorage' },
    { title: 'Hawaii–Aleutian (Honolulu)', value: 'Pacific/Honolulu' },
]

const statusOptions = [
    { title: 'Active', value: 'active' },
    { title: 'Suspended', value: 'suspended' },
    { title: 'Pending', value: 'pending' },
]

// Form holds service charge in display units (% and $); converted to bps/cents
// on save. Everything else maps straight to the API payload.
interface TenantEditForm {
    displayName: string
    status: string
    timezone: string
    isPublished: boolean
    serviceChargePct: number
    serviceChargeCapDollars: number | null
    stripeChargeMode: 'platform' | 'direct'
    addressLine: string | null
    city: string | null
    region: string | null
    postalCode: string | null
    country: string | null
    latitude: number | null
    longitude: number | null
    contactEmail: string | null
    phone: string | null
    loampassMxDestinationId: string | null
    clientType: 'hosted' | 'custom_domain' | 'embedded'
    customDomain: string | null
    customDomainVerified: boolean
    embedEnabled: boolean
    embedAllowedOrigins: string[]
    externalHomeUrl: string | null
    externalEventsUrl: string | null
    embedEventTarget: 'external' | 'ridepass'
    giftCardsEnabled: boolean
    rentalsEnabled: boolean
    extrasEnabled: boolean
    seasonPassesEnabled: boolean
    concessionsEnabled: boolean
    blogEnabled: boolean
    membershipEnabled: boolean
    waitlistEnabled: boolean
    allowSelfCancel: boolean
}

const clientTypeOptions = [
    { title: 'Hosted (subdomain.ridepass.io)', value: 'hosted' },
    { title: 'Custom domain (their own domain → RidePass)', value: 'custom_domain' },
    { title: 'Embedded widgets (their site, our widgets)', value: 'embedded' },
]

const eventTargetOptions = [
    { title: "The track's own site (external)", value: 'external' },
    { title: 'The hosted RidePass event page', value: 'ridepass' },
]

const editDialog = ref(false)
const editTenant = ref<TenantSummary | null>(null)
const savingEdit = ref(false)
const geocoding = ref(false)
const editError = ref<string | null>(null)
const editForm = ref<TenantEditForm>(emptyEditForm())
const editTab = ref<'general' | 'features' | 'embed'>('general')
const chargeModeItems = [
    { label: 'Platform (RidePass account + payout)', value: 'platform' },
    { label: 'Direct (track\'s own Stripe account)', value: 'direct' },
]
const testingConnect = ref(false)
const connectTestResult = ref<{ ok: boolean; message: string } | null>(null)

async function testStripeConnect() {
    if (!editTenant.value) return
    testingConnect.value = true
    connectTestResult.value = null
    try {
        const r = await service.testStripeConnect(editTenant.value.id)
        const d = (r.data as any).data
        connectTestResult.value = {
            ok: true,
            message: `Connected. Charges ${d.chargesEnabled ? 'enabled' : 'NOT enabled'}, payouts ${d.payoutsEnabled ? 'enabled' : 'NOT enabled'}. Available $${(d.availableCents / 100).toFixed(2)} ${String(d.currency).toUpperCase()}.`,
        }
    } catch (err: any) {
        connectTestResult.value = { ok: false, message: err.response?.data?.error || 'Stripe connection test failed.' }
    } finally {
        testingConnect.value = false
    }
}

// Snippet builder: pick a widget + fill its options, get the paste-able tag.
const embedWidgetItems = EMBED_WIDGETS.map(w => ({ title: w.label, value: w.key }))
const snippetWidget = ref(EMBED_WIDGETS[0].key)
const snippetParams = reactive<Record<string, string>>({})
const snippetWidgetDef = computed(() => getEmbedWidget(snippetWidget.value))
const snippetWidgetParams = computed(() => snippetWidgetDef.value?.params ?? [])
const embedSnippet = computed(() =>
    buildEmbedSnippet(snippetWidget.value, editTenant.value?.subdomain ?? 'yourtrack', snippetParams))
async function copyEmbedSnippet() {
    try {
        await navigator.clipboard.writeText(embedSnippet.value)
        flash('Snippet copied.', 'success')
    } catch {
        flash('Could not copy — select the snippet and copy manually.', 'error')
    }
}

// Live preview: frame the selected widget's chromeless route on the tenant's own
// subdomain with ?preview=1 (which bypasses the embed enable/origin check; the
// server CSP still guards real external framing). Null when a required param is
// missing (e.g. the single-event widget needs an event id).
const showPreview = ref(false)
const previewIframe = ref<HTMLIFrameElement | null>(null)
const widgetPreviewUrl = computed(() => {
    const sub = editTenant.value?.subdomain
    if (!sub) return null
    const path = buildEmbedPath(snippetWidget.value, snippetParams)
    if (!path) return null
    const proto = window.location.protocol
    const port = window.location.port ? `:${window.location.port}` : ''
    const sep = path.includes('?') ? '&' : '?'
    return `${proto}//${sub}.${tenantHelper.rootDomain()}${port}${path}${sep}preview=1&rpfid=preview`
})

// Size the preview iframe from the widget's height postMessage (mirrors embed.js).
function onPreviewMessage(ev: MessageEvent) {
    const d = ev.data
    if (!d || d.type !== 'ridepass:resize' || typeof d.height !== 'number') return
    if (d.frameId && d.frameId !== 'preview') return
    if (previewIframe.value) previewIframe.value.style.height = Math.max(200, Math.ceil(d.height)) + 'px'
}
onMounted(() => window.addEventListener('message', onPreviewMessage))
onBeforeUnmount(() => window.removeEventListener('message', onPreviewMessage))

function clientTypeShort(v: string): string {
    return v === 'custom_domain' ? 'Custom domain' : v === 'embedded' ? 'Embedded' : 'Hosted'
}
function clientTypeColor(v: string): string {
    // Solid, distinct colors so the type reads at a glance (the old grey tonal
    // "Hosted" chip was nearly invisible against the row).
    return v === 'custom_domain' ? 'indigo' : v === 'embedded' ? 'deep-purple' : 'blue-grey-darken-1'
}
function clientTypeIcon(v: string): string {
    return v === 'custom_domain' ? 'mdi-web' : v === 'embedded' ? 'mdi-code-tags' : 'mdi-server'
}

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

onMounted(loadTenants)

async function loadTenants() {
    loadingTenants.value = true
    try {
        const r = await service.listTenants()
        tenants.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load tenants.', 'error')
    } finally {
        loadingTenants.value = false
    }
}

// ── Import a tenant from staging ─────────────────────────────────────────────
const importDialog = ref(false)
const importLoading = ref(false)
const importing = ref(false)
const importError = ref<string | null>(null)
const stageTenants = ref<StageTenant[]>([])
const preview = ref<PromotionResult | null>(null)
const selectedStageId = ref<string | null>(null)

async function openImport() {
    importDialog.value = true
    preview.value = null
    importError.value = null
    stageTenants.value = []
    selectedStageId.value = null
    importLoading.value = true
    try {
        const r = await service.listStageTenants()
        stageTenants.value = (r.data as any).data
    } catch (err: any) {
        importError.value = err.response?.data?.error || 'Could not reach staging.'
    } finally {
        importLoading.value = false
    }
}

async function doPreview(t: StageTenant) {
    selectedStageId.value = t.id
    importError.value = null
    importLoading.value = true
    try {
        const r = await service.promoteTenant(t.id, false)
        preview.value = (r.data as any).data
    } catch (err: any) {
        importError.value = err.response?.data?.error || 'Preview failed.'
    } finally {
        importLoading.value = false
    }
}

async function doImport() {
    if (!selectedStageId.value) return
    importing.value = true
    importError.value = null
    try {
        const r = await service.promoteTenant(selectedStageId.value, true)
        const res = (r.data as any).data as PromotionResult
        if (res.status === 'blocked') { importError.value = res.reason; return }
        flash(`${res.status === 'replaced' ? 'Replaced' : 'Created'} ${res.subdomain} on production.`, 'success')
        importDialog.value = false
        await loadTenants()
    } catch (err: any) {
        importError.value = err.response?.data?.error || 'Import failed.'
    } finally {
        importing.value = false
    }
}


function openCreateTenant() {
    createForm.value = blankCreateForm()
    createTab.value = 'general'
    createDialog.value = true
}

async function submitCreateTenant() {
    try {
        creating.value = true
        const body = {
            subdomain: createForm.value.subdomain.trim().toLowerCase(),
            displayName: createForm.value.displayName.trim(),
            tenantType: createForm.value.tenantType,
            venueCategory: createForm.value.tenantType === 'mountain_bike' ? createForm.value.venueCategory : null,
            timezone: createForm.value.timezone,
            adminEmail: createForm.value.adminEmail.trim() || null,
            adminFirstName: createForm.value.adminFirstName.trim() || null,
            adminLastName: createForm.value.adminLastName.trim() || null,
            clientType: createForm.value.clientType,
            customDomain: createForm.value.clientType === 'custom_domain'
                ? (createForm.value.customDomain.trim() || null) : null,
            customDomainVerified: createForm.value.clientType === 'custom_domain' ? createForm.value.customDomainVerified : false,
            embedEnabled: createForm.value.embedEnabled,
            embedAllowedOrigins: createForm.value.embedAllowedOrigins.length > 0 ? createForm.value.embedAllowedOrigins : null,
            externalHomeUrl: createForm.value.externalHomeUrl.trim() || null,
            externalEventsUrl: createForm.value.externalEventsUrl.trim() || null,
            embedEventTarget: createForm.value.embedEventTarget,
            giftCardsEnabled: createForm.value.giftCardsEnabled,
            rentalsEnabled: createForm.value.rentalsEnabled,
            extrasEnabled: createForm.value.extrasEnabled,
            seasonPassesEnabled: createForm.value.seasonPassesEnabled,
            concessionsEnabled: createForm.value.concessionsEnabled,
            blogEnabled: createForm.value.blogEnabled,
            membershipEnabled: createForm.value.membershipEnabled,
            waitlistEnabled: createForm.value.waitlistEnabled,
            allowSelfCancel: createForm.value.allowSelfCancel,
        }
        const r = await service.createTenant(body)
        createdResult.value = (r.data as any).data
        createDialog.value = false
        credsDialog.value = true
        await loadTenants()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to create tenant.', 'error')
    } finally {
        creating.value = false
    }
}

function emptyEditForm(): TenantEditForm {
    return {
        displayName: '', status: 'active', timezone: 'America/New_York', isPublished: false,
        serviceChargePct: 3, serviceChargeCapDollars: null, stripeChargeMode: 'platform',
        addressLine: null, city: null, region: null, postalCode: null, country: null,
        latitude: null, longitude: null, contactEmail: null, phone: null,
        loampassMxDestinationId: null,
        clientType: 'hosted', customDomain: null, customDomainVerified: false, embedEnabled: false, embedAllowedOrigins: [],
        externalHomeUrl: null, externalEventsUrl: null, embedEventTarget: 'external',
        giftCardsEnabled: false, rentalsEnabled: false, extrasEnabled: false, seasonPassesEnabled: true,
        concessionsEnabled: false, blogEnabled: false, membershipEnabled: false,
        waitlistEnabled: true, allowSelfCancel: false,
    }
}

function openEdit(t: TenantSummary) {
    editTenant.value = t
    editError.value = null
    connectTestResult.value = null
    editForm.value = {
        displayName: t.displayName,
        status: t.status,
        timezone: t.timezone,
        isPublished: t.isPublished,
        serviceChargePct: t.serviceChargeBps / 100,
        serviceChargeCapDollars: t.monthlyServiceChargeCapCents !== null ? t.monthlyServiceChargeCapCents / 100 : null,
        stripeChargeMode: t.stripeChargeMode ?? 'platform',
        addressLine: t.addressLine,
        city: t.city,
        region: t.region,
        postalCode: t.postalCode,
        country: t.country,
        latitude: t.latitude,
        longitude: t.longitude,
        contactEmail: t.contactEmail,
        phone: t.phone,
        loampassMxDestinationId: t.loampassMxDestinationId,
        clientType: t.clientType,
        customDomain: t.customDomain,
        customDomainVerified: t.customDomainVerified,
        embedEnabled: t.embedEnabled,
        embedAllowedOrigins: t.embedAllowedOrigins ?? [],
        externalHomeUrl: t.externalHomeUrl,
        externalEventsUrl: t.externalEventsUrl,
        embedEventTarget: t.embedEventTarget ?? 'external',
        giftCardsEnabled: t.giftCardsEnabled,
        rentalsEnabled: t.rentalsEnabled,
        extrasEnabled: t.extrasEnabled,
        seasonPassesEnabled: t.seasonPassesEnabled,
        concessionsEnabled: t.concessionsEnabled,
        blogEnabled: t.blogEnabled,
        membershipEnabled: t.membershipEnabled,
        waitlistEnabled: t.waitlistEnabled,
        allowSelfCancel: t.allowSelfCancel,
    }
    editTab.value = 'general'
    editDialog.value = true
}

async function lookupCoords() {
    const parts = [editForm.value.addressLine, editForm.value.city, editForm.value.region,
        editForm.value.postalCode, editForm.value.country]
        .map(s => (s ?? '').trim()).filter(Boolean)
    if (parts.length === 0) {
        flash('Enter an address to look up.', 'error')
        return
    }
    geocoding.value = true
    try {
        const g = await geocode(parts.join(', '))
        if (g) {
            editForm.value.latitude = Number(g.lat.toFixed(6))
            editForm.value.longitude = Number(g.lng.toFixed(6))
            flash('Coordinates found.', 'success')
        } else {
            flash('No match found for that address.', 'error')
        }
    } catch {
        flash('Geocoding failed.', 'error')
    } finally {
        geocoding.value = false
    }
}

async function saveEdit() {
    if (!editTenant.value) return
    if (!editForm.value.displayName.trim()) {
        editError.value = 'Display name is required.'
        return
    }
    savingEdit.value = true
    editError.value = null
    try {
        const f = editForm.value
        const pct = numOrNull(f.serviceChargePct) ?? 0
        const capDollars = numOrNull(f.serviceChargeCapDollars)
        const body: UpdateTenantPayload = {
            displayName: f.displayName.trim(),
            status: f.status,
            timezone: f.timezone,
            isPublished: f.isPublished,
            serviceChargeBps: Math.round(pct * 100),
            monthlyServiceChargeCapCents: capDollars !== null ? Math.round(capDollars * 100) : null,
            stripeChargeMode: f.stripeChargeMode,
            addressLine: norm(f.addressLine),
            city: norm(f.city),
            region: norm(f.region),
            postalCode: norm(f.postalCode),
            country: norm(f.country),
            latitude: numOrNull(f.latitude),
            longitude: numOrNull(f.longitude),
            contactEmail: norm(f.contactEmail),
            phone: norm(f.phone),
            loampassMxDestinationId: norm(f.loampassMxDestinationId),
            clientType: f.clientType,
            customDomain: f.clientType === 'custom_domain' ? norm(f.customDomain) : null,
            customDomainVerified: f.clientType === 'custom_domain' ? f.customDomainVerified : false,
            embedEnabled: f.embedEnabled,
            embedAllowedOrigins: f.embedAllowedOrigins.length > 0 ? f.embedAllowedOrigins : null,
            externalHomeUrl: norm(f.externalHomeUrl),
            externalEventsUrl: norm(f.externalEventsUrl),
            embedEventTarget: f.embedEventTarget,
            giftCardsEnabled: f.giftCardsEnabled,
            rentalsEnabled: f.rentalsEnabled,
            extrasEnabled: f.extrasEnabled,
            seasonPassesEnabled: f.seasonPassesEnabled,
            concessionsEnabled: f.concessionsEnabled,
            blogEnabled: f.blogEnabled,
            membershipEnabled: f.membershipEnabled,
            waitlistEnabled: f.waitlistEnabled,
            allowSelfCancel: f.allowSelfCancel,
        }
        await service.updateTenant(editTenant.value.id, body)
        flash('Tenant updated.', 'success')
        editDialog.value = false
        await loadTenants()
    } catch (err: any) {
        editError.value = err.response?.data?.error || 'Failed to update tenant.'
    } finally {
        savingEdit.value = false
    }
}

function norm(s: string | null): string | null {
    const t = (s ?? '').trim()
    return t.length ? t : null
}
function numOrNull(n: number | null): number | null {
    const x = typeof n === 'number' ? n : parseFloat(n as any)
    return Number.isFinite(x) ? x : null
}

function tenantUrl(subdomain: string): string {
    const rootDomain = import.meta.env.VITE_ROOT_DOMAIN ?? 'ridepass.local'
    const port = window.location.port ? `:${window.location.port}` : ''
    return `${window.location.protocol}//${subdomain}.${rootDomain}${port}/`
}

// Preview an unpublished tenant: bridge the super admin's token to the subdomain
// via the URL fragment so the publish gate lets the request through (main.ts
// reads it on load, stores it, and strips it from the URL).
function previewUrl(subdomain: string): string {
    const token = authHelper.getToken() ?? ''
    return `${tenantUrl(subdomain)}#preview_token=${encodeURIComponent(token)}`
}

function formatDate(utc: string): string {
    return dayjs.utc(utc).format('YYYY-MM-DD HH:mm')
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>
