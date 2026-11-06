<template>
    <v-container fluid class="pa-0">
        <div class="pos-root d-flex flex-column">
            <!-- Header -->
            <div class="pos-header d-flex align-center px-4 py-2 ga-2">
                <h1 class="pos-title">Food &amp; Beverage POS</h1>
                <v-chip :color="readerColor" size="small" variant="tonal">
                    <v-icon start size="small">mdi-contactless-payment</v-icon>{{ readerStatus }}
                </v-chip>
                <v-btn v-if="readerState !== 'connected'" size="small" variant="text"
                    :loading="readerConnecting" @click="connectReader">Connect</v-btn>
                <v-chip v-if="online.capacityEnabled" size="small" variant="tonal"
                    :color="online.openNow ? 'success' : 'error'">
                    <v-icon start size="small">{{ online.openNow ? 'mdi-cloud-check' : 'mdi-cloud-off-outline' }}</v-icon>
                    Online: {{ online.openNow ? 'Open' : (online.pausedManual ? 'Paused' : (online.capReached ? 'Busy' : 'Closed')) }}
                </v-chip>
                <v-spacer />
                <v-btn v-if="online.capacityEnabled && !onlineBaseClosed" variant="text"
                    :prepend-icon="online.pausedManual ? 'mdi-play' : 'mdi-pause'"
                    :loading="pausing" @click="toggleOnlinePause">
                    {{ online.pausedManual ? 'Resume online' : 'Pause online' }}
                </v-btn>
                <span v-else-if="online.capacityEnabled" class="text-caption text-medium-emphasis mr-2">
                    Online ordering is closed today
                </span>
                <v-btn variant="text" prepend-icon="mdi-printer-settings" @click="printerDialog = true">Printer</v-btn>
                <v-btn variant="text" prepend-icon="mdi-tablet"
                    :color="displayAttention ? 'error' : undefined" @click="displayDialog = true">
                    Display
                    <v-tooltip v-if="displayAttention" activator="parent" location="bottom">
                        {{ displayPushFailing ? 'Customer display stopped syncing' : 'No customer display paired' }}
                    </v-tooltip>
                </v-btn>
                <v-btn v-if="canTurnIn" variant="tonal" prepend-icon="mdi-cash-register"
                    @click="drawerSession ? openCloseOut() : openDrawerDialog()">
                    {{ drawerSession ? 'Close out' : 'Open drawer' }}
                </v-btn>
                <v-btn v-if="tabsEnabled" variant="tonal" prepend-icon="mdi-book-open-variant" @click="openTabsDrawer">
                    Tabs ({{ openTabs.length }})
                </v-btn>
                <v-btn variant="tonal" prepend-icon="mdi-receipt-text-clock" :to="{ name: 'AdminConcessionOrders' }">Orders</v-btn>
                <v-btn variant="tonal" prepend-icon="mdi-stove" :to="{ name: 'AdminConcessionKitchen' }">Cook screen</v-btn>
            </div>

            <div class="pos-body d-flex">
                <!-- Catalog -->
                <div class="pos-catalog d-flex flex-column flex-grow-1">
                    <div class="pos-tabs px-2 pt-1">
                        <v-chip-group v-model="category" mandatory selected-class="pos-chip--active" show-arrows>
                            <v-chip value="all" size="large" variant="tonal">All</v-chip>
                            <v-chip v-for="c in categories" :key="c.key" :value="c.key" size="large" variant="tonal">{{ c.name }}</v-chip>
                        </v-chip-group>
                    </div>
                    <div class="pos-grid-wrap flex-grow-1">
                        <div v-if="loading" class="d-flex justify-center pa-8"><v-progress-circular indeterminate /></div>
                        <div v-else-if="filteredProducts.length === 0" class="text-medium-emphasis text-center pa-8">No items in this category.</div>
                        <div v-else class="pos-grid">
                            <div v-for="p in filteredProducts" :key="p.id" class="tile" :class="{ 'tile--out': p.soldOut }" @click="beginAdd(p)">
                                <div class="tile__media">
                                    <v-img v-if="p.imageUrl" :src="p.imageUrl" cover height="100%" />
                                    <div v-else class="tile__placeholder"><v-icon size="34" class="text-muted2">mdi-silverware-fork-knife</v-icon></div>
                                    <div v-if="p.soldOut" class="tile__ribbon">SOLD OUT</div>
                                    <v-menu>
                                        <template #activator="{ props }">
                                            <v-btn icon="mdi-dots-vertical" size="x-small" variant="flat" class="tile__menu"
                                                :loading="toggling === p.id" v-bind="props" @click.stop />
                                        </template>
                                        <v-list density="compact">
                                            <v-list-item title="Details" prepend-icon="mdi-information-outline" @click="openDetails(p)" />
                                            <v-list-item :title="p.manuallySoldOut ? 'Mark available' : 'Mark sold out (86)'"
                                                :prepend-icon="p.manuallySoldOut ? 'mdi-check-circle' : 'mdi-cancel'"
                                                @click="toggle86(p)" />
                                        </v-list>
                                    </v-menu>
                                </div>
                                <div class="tile__body">
                                    <div class="tile__name">{{ p.name }}</div>
                                    <div class="d-flex align-center justify-space-between mt-1">
                                        <span class="tile__price">{{ money(p.priceCents) }}</span>
                                        <span v-if="!p.soldOut && p.remaining >= 0" class="tile__stock">{{ p.remaining }} left</span>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>

                <!-- Cart -->
                <div class="pos-cart d-flex flex-column">
                    <!-- Tab mode: the cart is the NEXT round of a selected tab, so the header says which
                         tab, which round, and what is already on it. Everything else about the cart
                         (lines, discounts, rush) works exactly as for a walk-up order. -->
                    <div v-if="selectedTab" class="px-4 py-3 d-flex align-center pos-tab-head">
                        <div class="flex-grow-1" style="min-width: 0">
                            <div class="rp-kicker">Tab · Round {{ nextRoundNumber }}</div>
                            <div class="d-flex align-center ga-1">
                                <span class="text-h6 font-weight-bold text-truncate">{{ selectedTab.tabName || 'Unnamed tab' }}</span>
                                <v-btn icon="mdi-pencil" size="x-small" variant="text" density="comfortable" @click="openRename" />
                            </div>
                            <div class="text-caption text-medium-emphasis d-flex align-center ga-2 flex-wrap">
                                <span>#{{ selectedTab.orderNumber ?? '—' }} · {{ nextRoundNumber - 1 }} round{{ nextRoundNumber === 2 ? '' : 's' }} fired · {{ money(selectedTab.totalCents) }} on tab</span>
                                <span v-if="selectedTab.holdCardLabel" class="rp-pill rp-pill--blue">{{ selectedTab.holdCardLabel }}</span>
                            </div>
                        </div>
                        <v-chip v-if="cartCount" size="small" class="ml-2" variant="tonal">{{ cartCount }}</v-chip>
                        <v-btn v-if="cart.length" size="small" variant="text" color="error" @click="clearOrder">Clear</v-btn>
                    </div>
                    <div v-else class="px-4 py-3 d-flex align-center">
                        <span class="text-h6 font-weight-bold">Order</span>
                        <v-chip v-if="cartCount" size="small" class="ml-2" variant="tonal">{{ cartCount }}</v-chip>
                        <v-spacer />
                        <v-btn v-if="cart.length" size="small" variant="text" color="error" @click="clearOrder">Clear</v-btn>
                    </div>
                    <v-divider />
                    <div class="pos-cart-items flex-grow-1">
                        <div v-if="cart.length === 0" class="empty-cart text-medium-emphasis">
                            <v-icon size="48" class="text-muted2">{{ selectedTab ? 'mdi-book-open-variant' : 'mdi-cart-outline' }}</v-icon>
                            <div class="mt-2">{{ selectedTab ? `Tap items to add round ${nextRoundNumber} to this tab` : 'Tap items to start an order' }}</div>
                        </div>
                        <div v-for="(line, i) in cart" :key="i" class="cart-line">
                            <div class="flex-grow-1">
                                <div class="font-weight-medium">{{ line.name }}</div>
                                <div v-if="line.variantLabel" class="text-caption text-medium-emphasis">{{ line.variantLabel }}</div>
                                <div v-for="m in line.modifierLabels" :key="m" class="text-caption text-medium-emphasis">+ {{ m }}</div>
                                <div v-if="line.notes" class="text-caption font-italic text-medium-emphasis">"{{ line.notes }}"</div>
                                <div v-if="line.input.discount" class="d-flex align-center text-caption mt-1" style="color: rgb(var(--v-theme-success))">
                                    <v-icon size="x-small" class="mr-1">mdi-tag-outline</v-icon>
                                    <span class="text-truncate">{{ line.discountLabel }} (-{{ money(estimateDiscount(line.input.discount, line.lineTotal, line.memberPerk ?? null)) }})</span>
                                    <v-btn icon="mdi-close" size="x-small" variant="text" density="comfortable" class="ml-1" @click="removeLineDiscount(i)" />
                                </div>
                                <div class="d-flex align-center mt-2">
                                    <v-btn icon="mdi-minus" size="x-small" variant="tonal" @click="setLineQty(i, line.quantity - 1)" />
                                    <span class="mx-3 font-weight-medium">{{ line.quantity }}</span>
                                    <v-btn icon="mdi-plus" size="x-small" variant="tonal" @click="setLineQty(i, line.quantity + 1)" />
                                    <v-btn v-if="lineCustomizable(line)" variant="text" size="small" class="ml-2"
                                        prepend-icon="mdi-tune-variant" @click="customizeLine(i)">Customize</v-btn>
                                </div>
                            </div>
                            <div class="text-right d-flex flex-column align-end">
                                <div class="font-weight-medium">{{ money(line.lineTotal) }}</div>
                                <v-tooltip text="Discount or comp this item" location="top">
                                    <template #activator="{ props }">
                                        <v-btn v-bind="props" icon="mdi-tag-outline" size="x-small" variant="text" class="mt-1" @click="openDiscount(i)" />
                                    </template>
                                </v-tooltip>
                                <v-btn icon="mdi-close" size="x-small" variant="text" class="mt-1" @click="cart.splice(i, 1)" />
                            </div>
                        </div>
                    </div>
                    <div class="pos-cart-footer pa-4">
                        <div v-if="taxCents || discountCents" class="d-flex justify-space-between text-body-2 text-medium-emphasis mb-1">
                            <span>Subtotal</span><span>{{ money(pricesIncludeTax ? subtotal - taxCents : subtotal) }}</span>
                        </div>
                        <div v-if="taxCents" class="d-flex justify-space-between text-body-2 text-medium-emphasis mb-2">
                            <span>Tax{{ pricesIncludeTax ? ' (incl.)' : '' }}</span><span>{{ money(taxCents) }}</span>
                        </div>
                        <v-btn variant="tonal" block size="small" prepend-icon="mdi-tag-outline" class="mb-2"
                            :disabled="cart.length === 0" @click="openDiscount('order')">
                            {{ orderDiscount ? 'Change discount' : 'Add discount or comp' }}
                        </v-btn>
                        <div v-if="lineDiscountCents" class="d-flex justify-space-between align-center text-body-2 mb-2" style="color: rgb(var(--v-theme-success))">
                            <span class="d-flex align-center text-truncate">
                                <v-icon size="small" class="mr-1">mdi-tag-multiple-outline</v-icon>
                                <span class="text-truncate">Item discounts</span>
                            </span>
                            <span>-{{ money(lineDiscountCents) }}</span>
                        </div>
                        <div v-if="orderDiscount" class="d-flex justify-space-between align-center text-body-2 mb-2" style="color: rgb(var(--v-theme-success))">
                            <span class="d-flex align-center text-truncate">
                                <v-icon size="small" class="mr-1">mdi-tag-outline</v-icon>
                                <span class="text-truncate">{{ orderDiscountLabel || 'Discount' }}</span>
                                <v-btn icon="mdi-close" size="x-small" variant="text" density="comfortable" class="ml-1" @click="removeOrderDiscount" />
                            </span>
                            <span>-{{ money(orderDiscountCents) }}</span>
                        </div>
                        <div v-if="managerName" class="text-caption text-medium-emphasis mb-2">
                            <v-icon size="x-small" class="mr-1">mdi-shield-check</v-icon>Approved by {{ managerName }}
                        </div>
                        <div class="d-flex justify-space-between align-center mb-3">
                            <span class="text-h6 font-weight-bold">Total</span>
                            <span class="pos-total">{{ money(total) }}</span>
                        </div>
                        <!-- Rush is decided while the customer is standing there ("I'm about to miss my
                             moto"), so it belongs next to the money, not on a screen the cashier would
                             have to go find afterwards. Applied to the ticket the moment the sale exists. -->
                        <v-btn v-if="cart.length > 0" class="mb-2" block variant="tonal" size="small"
                            :color="rushOrder ? 'error' : undefined"
                            :prepend-icon="rushOrder ? 'mdi-fire' : 'mdi-fire-off'"
                            @click="rushOrder = !rushOrder">
                            {{ rushOrder ? 'Rush order: on' : 'Mark as rush' }}
                        </v-btn>
                        <template v-if="selectedTab">
                            <v-btn block color="primary" size="large" height="56" class="mb-2"
                                :disabled="cart.length === 0 || paying || tabBusy" :loading="tabBusy"
                                prepend-icon="mdi-silverware-fork-knife" @click="addRoundToTab">
                                Add to tab · Round {{ nextRoundNumber }}
                            </v-btn>
                            <div class="d-flex ga-2">
                                <v-btn class="flex-grow-1" color="success" variant="tonal" prepend-icon="mdi-cash-register"
                                    :disabled="paying || tabBusy" @click="startTabClose(selectedTab)">Close tab</v-btn>
                                <v-btn variant="outlined" prepend-icon="mdi-pause" :disabled="paying || tabBusy" @click="parkTab">Park</v-btn>
                                <v-btn variant="text" color="error" :disabled="paying || tabBusy" @click="openVoid(selectedTab)">Void</v-btn>
                            </div>
                        </template>
                        <template v-else>
                            <div class="d-flex ga-2">
                                <v-btn class="flex-grow-1" color="success" size="large" height="56" :disabled="cart.length === 0 || paying"
                                    prepend-icon="mdi-cash" @click="startCheckout('cash')">Cash</v-btn>
                                <v-btn class="flex-grow-1" color="primary" size="large" height="56" :disabled="cart.length === 0 || paying"
                                    prepend-icon="mdi-credit-card" @click="startCheckout('card')">Card</v-btn>
                            </div>
                            <!-- A tab may open empty (just a name, or a held card) and take its rounds later. -->
                            <v-btn v-if="tabsEnabled" block variant="outlined" class="mt-2" :disabled="paying"
                                prepend-icon="mdi-book-open-variant" @click="openOpenTab">Open tab</v-btn>
                        </template>
                        <div v-if="readerState !== 'connected'" class="text-caption text-medium-emphasis text-center mt-2">
                            Connect a reader to take card payments.
                        </div>
                    </div>
                </div>
            </div>
        </div>

        <!-- Add item (variant + modifiers + notes + qty) -->
        <v-dialog v-model="addDialog" max-width="480">
            <v-card v-if="adding">
                <v-card-title class="d-flex align-center">
                    {{ adding.name }}
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="addDialog = false" />
                </v-card-title>
                <v-card-text>
                    <div v-if="adding.variants.length" class="mb-2">
                        <div class="text-subtitle-2">Option</div>
                        <v-radio-group v-model="selVariantId" density="compact" hide-details>
                            <v-radio v-for="v in adding.variants" :key="v.id"
                                :value="v.id" :label="`${variantLabel(v)} ${priceSuffix(v.priceCents)}`" />
                        </v-radio-group>
                    </div>
                    <div v-for="g in adding.modifierGroups" :key="g.id" class="mb-2">
                        <div class="text-subtitle-2">
                            {{ g.name }}
                            <span class="text-caption text-medium-emphasis">{{ groupHint(g) }}</span>
                        </div>
                        <v-checkbox v-for="o in g.options" :key="o.id" density="compact" hide-details
                            :label="`${o.name} ${priceSuffix(o.priceDeltaCents)}`"
                            :model-value="selOptions[g.id]?.includes(o.id) ?? false"
                            @update:model-value="toggleOption(g, o.id, !!$event)" />
                    </div>
                    <!-- Make it a combo -->
                    <div v-if="canCombo(adding)" ref="comboSectionEl" class="mt-4 pa-3" style="border: 1px solid var(--rp-line2); border-radius: 10px;">
                        <div class="text-subtitle-2 mb-1">Make it a combo</div>
                        <v-radio-group v-model="selComboTierId" density="compact" hide-details>
                            <v-radio :value="null" label="No thanks" />
                            <v-radio v-for="t in comboConfig.tiers" :key="t.id" :value="t.id"
                                :label="`${t.name} ${priceSuffix(t.priceCents)}`" />
                        </v-radio-group>
                        <template v-if="selComboTierId">
                            <div v-for="slot in comboConfig.slots" :key="slot.id" class="mt-2">
                                <div class="text-caption font-weight-medium">{{ slot.name }}</div>
                                <v-radio-group v-model="selComboSlots[slot.id]" density="compact" hide-details>
                                    <v-radio v-for="o in slot.options" :key="o.id" :value="o.id"
                                        :label="comboOptionLabel(slot, o)" />
                                </v-radio-group>
                            </div>
                        </template>
                    </div>

                    <v-text-field v-model="selNotes" label="Notes (e.g. onions on the side)" density="compact" class="mt-4" hide-details />
                    <div class="d-flex align-center mt-4">
                        <span class="mr-2">Qty</span>
                        <v-btn icon="mdi-minus" size="x-small" variant="tonal" @click="selQty = Math.max(1, selQty - 1)" />
                        <span class="mx-3 text-h6">{{ selQty }}</span>
                        <v-btn icon="mdi-plus" size="x-small" variant="tonal" @click="selQty++" />
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="addDialog = false">Cancel</v-btn>
                    <v-btn color="primary" @click="confirmAdd">{{ editingIndex == null ? 'Add' : 'Update' }} {{ money(addPreviewTotal) }}</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Item details (description + default selections) -->
        <v-dialog v-model="detailsDialog" max-width="420">
            <v-card v-if="detailsProduct">
                <v-img v-if="detailsProduct.imageUrl" :src="detailsProduct.imageUrl" height="180" cover />
                <v-card-title class="d-flex align-center">
                    <span class="text-truncate">{{ detailsProduct.name }}</span>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="detailsDialog = false" />
                </v-card-title>
                <v-card-text>
                    <div class="text-h6 mb-2">{{ money(detailsProduct.priceCents) }}</div>
                    <p v-if="detailsProduct.description" class="mb-3">{{ detailsProduct.description }}</p>
                    <p v-else class="text-medium-emphasis mb-3">No description.</p>
                    <template v-if="detailDefaults(detailsProduct).length">
                        <div class="font-weight-medium">Comes with</div>
                        <div class="mb-3">{{ detailDefaults(detailsProduct).join(', ') }}</div>
                    </template>
                    <template v-if="detailsProduct.modifierGroups.length">
                        <div class="font-weight-medium">Options</div>
                        <div v-for="g in detailsProduct.modifierGroups" :key="g.id" class="text-body-2 text-medium-emphasis">
                            {{ g.name }}: {{ g.options.map(o => o.name).join(', ') }}
                        </div>
                    </template>
                </v-card-text>
            </v-card>
        </v-dialog>

        <!-- Customer confirmation: review order, choose tip + receipt, then tender -->
        <v-dialog v-model="confirmDialog" fullscreen transition="dialog-bottom-transition">
            <v-card class="d-flex flex-column">
                <v-toolbar color="transparent" density="comfortable" class="border-b">
                    <v-btn icon="mdi-arrow-left" @click="cancelConfirm" />
                    <v-toolbar-title>
                        <template v-if="closingTab">Close tab · {{ closingTab.tabName || 'Unnamed tab' }}</template>
                        <template v-else>Review your order · {{ tender === 'cash' ? 'Cash' : 'Card' }}</template>
                    </v-toolbar-title>
                </v-toolbar>

                <div class="confirm-body flex-grow-1">
                    <div class="confirm-inner pa-4">
                        <!-- reviewLines is the cart for a walk-up sale and every fired round for a tab
                             close (grouped under a round header), so the review reads the same either way. -->
                        <v-list class="mb-2">
                            <template v-for="(line, i) in reviewLines" :key="i">
                                <v-list-subheader v-if="closingTab && (i === 0 || reviewLines[i - 1].roundNumber !== line.roundNumber)">
                                    Round {{ line.roundNumber ?? 1 }}
                                </v-list-subheader>
                                <v-list-item class="px-0">
                                    <v-list-item-title class="font-weight-medium">{{ line.quantity }}× {{ line.name }}</v-list-item-title>
                                    <v-list-item-subtitle v-if="line.variantLabel">{{ line.variantLabel }}</v-list-item-subtitle>
                                    <v-list-item-subtitle v-for="m in line.modifierLabels" :key="m">+ {{ m }}</v-list-item-subtitle>
                                    <v-list-item-subtitle v-if="line.notes" class="font-italic">"{{ line.notes }}"</v-list-item-subtitle>
                                    <template #append><span class="font-weight-medium">{{ money(line.lineTotal) }}</span></template>
                                </v-list-item>
                            </template>
                        </v-list>

                        <template v-if="tipsEnabled && pairedDisplayId && !cashierTipEntry">
                            <div class="text-subtitle-1 font-weight-bold mb-2">Tip</div>
                            <div class="d-flex align-center ga-3 mb-1">
                                <v-progress-circular v-if="!customerTipReceived" indeterminate size="20" width="2" />
                                <v-icon v-else color="success">mdi-check-circle</v-icon>
                                <span class="text-body-1">
                                    {{ customerTipReceived
                                        ? (tipCents ? `Customer added a ${money(tipCents)} tip.` : 'Customer chose no tip.')
                                        : 'Customer is choosing a tip on their screen…' }}
                                </span>
                            </div>
                            <v-btn variant="text" size="small" @click="cashierTipEntry = true">Enter tip on this screen instead</v-btn>
                            <v-divider class="my-4" />
                        </template>
                        <template v-else-if="tipsEnabled">
                            <div class="text-subtitle-1 font-weight-bold mb-2">Add a tip?</div>
                            <div class="d-flex flex-wrap ga-2">
                                <v-btn :variant="tipMode === 'none' ? 'flat' : 'outlined'" :color="tipMode === 'none' ? 'primary' : undefined" @click="tipMode = 'none'">No tip</v-btn>
                                <v-btn v-for="pct in [15, 18, 20]" :key="pct"
                                    :variant="tipMode === 'pct' && tipPct === pct ? 'flat' : 'outlined'"
                                    :color="tipMode === 'pct' && tipPct === pct ? 'primary' : undefined"
                                    @click="tipMode = 'pct'; tipPct = pct">{{ pct }}% · {{ money(Math.round(ckSubtotal * pct / 100)) }}</v-btn>
                                <v-btn :variant="tipMode === 'custom' ? 'flat' : 'outlined'" :color="tipMode === 'custom' ? 'primary' : undefined" @click="tipMode = 'custom'">Custom</v-btn>
                            </div>
                            <v-text-field v-if="tipMode === 'custom'" v-model.number="tipCustomDollars" type="number" min="0" step="0.50"
                                prefix="$" label="Custom tip" density="compact" hide-details class="mt-3" style="max-width: 200px" />
                            <v-divider class="my-4" />
                        </template>

                        <!-- A tab already has its name; renaming lives on the cart header. -->
                        <template v-if="!closingTab">
                            <div class="text-subtitle-1 font-weight-bold mb-2">Name for the order (optional)</div>
                            <v-text-field v-model="customerName" label="Customer name" density="compact" hide-details
                                placeholder="e.g. Alex" prepend-inner-icon="mdi-account" style="max-width: 320px" class="mb-4" />
                            <v-divider class="mb-4" />
                        </template>

                        <div class="text-subtitle-1 font-weight-bold mb-2">Store credit</div>
                        <CreditLookupField v-model="creditAccount" class="mb-4" style="max-width: 420px" />
                        <v-divider class="mb-4" />

                        <div class="text-subtitle-1 font-weight-bold mb-2">Receipt</div>
                        <v-btn-toggle v-model="receiptMethod" mandatory divided class="flex-wrap">
                            <v-btn value="print" prepend-icon="mdi-printer">Print</v-btn>
                            <v-btn value="sms" prepend-icon="mdi-message-text">Text</v-btn>
                            <v-btn value="email" prepend-icon="mdi-email">Email</v-btn>
                            <v-btn value="none">None</v-btn>
                        </v-btn-toggle>
                        <v-text-field v-if="receiptMethod === 'sms'" v-model="receiptDest" label="Mobile number" type="tel"
                            density="compact" hide-details class="mt-3" style="max-width: 280px" />
                        <v-text-field v-if="receiptMethod === 'email'" v-model="receiptDest" label="Email address" type="email"
                            density="compact" hide-details class="mt-3" style="max-width: 320px" />
                    </div>
                </div>

                <div class="confirm-footer pa-4">
                    <div class="confirm-inner">
                        <!-- Closing a tab: the tender is chosen here (a walk-up sale picks it in the cart)
                             because the card on file is only an option once we know the tab has one. -->
                        <template v-if="closingTab">
                            <div class="d-flex align-center flex-wrap ga-2 mb-3">
                                <v-btn-toggle v-model="tender" mandatory divided density="comfortable" class="flex-wrap">
                                    <v-btn value="cash" prepend-icon="mdi-cash">Cash</v-btn>
                                    <v-btn value="card" prepend-icon="mdi-credit-card">Card</v-btn>
                                    <v-btn v-if="closingTab.holdCardLabel" value="hold" prepend-icon="mdi-credit-card-check">
                                        Card on file · {{ closingTab.holdCardLabel }}
                                    </v-btn>
                                </v-btn-toggle>
                                <v-spacer />
                                <v-btn variant="text" size="small" prepend-icon="mdi-tag-outline" @click="openDiscount('order')">
                                    {{ orderDiscount ? 'Change discount' : 'Discount or comp' }}
                                </v-btn>
                            </div>
                            <div v-if="managerName" class="text-caption text-medium-emphasis mb-2">
                                <v-icon size="x-small" class="mr-1">mdi-shield-check</v-icon>Approved by {{ managerName }}
                            </div>
                        </template>
                        <div class="d-flex justify-space-between"><span class="text-medium-emphasis">Subtotal</span><span>{{ money(ckPricesIncludeTax ? ckSubtotal - ckTax : ckSubtotal) }}</span></div>
                        <div v-if="ckTax" class="d-flex justify-space-between mt-1"><span class="text-medium-emphasis">Tax{{ ckPricesIncludeTax ? ' (incl.)' : '' }}</span><span>{{ money(ckTax) }}</span></div>
                        <div v-if="ckDiscount" class="d-flex justify-space-between mt-1" style="color: rgb(var(--v-theme-success))"><span>{{ orderDiscountLabel || 'Discount' }}</span><span>-{{ money(ckDiscount) }}</span></div>
                        <div v-if="tipCents" class="d-flex justify-space-between mt-1"><span class="text-medium-emphasis">Tip</span><span>{{ money(tipCents) }}</span></div>
                        <div v-if="creditAppliedEstimate" class="d-flex justify-space-between mt-1" style="color: rgb(var(--v-theme-success))">
                            <span>Store credit</span><span>-{{ money(creditAppliedEstimate) }}</span>
                        </div>
                        <div class="d-flex justify-space-between align-center my-2">
                            <span class="text-h6 font-weight-bold">{{ creditAppliedEstimate ? 'Due' : 'Total' }}</span>
                            <span class="pos-total">{{ money(dueEstimate) }}</span>
                        </div>
                        <v-btn block size="large" height="56" :loading="paying"
                            :color="tender === 'cash' ? 'success' : 'primary'"
                            :disabled="tender === 'card' && dueEstimate > 0 && readerState !== 'connected'"
                            :prepend-icon="tender === 'cash' ? 'mdi-cash' : tender === 'hold' ? 'mdi-credit-card-check' : 'mdi-credit-card'"
                            @click="confirmPay">
                            {{ payButtonLabel }}
                        </v-btn>
                        <div v-if="paying && tender === 'hold'" class="text-center text-medium-emphasis mt-2">Charging the card on file…</div>
                        <div v-else-if="paying && tender === 'card'" class="text-center text-medium-emphasis mt-2">Processing payment on the reader…</div>
                        <div v-else-if="tender === 'card' && dueEstimate > 0 && readerState !== 'connected'" class="text-caption text-medium-emphasis text-center mt-2">Connect a reader to take card payments.</div>
                        <div v-else-if="tender === 'hold' && closingTab?.holdAmountCents != null && dueEstimate > closingTab.holdAmountCents" class="text-caption text-medium-emphasis text-center mt-2">
                            The total is above the {{ money(closingTab.holdAmountCents) }} hold. If the card can't be charged for the full amount, you'll be asked to collect it on the reader.
                        </div>
                    </div>
                </div>
            </v-card>
        </v-dialog>

        <!-- Cash tender -->
        <v-dialog v-model="cashDialog" max-width="360">
            <v-card>
                <v-card-title class="d-flex align-center">
                    Cash
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="cancelCash" />
                </v-card-title>
                <v-card-text>
                    <div class="d-flex justify-space-between text-h6 mb-3">
                        <span>{{ creditAppliedEstimate ? 'Due after credit' : 'Total' }}</span><span>{{ money(dueEstimate) }}</span>
                    </div>
                    <v-text-field v-model.number="cashTenderedDollars" label="Cash received" type="number" prefix="$"
                        density="compact" autofocus hide-details />
                    <div class="d-flex justify-space-between mt-3 text-h6">
                        <span>Change</span><span>{{ money(changeCents) }}</span>
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="cancelCash">Cancel</v-btn>
                    <v-btn color="success" :loading="paying" :disabled="changeCents < 0" @click="payCash">Take payment</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Order confirmation -->
        <v-dialog v-model="doneDialog" max-width="340" persistent>
            <v-card class="text-center pa-4">
                <v-icon color="success" size="64">mdi-check-circle</v-icon>
                <div v-if="lastClosedTabName" class="rp-kicker mt-2">Tab closed · {{ lastClosedTabName }}</div>
                <div class="pos-total mt-2">{{ lastOrderNumber != null ? `Order #${lastOrderNumber}` : 'Order placed' }}</div>
                <div class="text-medium-emphasis mb-4">
                    {{ lastClosedTabName ? 'Payment received. The tab is settled.'
                        : lastOrderNumber != null ? 'Call this number at pickup.' : 'Payment received — the order number will appear on the cook screen shortly.' }}
                </div>
                <v-btn block color="primary" class="mb-2" prepend-icon="mdi-printer" @click="printLast">Print receipt</v-btn>
                <v-btn v-if="kitchenPrinters.length && lastKitchenJob" block variant="tonal" class="mb-2" prepend-icon="mdi-printer-pos"
                    @click="sendKitchenTickets(lastKitchenJob, true)">Reprint kitchen tickets</v-btn>
                <v-btn block variant="tonal" @click="newOrder">New order</v-btn>
            </v-card>
        </v-dialog>

        <!-- Receipt printer setup (per tablet) -->
        <v-dialog v-model="printerDialog" max-width="440">
            <v-card>
                <v-card-title class="d-flex align-center">
                    Receipt printer
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="printerDialog = false" />
                </v-card-title>
                <v-card-text>
                    <p class="text-caption text-medium-emphasis mb-3">
                        Enter this tablet's Epson receipt printer address (e.g. <code>https://192.168.1.50</code>).
                        Receipts then print automatically with no dialog. The printer must be reachable over HTTPS;
                        trust its certificate on the tablet once.
                    </p>
                    <v-text-field v-model="printerUrl" label="Printer URL" placeholder="https://192.168.1.50"
                        density="compact" hide-details />
                </v-card-text>
                <v-card-actions>
                    <v-btn v-if="printerUrl" variant="text" size="small" :disabled="!lastReceipt" @click="printLast">Test print last</v-btn>
                    <v-spacer />
                    <v-btn variant="text" @click="printerDialog = false">Cancel</v-btn>
                    <v-btn color="primary" variant="flat" @click="savePrinter">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Customer-facing display pairing (per tablet) -->
        <v-dialog v-model="displayDialog" max-width="440">
            <v-card>
                <v-card-title class="d-flex align-center">
                    Customer display
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="displayDialog = false" />
                </v-card-title>
                <v-card-text>
                    <template v-if="pairedDisplayId">
                        <p class="text-body-2 mb-2">
                            <v-icon color="success" size="small">mdi-check-circle</v-icon>
                            Paired with a customer display. It mirrors the order as you ring items up
                            and asks the customer for a tip at checkout.
                        </p>
                        <p v-if="displayPushFailing" class="text-body-2 text-error mb-0">
                            The display isn't syncing right now. Check the tablet's connection or re-pair.
                        </p>
                    </template>
                    <template v-else>
                        <p class="text-caption text-medium-emphasis mb-3">
                            Open <strong>Customer Display</strong> from the menu on the customer-facing tablet,
                            then enter the pair code it shows here.
                        </p>
                        <v-text-field v-model="displayPairCodeInput" label="Pair code" placeholder="123456"
                            density="compact" hide-details autofocus @keyup.enter="pairDisplay" />
                    </template>
                </v-card-text>
                <v-card-actions>
                    <v-btn v-if="pairedDisplayId" variant="text" color="error" @click="unpairDisplay">Unpair</v-btn>
                    <v-spacer />
                    <v-btn variant="text" @click="displayDialog = false">Close</v-btn>
                    <v-btn v-if="!pairedDisplayId" color="primary" variant="flat" :loading="pairingDisplay" @click="pairDisplay">Pair</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Apply discount / comp (order- or line-level) -->
        <v-dialog v-model="discountDialog" max-width="520" scrollable>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span class="text-truncate">Discount or comp</span>
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="discountDialog = false" />
                </v-card-title>
                <v-card-subtitle>Applying to {{ discountTargetLabel }}</v-card-subtitle>
                <v-card-text>
                    <!-- Tenant discounts for this counter. Some may require a manager PIN;
                         those carry a lock so the prompt isn't a surprise. -->
                    <template v-if="discountPresets.length">
                        <div class="text-subtitle-2 mb-2">Discounts</div>
                        <div class="d-flex flex-wrap ga-2 mb-4">
                            <v-btn v-for="p in discountPresets" :key="p.id" variant="tonal" size="small"
                                :prepend-icon="p.requiresManager ? 'mdi-shield-key' : undefined"
                                @click="applyPreset(p)">
                                {{ p.name }} · {{ p.kind === 'percent' ? `${p.value / 100}%` : money(p.value) }}
                            </v-btn>
                        </div>
                    </template>

                    <!-- Manual percent / dollar (manager PIN required) -->
                    <div class="text-subtitle-2 mb-1">Manual discount</div>
                    <div class="text-caption text-medium-emphasis mb-2">
                        <v-icon size="x-small" class="mr-1">mdi-shield-lock-outline</v-icon>Needs a manager PIN
                    </div>
                    <div class="d-flex align-center ga-2">
                        <v-text-field v-model.number="manualPercent" type="number" min="0" max="100" suffix="%"
                            label="Percent off" density="compact" hide-details style="max-width: 160px" />
                        <v-btn variant="tonal" @click="applyManualPercent">Apply %</v-btn>
                    </div>
                    <div class="d-flex align-center ga-2 mt-3">
                        <v-text-field v-model.number="manualDollars" type="number" min="0" step="0.50" prefix="$"
                            label="Dollar off" density="compact" hide-details style="max-width: 160px" />
                        <v-btn variant="tonal" @click="applyManualAmount">Apply $</v-btn>
                    </div>

                    <!-- Comp (manager PIN required) -->
                    <template v-if="compReasonsList.length">
                        <v-divider class="my-4" />
                        <div class="text-subtitle-2 mb-1">Comp (on the house)</div>
                        <div class="text-caption text-medium-emphasis mb-2">
                            <v-icon size="x-small" class="mr-1">mdi-shield-lock-outline</v-icon>Needs a manager PIN
                        </div>
                        <div class="d-flex flex-wrap ga-2">
                            <v-btn v-for="r in compReasonsList" :key="r.id" variant="tonal" size="small" color="error"
                                @click="applyComp(r)">{{ r.name }}</v-btn>
                        </div>
                    </template>

                    <!-- Member perk (season pass / loampass; no PIN) -->
                    <v-divider class="my-4" />
                    <div class="text-subtitle-2 mb-2">Member discount</div>
                    <div class="d-flex align-center ga-2">
                        <v-text-field v-model="memberQuery" label="Customer email or phone" density="compact" hide-details
                            prepend-inner-icon="mdi-account-search" @keyup.enter="lookupMember" />
                        <v-btn variant="tonal" :loading="memberLooking" @click="lookupMember">Look up</v-btn>
                    </div>
                    <div v-if="memberResult" class="mt-3 pa-3" style="border: 1px solid var(--rp-line2); border-radius: 10px;">
                        <div class="font-weight-medium">{{ memberResult.customerName || memberResult.customerEmail || 'Customer' }}</div>
                        <div v-if="memberResult.seasonPass?.eligible" class="d-flex align-center justify-space-between mt-2">
                            <span>{{ memberResult.seasonPass.label }}</span>
                            <v-btn size="small" color="primary" variant="tonal" @click="applyMemberPerk('season_pass')">Apply</v-btn>
                        </div>
                        <div v-if="memberResult.loampass?.eligible" class="d-flex align-center justify-space-between mt-2">
                            <span>{{ memberResult.loampass.label }}</span>
                            <v-btn size="small" color="primary" variant="tonal" @click="applyMemberPerk('loampass')">Apply</v-btn>
                        </div>
                        <div v-if="!memberResult.seasonPass?.eligible && !memberResult.loampass?.eligible"
                            class="text-caption text-medium-emphasis mt-2">No member perks available for this customer.</div>
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="discountDialog = false">Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Manager PIN to authorize a comp or manual discount -->
        <v-dialog v-model="pinDialog" max-width="360" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    Manager PIN
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" @click="cancelPin" />
                </v-card-title>
                <v-card-text>
                    <p class="text-caption text-medium-emphasis mb-3">A manager must authorize this discount or comp.</p>
                    <v-text-field v-model="pinInput" label="Manager PIN" type="password" inputmode="numeric"
                        autocomplete="one-time-code" autofocus density="compact" hide-details @keyup.enter="verifyPin" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="cancelPin">Cancel</v-btn>
                    <v-btn color="primary" :loading="pinVerifying" @click="verifyPin">Approve</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Open a tab: name, optional member, optional card hold on the reader -->
        <v-dialog v-model="openTabDialog" max-width="480" :persistent="openingTab">
            <v-card>
                <v-card-title class="d-flex align-center">
                    Open a tab
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="openingTab" @click="openTabDialog = false" />
                </v-card-title>
                <v-card-text>
                    <!-- Reader phase: the tab already exists; we are waiting on the card for the hold. -->
                    <div v-if="tabHoldPhase === 'reader'" class="text-center py-6">
                        <v-progress-circular indeterminate size="48" width="3" />
                        <div class="text-subtitle-1 font-weight-bold mt-4">Present the card on the reader</div>
                        <div class="text-medium-emphasis">
                            Holding {{ money(tabHoldCents) }} on the card. Nothing is charged until the tab closes.
                        </div>
                    </div>
                    <template v-else>
                        <v-text-field v-model="tabNameInput" label="Tab name" placeholder="e.g. Red hat table 4" density="compact"
                            autofocus hide-details prepend-inner-icon="mdi-book-open-variant" @keyup.enter="submitOpenTab" />
                        <div class="text-caption text-medium-emphasis mt-1 mb-4">
                            {{ cart.length
                                ? `${cartCount} item${cartCount === 1 ? '' : 's'} (${money(total)}) go to the kitchen now as round 1.`
                                : 'No items yet. Rounds are added as the group orders.' }}
                        </div>

                        <div class="text-subtitle-2 mb-1">Member (optional)</div>
                        <div v-if="tabMember" class="pa-3 d-flex align-center ga-2" style="border: 1px solid var(--rp-line2); border-radius: 10px;">
                            <v-icon color="success" size="small">mdi-account-check</v-icon>
                            <div class="flex-grow-1" style="min-width: 0">
                                <div class="font-weight-medium text-truncate">{{ tabMember.name || tabMember.email || 'Member' }}</div>
                                <div class="text-caption text-medium-emphasis">
                                    {{ tabMember.perk ? `${tabMember.perk.label} · applied when the tab closes` : 'No member perks' }}
                                </div>
                            </div>
                            <v-btn icon="mdi-close" size="x-small" variant="text" @click="tabMember = null" />
                        </div>
                        <template v-else>
                            <div class="d-flex align-center ga-2">
                                <v-text-field v-model="memberQuery" label="Email or phone" density="compact" hide-details
                                    prepend-inner-icon="mdi-account-search" @keyup.enter="lookupMember" />
                                <v-btn variant="tonal" :loading="memberLooking" @click="lookupMember">Look up</v-btn>
                            </div>
                            <div v-if="memberResult" class="mt-2 pa-3 d-flex align-center ga-2" style="border: 1px solid var(--rp-line2); border-radius: 10px;">
                                <div class="flex-grow-1" style="min-width: 0">
                                    <div class="font-weight-medium text-truncate">{{ memberResult.customerName || memberResult.customerEmail || 'Customer' }}</div>
                                    <div class="text-caption text-medium-emphasis">{{ memberPerkSummary(memberResult) }}</div>
                                </div>
                                <v-btn size="small" color="primary" variant="tonal" @click="attachMember">Attach</v-btn>
                            </div>
                        </template>

                        <v-divider class="my-4" />
                        <template v-if="readerState === 'connected'">
                            <v-switch v-model="tabHoldCard" color="primary" density="compact" hide-details
                                :label="`Hold a card (${money(tabHoldCents)})`" />
                            <div class="text-caption text-medium-emphasis mt-1">
                                Authorizes {{ money(tabHoldCents) }} on the reader now and keeps the card on file to close
                                the tab with. With a card held the name can stay blank; the tab is named from the card.
                            </div>
                        </template>
                        <div v-else class="text-caption text-medium-emphasis">Connect the reader to hold a card on this tab.</div>
                    </template>
                </v-card-text>
                <v-card-actions v-if="tabHoldPhase !== 'reader'">
                    <v-spacer />
                    <v-btn variant="text" :disabled="openingTab" @click="openTabDialog = false">Cancel</v-btn>
                    <v-btn color="primary" variant="flat" :loading="openingTab"
                        :disabled="!tabNameInput.trim() && !tabHoldCard" @click="submitOpenTab">
                        {{ tabHoldCard ? 'Open & hold card' : 'Open tab' }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Rename a tab (also prompted when a card hold failed on a tab that had no typed name) -->
        <v-dialog v-model="renameDialog" max-width="400">
            <v-card>
                <v-card-title>Name this tab</v-card-title>
                <v-card-text>
                    <v-text-field v-model="renameInput" label="Tab name" density="compact" hide-details autofocus
                        placeholder="e.g. Red hat table 4" @keyup.enter="confirmRename" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="renameDialog = false">Cancel</v-btn>
                    <v-btn color="primary" variant="flat" :loading="renaming" :disabled="!renameInput.trim()" @click="confirmRename">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Void a tab: manager PIN + reason -->
        <v-dialog v-model="voidDialog" max-width="420" persistent>
            <v-card>
                <v-card-title class="d-flex align-center">
                    Void tab
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="voiding" @click="voidDialog = false" />
                </v-card-title>
                <v-card-text>
                    <p class="text-body-2 mb-3">
                        Voids <strong>{{ voidTarget?.tabName || 'this tab' }}</strong> ({{ money(voidTarget?.totalCents ?? 0) }}).
                        Items already sent to the kitchen are not restocked, and any card hold is released.
                    </p>
                    <div class="text-caption text-medium-emphasis mb-2">
                        <v-icon size="x-small" class="mr-1">mdi-shield-lock-outline</v-icon>Needs a manager PIN
                    </div>
                    <v-text-field v-model="voidReason" label="Reason" density="compact" hide-details class="mb-3" autofocus
                        placeholder="e.g. Walked out, duplicate tab" autocomplete="off" />
                    <v-text-field v-model="voidPin" label="Manager PIN" type="password" inputmode="numeric"
                        autocomplete="one-time-code" density="compact" hide-details @keyup.enter="confirmVoid" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" :disabled="voiding" @click="voidDialog = false">Cancel</v-btn>
                    <v-btn color="error" variant="flat" :loading="voiding" :disabled="!voidReason.trim() || !voidPin.trim()" @click="confirmVoid">Void tab</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Open tabs drawer -->
        <v-navigation-drawer v-model="tabsDrawer" location="right" width="420" temporary>
            <div class="d-flex align-center px-4 py-3">
                <span class="text-h6 font-weight-bold">Open tabs</span>
                <v-chip v-if="openTabs.length" size="small" class="ml-2" variant="tonal">{{ openTabs.length }}</v-chip>
                <v-spacer />
                <v-btn icon="mdi-refresh" size="small" variant="text" :loading="tabsLoading" @click="refreshTabs" />
                <v-btn icon="mdi-close" size="small" variant="text" @click="tabsDrawer = false" />
            </div>
            <v-divider />
            <div v-if="openTabs.length === 0" class="empty-cart text-medium-emphasis">
                <v-icon size="48" class="text-muted2">mdi-book-open-variant</v-icon>
                <div class="mt-2">No open tabs.</div>
            </div>
            <div v-for="t in openTabs" :key="t.saleId" class="tab-row" :class="{ 'tab-row--selected': selectedTab?.saleId === t.saleId }">
                <div class="d-flex align-start ga-2">
                    <div class="flex-grow-1" style="min-width: 0">
                        <div class="font-weight-bold text-truncate">{{ t.tabName }}</div>
                        <div class="text-caption text-medium-emphasis">
                            #{{ t.orderNumber ?? '—' }} · opened {{ tabAge(t.openedAtUtc) }} · {{ t.rounds }} round{{ t.rounds === 1 ? '' : 's' }} · {{ t.itemCount }} item{{ t.itemCount === 1 ? '' : 's' }}
                        </div>
                        <div v-if="t.holdCardLabel || t.customerName" class="d-flex align-center ga-2 mt-1 flex-wrap">
                            <span v-if="t.holdCardLabel" class="rp-pill rp-pill--blue"><v-icon size="12">mdi-credit-card-check</v-icon>{{ t.holdCardLabel }}</span>
                            <span v-if="t.customerName" class="rp-pill">{{ t.customerName }}</span>
                        </div>
                    </div>
                    <div class="tab-row__total">{{ money(t.totalCents) }}</div>
                </div>
                <div class="d-flex ga-2 mt-2">
                    <v-btn size="small" class="flex-grow-1" :color="selectedTab?.saleId === t.saleId ? undefined : 'primary'"
                        :variant="selectedTab?.saleId === t.saleId ? 'tonal' : 'flat'" :disabled="tabBusy"
                        @click="selectTab(t.saleId)">{{ selectedTab?.saleId === t.saleId ? 'Selected' : 'Select' }}</v-btn>
                    <v-btn size="small" variant="outlined" :disabled="tabBusy || paying" @click="startTabClose(t)">Close</v-btn>
                    <v-btn size="small" variant="text" color="error" :disabled="tabBusy" @click="openVoid(t)">Void</v-btn>
                </div>
            </div>
        </v-navigation-drawer>

        <!-- Open drawer: starts this cashier's no-event cash session with whatever float is in the till -->
        <v-dialog v-model="drawerDialog" max-width="360" :persistent="openingDrawer">
            <v-card>
                <v-card-title class="d-flex align-center">
                    Open drawer
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="openingDrawer" @click="drawerDialog = false" />
                </v-card-title>
                <v-card-text>
                    <p class="text-caption text-medium-emphasis mb-3">
                        Count the cash in the drawer before your first sale. You close out against this at the end of your shift.
                    </p>
                    <v-text-field v-model.number="openFloatDollars" label="Starting cash" type="number" prefix="$" min="0" step="0.01"
                        inputmode="decimal" autocomplete="off" density="compact" autofocus hide-details @keyup.enter="openDrawer" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" :disabled="openingDrawer" @click="drawerDialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="openingDrawer" :disabled="openFloatCents < 0" @click="openDrawer">Open</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Close out: the shift review. Tips come out of the drawer first; whatever the drawer can't
             cover is recorded as owed to the worker. The count that follows is blind: no expected figure. -->
        <v-dialog v-model="closeOutDialog" max-width="480" :persistent="submittingCloseOut">
            <v-card>
                <v-card-title class="d-flex align-center">
                    {{ closeOutResult ? 'Shift closed' : 'Close out' }}
                    <v-spacer />
                    <v-btn icon="mdi-close" variant="text" size="small" :disabled="submittingCloseOut" @click="finishCloseOut" />
                </v-card-title>

                <!-- Result -->
                <template v-if="closeOutResult">
                    <v-card-text>
                        <div class="d-flex justify-space-between text-h6 mb-2">
                            <span>Turned in</span><span>{{ money(closeOutResult.workerCountedCents) }}</span>
                        </div>
                        <div class="d-flex justify-space-between mb-2">
                            <span>Tips taken from the drawer</span><span>{{ money(closeOutResult.tipsFromDrawerCents ?? 0) }}</span>
                        </div>
                        <div v-if="(closeOutResult.cashTipsDeclaredCents ?? 0) > 0" class="d-flex justify-space-between mb-2">
                            <span>Cash tips declared</span><span>{{ money(closeOutResult.cashTipsDeclaredCents) }}</span>
                        </div>
                        <template v-if="(closeOutResult.houseOwesCents ?? 0) > 0">
                            <div class="d-flex justify-space-between text-h6 mt-3">
                                <span>House owes you</span><span>{{ money(closeOutResult.houseOwesCents) }}</span>
                            </div>
                            <p class="text-caption text-medium-emphasis mt-1 mb-0">A manager will pay this out to you in cash or through payroll.</p>
                        </template>
                    </v-card-text>
                    <v-card-actions>
                        <v-spacer />
                        <v-btn color="primary" @click="finishCloseOut">Done</v-btn>
                    </v-card-actions>
                </template>

                <!-- Review + count -->
                <template v-else>
                    <v-card-text>
                        <div v-if="reviewLoading" class="d-flex justify-center py-4"><v-progress-circular indeterminate size="24" /></div>
                        <template v-else>
                            <v-alert v-if="reviewError" type="warning" variant="tonal" density="compact" class="mb-3" :text="reviewError" />

                            <!-- 1. Shift summary -->
                            <div class="closeout-summary mb-4">
                                <div class="d-flex justify-space-between text-h6">
                                    <span>Tips to settle</span><span>{{ money(tipsEarnedCents) }}</span>
                                </div>
                                <div class="text-caption text-medium-emphasis">
                                    {{ money(review?.cardTipsCents ?? 0) }} on card · {{ money(review?.cashSaleTipsCents ?? 0) }} on cash sales ·
                                    {{ review?.saleCount ?? 0 }} sale{{ (review?.saleCount ?? 0) === 1 ? '' : 's' }}
                                </div>
                                <!-- A tab opened last shift and closed after that turn-in settles here, not nowhere. -->
                                <div v-if="(review?.tipsFromEarlierShiftsCents ?? 0) > 0" class="text-caption text-medium-emphasis">
                                    Includes {{ money(review?.tipsFromEarlierShiftsCents ?? 0) }} from tabs closed after your last close-out.
                                </div>
                                <div v-if="(review?.owedToYouCents ?? 0) > 0" class="d-flex justify-space-between mt-2">
                                    <span>Owed to you from earlier shifts</span><span>{{ money(review?.owedToYouCents ?? 0) }}</span>
                                </div>
                            </div>

                            <!-- 2. Tips from the drawer (default: everything earned) -->
                            <v-text-field v-model.number="tipsFromDrawerDollars" label="Tips you're taking from the drawer" type="number"
                                prefix="$" min="0" step="0.01" :max="tipsEarnedCents / 100" inputmode="decimal" autocomplete="off"
                                density="compact" :disabled="tipsEarnedCents <= 0" :error-messages="tipsFromDrawerError" />
                            <div class="mb-3">
                                <p class="text-caption text-medium-emphasis mb-1">
                                    Take your tips in cash from the drawer. If the drawer can't cover them, take what's there;
                                    the rest is recorded as owed to you.
                                </p>
                                <div v-if="!tipsFromDrawerError && houseOwesLiveCents > 0" class="d-flex justify-space-between font-weight-medium">
                                    <span>House owes you</span><span>{{ money(houseOwesLiveCents) }}</span>
                                </div>
                            </div>

                            <!-- 3. Declared cash tips (not from the register) -->
                            <v-text-field v-model.number="cashTipsDeclaredDollars" label="Cash tips you received outside the register (optional)"
                                type="number" prefix="$" min="0" step="0.01" inputmode="decimal" autocomplete="off" density="compact" class="mb-3" />

                            <!-- 4. Blind count -->
                            <v-text-field v-model.number="drawerCountDollars" label="Count the cash left in the drawer" type="number"
                                prefix="$" min="0" step="0.01" inputmode="decimal" autocomplete="off" density="compact" class="mb-3"
                                :error-messages="drawerCountError" />
                            <v-text-field v-model="closeNote" label="Note (optional)" autocomplete="off" density="compact" hide-details />
                        </template>
                    </v-card-text>
                    <v-card-actions>
                        <v-spacer />
                        <v-btn variant="text" :disabled="submittingCloseOut" @click="closeOutDialog = false">Cancel</v-btn>
                        <v-btn color="primary" :loading="submittingCloseOut" :disabled="reviewLoading || !canSubmitCloseOut"
                            @click="submitCloseOut">Turn in {{ money(drawerCountCents) }}</v-btn>
                    </v-card-actions>
                </template>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snack.show" :color="snack.color" timeout="5000">{{ snack.text }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue'
import {
    ConcessionService,
    type ConcessionProduct, type ConcessionVariant, type ConcessionModifierGroup,
    type ConcessionSaleLineInput, type ConcessionComboConfig, type ConcessionComboSlot,
    type ConcessionComboSlotOption, type ConcessionDiscountInput, type ConcessionDiscountPreset,
    type ConcessionCompReason, type ConcessionMemberLookup, type ConcessionMemberPerk,
    type ConcessionPrinter, type DisplayState,
    type ConcessionTabSummary, type ConcessionTabDetail, type ConcessionSaleResult, type ConcessionCloseTabRequest,
} from '@/services/ConcessionService'
import { getTerminal, discoverAndConnect, collectAndProcess } from '@/helpers/TerminalHelper'
import CreditLookupField from '@/components/CreditLookupField.vue'
import { type CreditLookupResult } from '@/services/CreditService'
import { printReceipt, type Receipt, type ReceiptLine } from '@/helpers/ReceiptPrinter'
import { printKitchenTicket, type KitchenTicket, type KitchenTicketLine } from '@/helpers/KitchenTicketPrinter'
import { branding } from '@/stores/branding'
import { setHomeScreenIcon } from '@/helpers/HomeScreenIcon'
import authHelper from '@/helpers/AuthHelper'
import { Perm } from '@/helpers/TenantPermissions'
import { CashService, houseOwesCents, type CashSession, type CashTurnIn, type ShiftReview } from '@/services/CashService'

interface CartLine {
    input: ConcessionSaleLineInput
    name: string
    variantLabel: string | null
    modifierLabels: string[]
    notes: string | null
    quantity: number
    unitPrice: number
    lineTotal: number
    discountLabel?: string | null            // human label for an applied line discount/comp
    memberPerk?: ConcessionMemberPerk | null  // perk backing a season_pass/loampass line discount (for the estimate)
}

const svc = new ConcessionService()
const products = ref<ConcessionProduct[]>([])
const loading = ref(true)
const category = ref('all')
const cart = ref<CartLine[]>([])
const tipsEnabled = ref(false)
const tipMode = ref<'none' | 'pct' | 'custom'>('none')
const tipPct = ref(18)
const tipCustomDollars = ref<number | null>(null)
const paying = ref(false)
// Cashier flagged this order as a rush. Applied right after the sale is created (the rush endpoint
// keys off a sale id, which does not exist until then) so it reaches the cook screen immediately.
const rushOrder = ref(false)
const snack = ref({ show: false, text: '', color: 'error' })
const toggling = ref<string | null>(null)

// ── Open tabs: state ─────────────────────────────────────────────────
// A tab is a sale that defers payment; each round of items fires to the kitchen as it is added.
const tabsEnabled = ref(false)
const tabHoldCents = ref(5000)
const openTabs = ref<ConcessionTabSummary[]>([])
const tabsLoading = ref(false)
const tabsDrawer = ref(false)
// Selected: the cart is the NEXT round of this tab (Add to tab / Close / Park / Void replace Cash / Card).
const selectedTab = ref<ConcessionTabDetail | null>(null)
// Closing: the confirm + cash dialogs settle this tab (its rounds, its totals) instead of the cart.
const closingTab = ref<ConcessionTabDetail | null>(null)
const tabBusy = ref(false)

// ── Drawer: open + close-out (shift review) ──────────────────────────
// The cashier's no-event cash session. Tips come out of the drawer at close; whatever the drawer
// can't cover is recorded as owed to them and a manager settles it from Reports > Tips owed.
// The server recomputes every tip figure; what we send is only what the cashier decided to take.
const cashSvc = new CashService()
const canTurnIn = computed(() => authHelper.hasPermission(Perm.CashTurnIn))
const drawerSession = ref<CashSession | null>(null)
const drawerDialog = ref(false)
const openFloatDollars = ref<number | null>(0)
const openingDrawer = ref(false)
const closeOutDialog = ref(false)
const review = ref<ShiftReview | null>(null)
const reviewLoading = ref(false)
const reviewError = ref('')
const tipsFromDrawerDollars = ref<number | null>(null)
const cashTipsDeclaredDollars = ref<number | null>(null)
const drawerCountDollars = ref<number | null>(null)
const closeNote = ref('')
const submittingCloseOut = ref(false)
const closeOutResult = ref<CashTurnIn | null>(null)

// Dollars in the field, cents everywhere else (same as the cash-received input).
// A cleared v-model.number field holds '' rather than null, so "has the cashier typed an amount"
// must be checked explicitly: an empty count or tips field is never silently sent as $0.00.
function hasAmount(d: unknown): d is number { return typeof d === 'number' && Number.isFinite(d) }
function dollarsToCents(d: unknown) { return hasAmount(d) ? Math.round(d * 100) : 0 }
const openFloatCents = computed(() => dollarsToCents(openFloatDollars.value))
const tipsEarnedCents = computed(() => review.value?.tipsEarnedCents ?? 0)
const tipsFromDrawerCents = computed(() => dollarsToCents(tipsFromDrawerDollars.value))
const tipsFromDrawerError = computed(() => {
    if (tipsEarnedCents.value > 0 && !hasAmount(tipsFromDrawerDollars.value)) return 'Enter how much you\'re taking ($0 is fine).'
    if (tipsFromDrawerCents.value < 0) return 'Tips taken can\'t be negative.'
    if (tipsFromDrawerCents.value > tipsEarnedCents.value) return `You have ${money(tipsEarnedCents.value)} in tips to settle; take at most that.`
    return ''
})
const houseOwesLiveCents = computed(() => houseOwesCents(tipsEarnedCents.value, tipsFromDrawerCents.value))
const cashTipsDeclaredCents = computed(() => dollarsToCents(cashTipsDeclaredDollars.value))
const drawerCountCents = computed(() => dollarsToCents(drawerCountDollars.value))
const drawerCountError = computed(() =>
    hasAmount(drawerCountDollars.value) ? (drawerCountCents.value < 0 ? 'The count can\'t be negative.' : '') : '')
const canSubmitCloseOut = computed(() => hasAmount(drawerCountDollars.value) && drawerCountCents.value >= 0
    && cashTipsDeclaredCents.value >= 0 && !tipsFromDrawerError.value)

async function refreshDrawerSession() {
    if (!canTurnIn.value) return
    try { drawerSession.value = (await cashSvc.currentSession(null)).data.data ?? null }
    catch { /* best-effort; the button simply reads "Open drawer" */ }
}
function openDrawerDialog() { openFloatDollars.value = 0; drawerDialog.value = true }
async function openDrawer() {
    if (openFloatCents.value < 0) return
    openingDrawer.value = true
    try {
        drawerSession.value = (await cashSvc.openSession({ eventId: null, openingFloatCents: openFloatCents.value })).data.data
        drawerDialog.value = false
        flash(`Drawer open with ${money(openFloatCents.value)} starting cash.`, 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not open the drawer. Please try again.')
    } finally {
        openingDrawer.value = false
    }
}
async function openCloseOut() {
    closeOutResult.value = null
    review.value = null
    reviewError.value = ''
    tipsFromDrawerDollars.value = null
    cashTipsDeclaredDollars.value = null
    drawerCountDollars.value = null
    closeNote.value = ''
    closeOutDialog.value = true
    await loadReview()
}
// Pulls the worker's tips to settle and resets the drawer-tips field to "take all of it". Called when
// the dialog opens and again if the server reports the figures moved while the dialog was open.
async function loadReview() {
    reviewLoading.value = true
    reviewError.value = ''
    try {
        review.value = (await cashSvc.shiftReview(null)).data.data
        // Default to taking every tip; the cashier lowers it when the drawer is short.
        tipsFromDrawerDollars.value = tipsEarnedCents.value / 100
    } catch (err: any) {
        // A hiccup must not strand the drawer: the blind count still works, tips simply read as -e.
        reviewError.value = err.response?.data?.error || 'Could not load your shift summary. You can still count and turn in the drawer.'
    } finally {
        reviewLoading.value = false
    }
}
async function submitCloseOut() {
    if (!canSubmitCloseOut.value) return
    submittingCloseOut.value = true
    try {
        closeOutResult.value = (await cashSvc.turnIn({
            eventId: null,
            workerCountedCents: drawerCountCents.value,
            note: closeNote.value.trim(),
            tipsFromDrawerCents: tipsFromDrawerCents.value,
            cashTipsDeclaredCents: cashTipsDeclaredCents.value,
        })).data.data
        drawerSession.value = null
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not turn in the drawer. Please try again.')
        // The server rolled the close-out back because a tab closed or a refund landed mid-dialog.
        // Refresh the figures so the field defaults to the real amount instead of the stale one.
        if (err.response?.status === 400 && err.response?.data?.data?.tipsEarnedCents != null) await loadReview()
    } finally {
        submittingCloseOut.value = false
    }
}
function finishCloseOut() { closeOutDialog.value = false; closeOutResult.value = null }

function flash(text: string, color: 'error' | 'success' = 'error') { snack.value = { show: true, text, color } }
function money(cents: number) { return `$${(cents / 100).toFixed(2)}` }
function variantLabel(v: ConcessionVariant) { return [v.size, v.color].filter(Boolean).join(' / ') || 'Standard' }
function priceSuffix(delta: number | null) {
    if (!delta) return ''
    return delta > 0 ? `(+${money(delta)})` : `(${money(delta)})`
}
function groupHint(g: ConcessionModifierGroup) {
    if (g.isRequired && g.maxSelect === 1) return '· choose 1'
    if (g.maxSelect) return `· up to ${g.maxSelect}${g.isRequired ? ', required' : ''}`
    return g.isRequired ? '· required' : ''
}

// Category tabs derived from the loaded products, ordered by each category's sort order.
const categories = computed(() => {
    const map = new Map<string, { key: string; name: string; sort: number }>()
    for (const p of products.value) {
        const key = p.categoryId ?? 'uncategorized'
        if (!map.has(key)) map.set(key, { key, name: p.categoryName ?? 'Other', sort: p.categoryId ? p.categorySortOrder : Number.MAX_SAFE_INTEGER })
    }
    return [...map.values()].sort((a, b) => a.sort - b.sort || a.name.localeCompare(b.name))
})
const filteredProducts = computed(() =>
    category.value === 'all' ? products.value : products.value.filter(p => (p.categoryId ?? 'uncategorized') === category.value))
const subtotal = computed(() => cart.value.reduce((s, l) => s + l.lineTotal, 0))
const cartCount = computed(() => cart.value.reduce((s, l) => s + l.quantity, 0))
const tipCents = computed(() => {
    if (!tipsEnabled.value || tipMode.value === 'none') return 0
    if (tipMode.value === 'custom') return Math.max(0, Math.round((tipCustomDollars.value || 0) * 100))
    // Percent tips run off whatever is being paid for: the cart, or the whole tab at close.
    return Math.round(ckSubtotal.value * tipPct.value / 100)
})

// Tax preview (server is authoritative). Each line's rate comes from its product's tax category, or
// the tenant default. Mirrors the server's exclusive/inclusive math.
const pricesIncludeTax = ref(false)
const taxRateByCategory = ref<Record<string, number>>({})
const defaultTaxBps = ref(0)
function lineTaxBps(productId: string): number {
    const p = products.value.find(x => x.id === productId)
    const id = p?.taxCategoryId
    return (id && taxRateByCategory.value[id] != null) ? taxRateByCategory.value[id] : defaultTaxBps.value
}
function computeTax(baseCents: number, rateBps: number): number {
    if (rateBps <= 0 || baseCents <= 0) return 0
    if (pricesIncludeTax.value) return baseCents - Math.round(baseCents * 10000 / (10000 + rateBps))
    return Math.round(baseCents * rateBps / 10000)
}
const taxCents = computed(() =>
    cart.value.reduce((s, l) => s + computeTax(l.lineTotal, lineTaxBps(l.input.productId)), 0))

// ── Discounts & comps (client estimate; server total is authoritative on the sale) ──
const discountPresets = ref<ConcessionDiscountPreset[]>([])
const compReasonsList = ref<ConcessionCompReason[]>([])
const orderDiscount = ref<ConcessionDiscountInput | null>(null)
const orderDiscountLabel = ref('')
const orderMemberPerk = ref<ConcessionMemberPerk | null>(null)
// One manager PIN authorizes every gated discount/comp on the order; sent with the sale.
const managerPin = ref('')
const managerName = ref('')

// Estimate the cents a discount removes from a base. The server recomputes the real number on the sale.
function estimateDiscount(d: ConcessionDiscountInput, baseCents: number, perk: ConcessionMemberPerk | null): number {
    if (baseCents <= 0) return 0
    switch (d.kind) {
        case 'preset': {
            const p = discountPresets.value.find(x => x.id === d.presetId)
            if (!p) return 0
            return p.kind === 'percent' ? Math.round(baseCents * p.value / 10000) : Math.min(baseCents, p.value)
        }
        case 'percent': return Math.min(baseCents, Math.round(baseCents * (d.percent ?? 0) / 10000))
        case 'amount': return Math.min(baseCents, d.amountCents ?? 0)
        case 'comp': {
            const r = compReasonsList.value.find(x => x.id === d.compReasonId)
            if (!r || r.defaultKind === 'full') return baseCents
            if (r.defaultKind === 'percent') return Math.min(baseCents, Math.round(baseCents * r.defaultValue / 10000))
            return Math.min(baseCents, r.defaultValue)
        }
        case 'season_pass':
        case 'loampass': {
            if (!perk) return 0
            return perk.kind === 'percent' ? Math.round(baseCents * perk.value / 10000) : Math.min(baseCents, perk.value)
        }
    }
    return 0
}
const lineDiscountCents = computed(() => cart.value.reduce((s, l) =>
    s + (l.input.discount ? estimateDiscount(l.input.discount, l.lineTotal, l.memberPerk ?? null) : 0), 0))
const orderDiscountCents = computed(() => {
    if (!orderDiscount.value) return 0
    const base = Math.max(0, subtotal.value - lineDiscountCents.value)
    return estimateDiscount(orderDiscount.value, base, orderMemberPerk.value)
})
const discountCents = computed(() => lineDiscountCents.value + orderDiscountCents.value)

const total = computed(() => Math.max(0,
    subtotal.value + (pricesIncludeTax.value ? 0 : taxCents.value) + tipCents.value - discountCents.value))

// ── Checkout source ("ck"): the cart for a walk-up sale, the tab's server totals when closing a tab ──
// The confirm / cash dialogs, the tip math and the customer display all read these, so one set of
// screens settles either thing. The tab's per-round discounts are already inside its discountCents;
// a discount applied AT close is estimated on top (the server recomputes across all lines).
const ckSubtotal = computed(() => closingTab.value ? closingTab.value.subtotalCents : subtotal.value)
const ckTax = computed(() => closingTab.value ? closingTab.value.taxCents : taxCents.value)
const ckPricesIncludeTax = computed(() => closingTab.value ? closingTab.value.pricesIncludeTax : pricesIncludeTax.value)
const ckDiscount = computed(() => {
    const t = closingTab.value
    if (!t) return discountCents.value
    const atClose = orderDiscount.value
        ? estimateDiscount(orderDiscount.value, Math.max(0, t.subtotalCents - t.discountCents), orderMemberPerk.value) : 0
    return t.discountCents + atClose
})
const ckTotal = computed(() => Math.max(0,
    ckSubtotal.value + (ckPricesIncludeTax.value ? 0 : ckTax.value) + tipCents.value - ckDiscount.value))

// What the customer reviews: cart lines, or every fired round of the closing tab (combo components
// folded under their entree so it reads like the cart does).
interface ReviewLine { name: string; quantity: number; variantLabel: string | null; modifierLabels: string[]; notes: string | null; lineTotal: number; roundNumber?: number }
const reviewLines = computed<ReviewLine[]>(() => {
    const t = closingTab.value
    if (!t) return cart.value.map(l => ({ name: l.name, quantity: l.quantity, variantLabel: l.variantLabel, modifierLabels: l.modifierLabels, notes: l.notes, lineTotal: l.lineTotal }))
    // The flat line list carries roundNumber; if a payload only nests lines under rounds, flatten those.
    const all = t.lines?.length ? t.lines
        : (t.rounds ?? []).flatMap(r => r.lines.map(l => ({ ...l, roundNumber: l.roundNumber ?? r.roundNumber })))
    const sorted = [...all].sort((a, b) => (a.roundNumber ?? 1) - (b.roundNumber ?? 1))
    return sorted.filter(l => !l.parentLineId).map(l => ({
        name: l.name, quantity: l.quantity, variantLabel: l.variantLabel,
        modifierLabels: [
            ...(l.isCombo && l.comboTier ? [`${l.comboTier} combo`] : []),
            ...l.modifiers,
            ...all.filter(c => c.parentLineId === l.lineId).map(c => `${c.name}${c.variantLabel ? ` (${c.variantLabel})` : ''}`),
        ],
        notes: l.notes, lineTotal: l.lineTotalCents, roundNumber: l.roundNumber ?? 1,
    }))
})
const nextRoundNumber = computed(() => {
    const rounds = selectedTab.value?.rounds ?? []
    return rounds.length ? Math.max(...rounds.map(r => r.roundNumber)) + 1 : 1
})

// Adjust a cart line's quantity inline; dropping to 0 removes it. lineTotal tracks unit price.
function setLineQty(i: number, qty: number) {
    if (qty <= 0) { cart.value.splice(i, 1); return }
    const l = cart.value[i]
    l.quantity = qty
    l.lineTotal = l.unitPrice * qty
}

// Online-ordering throttle status (chip + pause control; shown only when the feature is enabled).
const online = ref({ openNow: true, pausedManual: false, capacityEnabled: false, capReached: false })
const pausing = ref(false)
let onlineTimer: number | undefined

// Online ordering closed by the base gate (season / hours / no event day) rather than the manual pause
// or the capacity cap. Pausing has no effect here, so the toggle is swapped for an explanatory caption.
const onlineBaseClosed = computed(() => online.value.capacityEnabled && !online.value.openNow
    && !online.value.pausedManual && !online.value.capReached)

onMounted(() => {
    // Make "Add to Home Screen" from here pin a Cashier icon that reopens the POS chromeless.
    setHomeScreenIcon({ title: `${branding.displayName || 'RidePass'} Cashier`, iconUrl: '/icon-cashier.png', startPath: '/Admin/ConcessionPos' })
    load()
    loadKitchenPrinters()
    refreshOnlineStatus()
    refreshDrawerSession()
    // Keep the Open/Busy chip and the Tabs (n) count current; another register may open or close a tab.
    onlineTimer = window.setInterval(() => { refreshOnlineStatus(); refreshTabs() }, 10000)
})
onUnmounted(() => { if (onlineTimer) window.clearInterval(onlineTimer) })

async function refreshOnlineStatus() {
    try {
        const s = (await svc.orderingStatus() as any).data.data
        online.value = { openNow: s.openNow, pausedManual: s.pausedManual, capacityEnabled: s.capacityEnabled, capReached: s.capReached }
    } catch { /* best-effort; keep last known */ }
}

async function toggleOnlinePause() {
    pausing.value = true
    try {
        await svc.pauseOrdering(!online.value.pausedManual)
        await refreshOnlineStatus()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not change online ordering. Please try again.')
    } finally {
        pausing.value = false
    }
}
async function load() {
    loading.value = true
    try {
        const r = await svc.items()
        products.value = (r.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load the menu. Refresh to try again.')
    } finally {
        loading.value = false
    }
    try {
        const ms = (await svc.menuSettings() as any).data.data
        tipsEnabled.value = ms.tipsEnabled
        customerDisplayEnabled.value = ms.customerDisplayEnabled
        pricesIncludeTax.value = ms.pricesIncludeTax
        tabsEnabled.value = !!ms.tabsEnabled
        tabHoldCents.value = ms.tabHoldCents ?? 5000
        refreshTabs()
    } catch { /* tips/tax optional */ }
    try {
        const cats = (await svc.taxCategories() as any).data.data as { id: string; rateBps: number; isDefault: boolean }[]
        taxRateByCategory.value = Object.fromEntries(cats.map(c => [c.id, c.rateBps]))
        defaultTaxBps.value = cats.find(c => c.isDefault)?.rateBps ?? 0
    } catch { /* tax optional */ }
    try { comboConfig.value = (await svc.getComboConfig() as any).data.data } catch { /* combos optional */ }
    connectReader()
}

// ── Reader ───────────────────────────────────────────────────────────
const readerState = ref<'idle' | 'connecting' | 'connected' | 'error'>('idle')
const readerConnecting = ref(false)
const readerLabel = ref('')
const readerStatus = computed(() =>
    readerState.value === 'connected' ? `Reader: ${readerLabel.value}`
    : readerState.value === 'connecting' ? 'Connecting reader…'
    : readerState.value === 'error' ? 'Reader not connected' : 'No reader')
const readerColor = computed(() =>
    readerState.value === 'connected' ? 'success' : readerState.value === 'error' ? 'error' : 'grey')

async function connectReader() {
    readerConnecting.value = true
    readerState.value = 'connecting'
    try {
        const terminal = await getTerminal(async () => {
            const r = await svc.terminalConnectionToken()
            return (r.data as any).data.secret
        })
        if (!terminal) throw new Error('Card reader SDK unavailable.')
        // Use simulated reader against Stripe test keys; real WisePOS E in production.
        readerLabel.value = await discoverAndConnect(terminal, import.meta.env.MODE !== 'production')
        readerState.value = 'connected'
    } catch (err: any) {
        readerState.value = 'error'
        flash(err.message || 'Could not connect the card reader. Cash still works.')
    } finally {
        readerConnecting.value = false
    }
}

// ── Add item ─────────────────────────────────────────────────────────
const addDialog = ref(false)
const adding = ref<ConcessionProduct | null>(null)
const selVariantId = ref<string | null>(null)
const selOptions = ref<Record<string, string[]>>({})
const selNotes = ref('')
const selQty = ref(1)
const editingIndex = ref<number | null>(null)   // null = adding new; else replacing this cart line

// ── Item details (description + default selections) ──────────────────
const detailsDialog = ref(false)
const detailsProduct = ref<ConcessionProduct | null>(null)
function openDetails(p: ConcessionProduct) { detailsProduct.value = p; detailsDialog.value = true }
function detailDefaults(p: ConcessionProduct): string[] {
    const ids = new Set(p.defaultModifierOptionIds ?? [])
    return p.modifierGroups.flatMap(g => g.options.filter(o => ids.has(o.id)).map(o => o.name))
}

// 86 / un-86 an item for the rest of the day, straight from the POS. Re-loads so stock + state refresh.
async function toggle86(p: ConcessionProduct) {
    toggling.value = p.id
    try {
        await svc.setSoldOut(p.id, !p.manuallySoldOut)
        const r = await svc.items()
        products.value = (r.data as any).data
        flash(p.manuallySoldOut ? `"${p.name}" is back on.` : `"${p.name}" marked sold out.`, 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || `Could not update "${p.name}". Try again.`)
    } finally {
        toggling.value = null
    }
}

// The item's default option ids, limited to options that actually belong to its groups.
function defaultOptionIdsFor(p: ConcessionProduct): string[] {
    const defaults = new Set(p.defaultModifierOptionIds ?? [])
    return p.modifierGroups.flatMap(g => g.options.filter(o => defaults.has(o.id)).map(o => o.id))
}
// Group a flat list of option ids back into the per-group selection shape the modal uses.
function groupSelections(p: ConcessionProduct, optionIds: string[]): Record<string, string[]> {
    const sel: Record<string, string[]> = {}
    for (const g of p.modifierGroups) sel[g.id] = g.options.filter(o => optionIds.includes(o.id)).map(o => o.id)
    return sel
}
// Can we add straight to the cart with defaults (no modal)? Only when there's no variant to pick and
// every required group is already satisfied by its defaults.
function canQuickAdd(p: ConcessionProduct): boolean {
    if (p.variants.length > 0) return false
    const defaults = new Set(defaultOptionIdsFor(p))
    for (const g of p.modifierGroups) {
        if (g.isRequired || g.minSelect > 0) {
            const count = g.options.filter(o => defaults.has(o.id)).length
            if (count < Math.max(1, g.minSelect)) return false
        }
    }
    return true
}
function makeLine(p: ConcessionProduct, variantId: string | null, optionIds: string[], notes: string, qty: number,
    comboTierId: string | null = null, comboSlotSel: Record<string, string> = {}): CartLine {
    const v = p.variants.find(x => x.id === variantId) ?? null
    const modifierLabels = p.modifierGroups.flatMap(g => g.options.filter(o => optionIds.includes(o.id)).map(o => o.name))
    let unit = unitPriceFor(p, v?.id ?? null, optionIds)
    const trimmed = notes.trim() || null
    let comboSelections: { slotId: string; optionId: string }[] | undefined

    const tier = comboTierId ? comboConfig.value.tiers.find(t => t.id === comboTierId) : null
    if (tier) {
        unit += tier.priceCents
        comboSelections = []
        modifierLabels.unshift(`${tier.name} combo`)
        for (const slot of comboConfig.value.slots) {
            const oid = comboSlotSel[slot.id]
            if (!oid) continue
            unit += comboSubDiff(slot, oid, tier.sizeLabel)
            comboSelections.push({ slotId: slot.id, optionId: oid })
            const o = slot.options.find(x => x.id === oid)
            if (o) modifierLabels.push(`${o.componentName}${tier.sizeLabel ? ` (${tier.sizeLabel})` : ''}`)
        }
    }

    return {
        input: { productId: p.id, variantId: v?.id ?? null, quantity: qty, modifierOptionIds: optionIds, notes: trimmed,
            comboTierId: comboTierId ?? null, comboSelections },
        name: p.name, variantLabel: v ? variantLabel(v) : null, modifierLabels, notes: trimmed,
        quantity: qty, unitPrice: unit, lineTotal: unit * qty,
    }
}

function beginAdd(p: ConcessionProduct) {
    if (p.soldOut) { flash(`"${p.name}" is sold out.`); return }
    const defaults = defaultOptionIdsFor(p)
    // Combo-available items always open the modal so the cashier can offer the upgrade.
    if (canQuickAdd(p) && !canCombo(p)) {
        cart.value.push(makeLine(p, null, defaults, '', 1))   // straight to the order with its defaults
        return
    }
    adding.value = p
    selVariantId.value = p.variants.length ? p.variants[0].id : null
    selOptions.value = groupSelections(p, defaults)
    selNotes.value = ''
    selQty.value = 1
    editingIndex.value = null
    selComboTierId.value = null
    selComboSlots.value = defaultComboSlotSel()
    addDialog.value = true
    // For a combo item, scroll the modal straight to the "Make it a combo" section.
    if (canCombo(p)) scrollToCombo()
}

const comboSectionEl = ref<HTMLElement | null>(null)
async function scrollToCombo() {
    await nextTick()
    // Wait out the dialog open transition before scrolling its body.
    setTimeout(() => comboSectionEl.value?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 300)
}

// Reopen the modal to change an already-added line (variant, modifiers, combo, notes, qty).
function lineCustomizable(line: CartLine): boolean {
    const p = products.value.find(x => x.id === line.input.productId)
    return !!p && (canCombo(p) || p.variants.length > 0 || p.modifierGroups.length > 0)
}
function customizeLine(i: number) {
    const line = cart.value[i]
    const p = products.value.find(x => x.id === line.input.productId)
    if (!p) { flash('That item is no longer on the menu.'); return }
    adding.value = p
    selVariantId.value = line.input.variantId
    selOptions.value = groupSelections(p, line.input.modifierOptionIds)
    selNotes.value = line.notes ?? ''
    selQty.value = line.quantity
    editingIndex.value = i
    selComboTierId.value = line.input.comboTierId ?? null
    const sel = defaultComboSlotSel()
    for (const s of line.input.comboSelections ?? []) sel[s.slotId] = s.optionId
    selComboSlots.value = sel
    addDialog.value = true
}

function toggleOption(g: ConcessionModifierGroup, optionId: string, checked: boolean) {
    const cur = selOptions.value[g.id] ?? []
    if (checked) {
        // Single-select group: replace; multi-select: append.
        selOptions.value[g.id] = g.maxSelect === 1 ? [optionId] : [...cur, optionId]
    } else {
        selOptions.value[g.id] = cur.filter(id => id !== optionId)
    }
}

function unitPriceFor(p: ConcessionProduct, variantId: string | null, optionIds: string[]) {
    const v = p.variants.find(x => x.id === variantId)
    let price = v?.priceCents ?? p.priceCents
    for (const g of p.modifierGroups)
        for (const o of g.options)
            if (optionIds.includes(o.id)) price += o.priceDeltaCents
    return price
}

const addPreviewTotal = computed(() => {
    if (!adding.value) return 0
    const ids = Object.values(selOptions.value).flat()
    return (unitPriceFor(adding.value, selVariantId.value, ids) + comboExtra.value) * selQty.value
})

function confirmAdd() {
    const p = adding.value!
    if (p.variants.length && !selVariantId.value) { flash('Choose an option.'); return }
    for (const g of p.modifierGroups) {
        const count = (selOptions.value[g.id] ?? []).length
        if (g.isRequired && count === 0) { flash(`Choose ${g.name}.`); return }
        if (count < g.minSelect) { flash(`Choose at least ${g.minSelect} for ${g.name}.`); return }
        if (g.maxSelect && count > g.maxSelect) { flash(`Choose at most ${g.maxSelect} for ${g.name}.`); return }
    }
    if (selComboTierId.value) {
        for (const slot of comboConfig.value.slots)
            if (slot.isRequired && !selComboSlots.value[slot.id]) { flash(`Choose ${slot.name}.`); return }
    }
    const optionIds = Object.values(selOptions.value).flat()
    const line = makeLine(p, selVariantId.value, optionIds, selNotes.value, selQty.value,
        selComboTierId.value, selComboSlots.value)
    if (editingIndex.value != null) {
        // Carry any applied line discount/comp across the in-place replacement.
        const prev = cart.value[editingIndex.value]
        line.input.discount = prev.input.discount ?? null
        line.discountLabel = prev.discountLabel ?? null
        line.memberPerk = prev.memberPerk ?? null
        cart.value[editingIndex.value] = line   // replace the customized line in place
        editingIndex.value = null
    } else {
        cart.value.push(line)
    }
    addDialog.value = false
}

// ── Make it a combo (layered onto the add modal) ─────────────────────
const comboConfig = ref<ConcessionComboConfig>({ tiers: [], slots: [] })
const selComboTierId = ref<string | null>(null)
const selComboSlots = ref<Record<string, string>>({})   // slotId -> chosen optionId

function canCombo(p: ConcessionProduct | null): boolean {
    return !!p && p.comboAvailable && comboConfig.value.tiers.length > 0
}
// Default selection per slot: the included (default) option, else the first.
function defaultComboSlotSel(): Record<string, string> {
    const sel: Record<string, string> = {}
    for (const slot of comboConfig.value.slots) {
        const def = slot.options.find(o => o.isDefault) ?? slot.options[0]
        if (def) sel[slot.id] = def.id
    }
    return sel
}
// A component's price at the tier's size: its matching size variant, else the base item price.
function componentPriceAtTier(productId: string, sizeLabel: string | null): number {
    const p = products.value.find(x => x.id === productId)
    if (!p) return 0
    if (sizeLabel) {
        const v = p.variants.find(v => (v.size ?? '').toLowerCase() === sizeLabel.toLowerCase())
        if (v) return v.priceCents ?? p.priceCents
    }
    return p.priceCents
}
// Premium substitution surcharge vs the slot's included option, at the tier size (cheaper subs = 0).
function comboSubDiff(slot: ConcessionComboSlot, optionId: string, sizeLabel: string | null): number {
    const chosen = slot.options.find(o => o.id === optionId)
    if (!chosen) return 0
    const included = slot.options.find(o => o.isDefault)
    const chosenPrice = componentPriceAtTier(chosen.componentProductId, sizeLabel)
    const includedPrice = included ? componentPriceAtTier(included.componentProductId, sizeLabel) : chosenPrice
    return Math.max(0, chosenPrice - includedPrice)
}
function comboOptionLabel(slot: ConcessionComboSlot, o: ConcessionComboSlotOption): string {
    const tier = comboConfig.value.tiers.find(t => t.id === selComboTierId.value)
    const diff = tier ? comboSubDiff(slot, o.id, tier.sizeLabel) : 0
    return `${o.componentName}${diff > 0 ? ` +${money(diff)}` : ''}`
}
// Per-unit combo upcharge for the live total: the tier price + any substitution differences.
const comboExtra = computed(() => {
    const tier = comboConfig.value.tiers.find(t => t.id === selComboTierId.value)
    if (!tier) return 0
    let extra = tier.priceCents
    for (const slot of comboConfig.value.slots) {
        const oid = selComboSlots.value[slot.id]
        if (oid) extra += comboSubDiff(slot, oid, tier.sizeLabel)
    }
    return extra
})

// ── Apply discount / comp (cashier picker, order- or line-level) ─────
const discountDialog = ref(false)
const discountTarget = ref<'order' | number>('order')   // 'order' or a cart line index
const discountListsLoaded = ref(false)
const manualPercent = ref<number | null>(null)
const manualDollars = ref<number | null>(null)
const memberQuery = ref('')
const memberResult = ref<ConcessionMemberLookup | null>(null)
const memberLooking = ref(false)
// Manager-PIN gate
const pinDialog = ref(false)
const pinInput = ref('')
const pinVerifying = ref(false)
const pendingDiscount = ref<{ input: ConcessionDiscountInput; label: string; perk: ConcessionMemberPerk | null } | null>(null)

const discountTargetLabel = computed(() =>
    discountTarget.value === 'order' ? (closingTab.value ? 'the whole tab' : 'this order')
    : (cart.value[discountTarget.value as number]?.name ?? 'this item'))

// Lazy-load the active presets + comp reasons the first time the picker opens.
async function ensureDiscountLists() {
    if (discountListsLoaded.value) return
    try {
        const [presets, comps] = await Promise.all([svc.discountPresets(), svc.compReasons()])
        discountPresets.value = ((presets.data as any).data as ConcessionDiscountPreset[])
            .filter(p => p.isActive).sort((a, b) => a.sortOrder - b.sortOrder)
        compReasonsList.value = ((comps.data as any).data as ConcessionCompReason[])
            .filter(c => c.isActive).sort((a, b) => a.sortOrder - b.sortOrder)
        discountListsLoaded.value = true
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load presets and comps. You can still apply a manual or member discount.')
    }
}

function openDiscount(target: 'order' | number) {
    // Closing a tab has no cart; the discount then applies across every round of the tab.
    if (cart.value.length === 0 && !closingTab.value) { flash('Add an item before applying a discount.'); return }
    discountTarget.value = target
    manualPercent.value = null
    manualDollars.value = null
    memberQuery.value = ''
    memberResult.value = null
    discountDialog.value = true
    ensureDiscountLists()
}

// A manual percent/amount or a comp needs manager approval; presets and member perks do not.
function discountNeedsPin(d: ConcessionDiscountInput | null | undefined): boolean {
    return !!d && (d.kind === 'comp' || d.kind === 'percent' || d.kind === 'amount')
}
// Drop the stored manager PIN once no gated discount remains anywhere on the order.
function refreshManagerAuth() {
    const stillGated = discountNeedsPin(orderDiscount.value) || cart.value.some(l => discountNeedsPin(l.input.discount))
    if (!stillGated) { managerPin.value = ''; managerName.value = '' }
}

// Route a chosen discount through the PIN gate when needed, otherwise commit it straight away. Every
// gated discount (each comp and each manual percent/amount) is approved individually, so a prior
// approval on this order does NOT carry over to a new one.
function chooseDiscount(input: ConcessionDiscountInput, label: string, needsPin: boolean, perk: ConcessionMemberPerk | null = null) {
    if (needsPin) {
        pendingDiscount.value = { input, label, perk }
        pinInput.value = ''
        pinDialog.value = true
        return
    }
    commitDiscount(input, label, perk)
}

function commitDiscount(input: ConcessionDiscountInput, label: string, perk: ConcessionMemberPerk | null) {
    if (discountTarget.value === 'order') {
        orderDiscount.value = input
        orderDiscountLabel.value = label
        orderMemberPerk.value = perk
    } else {
        const l = cart.value[discountTarget.value as number]
        if (l) { l.input.discount = input; l.discountLabel = label; l.memberPerk = perk }
    }
    discountDialog.value = false
}

async function verifyPin() {
    const pin = pinInput.value.trim()
    if (!pin) { flash('Enter the manager PIN.'); return }
    pinVerifying.value = true
    try {
        const res = (await svc.verifyManagerPin(pin) as any).data.data as { managerUserId: string; managerName: string }
        managerPin.value = pin
        managerName.value = res.managerName
        pinDialog.value = false
        flash(`Approved by ${res.managerName}`, 'success')
        if (pendingDiscount.value) {
            commitDiscount(pendingDiscount.value.input, pendingDiscount.value.label, pendingDiscount.value.perk)
            pendingDiscount.value = null
        }
    } catch (err: any) {
        flash(err.response?.data?.error || "That manager PIN wasn't recognized.")
    } finally {
        pinVerifying.value = false
    }
}
function cancelPin() {
    pinDialog.value = false
    pendingDiscount.value = null
}

function applyPreset(p: ConcessionDiscountPreset) {
    const label = p.kind === 'percent' ? `${p.name} (${p.value / 100}% off)` : `${p.name} (${money(p.value)} off)`
    // Whether a preset needs a manager is per-discount config now, not a fixed rule. The server
    // re-checks it either way; passing it here just means the cashier is asked for the PIN before
    // the sale is submitted rather than being rejected after.
    chooseDiscount({ kind: 'preset', presetId: p.id }, label, p.requiresManager)
}
function applyManualPercent() {
    const pct = manualPercent.value
    if (!pct || pct <= 0) { flash('Enter a percent greater than 0.'); return }
    if (pct > 100) { flash('Percent cannot be more than 100.'); return }
    chooseDiscount({ kind: 'percent', percent: Math.round(pct * 100) }, `${pct}% off`, true)
}
function applyManualAmount() {
    const dollars = manualDollars.value
    if (!dollars || dollars <= 0) { flash('Enter a dollar amount greater than 0.'); return }
    chooseDiscount({ kind: 'amount', amountCents: Math.round(dollars * 100) }, `${money(Math.round(dollars * 100))} off`, true)
}
function applyComp(r: ConcessionCompReason) {
    chooseDiscount({ kind: 'comp', compReasonId: r.id }, `Comp: ${r.name}`, true)
}
function applyMemberPerk(which: 'season_pass' | 'loampass') {
    const perk = which === 'season_pass' ? memberResult.value?.seasonPass : memberResult.value?.loampass
    if (!perk || !perk.eligible) return
    chooseDiscount({ kind: which, customerEmailOrPhone: memberQuery.value.trim() }, perk.label, false, perk)
}

async function lookupMember() {
    const q = memberQuery.value.trim()
    if (!q) { flash('Enter an email or phone number to look up.'); return }
    memberLooking.value = true
    memberResult.value = null
    try {
        const res = (await svc.memberLookup(q) as any).data.data as ConcessionMemberLookup
        if (!res.found) { flash('No customer found for that email or phone.'); return }
        memberResult.value = res
    } catch (err: any) {
        flash(err.response?.data?.error || 'Member lookup failed. Check the email or phone and try again.')
    } finally {
        memberLooking.value = false
    }
}

function removeOrderDiscount() {
    orderDiscount.value = null
    orderDiscountLabel.value = ''
    orderMemberPerk.value = null
    refreshManagerAuth()
}
function removeLineDiscount(i: number) {
    const l = cart.value[i]
    if (!l) return
    l.input.discount = null
    l.discountLabel = null
    l.memberPerk = null
    refreshManagerAuth()
}
// Reset cart + any applied discounts/manager approval (the cart "Clear" button and a new order).
function clearOrder() {
    cart.value = []
    removeOrderDiscount()
    // Must reset with the cart: a sticky rush flag would silently mark the NEXT customer's order
    // urgent, which the cashier would never think to check.
    rushOrder.value = false
}

// ── Payment ──────────────────────────────────────────────────────────
// Store credit tender: applied up to the total (client estimate; the server re-verifies and
// caps). The money paths (reader charge, cash drawer, change math) all work off the remainder.
const creditAccount = ref<CreditLookupResult | null>(null)
const creditAppliedEstimate = computed(() =>
    creditAccount.value ? Math.min(creditAccount.value.balanceCents, ckTotal.value) : 0)
const dueEstimate = computed(() => ckTotal.value - creditAppliedEstimate.value)

const cashDialog = ref(false)
const cashTenderedDollars = ref<number | null>(null)
const changeCents = computed(() =>
    cashTenderedDollars.value == null ? 0 : Math.round(cashTenderedDollars.value * 100) - dueEstimate.value)
const doneDialog = ref(false)
const lastOrderNumber = ref<number | null>(null)
const lastClosedTabName = ref<string | null>(null)   // done dialog: this sale settled a tab
const lastReceipt = ref<{ orderNumber: number | null; lines: ReceiptLine[]; subtotal: number; tax: number; pricesIncludeTax: boolean; tip: number; discount: number; total: number; method: string } | null>(null)

// ── Customer confirmation (cashier picks tender first; customer reviews + tips + receipt) ──
const confirmDialog = ref(false)
// 'hold' = charge the card held when the tab was opened (tab close only).
const tender = ref<'cash' | 'card' | 'hold'>('card')
const receiptMethod = ref<'print' | 'sms' | 'email' | 'none'>('print')
const receiptDest = ref('')
const customerName = ref('')   // optional name on a counter order
const payButtonLabel = computed(() =>
    tender.value === 'cash' ? 'Continue to cash'
    : tender.value === 'hold' ? `Charge card on file · ${money(dueEstimate.value)}`
    : `Charge card · ${money(dueEstimate.value)}`)

// Back arrow on the confirm screen. Abandoning a tab close must also drop the tab's totals out of the
// checkout math, or the next walk-up cart would show the tab's tip and discount.
function cancelConfirm() {
    confirmDialog.value = false
    // Mid-charge the tab stays "closing" so the finish step can still build its receipt from it.
    if (closingTab.value && !paying.value) abandonTabClose()
}
function cancelCash() {
    cashDialog.value = false
    if (closingTab.value) abandonTabClose()
}

// Cashier taps Cash or Card in the cart, then turns the tablet to the customer to confirm.
function startCheckout(t: 'cash' | 'card') {
    if (cart.value.length === 0) return
    if (t === 'card' && readerState.value !== 'connected') { flash('Connect the card reader first.'); return }
    tender.value = t
    tipMode.value = 'none'
    tipCustomDollars.value = null
    customerTipReceived.value = false
    cashierTipEntry.value = false
    receiptMethod.value = printerUrl.value ? 'print' : 'none'
    receiptDest.value = ''
    customerName.value = ''
    creditAccount.value = null
    confirmDialog.value = true
}
function validateReceipt(): boolean {
    if ((receiptMethod.value === 'sms' || receiptMethod.value === 'email') && !receiptDest.value.trim()) {
        flash(receiptMethod.value === 'sms' ? 'Enter a mobile number for the text receipt.' : 'Enter an email for the receipt.')
        return false
    }
    return true
}
function confirmPay() {
    if (!validateReceipt()) return
    if (closingTab.value) { closeTabPay(); return }
    if (tender.value === 'cash') { confirmDialog.value = false; openCash() }
    else { payCard() }   // keep the confirmation open (progress) until the charge completes
}

function openCash() { cashTenderedDollars.value = null; cashDialog.value = true }

function buildRequest(method: 'cash' | 'card') {
    return {
        // Each line's input already carries its own `discount` when one was applied.
        items: cart.value.map(l => l.input),
        tipCents: tipCents.value,
        paymentMethod: method,
        customerName: customerName.value.trim() || undefined,
        discount: orderDiscount.value ?? undefined,
        managerPin: managerPin.value || undefined,
        creditAccountId: creditAccount.value?.id ?? undefined,
        creditCents: creditAccount.value?.balanceCents ?? 0,
    }
}

async function payCash() {
    if (closingTab.value) { await closeTabCash(); return }
    paying.value = true
    try {
        const r = await svc.createSale(buildRequest('cash'))
        const res = (r.data as any).data
        cashDialog.value = false
        finishOrder(res.saleId, res.orderNumber,
            (res.creditAppliedCents ?? 0) > 0 ? 'Cash + credit' : 'Cash', res.totalCents, res.discountCents)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Cash sale failed. Nothing was charged.')
    } finally {
        paying.value = false
    }
}

async function payCard() {
    // Nothing due on the reader when the order is fully comped OR fully covered by store credit.
    const isZero = dueEstimate.value <= 0
    if (!isZero && readerState.value !== 'connected') { flash('Connect the card reader first.'); return }
    paying.value = true
    try {
        const r = await svc.createSale(buildRequest('card'))
        const res = (r.data as any).data
        // No client secret means the server recorded it paid immediately (a $0 / fully-comped /
        // fully-credit order): skip the reader and finish with the order number it assigned.
        if (!res.clientSecret) {
            finishOrder(res.saleId, res.orderNumber ?? null,
                (res.creditAppliedCents ?? 0) > 0 ? 'Store credit' : 'Comp', res.totalCents, res.discountCents)
            return
        }
        const terminal = await getTerminal(async () => (await svc.terminalConnectionToken() as any).data.data.secret)
        await collectAndProcess(terminal!, res.clientSecret)
        // Finalize right away so the order number is assigned without waiting on the Stripe webhook;
        // fall back to polling if the finalize call itself fails for some reason.
        let orderNumber: number | null = null
        try { orderNumber = (await svc.finalizeCard(res.saleId) as any).data.data?.orderNumber ?? null }
        catch { /* fall back to the webhook/poll */ }
        if (orderNumber == null) orderNumber = await pollOrderNumber(res.saleId)
        finishOrder(res.saleId, orderNumber,
            (res.creditAppliedCents ?? 0) > 0 ? 'Card + credit' : 'Card', res.totalCents, res.discountCents)
    } catch (err: any) {
        flash(err.message || err.response?.data?.error || 'Card payment failed. Try again or use cash.')
    } finally {
        paying.value = false
    }
}

async function pollOrderNumber(saleId: string): Promise<number | null> {
    for (let i = 0; i < 10; i++) {
        try {
            const s = (await svc.saleStatus(saleId) as any).data.data
            if (s.status === 'paid' && s.orderNumber != null) return s.orderNumber
        } catch { /* keep polling */ }
        await new Promise(res => setTimeout(res, 1000))
    }
    return null   // paid, but number lagged; receipt still prints without it
}

function finishOrder(saleId: string, orderNumber: number | null, method: string, serverTotalCents?: number, serverDiscountCents?: number) {
    lastOrderNumber.value = orderNumber
    lastClosedTabName.value = null
    // Fire-and-forget: the money is already taken, so a failed rush flag must not read as a failed
    // sale. It is surfaced rather than swallowed, because a cook who never sees RUSH will not hurry.
    if (rushOrder.value) {
        svc.setRush(saleId, true).catch((err: any) => {
            flash(err.response?.data?.error
                || `Order #${orderNumber ?? ''} was paid but could not be flagged as a rush. Tell the kitchen directly.`)
        })
    }
    // Trust the server's total + discount when present; fall back to the client estimate otherwise.
    const discount = serverDiscountCents ?? discountCents.value
    const finalTotal = serverTotalCents ?? total.value
    lastReceipt.value = { orderNumber, lines: [...cart.value], subtotal: pricesIncludeTax.value ? subtotal.value - taxCents.value : subtotal.value, tax: taxCents.value, pricesIncludeTax: pricesIncludeTax.value, tip: tipCents.value, discount, total: finalTotal, method }
    confirmDialog.value = false
    doneDialog.value = true
    deliverReceipt(saleId)
    // Independent of the customer receipt: the kitchen gets its ticket whether or not the customer
    // wanted one, and whichever of the two fails must not stop the other.
    sendKitchenTickets({ lines: [...cart.value], orderNumber, customerName: customerName.value.trim() || null, tabLabel: null })
}

// ── Open tabs: list, select, open (with optional card hold), rounds, rename, close, void ──
async function refreshTabs() {
    if (!tabsEnabled.value) return
    tabsLoading.value = true
    try {
        openTabs.value = (await svc.tabs()).data.data
        pruneTabMembers()
        // Closed or voided from another register: leave tab mode rather than post a round to a sale
        // that will reject it.
        if (selectedTab.value && !openTabs.value.some(t => t.saleId === selectedTab.value!.saleId)) {
            flash(`Tab "${selectedTab.value.tabName ?? ''}" is no longer open.`)
            selectedTab.value = null
        }
    } catch { /* best-effort; keep the last known list */ }
    finally { tabsLoading.value = false }
}
function openTabsDrawer() { tabsDrawer.value = true; refreshTabs() }
function tabAge(iso: string) {
    const mins = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 60000))
    if (mins < 1) return 'just now'
    if (mins < 60) return `${mins}m ago`
    return `${Math.floor(mins / 60)}h ${String(mins % 60).padStart(2, '0')}m ago`
}

// The member attached when a tab was opened. The server keeps only the purchaser name, so the perk
// and the receipt email are remembered here (per tablet) to pre-fill the close; a cashier can still
// apply the perk by hand at close if this tablet never saw the open.
interface TabMember { name: string | null; email: string | null; query: string; perkKind: 'season_pass' | 'loampass' | null; perk: ConcessionMemberPerk | null }
const TAB_MEMBERS_KEY = 'concessionTabMembers'
function loadTabMembers(): Record<string, TabMember> {
    try { return JSON.parse(localStorage.getItem(TAB_MEMBERS_KEY) || '{}') } catch { return {} }
}
const tabMembers = ref<Record<string, TabMember>>(loadTabMembers())
function saveTabMembers() { localStorage.setItem(TAB_MEMBERS_KEY, JSON.stringify(tabMembers.value)) }
function rememberTabMember(saleId: string, m: TabMember | null) { if (!m) return; tabMembers.value[saleId] = m; saveTabMembers() }
function forgetTabMember(saleId: string) { if (saleId in tabMembers.value) { delete tabMembers.value[saleId]; saveTabMembers() } }
function pruneTabMembers() {
    const open = new Set(openTabs.value.map(t => t.saleId))
    let changed = false
    for (const id of Object.keys(tabMembers.value)) if (!open.has(id)) { delete tabMembers.value[id]; changed = true }
    if (changed) saveTabMembers()
}

// Open-tab dialog
const openTabDialog = ref(false)
const openingTab = ref(false)
const tabNameInput = ref('')
const tabHoldCard = ref(false)
const tabHoldPhase = ref<'form' | 'reader'>('form')
const tabMember = ref<TabMember | null>(null)

function openOpenTab() {
    tabNameInput.value = ''
    tabHoldCard.value = false
    tabHoldPhase.value = 'form'
    tabMember.value = null
    memberQuery.value = ''
    memberResult.value = null
    openTabDialog.value = true
}
function memberPerkSummary(m: ConcessionMemberLookup) {
    const perks = [m.seasonPass, m.loampass].filter(p => p?.eligible).map(p => p!.label)
    return perks.length ? perks.join(' · ') : 'No member perks'
}
function attachMember() {
    const m = memberResult.value
    if (!m) return
    const kind: TabMember['perkKind'] = m.seasonPass?.eligible ? 'season_pass' : m.loampass?.eligible ? 'loampass' : null
    const perk = kind === 'season_pass' ? m.seasonPass! : kind === 'loampass' ? m.loampass! : null
    tabMember.value = { name: m.customerName ?? null, email: m.customerEmail ?? null, query: memberQuery.value.trim(), perkKind: kind, perk }
    memberResult.value = null
}

async function submitOpenTab() {
    const name = tabNameInput.value.trim()
    const hold = tabHoldCard.value && readerState.value === 'connected'
    if (!name && !hold) { flash('Give the tab a name, or hold a card and it will be named from the card.'); return }
    openingTab.value = true
    const fired = [...cart.value]   // round 1, for the kitchen tickets
    const member = tabMember.value
    try {
        const res = (await svc.openTab({
            name: name || undefined,
            customerName: member?.name ?? undefined,
            items: cart.value.map(l => l.input),
            discount: orderDiscount.value ?? undefined,
            managerPin: managerPin.value || undefined,
            holdCard: hold,
        })).data.data
        rememberTabMember(res.saleId, member)
        if (rushOrder.value) {
            svc.setRush(res.saleId, true).catch((err: any) => {
                flash(err.response?.data?.error || 'The tab opened but could not be flagged as a rush. Tell the kitchen directly.')
            })
        }
        clearOrder()

        let detail: ConcessionTabDetail | null = null
        let needsName = false
        if (hold && res.holdClientSecret) {
            // Same reader flow as a card sale, on the hold intent (manual capture: nothing is charged
            // now). The tab already exists at this point, so a failed hold leaves it open, cardless.
            tabHoldPhase.value = 'reader'
            try {
                const terminal = await getTerminal(async () => (await svc.terminalConnectionToken() as any).data.data.secret)
                await collectAndProcess(terminal!, res.holdClientSecret)
                detail = (await svc.tabHoldConfirmed(res.saleId)).data.data
                flash(`Card held${detail.holdCardLabel ? ` · ${detail.holdCardLabel}` : ''}. Tab "${detail.tabName ?? name}" is open.`, 'success')
            } catch (err: any) {
                flash(`${err.message || 'The card hold did not go through.'} The tab is open without a card on file.`)
                needsName = !name
            }
        } else {
            flash(`Tab "${res.tabName || name}" opened · order #${res.orderNumber ?? '—'}.`, 'success')
        }
        const tabName = detail?.tabName || res.tabName || name
        // Printed after the hold so an auto-named tab's ticket carries the card name, not a blank.
        if (fired.length) {
            sendKitchenTickets({ lines: fired, orderNumber: res.orderNumber, customerName: member?.name ?? null,
                tabLabel: `Tab: ${tabName || `#${res.orderNumber ?? ''}`} · Round 1` })
        }
        openTabDialog.value = false
        await selectTab(res.saleId, detail)
        refreshTabs()
        if (needsName) openRename()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not open the tab. Nothing was sent to the kitchen.')
    } finally {
        openingTab.value = false
        tabHoldPhase.value = 'form'
    }
}

async function selectTab(saleId: string, detail: ConcessionTabDetail | null = null) {
    tabBusy.value = true
    try {
        selectedTab.value = detail ?? (await svc.tab(saleId)).data.data
        tabsDrawer.value = false
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load that tab.')
    } finally { tabBusy.value = false }
}
// Park = leave tab mode. The cart is left alone: anything rung up stays rung up, as a walk-up order.
function parkTab() { selectedTab.value = null }

// Post the cart as the tab's next round. The kitchen gets it now; the money waits for close.
async function addRoundToTab() {
    const tab = selectedTab.value
    if (!tab || cart.value.length === 0 || tabBusy.value) return
    tabBusy.value = true
    const fired = [...cart.value]
    const expectedRound = nextRoundNumber.value
    try {
        const detail = (await svc.tabRound(tab.saleId, {
            items: cart.value.map(l => l.input),
            discount: orderDiscount.value ?? undefined,
            managerPin: managerPin.value || undefined,
        })).data.data
        if (rushOrder.value) {
            svc.setRush(tab.saleId, true).catch((err: any) => {
                flash(err.response?.data?.error || 'The round was sent but could not be flagged as a rush. Tell the kitchen directly.')
            })
        }
        const round = detail.rounds.length ? Math.max(...detail.rounds.map(r => r.roundNumber)) : expectedRound
        selectedTab.value = detail
        clearOrder()
        flash(`Round ${round} sent to the kitchen · ${detail.tabName ?? ''}`, 'success')
        sendKitchenTickets({ lines: fired, orderNumber: detail.orderNumber, customerName: null,
            tabLabel: `Tab: ${detail.tabName ?? `#${detail.orderNumber ?? ''}`} · Round ${round}` })
        refreshTabs()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not add the round. Nothing was sent to the kitchen.')
    } finally { tabBusy.value = false }
}

// Rename
const renameDialog = ref(false)
const renameInput = ref('')
const renaming = ref(false)
function openRename() { renameInput.value = selectedTab.value?.tabName ?? ''; renameDialog.value = true }
async function confirmRename() {
    const tab = selectedTab.value
    const name = renameInput.value.trim()
    if (!tab || !name) return
    renaming.value = true
    try {
        selectedTab.value = (await svc.renameTab(tab.saleId, { name, customerName: tab.customerName ?? undefined })).data.data
        renameDialog.value = false
        refreshTabs()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not rename the tab.')
    } finally { renaming.value = false }
}

// Close: the existing customer-confirmation screen, fed by the tab instead of the cart.
async function startTabClose(t: { saleId: string }) {
    if (cart.value.length > 0) { flash('Add the cart to the tab (or clear it) before closing a tab.'); return }
    tabBusy.value = true
    try {
        const detail = (await svc.tab(t.saleId)).data.data   // fresh totals, in case another register added a round
        closingTab.value = detail
        tabsDrawer.value = false
        tender.value = detail.holdCardLabel ? 'hold' : readerState.value === 'connected' ? 'card' : 'cash'
        tipMode.value = 'none'
        tipCustomDollars.value = null
        customerTipReceived.value = false
        cashierTipEntry.value = false
        customerName.value = detail.customerName ?? ''
        creditAccount.value = null
        receiptMethod.value = printerUrl.value ? 'print' : 'none'
        receiptDest.value = ''
        // Member attached at open: perk pre-applied across the whole tab, receipt email pre-filled.
        removeOrderDiscount()
        const member = tabMembers.value[detail.saleId]
        if (member?.perk && member.perkKind) {
            orderDiscount.value = { kind: member.perkKind, customerEmailOrPhone: member.query }
            orderDiscountLabel.value = member.perk.label
            orderMemberPerk.value = member.perk
        }
        if (member?.email) {
            receiptDest.value = member.email
            if (receiptMethod.value === 'none') receiptMethod.value = 'email'
        }
        confirmDialog.value = true
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load that tab.')
    } finally { tabBusy.value = false }
}
function abandonTabClose() {
    closingTab.value = null
    tipMode.value = 'none'
    tipCustomDollars.value = null
    creditAccount.value = null
    removeOrderDiscount()
}
function buildCloseRequest(method: ConcessionCloseTabRequest['paymentMethod']): ConcessionCloseTabRequest {
    return {
        paymentMethod: method,
        tipCents: tipCents.value,
        creditAccountId: creditAccount.value?.id ?? undefined,
        creditCents: creditAccount.value?.balanceCents ?? 0,
        discount: orderDiscount.value ?? undefined,
        managerPin: managerPin.value || undefined,
    }
}
async function closeTabPay() {
    if (tender.value === 'cash') { confirmDialog.value = false; openCash(); return }
    await closeTabWithCard(tender.value)
}
async function closeTabCash() {
    const tab = closingTab.value
    if (!tab) return
    paying.value = true
    try {
        const res = (await svc.closeTab(tab.saleId, buildCloseRequest('cash'))).data.data
        cashDialog.value = false
        finishTabClose(tab, res, (res.creditAppliedCents ?? 0) > 0 ? 'Cash + credit' : 'Cash')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not close the tab. Nothing was charged.')
    } finally { paying.value = false }
}
// The server refuses to capture a hold smaller than what is due; the POS then collects the card again.
function holdTooSmall(err: any): boolean {
    const msg = String(err?.response?.data?.error ?? err?.response?.data?.message ?? '')
    return err?.response?.status === 400 && /exceeds the card hold/i.test(msg)
}
async function closeTabWithCard(method: 'card' | 'hold') {
    const tab = closingTab.value
    if (!tab) return
    const isZero = dueEstimate.value <= 0
    if (method === 'card' && !isZero && readerState.value !== 'connected') { flash('Connect the card reader first.'); return }
    paying.value = true
    try {
        let used = method
        let res: ConcessionSaleResult
        try {
            res = (await svc.closeTab(tab.saleId, buildCloseRequest(method))).data.data
        } catch (err: any) {
            if (method !== 'hold' || !holdTooSmall(err)) throw err
            if (readerState.value !== 'connected') {
                flash('The tab total is more than the card hold and no reader is connected. Connect the reader to collect the card again, or take cash.')
                return
            }
            flash('The tab total is more than the card hold. Collect the card on the reader.', 'success')
            used = 'card'
            tender.value = 'card'
            res = (await svc.closeTab(tab.saleId, buildCloseRequest('card'))).data.data
        }
        const credited = (res.creditAppliedCents ?? 0) > 0
        // No client secret: settled server-side (card on file captured, $0 due, comp, or credit covered it).
        if (!res.clientSecret) {
            finishTabClose(tab, res, used === 'hold' ? (credited ? 'Card on file + credit' : 'Card on file') : credited ? 'Store credit' : 'Comp')
            return
        }
        const terminal = await getTerminal(async () => (await svc.terminalConnectionToken() as any).data.data.secret)
        await collectAndProcess(terminal!, res.clientSecret)
        let orderNumber: number | null = null
        try { orderNumber = (await svc.finalizeCard(res.saleId) as any).data.data?.orderNumber ?? null }
        catch { /* fall back to the webhook/poll */ }
        if (orderNumber == null) orderNumber = await pollOrderNumber(res.saleId)
        finishTabClose(tab, { ...res, orderNumber: orderNumber ?? tab.orderNumber }, credited ? 'Card + credit' : 'Card')
    } catch (err: any) {
        flash(err.message || err.response?.data?.error || 'Card payment failed. Try again, another card, or cash.')
    } finally { paying.value = false }
}
function finishTabClose(tab: ConcessionTabDetail, res: ConcessionSaleResult, method: string) {
    lastOrderNumber.value = res.orderNumber ?? tab.orderNumber
    lastClosedTabName.value = tab.tabName || `#${tab.orderNumber ?? ''}`
    // Capture the review lines + totals BEFORE leaving close mode (they are derived from closingTab).
    lastReceipt.value = {
        orderNumber: lastOrderNumber.value, lines: reviewLines.value,
        subtotal: ckPricesIncludeTax.value ? ckSubtotal.value - ckTax.value : ckSubtotal.value, tax: ckTax.value,
        pricesIncludeTax: ckPricesIncludeTax.value, tip: tipCents.value,
        discount: res.discountCents ?? ckDiscount.value, total: res.totalCents ?? ckTotal.value, method,
    }
    lastKitchenJob.value = null   // every round already went to the kitchen when it was fired
    confirmDialog.value = false
    doneDialog.value = true
    deliverReceipt(tab.saleId)
    openTabs.value = openTabs.value.filter(t => t.saleId !== tab.saleId)
    forgetTabMember(tab.saleId)
    if (selectedTab.value?.saleId === tab.saleId) selectedTab.value = null
    // Leave close mode with clean checkout state so the next cart starts from zero.
    closingTab.value = null
    tipMode.value = 'none'
    tipCustomDollars.value = null
    removeOrderDiscount()
    creditAccount.value = null
    refreshTabs()
}

// Void: manager PIN + reason. Fired items are not restocked; the hold is released server-side.
const voidDialog = ref(false)
const voidTarget = ref<{ saleId: string; tabName: string | null; totalCents: number } | null>(null)
const voidReason = ref('')
const voidPin = ref('')
const voiding = ref(false)
function openVoid(t: { saleId: string; tabName: string | null; totalCents: number }) {
    voidTarget.value = t
    voidReason.value = ''
    voidPin.value = ''
    voidDialog.value = true
}
async function confirmVoid() {
    const t = voidTarget.value
    if (!t) return
    const reason = voidReason.value.trim(), pin = voidPin.value.trim()
    if (!reason || !pin) { flash('Enter a reason and the manager PIN.'); return }
    voiding.value = true
    try {
        await svc.voidTab(t.saleId, pin, reason)
        voidDialog.value = false
        openTabs.value = openTabs.value.filter(x => x.saleId !== t.saleId)
        forgetTabMember(t.saleId)
        if (selectedTab.value?.saleId === t.saleId) selectedTab.value = null
        if (closingTab.value?.saleId === t.saleId) { confirmDialog.value = false; cashDialog.value = false; abandonTabClose() }
        flash(`Tab "${t.tabName ?? ''}" voided.`, 'success')
        refreshTabs()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not void the tab. Check the manager PIN.')
    } finally { voiding.value = false }
}

// ── Kitchen ticket printing ────────────────────────────────────────────────
// Tenant-configured (Concessions > Stations > Kitchen ticket printers), unlike the customer receipt
// printer which is per-tablet localStorage. Printed from here because the API is in the cloud and
// cannot reach a printer on the venue LAN.
const kitchenPrinters = ref<ConcessionPrinter[]>([])
// What last went to the kitchen (a sale, or one tab round), kept for "Reprint kitchen tickets".
interface KitchenJob { lines: CartLine[]; orderNumber: number | null; customerName: string | null; tabLabel: string | null }
const lastKitchenJob = ref<KitchenJob | null>(null)

async function loadKitchenPrinters() {
    try { kitchenPrinters.value = (await svc.activePrinters()).data.data }
    catch { kitchenPrinters.value = [] }   // no printers is a valid setup; the cook screen still works
}

// The lines a given printer is responsible for. A printer with no stations prints the whole order,
// which is also what catches items whose product has no station assigned.
function linesForPrinter(p: ConcessionPrinter, lines: CartLine[]): KitchenTicketLine[] {
    const scoped = p.stationIds.length > 0
    return lines
        .filter(l => {
            const product = products.value.find(pr => pr.id === l.input.productId)
            // Grab-and-go never reaches the cook screen, so it has no business on a ticket either.
            if (!product || !product.requiresPrep) return false
            if (!scoped) return true
            return product.stationId !== null && p.stationIds.includes(product.stationId)
        })
        .map(l => ({
            quantity: l.quantity, name: l.name, variantLabel: l.variantLabel,
            modifierLabels: l.modifierLabels, notes: l.notes,
        }))
}

// Sends one ticket per configured printer that has anything to make. Never throws: the money is
// already taken, so a dead printer must not read as a failed sale. Failures name the printer so the
// cashier knows which one to walk to, and can hit "Reprint tickets" once it's fixed.
async function sendKitchenTickets(job: KitchenJob | null, isReprint = false) {
    if (!job) return
    lastKitchenJob.value = job
    if (kitchenPrinters.value.length === 0) return

    const failed: string[] = []
    let printed = 0

    for (const p of kitchenPrinters.value) {
        const lines = linesForPrinter(p, job.lines)
        if (lines.length === 0) continue   // nothing for this station on this order

        const ticket: KitchenTicket = {
            header: branding.displayName || 'Order',
            orderNumber: job.orderNumber,
            customerName: job.customerName,
            tabLabel: job.tabLabel,
            stationLabel: p.name,
            placedAt: new Date(),
            lines,
            isReprint,
        }
        try { await printKitchenTicket(p.url, ticket); printed++ }
        catch (err: any) { failed.push(`${p.name} ${err.message || 'failed'}`) }
    }

    if (failed.length > 0) {
        flash(`Kitchen ticket problem - ${failed.join('; ')}. The order is on the cook screen; use Reprint tickets to retry.`, 'error')
    } else if (isReprint && printed > 0) {
        flash(`Reprinted ${printed} kitchen ticket${printed === 1 ? '' : 's'}.`, 'success')
    } else if (isReprint) {
        flash('Nothing on this order needs making, so there was no ticket to reprint.')
    }
}

// Deliver the receipt per the customer's choice on the confirmation screen.
async function deliverReceipt(saleId: string) {
    if (receiptMethod.value === 'print') { await autoPrint(); return }
    if (receiptMethod.value === 'sms' || receiptMethod.value === 'email') {
        try {
            await svc.sendReceipt(saleId, receiptMethod.value, receiptDest.value.trim())
            flash('Receipt sent.', 'success')
        } catch (err: any) {
            flash(err.response?.data?.error || 'Could not send the receipt.')
        }
    }
}

function newOrder() {
    cart.value = []
    removeOrderDiscount()
    rushOrder.value = false
    tipMode.value = 'none'
    tipCustomDollars.value = null
    receiptMethod.value = 'print'
    receiptDest.value = ''
    closingTab.value = null
    lastClosedTabName.value = null
    doneDialog.value = false
}

// ── Receipt printing (silent, straight to the Epson network printer; no dialog) ──
const printerUrl = ref(localStorage.getItem('concessionPrinterUrl') || '')
const printerDialog = ref(false)
function savePrinter() {
    localStorage.setItem('concessionPrinterUrl', printerUrl.value.trim())
    printerDialog.value = false
    flash('Printer saved.', 'success')
}

// ── Customer-facing display (paired second tablet: mirrors the order, captures the tip) ──
const displayDialog = ref(false)
const customerDisplayEnabled = ref(false)   // tenant setting: this venue uses customer-facing displays
const pairedDisplayId = ref(localStorage.getItem('concessionPosDisplayId') || '')
// Red-flag the toolbar button when this venue expects a customer display but this register has none
// paired, or the paired one has stopped syncing.
const displayAttention = computed(() =>
    (customerDisplayEnabled.value && !pairedDisplayId.value) || displayPushFailing.value)
const displayPairCodeInput = ref('')
const pairingDisplay = ref(false)
const displayPushFailing = ref(false)
// Customer answered the tip prompt (including "no tip"); shown on the POS confirm screen.
const customerTipReceived = ref(false)
// Cashier fallback: reveal the on-POS tip buttons when the customer walks away from the display.
const cashierTipEntry = ref(false)

async function pairDisplay() {
    const code = displayPairCodeInput.value.trim()
    if (!code) { flash('Enter the pair code shown on the customer display tablet.'); return }
    pairingDisplay.value = true
    try {
        const d = (await svc.displayByCode(code) as any).data.data
        pairedDisplayId.value = d.id
        localStorage.setItem('concessionPosDisplayId', d.id)
        displayPairCodeInput.value = ''
        lastPushedDisplayState = ''   // force a fresh push so the display leaves its welcome screen promptly
        pushDisplayState()
        flash('Customer display paired.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Pairing failed. Check the code on the customer display tablet.')
    } finally { pairingDisplay.value = false }
}

function unpairDisplay() {
    // Best-effort: send the display back to its welcome screen before forgetting it.
    if (pairedDisplayId.value) svc.updateDisplayState(pairedDisplayId.value, idleDisplayState()).catch(() => { /* it will fall idle on its own */ })
    pairedDisplayId.value = ''
    localStorage.removeItem('concessionPosDisplayId')
    flash('Customer display unpaired.', 'success')
}

function idleDisplayState(): DisplayState {
    return { status: 'idle', lines: [], subtotal: 0, taxCents: 0, pricesIncludeTax: false, discountCents: 0, tipCents: 0, totalCents: 0, tipsEnabled: false, orderNumber: null }
}

// Snapshot of what the customer should see right now. Recomputes on any cart/checkout change;
// the watcher below pushes it (debounced) whenever the serialized form actually changes.
const displaySnapshot = computed<DisplayState>(() => {
    let status: DisplayState['status'] = 'idle'
    if (doneDialog.value) status = 'done'
    else if (paying.value) status = 'processing'
    else if (confirmDialog.value) status = tipsEnabled.value ? 'tip' : 'building'
    else if (cart.value.length > 0 || closingTab.value) status = 'building'
    return {
        status,
        // The cart, or the whole tab while it is being closed (same shape either way).
        lines: status === 'idle' || status === 'done' ? [] : reviewLines.value.map(l => ({
            name: l.name, quantity: l.quantity, variantLabel: l.variantLabel,
            modifierLabels: l.modifierLabels, lineTotal: l.lineTotal,
        })),
        subtotal: ckSubtotal.value,
        taxCents: ckTax.value,
        pricesIncludeTax: ckPricesIncludeTax.value,
        discountCents: ckDiscount.value,
        tipCents: tipCents.value,
        totalCents: ckTotal.value,
        tipsEnabled: tipsEnabled.value,
        orderNumber: status === 'done' ? lastOrderNumber.value : null,
    }
})

let lastPushedDisplayState = ''
let displayPushTimer: number | undefined
let displayPushFailures = 0

watch(displaySnapshot, () => {
    if (!pairedDisplayId.value) return
    if (displayPushTimer) window.clearTimeout(displayPushTimer)
    displayPushTimer = window.setTimeout(pushDisplayState, 250)
})

async function pushDisplayState() {
    if (!pairedDisplayId.value) return
    const snap = displaySnapshot.value
    const json = JSON.stringify(snap)
    if (json === lastPushedDisplayState) return
    try {
        await svc.updateDisplayState(pairedDisplayId.value, snap)
        lastPushedDisplayState = json
        displayPushFailures = 0
        displayPushFailing.value = false
    } catch {
        // Keep ringing items regardless; tell the cashier once if the display stays out of sync.
        if (++displayPushFailures === 3) {
            displayPushFailing.value = true
            flash('The customer display stopped syncing. Check its connection or re-pair from the Display button.')
        }
    }
}

// While the confirm screen is open with a paired display, poll for the customer's tip choice and
// apply it to the order as a custom tip (server re-validates on the sale).
let tipPollTimer: number | undefined
watch(confirmDialog, (open) => {
    if (open && pairedDisplayId.value && tipsEnabled.value) {
        tipPollTimer = window.setInterval(pollCustomerTip, 1000)
    } else if (tipPollTimer) {
        window.clearInterval(tipPollTimer)
        tipPollTimer = undefined
    }
})

async function pollCustomerTip() {
    if (!pairedDisplayId.value || cashierTipEntry.value) return
    try {
        const d = (await svc.display(pairedDisplayId.value) as any).data.data
        if (d.tipCents != null) {
            customerTipReceived.value = true
            tipMode.value = d.tipCents > 0 ? 'custom' : 'none'
            tipCustomDollars.value = d.tipCents > 0 ? d.tipCents / 100 : null
        }
    } catch { /* transient; next poll retries */ }
}

onUnmounted(() => {
    if (displayPushTimer) window.clearTimeout(displayPushTimer)
    if (tipPollTimer) window.clearInterval(tipPollTimer)
    // Leaving the POS mid-order: send the display back to welcome rather than a frozen cart.
    if (pairedDisplayId.value) svc.updateDisplayState(pairedDisplayId.value, idleDisplayState()).catch(() => { /* best-effort */ })
})

async function sendReceipt() {
    const rec = lastReceipt.value
    if (!rec) return
    const receipt: Receipt = {
        header: branding.displayName || 'Receipt',
        orderNumber: rec.orderNumber,
        lines: rec.lines.map(l => ({
            quantity: l.quantity, name: l.name, variantLabel: l.variantLabel,
            modifierLabels: l.modifierLabels, notes: l.notes, lineTotal: l.lineTotal,
        })),
        subtotalCents: rec.subtotal, discountCents: rec.discount ?? 0, discountLabel: null,
        taxCents: rec.tax, pricesIncludeTax: rec.pricesIncludeTax,
        tipCents: rec.tip, totalCents: rec.total, method: rec.method,
    }
    await printReceipt(printerUrl.value, receipt)
}

// Auto-print on sale completion (silent). If no printer is set or it fails, surface a toast rather
// than a dialog so the line keeps moving; the cashier can hit "Print receipt" to retry.
async function autoPrint() {
    if (!printerUrl.value) { flash('No receipt printer set — configure it via the Printer button.'); return }
    try { await sendReceipt() } catch (err: any) { flash(err.message || 'Receipt did not print.') }
}

async function printLast() {
    try { await sendReceipt(); flash('Receipt sent.', 'success') }
    catch (err: any) { flash(err.message || 'Receipt did not print.') }
}
</script>

<style scoped>
.pos-root { height: calc(100dvh - 64px); }
.pos-header { flex: 0 0 auto; border-bottom: 1px solid var(--rp-line); }
.pos-body { flex: 1 1 auto; min-height: 0; }
.pos-catalog { min-width: 0; }
.pos-tabs { flex: 0 0 auto; }
.pos-chip--active { background-color: var(--rp-ink) !important; color: var(--rp-on-ink) !important; }
.pos-title {
    margin: 0;
    font-family: var(--rp-font-display);
    font-style: italic;
    font-weight: 400;
    font-size: 26px;
    line-height: 1;
    text-transform: uppercase;
    letter-spacing: 0.01em;
}
.pos-total {
    font-family: var(--rp-font-display);
    font-style: italic;
    font-weight: 400;
    font-size: 2.6rem;
    line-height: 1;
    text-transform: uppercase;
    font-variant-numeric: tabular-nums;
}

.pos-grid-wrap { overflow-y: auto; min-height: 0; padding: 12px; }
.pos-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 12px; }

.tile {
    display: flex;
    flex-direction: column;
    border: 1px solid var(--rp-line);
    border-radius: 14px;
    overflow: hidden;
    cursor: pointer;
    background: var(--rp-card);
    transition: border-color 0.15s ease, transform 0.15s ease;
}
.tile:hover { border-color: var(--rp-line3); transform: translateY(-2px); }
.tile:active { transform: translateY(0); }
.tile--out { opacity: 0.5; }
.tile__media { position: relative; height: 104px; background: var(--rp-panel2); }
.tile__placeholder { height: 100%; display: flex; align-items: center; justify-content: center; }
/* Sits over the product photo: dark scrim + white glyph in both modes, like any photo overlay. */
.tile__menu { position: absolute; top: 6px; right: 6px; background: rgba(var(--rp-scrim), 0.55) !important; color: var(--rp-photo-ink) !important; }
.tile__ribbon {
    position: absolute; top: 12px; left: 0; right: 0; text-align: center;
    background: var(--rp-red); color: var(--rp-on-ink);
    font-weight: 700; font-size: 0.72rem; letter-spacing: 0.06em; padding: 3px 0;
}
.tile__body { padding: 8px 10px 10px; }
.tile__name {
    font-weight: 600; line-height: 1.2; font-size: 0.92rem;
    min-height: 2.3em; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;
}
.tile__price { font-weight: 700; font-size: 1rem; }
.tile__stock { font-size: 0.72rem; color: rgb(var(--v-theme-warning)); font-weight: 600; }

.pos-cart {
    flex: 0 0 380px; width: 380px;
    border-left: 1px solid var(--rp-line);
    background: var(--rp-panel);
}
.pos-cart-items { overflow-y: auto; min-height: 0; }
.empty-cart { text-align: center; padding: 56px 16px; }
.cart-line { display: flex; gap: 8px; padding: 12px 16px; border-bottom: 1px solid var(--rp-line); }
.pos-cart-footer { flex: 0 0 auto; border-top: 1px solid var(--rp-line); }
/* Tab mode is visibly different from a walk-up cart: raised header so a cashier never adds a
   stranger's burger to someone's tab without noticing. */
.pos-tab-head { background: var(--rp-raise); }

/* Open-tabs drawer rows. The running total takes the display face, like every other money figure. */
.tab-row { padding: 12px 16px; border-bottom: 1px solid var(--rp-line); }
.tab-row--selected { background: var(--rp-raise); }
.tab-row__total {
    font-family: var(--rp-font-display);
    font-style: italic;
    font-weight: 400;
    font-size: 1.7rem;
    line-height: 1;
    font-variant-numeric: tabular-nums;
    white-space: nowrap;
}

.confirm-body { overflow-y: auto; min-height: 0; }
.confirm-inner { max-width: 640px; margin: 0 auto; }
.confirm-footer { flex: 0 0 auto; border-top: 1px solid var(--rp-line); }

/* Shift-review summary block at the top of Close out: a quiet panel so the figures read before the inputs. */
.closeout-summary { padding: 12px 14px; border-radius: var(--rp-radius-sm); background: var(--rp-panel2); }

/* Stack to a single column on phones / small tablets so nothing gets cramped. */
@media (max-width: 960px) {
    .pos-root { height: auto; }
    .pos-body { flex-direction: column; }
    .pos-cart { flex-basis: auto; width: 100%; border-left: none; border-top: 1px solid var(--rp-line); }
    .pos-grid-wrap, .pos-cart-items { overflow: visible; }
}
</style>
