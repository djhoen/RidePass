<template>
    <v-container fluid class="pa-0">
        <div class="pos-root d-flex flex-column">
            <!-- Header -->
            <div class="pos-header d-flex align-center px-4 py-2 ga-2">
                <h1 class="text-h6 font-weight-bold">Food &amp; Beverage POS</h1>
                <v-chip :color="readerColor" size="small" variant="flat">
                    <v-icon start size="small">mdi-contactless-payment</v-icon>{{ readerStatus }}
                </v-chip>
                <v-btn v-if="readerState !== 'connected'" size="small" variant="text"
                    :loading="readerConnecting" @click="connectReader">Connect</v-btn>
                <v-chip v-if="online.capacityEnabled" size="small" variant="flat"
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
                                    <div v-else class="tile__placeholder"><v-icon size="34" color="grey-lighten-1">mdi-silverware-fork-knife</v-icon></div>
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
                    <div class="px-4 py-3 d-flex align-center">
                        <span class="text-h6 font-weight-bold">Order</span>
                        <v-chip v-if="cartCount" size="small" class="ml-2" variant="tonal">{{ cartCount }}</v-chip>
                        <v-spacer />
                        <v-btn v-if="cart.length" size="small" variant="text" color="error" @click="clearOrder">Clear</v-btn>
                    </div>
                    <v-divider />
                    <div class="pos-cart-items flex-grow-1">
                        <div v-if="cart.length === 0" class="empty-cart text-medium-emphasis">
                            <v-icon size="48" color="grey-lighten-1">mdi-cart-outline</v-icon>
                            <div class="mt-2">Tap items to start an order</div>
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
                                <v-btn icon="mdi-tag-outline" size="x-small" variant="text" class="mt-1" title="Discount or comp this item" @click="openDiscount(i)" />
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
                            <span class="text-h4 font-weight-bold">{{ money(total) }}</span>
                        </div>
                        <div class="d-flex ga-2">
                            <v-btn class="flex-grow-1" color="success" size="large" height="56" :disabled="cart.length === 0 || paying"
                                prepend-icon="mdi-cash" @click="startCheckout('cash')">Cash</v-btn>
                            <v-btn class="flex-grow-1" color="primary" size="large" height="56" :disabled="cart.length === 0 || paying"
                                prepend-icon="mdi-credit-card" @click="startCheckout('card')">Card</v-btn>
                        </div>
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
                    <div v-if="canCombo(adding)" ref="comboSectionEl" class="mt-4 pa-3" style="border: 1px solid rgba(128, 128, 128, 0.25); border-radius: 8px;">
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
                <v-toolbar color="primary" density="comfortable">
                    <v-btn icon="mdi-arrow-left" @click="confirmDialog = false" />
                    <v-toolbar-title>Review your order · {{ tender === 'cash' ? 'Cash' : 'Card' }}</v-toolbar-title>
                </v-toolbar>

                <div class="confirm-body flex-grow-1">
                    <div class="confirm-inner pa-4">
                        <v-list class="mb-2">
                            <v-list-item v-for="(line, i) in cart" :key="i" class="px-0">
                                <v-list-item-title class="font-weight-medium">{{ line.quantity }}× {{ line.name }}</v-list-item-title>
                                <v-list-item-subtitle v-if="line.variantLabel">{{ line.variantLabel }}</v-list-item-subtitle>
                                <v-list-item-subtitle v-for="m in line.modifierLabels" :key="m">+ {{ m }}</v-list-item-subtitle>
                                <v-list-item-subtitle v-if="line.notes" class="font-italic">"{{ line.notes }}"</v-list-item-subtitle>
                                <template #append><span class="font-weight-medium">{{ money(line.lineTotal) }}</span></template>
                            </v-list-item>
                        </v-list>

                        <template v-if="tipsEnabled">
                            <div class="text-subtitle-1 font-weight-bold mb-2">Add a tip?</div>
                            <div class="d-flex flex-wrap ga-2">
                                <v-btn :variant="tipMode === 'none' ? 'flat' : 'outlined'" :color="tipMode === 'none' ? 'primary' : undefined" @click="tipMode = 'none'">No tip</v-btn>
                                <v-btn v-for="pct in [15, 18, 20]" :key="pct"
                                    :variant="tipMode === 'pct' && tipPct === pct ? 'flat' : 'outlined'"
                                    :color="tipMode === 'pct' && tipPct === pct ? 'primary' : undefined"
                                    @click="tipMode = 'pct'; tipPct = pct">{{ pct }}% · {{ money(Math.round(subtotal * pct / 100)) }}</v-btn>
                                <v-btn :variant="tipMode === 'custom' ? 'flat' : 'outlined'" :color="tipMode === 'custom' ? 'primary' : undefined" @click="tipMode = 'custom'">Custom</v-btn>
                            </div>
                            <v-text-field v-if="tipMode === 'custom'" v-model.number="tipCustomDollars" type="number" min="0" step="0.50"
                                prefix="$" label="Custom tip" density="compact" hide-details class="mt-3" style="max-width: 200px" />
                            <v-divider class="my-4" />
                        </template>

                        <div class="text-subtitle-1 font-weight-bold mb-2">Name for the order (optional)</div>
                        <v-text-field v-model="customerName" label="Customer name" density="compact" hide-details
                            placeholder="e.g. Alex" prepend-inner-icon="mdi-account" style="max-width: 320px" class="mb-4" />
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
                        <div class="d-flex justify-space-between"><span class="text-medium-emphasis">Subtotal</span><span>{{ money(pricesIncludeTax ? subtotal - taxCents : subtotal) }}</span></div>
                        <div v-if="taxCents" class="d-flex justify-space-between mt-1"><span class="text-medium-emphasis">Tax{{ pricesIncludeTax ? ' (incl.)' : '' }}</span><span>{{ money(taxCents) }}</span></div>
                        <div v-if="discountCents" class="d-flex justify-space-between mt-1" style="color: rgb(var(--v-theme-success))"><span>{{ orderDiscountLabel || 'Discount' }}</span><span>-{{ money(discountCents) }}</span></div>
                        <div v-if="tipCents" class="d-flex justify-space-between mt-1"><span class="text-medium-emphasis">Tip</span><span>{{ money(tipCents) }}</span></div>
                        <div class="d-flex justify-space-between align-center my-2">
                            <span class="text-h6 font-weight-bold">Total</span><span class="text-h4 font-weight-bold">{{ money(total) }}</span>
                        </div>
                        <v-btn block size="large" height="56" :loading="paying"
                            :color="tender === 'cash' ? 'success' : 'primary'"
                            :disabled="tender === 'card' && readerState !== 'connected'"
                            :prepend-icon="tender === 'cash' ? 'mdi-cash' : 'mdi-credit-card'"
                            @click="confirmPay">
                            {{ tender === 'cash' ? 'Continue to cash' : `Charge card · ${money(total)}` }}
                        </v-btn>
                        <div v-if="paying && tender === 'card'" class="text-center text-medium-emphasis mt-2">Processing payment on the reader…</div>
                        <div v-else-if="tender === 'card' && readerState !== 'connected'" class="text-caption text-medium-emphasis text-center mt-2">Connect a reader to take card payments.</div>
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
                    <v-btn icon="mdi-close" variant="text" size="small" @click="cashDialog = false" />
                </v-card-title>
                <v-card-text>
                    <div class="d-flex justify-space-between text-h6 mb-3"><span>Total</span><span>{{ money(total) }}</span></div>
                    <v-text-field v-model.number="cashTenderedDollars" label="Cash received" type="number" prefix="$"
                        density="compact" autofocus hide-details />
                    <div class="d-flex justify-space-between mt-3 text-h6">
                        <span>Change</span><span>{{ money(changeCents) }}</span>
                    </div>
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="cashDialog = false">Cancel</v-btn>
                    <v-btn color="success" :loading="paying" :disabled="changeCents < 0" @click="payCash">Take payment</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Order confirmation -->
        <v-dialog v-model="doneDialog" max-width="340" persistent>
            <v-card class="text-center pa-4">
                <v-icon color="success" size="64">mdi-check-circle</v-icon>
                <div class="text-h4 mt-2">{{ lastOrderNumber != null ? `Order #${lastOrderNumber}` : 'Order placed' }}</div>
                <div class="text-medium-emphasis mb-4">
                    {{ lastOrderNumber != null ? 'Call this number at pickup.' : 'Payment received — the order number will appear on the cook screen shortly.' }}
                </div>
                <v-btn block color="primary" class="mb-2" prepend-icon="mdi-printer" @click="printLast">Print receipt</v-btn>
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
                    <!-- Presets (no PIN needed) -->
                    <template v-if="discountPresets.length">
                        <div class="text-subtitle-2 mb-2">Presets</div>
                        <div class="d-flex flex-wrap ga-2 mb-4">
                            <v-btn v-for="p in discountPresets" :key="p.id" variant="tonal" size="small"
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
                    <div v-if="memberResult" class="mt-3 pa-3" style="border: 1px solid rgba(128, 128, 128, 0.25); border-radius: 8px;">
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
                        autofocus density="compact" hide-details @keyup.enter="verifyPin" />
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" @click="cancelPin">Cancel</v-btn>
                    <v-btn color="primary" :loading="pinVerifying" @click="verifyPin">Approve</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snack.show" :color="snack.color" timeout="5000">{{ snack.text }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import {
    ConcessionService,
    type ConcessionProduct, type ConcessionVariant, type ConcessionModifierGroup,
    type ConcessionSaleLineInput, type ConcessionComboConfig, type ConcessionComboSlot,
    type ConcessionComboSlotOption, type ConcessionDiscountInput, type ConcessionDiscountPreset,
    type ConcessionCompReason, type ConcessionMemberLookup, type ConcessionMemberPerk,
} from '@/services/ConcessionService'
import { getTerminal, discoverAndConnect, collectAndProcess } from '@/helpers/TerminalHelper'
import { printReceipt, type Receipt } from '@/helpers/ReceiptPrinter'
import { branding } from '@/stores/branding'
import { setHomeScreenIcon } from '@/helpers/HomeScreenIcon'

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
const snack = ref({ show: false, text: '', color: 'error' })
const toggling = ref<string | null>(null)

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
    return Math.round(subtotal.value * tipPct.value / 100)
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
    refreshOnlineStatus()
    onlineTimer = window.setInterval(refreshOnlineStatus, 10000)   // keep the Open/Busy chip current
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
        pricesIncludeTax.value = ms.pricesIncludeTax
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
    discountTarget.value === 'order' ? 'this order' : (cart.value[discountTarget.value as number]?.name ?? 'this item'))

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
    if (cart.value.length === 0) { flash('Add an item before applying a discount.'); return }
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
    chooseDiscount({ kind: 'preset', presetId: p.id }, label, false)
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
}

// ── Payment ──────────────────────────────────────────────────────────
const cashDialog = ref(false)
const cashTenderedDollars = ref<number | null>(null)
const changeCents = computed(() =>
    cashTenderedDollars.value == null ? 0 : Math.round(cashTenderedDollars.value * 100) - total.value)
const doneDialog = ref(false)
const lastOrderNumber = ref<number | null>(null)
const lastReceipt = ref<{ orderNumber: number | null; lines: CartLine[]; subtotal: number; tax: number; pricesIncludeTax: boolean; tip: number; discount: number; total: number; method: string } | null>(null)

// ── Customer confirmation (cashier picks tender first; customer reviews + tips + receipt) ──
const confirmDialog = ref(false)
const tender = ref<'cash' | 'card'>('card')
const receiptMethod = ref<'print' | 'sms' | 'email' | 'none'>('print')
const receiptDest = ref('')
const customerName = ref('')   // optional name on a counter order

// Cashier taps Cash or Card in the cart, then turns the tablet to the customer to confirm.
function startCheckout(t: 'cash' | 'card') {
    if (cart.value.length === 0) return
    if (t === 'card' && readerState.value !== 'connected') { flash('Connect the card reader first.'); return }
    tender.value = t
    tipMode.value = 'none'
    tipCustomDollars.value = null
    receiptMethod.value = printerUrl.value ? 'print' : 'none'
    receiptDest.value = ''
    customerName.value = ''
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
    }
}

async function payCash() {
    paying.value = true
    try {
        const r = await svc.createSale(buildRequest('cash'))
        const res = (r.data as any).data
        cashDialog.value = false
        finishOrder(res.saleId, res.orderNumber, 'Cash', res.totalCents, res.discountCents)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Cash sale failed. Nothing was charged.')
    } finally {
        paying.value = false
    }
}

async function payCard() {
    // A fully-comped order rings up to $0: there is no card to run, so the reader isn't required.
    const isZero = total.value <= 0
    if (!isZero && readerState.value !== 'connected') { flash('Connect the card reader first.'); return }
    paying.value = true
    try {
        const r = await svc.createSale(buildRequest('card'))
        const res = (r.data as any).data
        // No client secret means the server recorded it paid immediately (a $0 / fully-comped order):
        // skip the reader collection and finish with the order number the server already assigned.
        if (!res.clientSecret) {
            finishOrder(res.saleId, res.orderNumber ?? null, 'Comp', res.totalCents, res.discountCents)
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
        finishOrder(res.saleId, orderNumber, 'Card', res.totalCents, res.discountCents)
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
    // Trust the server's total + discount when present; fall back to the client estimate otherwise.
    const discount = serverDiscountCents ?? discountCents.value
    const finalTotal = serverTotalCents ?? total.value
    lastReceipt.value = { orderNumber, lines: [...cart.value], subtotal: pricesIncludeTax.value ? subtotal.value - taxCents.value : subtotal.value, tax: taxCents.value, pricesIncludeTax: pricesIncludeTax.value, tip: tipCents.value, discount, total: finalTotal, method }
    confirmDialog.value = false
    doneDialog.value = true
    deliverReceipt(saleId)
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
    tipMode.value = 'none'
    tipCustomDollars.value = null
    receiptMethod.value = 'print'
    receiptDest.value = ''
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
        subtotalCents: rec.subtotal, taxCents: rec.tax, pricesIncludeTax: rec.pricesIncludeTax,
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
.pos-header { flex: 0 0 auto; border-bottom: 1px solid rgba(128, 128, 128, 0.18); }
.pos-body { flex: 1 1 auto; min-height: 0; }
.pos-catalog { min-width: 0; }
.pos-tabs { flex: 0 0 auto; }
.pos-chip--active { background-color: rgb(var(--v-theme-primary)) !important; color: rgb(var(--v-theme-on-primary)) !important; }

.pos-grid-wrap { overflow-y: auto; min-height: 0; padding: 12px; }
.pos-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 12px; }

.tile {
    display: flex;
    flex-direction: column;
    border: 1px solid rgba(128, 128, 128, 0.2);
    border-radius: 14px;
    overflow: hidden;
    cursor: pointer;
    background: rgb(var(--v-theme-surface));
    transition: box-shadow 0.15s ease, transform 0.15s ease;
}
.tile:hover { box-shadow: 0 6px 18px rgba(0, 0, 0, 0.14); transform: translateY(-2px); }
.tile:active { transform: translateY(0); }
.tile--out { opacity: 0.5; }
.tile__media { position: relative; height: 104px; background: rgba(128, 128, 128, 0.1); }
.tile__placeholder { height: 100%; display: flex; align-items: center; justify-content: center; }
.tile__menu { position: absolute; top: 6px; right: 6px; background: rgba(0, 0, 0, 0.45) !important; color: #fff !important; }
.tile__ribbon {
    position: absolute; top: 12px; left: 0; right: 0; text-align: center;
    background: rgba(211, 47, 47, 0.92); color: #fff;
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
    border-left: 1px solid rgba(128, 128, 128, 0.18);
    background: rgb(var(--v-theme-surface));
}
.pos-cart-items { overflow-y: auto; min-height: 0; }
.empty-cart { text-align: center; padding: 56px 16px; }
.cart-line { display: flex; gap: 8px; padding: 12px 16px; border-bottom: 1px solid rgba(128, 128, 128, 0.14); }
.pos-cart-footer { flex: 0 0 auto; border-top: 1px solid rgba(128, 128, 128, 0.18); }

.confirm-body { overflow-y: auto; min-height: 0; }
.confirm-inner { max-width: 640px; margin: 0 auto; }
.confirm-footer { flex: 0 0 auto; border-top: 1px solid rgba(128, 128, 128, 0.18); }

/* Stack to a single column on phones / small tablets so nothing gets cramped. */
@media (max-width: 960px) {
    .pos-root { height: auto; }
    .pos-body { flex-direction: column; }
    .pos-cart { flex-basis: auto; width: 100%; border-left: none; border-top: 1px solid rgba(128, 128, 128, 0.18); }
    .pos-grid-wrap, .pos-cart-items { overflow: visible; }
}
</style>
