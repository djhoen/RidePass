<template>
    <v-container>
        <h1 class="text-h4 mb-3">Food &amp; Beverage</h1>

        <v-alert v-if="!branding.concessionsEnabled" type="info" variant="tonal" density="compact" class="mb-4">
            Food &amp; Beverage is turned off, so the cashier app won't show these items. You can still set up the catalog now,
            then flip it on in <router-link to="/Admin/Settings/Features">Settings &rarr; Features</router-link>.
        </v-alert>

        <v-tabs v-model="tab" color="primary" class="mb-4">
            <v-tab value="items">Items</v-tab>
            <v-tab value="categories">Categories</v-tab>
            <v-tab value="stations">Stations</v-tab>
            <v-tab value="modifiers">Modifiers</v-tab>
            <v-tab value="combos">Combos</v-tab>
            <v-tab value="inventory">
                Inventory
                <v-chip v-if="inventoryLowCount" size="x-small" color="warning" variant="flat" class="ml-2"
                    prepend-icon="mdi-alert">{{ inventoryLowCount }}</v-chip>
            </v-tab>
            <v-tab value="settings">Settings</v-tab>
        </v-tabs>

        <v-window v-model="tab">
            <!-- Items -->
            <v-window-item value="items">
                <div class="d-flex align-center ga-3 mb-3">
                    <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">Add item</v-btn>
                    <v-chip v-if="rows.length" size="small" color="grey" variant="tonal">{{ rows.length }} items</v-chip>
                </div>
                <p class="text-caption text-medium-emphasis mb-4" style="max-width: 720px;">
                    Food, drink, and swag a cashier rings up in the mobile tap-to-pay app, separate from events. Drag the
                    handle to reorder how items appear. Add sizes/colors as variants on a product (e.g. a shirt's S/M/L/XL).
                </p>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th style="width: 36px"></th>
                        <th>Item</th>
                        <th style="width: 110px">Category</th>
                        <th style="width: 100px">Price</th>
                        <th style="width: 140px">Variants</th>
                        <th style="width: 80px" class="text-center">Is Combo</th>
                        <th style="width: 90px">Status</th>
                        <th style="width: 150px" class="text-right"></th>
                    </tr>
                </thead>
                <draggable tag="tbody" :list="rows" item-key="id" handle=".drag-handle"
                    :animation="180" ghost-class="drag-ghost" @end="onReorderEnd">
                    <template #item="{ element: p }">
                        <tr>
                            <td class="drag-handle-cell">
                                <v-icon class="drag-handle" color="grey">mdi-drag-vertical</v-icon>
                            </td>
                            <td>
                                <div class="d-flex align-center ga-3">
                                    <v-avatar v-if="p.imageUrl" size="36" rounded="lg">
                                        <v-img :src="absoluteUrl(p.imageUrl)"></v-img>
                                    </v-avatar>
                                    <v-icon v-else icon="mdi-silverware-fork-knife" color="grey"></v-icon>
                                    <div>{{ p.name }}</div>
                                </div>
                            </td>
                            <td><v-chip size="x-small" variant="tonal">{{ p.categoryName || 'Uncategorized' }}</v-chip></td>
                            <td>${{ (p.priceCents / 100).toFixed(2) }}</td>
                            <td>
                                <v-btn variant="text" size="small" @click="openVariants(p)">
                                    {{ p.variants.length ? `${p.variants.length} variant${p.variants.length === 1 ? '' : 's'}` : 'Add sizes' }}
                                </v-btn>
                            </td>
                            <td class="text-center">
                                <v-icon v-if="p.comboAvailable" color="success" size="small">mdi-check</v-icon>
                            </td>
                            <td>
                                <v-chip size="x-small" :color="p.isActive ? 'success' : 'grey'" variant="tonal">
                                    {{ p.isActive ? 'Active' : 'Hidden' }}
                                </v-chip>
                                <v-chip v-if="p.soldOut" size="x-small" color="error" variant="flat" class="ml-1">Sold out</v-chip>
                                <span v-else-if="p.remaining >= 0" class="text-caption text-medium-emphasis ml-2">{{ p.remaining }} left</span>
                            </td>
                            <td class="text-right">
                                <v-btn variant="text" size="small" @click="openEdit(p)">Edit</v-btn>
                                <v-btn variant="text" size="small" color="error" @click="remove(p)">Delete</v-btn>
                            </td>
                        </tr>
                    </template>
                </draggable>
            </v-table>
            <div v-if="!loading && rows.length === 0" class="text-center text-medium-emphasis py-8">
                <div class="mb-3">No items yet. Add your own{{ starterSeeded ? '.' : ', or start from sample content you can edit.' }}</div>
                <v-btn v-if="!starterSeeded" color="primary" variant="tonal" prepend-icon="mdi-auto-fix" :loading="seeding" @click="loadStarter">
                    Load starter content
                </v-btn>
            </div>
        </v-card>
            </v-window-item>

            <!-- Categories -->
            <v-window-item value="categories">
                <v-card>
                    <v-card-text>
                        <div class="text-h6 mb-2">Menu categories</div>
                        <p class="text-caption text-medium-emphasis mb-2">
                            Define how items are grouped on the POS, menu board, and online ordering (e.g. Sandwiches, Burgers, Sides).
                            Drag the handle to set the display order.
                        </p>
                        <v-table density="compact">
                            <thead>
                                <tr><th style="width: 36px"></th><th>Name</th><th style="width: 70px">Active</th><th style="width: 40px"></th></tr>
                            </thead>
                            <draggable tag="tbody" :list="categoryRows" :item-key="catKey" handle=".drag-handle"
                                :animation="180" ghost-class="drag-ghost">
                                <template #item="{ element: c, index: i }">
                                    <tr>
                                        <td class="drag-handle-cell"><v-icon class="drag-handle" color="grey">mdi-drag-vertical</v-icon></td>
                                        <td><v-text-field v-model="c.name" density="compact" hide-details placeholder="Sandwiches"></v-text-field></td>
                                        <td><v-switch v-model="c.isActive" color="primary" density="compact" hide-details></v-switch></td>
                                        <td><v-btn icon="mdi-delete" variant="text" size="small" color="error" @click="removeCategoryRow(i)"></v-btn></td>
                                    </tr>
                                </template>
                            </draggable>
                        </v-table>
                        <v-btn variant="tonal" size="small" prepend-icon="mdi-plus" class="mt-2" @click="addCategoryRow">Add category</v-btn>
                    </v-card-text>
                    <v-card-actions class="px-4 pb-4">
                        <v-btn color="primary" variant="flat" :loading="savingCategories" @click="saveCategories">Save</v-btn>
                    </v-card-actions>
                </v-card>
            </v-window-item>

            <!-- Stations -->
            <v-window-item value="stations">
                <v-card>
                    <v-card-text>
                        <div class="text-h6 mb-2">Kitchen stations</div>
                        <p class="text-caption text-medium-emphasis mb-3">Split the cook screen by station (Fryer, Grill, Drinks). Assign items to a station in the item editor. Drag the handle to set the order.</p>
                        <v-table density="compact">
                            <thead><tr><th style="width: 36px"></th><th>Name</th><th style="width: 70px">Active</th><th style="width: 60px"></th></tr></thead>
                            <draggable tag="tbody" :list="stationRows" :item-key="stationKey" handle=".drag-handle"
                                :animation="180" ghost-class="drag-ghost">
                                <template #item="{ element: s, index: i }">
                                    <tr>
                                        <td class="drag-handle-cell"><v-icon class="drag-handle" color="grey">mdi-drag-vertical</v-icon></td>
                                        <td><v-text-field v-model="s.name" density="compact" hide-details placeholder="Grill"></v-text-field></td>
                                        <td><v-switch v-model="s.isActive" color="primary" density="compact" hide-details></v-switch></td>
                                        <td class="text-right"><v-btn icon="mdi-delete" variant="text" size="small" color="error" @click="removeStationRow(s, i)"></v-btn></td>
                                    </tr>
                                </template>
                            </draggable>
                        </v-table>
                        <div v-if="stationRows.length === 0" class="text-center text-medium-emphasis py-3">No stations. Items fall to one default queue.</div>
                        <v-btn variant="tonal" size="small" prepend-icon="mdi-plus" class="mt-2" @click="addStationRow">Add station</v-btn>
                    </v-card-text>
                    <v-card-actions class="px-4 pb-4">
                        <v-btn color="primary" variant="flat" :loading="savingStations" @click="saveStations">Save</v-btn>
                    </v-card-actions>
                </v-card>

                <!-- Cook screen targets -->
                <v-card class="mt-4">
                    <v-card-text>
                        <div class="text-h6 mb-1 d-flex align-center ga-2">
                            <v-icon size="small" color="primary">mdi-stove</v-icon> Cook screen targets
                        </div>
                        <p class="text-caption text-medium-emphasis mb-3">How long a ticket can sit before the cook screen flags it.</p>
                        <div class="d-flex ga-3">
                            <v-text-field v-model.number="menuStyle.prepWarnMinutes" type="number" min="1" max="240"
                                label="Amber after (min)" density="compact" hide-details style="max-width: 160px"></v-text-field>
                            <v-text-field v-model.number="menuStyle.prepLateMinutes" type="number" min="1" max="240"
                                label="Red after (min)" density="compact" hide-details style="max-width: 160px"></v-text-field>
                        </div>
                    </v-card-text>
                    <v-card-actions class="px-4 pb-4">
                        <v-btn color="primary" variant="flat" :loading="savingMenuStyle" @click="saveMenuStyle">Save</v-btn>
                    </v-card-actions>
                </v-card>
            </v-window-item>

            <!-- Modifiers -->
            <v-window-item value="modifiers">
                <v-card>
                    <v-card-text>
                        <div class="text-h6 mb-2">Modifier groups</div>
                        <v-expansion-panels variant="accordion">
                            <v-expansion-panel v-for="(g, gi) in groupRows" :key="g.id ?? `new-${gi}`">
                                <v-expansion-panel-title>{{ g.name || 'New group' }}
                                    <v-chip v-if="g.isRequired" size="x-small" color="primary" variant="tonal" class="ml-2">required</v-chip>
                                </v-expansion-panel-title>
                                <v-expansion-panel-text>
                                    <v-text-field v-model="g.name" label="Group name" density="compact" placeholder="Choose a side"></v-text-field>
                                    <v-row class="mt-0">
                                        <v-col cols="4"><v-text-field v-model.number="g.minSelect" type="number" min="0" label="Min" density="compact" class="mt-4"></v-text-field></v-col>
                                        <v-col cols="4"><v-text-field v-model.number="g.maxSelect" type="number" min="1" label="Max (blank=∞)" density="compact" class="mt-4"></v-text-field></v-col>
                                        <v-col cols="4" class="d-flex align-center"><v-switch v-model="g.isRequired" label="Required" color="primary" density="compact" hide-details class="mt-4"></v-switch></v-col>
                                    </v-row>
                                    <v-switch v-model="g.isActive" label="Active" color="primary" density="compact" hide-details class="mt-2"></v-switch>

                                    <div class="text-subtitle-2 mt-3 mb-1">Options</div>
                                    <v-table density="compact">
                                        <thead><tr><th>Name</th><th style="width: 120px">Price +/-</th><th style="width: 70px">Active</th><th style="width: 60px"></th></tr></thead>
                                        <tbody>
                                            <tr v-for="(o, oi) in g.options" :key="o.id ?? `new-${oi}`">
                                                <td><v-text-field v-model="o.name" density="compact" hide-details placeholder="Fries"></v-text-field></td>
                                                <td><v-text-field v-model.number="o.priceDollars" type="number" step="0.01" prefix="$" density="compact" hide-details></v-text-field></td>
                                                <td><v-switch v-model="o.isActive" color="primary" density="compact" hide-details></v-switch></td>
                                                <td class="text-right"><v-btn icon="mdi-delete" variant="text" size="small" color="error" @click="removeOptionRow(g, o, oi)"></v-btn></td>
                                            </tr>
                                        </tbody>
                                    </v-table>
                                    <v-btn variant="tonal" size="small" prepend-icon="mdi-plus" class="mt-2"
                                        @click="g.options.push({ id: null, name: '', priceDollars: 0, isActive: true, sortOrder: g.options.length * 10 })">Add option</v-btn>

                                    <div class="d-flex mt-3">
                                        <v-btn color="error" variant="text" size="small" @click="removeGroup(g, gi)">Delete group</v-btn>
                                        <v-spacer></v-spacer>
                                        <v-btn color="primary" variant="flat" size="small" :loading="savingGroupId === (g.id ?? 'new')" @click="saveGroup(g)">Save group</v-btn>
                                    </div>
                                </v-expansion-panel-text>
                            </v-expansion-panel>
                        </v-expansion-panels>
                        <v-btn variant="tonal" size="small" prepend-icon="mdi-plus" class="mt-3"
                            @click="groupRows.push({ id: null, name: '', minSelect: 0, maxSelect: null, isRequired: false, isActive: true, sortOrder: groupRows.length * 10, options: [] })">Add group</v-btn>
                    </v-card-text>
                </v-card>
            </v-window-item>

            <!-- Combos -->
            <v-window-item value="combos">
                <v-card>
                    <v-card-text>
                        <div class="text-h6 mb-2">Make it a combo</div>
                        <p class="text-caption text-medium-emphasis mb-3">
                            This combo applies to any item marked "Available as combo". Customers pick a size tier, then one
                            option per slot. A tier's size resolves each side/drink to its matching size variant. The included
                            (default) option is covered by the tier price; pricier substitutions add the difference, cheaper
                            ones don't change the price.
                        </p>

                        <div class="text-subtitle-2 font-weight-bold mb-1">Size tiers</div>
                        <v-table density="compact" class="mb-2">
                            <thead>
                                <tr>
                                    <th>Name</th>
                                    <th style="width: 180px">Size (matches variant)</th>
                                    <th style="width: 140px">Upcharge</th>
                                    <th style="width: 48px"></th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr v-for="(t, ti) in comboTiers" :key="ti">
                                    <td><v-text-field v-model="t.name" density="compact" hide-details placeholder="Large"></v-text-field></td>
                                    <td><v-text-field v-model="t.sizeLabel" density="compact" hide-details placeholder="Large"></v-text-field></td>
                                    <td><v-text-field v-model.number="t.priceDollars" type="number" min="0" step="0.01" prefix="$" density="compact" hide-details></v-text-field></td>
                                    <td><v-btn icon="mdi-close" variant="text" size="small" @click="comboTiers.splice(ti, 1)"></v-btn></td>
                                </tr>
                                <tr v-if="comboTiers.length === 0">
                                    <td colspan="4" class="text-center text-medium-emphasis py-3">No tiers yet. Add at least one.</td>
                                </tr>
                            </tbody>
                        </v-table>
                        <v-btn variant="tonal" size="small" prepend-icon="mdi-plus" class="mb-4" @click="addComboTier">Add tier</v-btn>

                        <div class="text-subtitle-2 font-weight-bold mb-1">Slots</div>
                        <div v-for="(slot, si) in comboSlots" :key="si" class="mb-4 pa-3" style="border: 1px solid rgba(128, 128, 128, 0.25); border-radius: 8px;">
                            <div class="d-flex align-center ga-2 mb-2">
                                <v-text-field v-model="slot.name" label="Slot name" density="compact" hide-details
                                    placeholder="Choose a side" style="flex: 1"></v-text-field>
                                <v-switch v-model="slot.isRequired" label="Required" color="primary" density="compact" hide-details></v-switch>
                                <v-btn icon="mdi-delete" variant="text" size="small" color="error" @click="comboSlots.splice(si, 1)"></v-btn>
                            </div>
                            <v-table density="compact">
                                <thead>
                                    <tr><th>Item</th><th style="width: 110px">Included</th><th style="width: 48px"></th></tr>
                                </thead>
                                <tbody>
                                    <tr v-for="(o, oi) in slot.options" :key="oi">
                                        <td><v-select v-model="o.componentProductId" :items="componentItems" density="compact" hide-details></v-select></td>
                                        <td><v-radio :model-value="o.isDefault" :true-value="true" @click="setComboDefault(si, oi)"></v-radio></td>
                                        <td><v-btn icon="mdi-close" variant="text" size="small" @click="slot.options.splice(oi, 1)"></v-btn></td>
                                    </tr>
                                    <tr v-if="slot.options.length === 0">
                                        <td colspan="3" class="text-center text-medium-emphasis py-3">No choices yet.</td>
                                    </tr>
                                </tbody>
                            </v-table>
                            <v-btn variant="text" size="small" prepend-icon="mdi-plus" class="mt-1"
                                :disabled="!componentItems.length" @click="addComboOption(slot)">Add choice</v-btn>
                        </div>
                        <v-btn variant="tonal" size="small" prepend-icon="mdi-plus" @click="addComboSlot">Add slot</v-btn>
                        <div v-if="!componentItems.length" class="text-caption text-medium-emphasis mt-2">
                            Add some items first; those become the combo's side/drink choices.
                        </div>
                    </v-card-text>
                    <v-card-actions class="px-4 pb-4">
                        <v-btn color="primary" variant="flat" :loading="savingCombo" @click="saveComboConfig">Save</v-btn>
                    </v-card-actions>
                </v-card>
            </v-window-item>

            <!-- Inventory -->
            <v-window-item value="inventory">
                <ConcessionInventory @low-count="inventoryLowFromChild = $event" />
            </v-window-item>

            <!-- Settings -->
            <v-window-item value="settings">
                <v-card>
                    <v-card-text>
                        <div class="text-h6 mb-4">Settings</div>
                        <div class="settings-sections">

                            <!-- Starter content -->
                            <v-card variant="outlined" class="pa-4 mb-4">
                                <div class="text-subtitle-1 font-weight-bold mb-3 d-flex align-center ga-2">
                                    <v-icon size="small" color="primary">mdi-auto-fix</v-icon> Starter content
                                </div>
                                <template v-if="!starterSeeded">
                                    <v-btn variant="tonal" prepend-icon="mdi-auto-fix" :loading="seeding" @click="loadStarter">Load starter content</v-btn>
                                    <div class="text-caption text-medium-emphasis mt-1">Adds sample categories, stations, modifiers, combos, and products you can edit. Existing items are kept and nothing is duplicated.</div>
                                </template>
                                <div v-else class="text-caption text-medium-emphasis d-flex align-center ga-1">
                                    <v-icon size="small" color="success">mdi-check-circle</v-icon>
                                    Starter content has been loaded.
                                </div>
                            </v-card>

                            <!-- Manager PIN (sets the signed-in user's own PIN; saved by its own button) -->
                            <v-card variant="outlined" class="pa-4 mb-4">
                                <div class="text-subtitle-1 font-weight-bold mb-1 d-flex align-center ga-2">
                                    <v-icon size="small" color="primary">mdi-lock-outline</v-icon> Manager PIN
                                </div>
                                <p class="text-caption text-medium-emphasis mb-3">
                                    Set or change the PIN you use to authorize comps and manual discounts at the POS.
                                    Only managers and admins can set a PIN.
                                </p>
                                <v-text-field v-model="managerPin" label="New PIN (4 to 8 digits)" type="password"
                                    inputmode="numeric" density="compact" hide-details style="max-width: 240px"
                                    placeholder="Leave blank to clear"></v-text-field>
                                <v-btn color="primary" variant="tonal" size="small" class="mt-4"
                                    :loading="savingPin" @click="saveManagerPin">Save PIN</v-btn>
                            </v-card>

                            <!-- Online ordering: when riders can place orders -->
                            <v-card variant="outlined" class="pa-4 mb-4">
                                <div class="text-subtitle-1 font-weight-bold mb-1 d-flex align-center ga-2">
                                    <v-icon size="small" color="primary">mdi-silverware-fork-knife</v-icon> Online ordering
                                </div>
                                <p class="text-caption text-medium-emphasis mb-3">Control when riders can order online. Times and dates use the track's timezone.</p>

                                <div class="text-subtitle-2 font-weight-bold mb-1">Hours</div>
                                <v-switch v-model="useOrderingHours" label="Limit online ordering to set hours" color="primary"
                                    density="compact" hide-details
                                    messages="Off = open whenever Food & Beverage is on."></v-switch>
                                <div v-if="useOrderingHours" class="mt-3">
                                    <div v-for="(d, i) in hoursRows" :key="i" class="d-flex align-center ga-2 mb-1">
                                        <div style="width: 36px" class="text-caption font-weight-medium">{{ dayLabels[i] }}</div>
                                        <v-switch v-model="d.open" color="primary" density="compact" hide-details></v-switch>
                                        <template v-if="d.open">
                                            <input type="time" v-model="d.openStr" class="hours-input" />
                                            <span class="text-caption">to</span>
                                            <input type="time" v-model="d.closeStr" class="hours-input" />
                                        </template>
                                        <span v-else class="text-caption text-medium-emphasis">Closed</span>
                                    </div>
                                </div>

                                <v-divider class="my-4"></v-divider>
                                <div class="text-subtitle-2 font-weight-bold mb-1">Open season</div>
                                <v-switch v-model="useSeason" label="Limit online ordering to open-season dates" color="primary"
                                    density="compact" hide-details
                                    messages="Off = open year-round. Outside every range, online ordering is closed."></v-switch>
                                <div v-if="useSeason" class="mt-3">
                                    <div v-for="(s, i) in seasonRows" :key="i" class="d-flex align-center ga-2 mb-1">
                                        <input type="date" v-model="s.startStr" class="hours-input" />
                                        <span class="text-caption">to</span>
                                        <input type="date" v-model="s.endStr" class="hours-input" />
                                        <v-btn icon="mdi-close" variant="text" size="x-small" @click="seasonRows.splice(i, 1)"></v-btn>
                                    </div>
                                    <v-btn variant="text" size="small" prepend-icon="mdi-plus"
                                        @click="seasonRows.push({ startStr: '', endStr: '' })">Add date range</v-btn>
                                </div>

                                <v-divider class="my-4"></v-divider>
                                <div class="text-subtitle-2 font-weight-bold mb-1">Event days</div>
                                <v-switch v-model="menuStyle.requireEventDay" label="Only take online orders on event days" color="primary"
                                    density="compact" hide-details
                                    messages="On = online ordering is closed on days with nothing on the events calendar."></v-switch>
                            </v-card>

                            <!-- Online order capacity (throttle + quote times) -->
                            <v-card variant="outlined" class="pa-4 mb-4">
                                <div class="text-subtitle-1 font-weight-bold mb-1 d-flex align-center ga-2">
                                    <v-icon size="small" color="primary">mdi-speedometer</v-icon> Online order capacity
                                </div>
                                <p class="text-caption text-medium-emphasis mb-3">
                                    Keep the kitchen from being flooded during a rush. Quote times use today's measured
                                    prep speed; walk-up window sales are never blocked.
                                </p>
                                <v-switch v-model="capacity.capacityEnabled" label="Throttle online orders when busy" color="primary"
                                    density="compact" hide-details></v-switch>
                                <template v-if="capacity.capacityEnabled">
                                    <v-text-field v-model.number="capacity.basePrepMinutes" type="number" min="0" max="240"
                                        label="Base prep time (minutes)" density="compact" class="mt-4"
                                        hint="The quote when the kitchen is idle" persistent-hint></v-text-field>
                                    <v-text-field v-model.number="capacity.maxActiveOrders" type="number" min="0" max="1000"
                                        label="Pause online ordering at this many active orders" density="compact" class="mt-4"
                                        hint="0 = no cap (quotes still apply)" persistent-hint></v-text-field>
                                    <v-switch v-model="capacity.showQuoteTimes" label="Show estimated ready time to customers" color="primary"
                                        density="compact" hide-details class="mt-2"></v-switch>
                                </template>
                            </v-card>

                            <!-- Tips -->
                            <v-card variant="outlined" class="pa-4 mb-4">
                                <div class="text-subtitle-1 font-weight-bold mb-3 d-flex align-center ga-2">
                                    <v-icon size="small" color="primary">mdi-cash-multiple</v-icon> Tips
                                </div>
                                <v-switch v-model="menuStyle.tipsEnabled" label="Accept tips" color="primary"
                                    density="compact" hide-details
                                    messages="When on, customers can add a tip on the confirmation screen and online. When off, no tip is shown or charged."></v-switch>
                            </v-card>

                            <!-- Member discounts (season pass / loampass perks) -->
                            <v-card variant="outlined" class="pa-4 mb-4">
                                <div class="text-subtitle-1 font-weight-bold mb-1 d-flex align-center ga-2">
                                    <v-icon size="small" color="primary">mdi-account-star-outline</v-icon> Member discounts
                                </div>
                                <p class="text-caption text-medium-emphasis mb-3">
                                    A verified Season Pass or LoamPass holder gets this discount when a cashier looks them up
                                    at the POS. The LoamPass discount is a perk and does not use a LoamPass credit.
                                </p>

                                <div class="text-subtitle-2 font-weight-bold mb-1">Season Pass</div>
                                <v-switch v-model="menuStyle.seasonPassDiscountEnabled" label="Give Season Pass holders a discount"
                                    color="primary" density="compact" hide-details></v-switch>
                                <div v-if="menuStyle.seasonPassDiscountEnabled" class="d-flex ga-3 mt-3">
                                    <v-select v-model="menuStyle.seasonPassDiscountKind" :items="discountKindItems"
                                        label="Type" density="compact" hide-details style="width: 150px"></v-select>
                                    <v-text-field v-model.number="seasonPassValueDisplay" type="number" min="0"
                                        :step="menuStyle.seasonPassDiscountKind === 'amount' ? 0.01 : 1"
                                        :prefix="menuStyle.seasonPassDiscountKind === 'amount' ? '$' : undefined"
                                        :suffix="menuStyle.seasonPassDiscountKind === 'percent' ? '%' : undefined"
                                        label="Amount" density="compact" hide-details style="width: 150px"></v-text-field>
                                </div>

                                <v-divider class="my-4"></v-divider>
                                <div class="text-subtitle-2 font-weight-bold mb-1">LoamPass</div>
                                <v-switch v-model="menuStyle.loampassDiscountEnabled" label="Give LoamPass holders a discount"
                                    color="primary" density="compact" hide-details></v-switch>
                                <div v-if="menuStyle.loampassDiscountEnabled" class="d-flex ga-3 mt-3">
                                    <v-select v-model="menuStyle.loampassDiscountKind" :items="discountKindItems"
                                        label="Type" density="compact" hide-details style="width: 150px"></v-select>
                                    <v-text-field v-model.number="loampassValueDisplay" type="number" min="0"
                                        :step="menuStyle.loampassDiscountKind === 'amount' ? 0.01 : 1"
                                        :prefix="menuStyle.loampassDiscountKind === 'amount' ? '$' : undefined"
                                        :suffix="menuStyle.loampassDiscountKind === 'percent' ? '%' : undefined"
                                        label="Amount" density="compact" hide-details style="width: 150px"></v-text-field>
                                </div>
                            </v-card>

                            <!-- Discount presets (quick POS discount buttons) -->
                            <v-card variant="outlined" class="pa-4 mb-4">
                                <div class="text-subtitle-1 font-weight-bold mb-1 d-flex align-center ga-2">
                                    <v-icon size="small" color="primary">mdi-sale</v-icon> Discount presets
                                </div>
                                <p class="text-caption text-medium-emphasis mb-3">
                                    Quick discounts a cashier can tap at the POS (for example "$1 off" or "10% off").
                                </p>
                                <div v-for="(d, i) in discountRows" :key="d.id || ('new' + i)" class="d-flex align-center ga-2 mt-3">
                                    <v-text-field v-model="d.name" label="Name" density="compact" hide-details style="flex: 1"></v-text-field>
                                    <v-select v-model="d.kind" :items="discountKindItems" label="Type" density="compact"
                                        hide-details style="width: 130px"></v-select>
                                    <v-text-field v-model.number="d.displayValue" type="number" min="0"
                                        :step="d.kind === 'amount' ? 0.01 : 1"
                                        :prefix="d.kind === 'amount' ? '$' : undefined"
                                        :suffix="d.kind === 'percent' ? '%' : undefined"
                                        label="Value" density="compact" hide-details style="width: 120px"></v-text-field>
                                    <v-btn icon="mdi-close" variant="text" size="small" @click="removeDiscountRow(i)"></v-btn>
                                </div>
                                <v-btn variant="text" size="small" prepend-icon="mdi-plus" class="mt-2" @click="addDiscountRow">Add preset</v-btn>
                            </v-card>

                            <!-- Comp reasons (always manager-gated; show on the void/comp report) -->
                            <v-card variant="outlined" class="pa-4 mb-4">
                                <div class="text-subtitle-1 font-weight-bold mb-1 d-flex align-center ga-2">
                                    <v-icon size="small" color="primary">mdi-gift-outline</v-icon> Comp reasons
                                </div>
                                <p class="text-caption text-medium-emphasis mb-3">
                                    Comps always require a manager PIN and appear on the void/comp report.
                                </p>
                                <div v-for="(c, i) in compRows" :key="c.id || ('new' + i)" class="d-flex align-center ga-2 mt-3">
                                    <v-text-field v-model="c.name" label="Reason" density="compact" hide-details style="flex: 1"></v-text-field>
                                    <v-select v-model="c.defaultKind" :items="compKindItems" label="Default" density="compact"
                                        hide-details style="width: 150px"></v-select>
                                    <v-text-field v-if="c.defaultKind !== 'full'" v-model.number="c.displayValue" type="number" min="0"
                                        :step="c.defaultKind === 'amount' ? 0.01 : 1"
                                        :prefix="c.defaultKind === 'amount' ? '$' : undefined"
                                        :suffix="c.defaultKind === 'percent' ? '%' : undefined"
                                        label="Value" density="compact" hide-details style="width: 120px"></v-text-field>
                                    <v-btn icon="mdi-close" variant="text" size="small" @click="removeCompRow(i)"></v-btn>
                                </div>
                                <v-btn variant="text" size="small" prepend-icon="mdi-plus" class="mt-2" @click="addCompRow">Add comp reason</v-btn>
                            </v-card>

                            <!-- Manual-discount approval gate -->
                            <v-card variant="outlined" class="pa-4 mb-4">
                                <div class="text-subtitle-1 font-weight-bold mb-1 d-flex align-center ga-2">
                                    <v-icon size="small" color="primary">mdi-shield-key-outline</v-icon> Manual discount approval
                                </div>
                                <v-switch v-model="menuStyle.requireManagerForManualDiscount"
                                    label="Require a manager PIN for manual discounts" color="primary"
                                    density="compact" hide-details
                                    messages="Presets and member discounts never need a PIN. An arbitrary percent or dollar amount typed in at the POS does."></v-switch>
                            </v-card>

                            <!-- Sales tax -->
                            <v-card variant="outlined" class="pa-4 mb-4">
                                <div class="text-subtitle-1 font-weight-bold mb-1 d-flex align-center ga-2">
                                    <v-icon size="small" color="primary">mdi-percent-outline</v-icon> Sales tax
                                </div>
                                <p class="text-caption text-medium-emphasis mb-3">
                                    Tax is applied at checkout on every F&amp;B sale. Leave a rate at 0% for no tax.
                                </p>

                                <v-switch v-model="menuStyle.pricesIncludeTax" color="primary" density="compact" hide-details
                                    label="Item prices already include tax"
                                    messages="On = tax is backed out of the listed price. Off = tax is added on top at checkout."></v-switch>

                                <div v-for="(t, i) in taxRows" :key="t.id || ('new' + i)" class="d-flex align-center ga-2 mt-4">
                                    <v-text-field v-model="t.name" label="Name" density="compact" hide-details style="flex: 1"></v-text-field>
                                    <v-text-field v-model.number="t.ratePct" type="number" min="0" max="100" step="0.001"
                                        suffix="%" label="Rate" density="compact" hide-details style="width: 120px"></v-text-field>
                                    <v-chip v-if="t.isDefault" size="x-small" color="primary" variant="tonal">Default</v-chip>
                                    <v-btn v-else icon="mdi-close" variant="text" size="small" @click="removeTaxRow(i)"></v-btn>
                                </div>
                                <v-btn variant="text" size="small" prepend-icon="mdi-plus" class="mt-2"
                                    @click="addTaxRow">Add tax category</v-btn>
                                <div class="text-caption text-medium-emphasis mt-1">
                                    Assign categories to items on each item's editor. Items with none use the default.
                                </div>
                            </v-card>

                            <!-- Menu board appearance -->
                            <v-card variant="outlined" class="pa-4 mb-4">
                                <div class="text-subtitle-1 font-weight-bold mb-1 d-flex align-center ga-2">
                                    <v-icon size="small" color="primary">mdi-television-guide</v-icon> Menu board
                                </div>
                                <p class="text-caption text-medium-emphasis mb-3">
                                    Customize the in-venue menu board. Leave a color or logo blank to use your brand defaults.
                                </p>
                                <div class="d-flex align-center ga-3">
                                    <v-avatar v-if="menuStyle.logoUrl" size="48" rounded="lg"><v-img :src="absoluteUrl(menuStyle.logoUrl)"></v-img></v-avatar>
                                    <v-file-input :model-value="null" label="Logo (optional)" accept="image/*" density="compact"
                                        prepend-icon="mdi-image" hide-details :loading="uploadingLogo" style="flex: 1"
                                        @update:model-value="onLogoSelected"></v-file-input>
                                    <v-btn v-if="menuStyle.logoUrl" icon="mdi-delete" variant="text" size="small" @click="menuStyle.logoUrl = null"></v-btn>
                                </div>

                                <div class="d-flex ga-4 mt-4 flex-wrap">
                                    <label class="d-flex flex-column text-caption">Background
                                        <input type="color" :value="menuStyle.backgroundColor || '#ffffff'" @input="menuStyle.backgroundColor = ($event.target as HTMLInputElement).value" />
                                    </label>
                                    <label class="d-flex flex-column text-caption">Text
                                        <input type="color" :value="menuStyle.textColor || '#111111'" @input="menuStyle.textColor = ($event.target as HTMLInputElement).value" />
                                    </label>
                                    <label class="d-flex flex-column text-caption">Accent
                                        <input type="color" :value="menuStyle.accentColor || branding.primaryColor" @input="menuStyle.accentColor = ($event.target as HTMLInputElement).value" />
                                    </label>
                                    <v-btn variant="text" size="small" class="align-self-end"
                                        @click="menuStyle.backgroundColor = null; menuStyle.textColor = null; menuStyle.accentColor = null">Reset colors</v-btn>
                                </div>

                                <v-switch v-model="menuStyle.showCarousel" label="Show product photo carousel" color="primary"
                                    density="compact" hide-details class="mt-4"></v-switch>
                                <v-text-field v-model.number="menuStyle.carouselSeconds" type="number" min="2" max="60"
                                    label="Carousel seconds per slide" density="compact" class="mt-4"
                                    :disabled="!menuStyle.showCarousel"></v-text-field>
                                <v-btn variant="tonal" prepend-icon="mdi-eye" class="mt-4" @click="openPreview">Preview menu</v-btn>
                            </v-card>
                        </div>
                    </v-card-text>
                    <v-card-actions class="px-4 pb-4">
                        <v-btn color="primary" variant="flat" :loading="savingMenuStyle" @click="saveSettings">Save</v-btn>
                    </v-card-actions>
                </v-card>
            </v-window-item>
        </v-window>

        <!-- Add / edit product (full page) -->
        <v-dialog v-model="productDialog" fullscreen scrollable transition="dialog-bottom-transition">
            <v-card class="d-flex flex-column" style="height: 100%;">
                <v-toolbar color="primary" density="comfortable">
                    <v-toolbar-title>{{ editing ? 'Edit item' : 'Add item' }}</v-toolbar-title>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" @click="productDialog = false"></v-btn>
                </v-toolbar>
                <v-card-text style="flex: 1 1 auto; overflow-y: auto; min-height: 0;">
                    <div style="max-width: 1280px; margin: 0 auto;">
                        <v-row>
                            <!-- Section 1: Details -->
                            <v-col cols="12" md="4">
                                <v-card variant="outlined" class="pa-4" style="height: 100%;">
                                    <div class="text-subtitle-1 font-weight-bold mb-3 d-flex align-center ga-2">
                                        <v-icon size="small" color="primary">mdi-information-outline</v-icon> Details
                                    </div>
                                    <v-text-field v-model="form.name" label="Name" density="compact"></v-text-field>
                                    <v-row class="mt-0">
                                        <v-col cols="7">
                                            <v-select v-model="form.categoryId" :items="categoryItems" label="Category"
                                                density="compact" class="mt-4" clearable
                                                :hint="categoryItems.length ? '' : 'Add categories first via the Categories tab'"
                                                :persistent-hint="!categoryItems.length"></v-select>
                                        </v-col>
                                        <v-col cols="5">
                                            <v-text-field v-model.number="form.priceDollars" type="number" min="0" step="0.01"
                                                prefix="$" label="Price" density="compact" class="mt-4"
                                                hint="Base price; variants can override" persistent-hint></v-text-field>
                                        </v-col>
                                    </v-row>
                                    <v-textarea v-model="form.description" label="Description (optional)" rows="2"
                                        density="compact" class="mt-4"></v-textarea>
                                    <v-select v-model="form.taxCategoryId" :items="taxCategoryItems" label="Tax category"
                                        density="compact" class="mt-4"
                                        hint="Sets this item's sales-tax rate. Manage rates on the Settings tab."
                                        persistent-hint></v-select>
                                    <div class="d-flex align-center ga-3 mt-4">
                                        <v-avatar v-if="form.imageUrl" size="48" rounded="lg">
                                            <v-img :src="absoluteUrl(form.imageUrl)"></v-img>
                                        </v-avatar>
                                        <v-file-input :model-value="null" label="Image (optional)" accept="image/*"
                                            density="compact" prepend-icon="mdi-camera" hide-details :loading="uploading"
                                            style="flex: 1" @update:model-value="onImageSelected"></v-file-input>
                                        <v-btn v-if="form.imageUrl" icon="mdi-delete" variant="text" size="small"
                                            @click="form.imageUrl = null"></v-btn>
                                    </div>
                                </v-card>
                            </v-col>

                            <!-- Section 2: Kitchen & options -->
                            <v-col cols="12" md="4">
                                <v-card variant="outlined" class="pa-4" style="height: 100%;">
                                    <div class="text-subtitle-1 font-weight-bold mb-3 d-flex align-center ga-2">
                                        <v-icon size="small" color="primary">mdi-tune-variant</v-icon> Kitchen &amp; options
                                    </div>
                                    <v-select v-model="form.stationId" :items="stationItems" label="Kitchen station (optional)"
                                        density="compact" clearable hint="Where the cook screen routes this item"
                                        persistent-hint></v-select>
                                    <v-select v-model="form.modifierGroupIds" :items="groupItems" label="Modifier groups (optional)"
                                        density="compact" class="mt-4" multiple chips closable-chips
                                        hint="e.g. Choose a side, Add-ons" persistent-hint></v-select>

                                    <div v-if="selectedDefaultGroups.length" class="mt-4">
                                        <div class="text-caption text-medium-emphasis mb-1">
                                            Default selections (auto-added when rung up; the cashier can change them)
                                        </div>
                                        <div v-for="g in selectedDefaultGroups" :key="g.id" class="mb-1">
                                            <div class="text-caption font-weight-medium">{{ g.name }}</div>
                                            <div class="d-flex flex-wrap ga-x-4">
                                                <v-checkbox v-for="o in g.options" :key="o.id" :label="o.name" density="compact" hide-details
                                                    :model-value="form.defaultOptionIds.includes(o.id)"
                                                    @update:model-value="toggleDefaultOption(o.id, !!$event)"></v-checkbox>
                                            </div>
                                        </div>
                                    </div>
                                </v-card>
                            </v-col>

                            <!-- Section 3: Stock & recipe -->
                            <v-col cols="12" md="4">
                                <v-card variant="outlined" class="pa-4" style="height: 100%;">
                                    <div class="text-subtitle-1 font-weight-bold mb-3 d-flex align-center ga-2">
                                        <v-icon size="small" color="primary">mdi-fridge-outline</v-icon> Stock &amp; recipe
                                    </div>
                                    <v-text-field v-model.number="form.inventory" type="number" min="0" label="Inventory (optional)"
                                        density="compact" clearable placeholder="∞"
                                        hint="Stock for items without size/color options. Blank = unlimited; auto-sells-out at 0."
                                        persistent-hint></v-text-field>

                                    <div class="mt-4">
                                        <div class="text-caption text-medium-emphasis mb-1">
                                            Recipe (ingredients depleted from F&amp;B inventory each time this sells)
                                        </div>
                                        <div v-for="(r, idx) in recipeRows" :key="idx" class="d-flex align-center ga-2 mb-1">
                                            <v-select v-model="r.inventoryItemId" :items="inventoryItemOptions" label="Ingredient"
                                                density="compact" hide-details style="flex: 1"></v-select>
                                            <v-text-field v-model.number="r.quantity" type="number" min="0" step="0.001" label="Qty"
                                                density="compact" hide-details style="width: 96px"></v-text-field>
                                            <v-btn icon="mdi-close" variant="text" size="small" @click="recipeRows.splice(idx, 1)"></v-btn>
                                        </div>
                                        <v-btn v-if="inventoryItems.length" variant="text" size="small" prepend-icon="mdi-plus"
                                            @click="recipeRows.push({ inventoryItemId: inventoryItems[0].id, quantity: 1 })">
                                            Add ingredient
                                        </v-btn>
                                        <div v-else class="text-caption text-medium-emphasis">
                                            Add inventory items first on the Inventory tab.
                                        </div>
                                    </div>
                                </v-card>
                            </v-col>
                        </v-row>

                        <!-- Section 4: Availability -->
                        <v-card variant="outlined" class="pa-4 mt-4">
                            <div class="text-subtitle-1 font-weight-bold mb-3 d-flex align-center ga-2">
                                <v-icon size="small" color="primary">mdi-eye-outline</v-icon> Availability
                            </div>
                            <div class="d-flex flex-wrap ga-8">
                                <v-switch v-model="form.comboAvailable" label="Available as combo" color="primary" density="compact" hide-details></v-switch>
                                <v-switch v-model="form.showInCarousel" label="Show in menu-board carousel" color="primary" density="compact" hide-details></v-switch>
                                <v-switch v-model="form.isActive" label="Active" color="primary" density="compact" hide-details></v-switch>
                            </div>

                            <!-- 86: mark sold out for today (existing items only; auto-clears tomorrow). -->
                            <div v-if="editing" class="mt-4 d-flex align-center ga-3 flex-wrap">
                                <v-btn :color="editing.manuallySoldOut ? 'success' : 'warning'" variant="tonal" size="small"
                                    :loading="saving86" :prepend-icon="editing.manuallySoldOut ? 'mdi-check-circle' : 'mdi-cancel'"
                                    @click="toggleEditor86">
                                    {{ editing.manuallySoldOut ? 'Back in stock' : '86 (sold out today)' }}
                                </v-btn>
                                <span class="text-caption text-medium-emphasis">
                                    {{ editing.manuallySoldOut ? "86'd for today; clears automatically tomorrow." : 'Temporarily mark this item sold out for today.' }}
                                </span>
                            </div>
                        </v-card>
                    </div>
                </v-card-text>
                <div class="pa-3 d-flex justify-start ga-2" style="flex: 0 0 auto; border-top: 1px solid rgba(128, 128, 128, 0.2);">
                    <v-btn :disabled="saving" @click="productDialog = false">Cancel</v-btn>
                    <v-btn color="primary" variant="flat" :loading="saving" @click="saveProduct">Save</v-btn>
                </div>
            </v-card>
        </v-dialog>

        <!-- Variant manager -->
        <v-dialog v-model="variantDialog" max-width="720">
            <v-card v-if="variantProduct">
                <v-card-title class="d-flex align-center">
                    <span>Variants &mdash; {{ variantProduct.name }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="variantDialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <p class="text-caption text-medium-emphasis mb-3">
                        Add a row per size/color. Leave Price blank to use the item's base price, and Stock blank for unlimited.
                    </p>
                    <v-table density="compact">
                        <thead>
                            <tr>
                                <th>Size</th>
                                <th>Color</th>
                                <th style="width: 110px">Price</th>
                                <th style="width: 110px">Stock</th>
                                <th style="width: 80px">Active</th>
                                <th style="width: 120px" class="text-right"></th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="(v, i) in variantRows" :key="v.id ?? `new-${i}`">
                                <td><v-text-field v-model="v.size" density="compact" hide-details placeholder="M"></v-text-field></td>
                                <td><v-text-field v-model="v.color" density="compact" hide-details placeholder="Red"></v-text-field></td>
                                <td><v-text-field v-model.number="v.priceDollars" type="number" min="0" step="0.01"
                                    prefix="$" density="compact" hide-details placeholder="base"></v-text-field></td>
                                <td><v-text-field v-model.number="v.inventory" type="number" min="0"
                                    density="compact" hide-details placeholder="∞"></v-text-field></td>
                                <td><v-switch v-model="v.isActive" color="primary" density="compact" hide-details></v-switch></td>
                                <td class="text-right">
                                    <v-btn icon="mdi-delete" variant="text" size="small" color="error"
                                        @click="removeVariant(v, i)"></v-btn>
                                </td>
                            </tr>
                            <tr v-if="variantRows.length === 0">
                                <td colspan="6" class="text-center text-medium-emphasis py-4">No variants. Flat item sold at the base price.</td>
                            </tr>
                        </tbody>
                    </v-table>
                    <v-btn variant="tonal" size="small" prepend-icon="mdi-plus" class="mt-2" @click="addVariantRow">Add variant</v-btn>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn variant="text" @click="variantDialog = false">Cancel</v-btn>
                    <v-btn color="primary" variant="flat" :loading="savingVariants" @click="saveAllVariants">Save &amp; Close</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <!-- Menu preview -->
        <v-dialog v-model="previewDialog" fullscreen scrollable>
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>Menu preview</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="previewDialog = false"></v-btn>
                </v-card-title>
                <v-divider></v-divider>
                <v-card-text class="pa-0">
                    <MenuBoardDisplay :products="rows.filter(p => p.isActive)" :settings="previewSettings"
                        :title="branding.displayName || 'RidePass'" :fallback-logo="branding.logoUrl"
                        :fallback-accent="branding.primaryColor" />
                </v-card-text>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3000">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import draggable from 'vuedraggable'
import {
    ConcessionService,
    type ConcessionProduct, type ConcessionVariant, type ConcessionStation, type ConcessionModifierGroup,
    type ConcessionCategory, type ConcessionMenuSettings, type ConcessionInventoryItem,
    type ConcessionTaxCategory, type ConcessionDiscountPreset, type ConcessionCompReason,
    type ConcessionOrderingCapacity,
} from '@/services/ConcessionService'
import { branding, loadBranding } from '@/stores/branding'
import { useConfirm } from '@/composables/useConfirm'
import MenuBoardDisplay from '@/components/MenuBoardDisplay.vue'
import ConcessionInventory from '@/views/Admin/ConcessionInventory.vue'

const service = new ConcessionService()
const confirm = useConfirm()

const rows = ref<ConcessionProduct[]>([])
const loading = ref(false)
const categories = ref<ConcessionCategory[]>([])
const categoryItems = computed(() =>
    categories.value.filter(c => c.isActive).map(c => ({ title: c.name, value: c.id })))

const productDialog = ref(false)
const editing = ref<ConcessionProduct | null>(null)
const saving = ref(false)
const uploading = ref(false)
const form = ref({
    name: '', categoryId: null as string | null, priceDollars: 0,
    description: '' as string | null, imageUrl: null as string | null, isActive: true,
    stationId: null as string | null, taxCategoryId: null as string | null, modifierGroupIds: [] as string[],
    inventory: null as number | null, showInCarousel: true, defaultOptionIds: [] as string[],
    comboAvailable: false,
})

// Tax categories: loaded for the item editor's picker and edited on the Settings tab.
const taxCategories = ref<ConcessionTaxCategory[]>([])
const taxCategoryItems = computed(() => [
    { title: 'Default', value: null as string | null },
    ...taxCategories.value.map(t => ({ title: `${t.name} (${(t.rateBps / 100).toFixed(2)}%)`, value: t.id as string | null })),
])

// Stations + modifier groups: loaded for the product editor pickers and managed in their own dialogs.
const stations = ref<ConcessionStation[]>([])
const modifierGroups = ref<ConcessionModifierGroup[]>([])
const stationItems = computed(() => stations.value.map(s => ({ title: s.name, value: s.id })))
const groupItems = computed(() => modifierGroups.value.map(g => ({ title: g.name, value: g.id })))
// The full group objects for the groups currently attached to the item, to render default-option toggles.
const selectedDefaultGroups = computed(() =>
    modifierGroups.value.filter(g => form.value.modifierGroupIds.includes(g.id)))
function toggleDefaultOption(optionId: string, checked: boolean) {
    if (checked) { if (!form.value.defaultOptionIds.includes(optionId)) form.value.defaultOptionIds.push(optionId) }
    else { form.value.defaultOptionIds = form.value.defaultOptionIds.filter(id => id !== optionId) }
}

// Recipe editor: rows of inventory item + quantity for the product currently open in the editor.
const inventoryItems = ref<ConcessionInventoryItem[]>([])
const inventoryItemOptions = computed(() =>
    inventoryItems.value.filter(i => i.isActive).map(i => ({ title: `${i.name} (${i.unit})`, value: i.id })))
// Low-stock count badged on the Inventory tab. Seeded from the items loaded on mount so the
// badge shows before the tab is opened; the live value emitted by ConcessionInventory takes
// over once that tab mounts (and stays fresh as stock is received/edited/counted there).
const inventoryLowFromChild = ref<number | null>(null)
const inventoryLowCount = computed(() => inventoryLowFromChild.value
    ?? inventoryItems.value.filter(i => i.isActive && i.isLow).length)
const recipeRows = ref<{ inventoryItemId: string; quantity: number }[]>([])

// Variant editor rows carry a dollar field for the price; id null = not yet created.
interface VariantRow {
    id: string | null
    size: string | null
    color: string | null
    priceDollars: number | null
    inventory: number | null
    isActive: boolean
}
const variantDialog = ref(false)
const variantProduct = ref<ConcessionProduct | null>(null)
const variantRows = ref<VariantRow[]>([])
const savingVariants = ref(false)

// Combo config editor (shared, tenant-level): size tiers + choose-one slots of component options.
interface ComboTierRow { name: string; sizeLabel: string | null; priceDollars: number }
interface ComboOptionRow { componentProductId: string; isDefault: boolean }
interface ComboSlotRow { name: string; isRequired: boolean; options: ComboOptionRow[] }
const comboTiers = ref<ComboTierRow[]>([])
const comboSlots = ref<ComboSlotRow[]>([])
const savingCombo = ref(false)
// Any item can be a combo component (side/drink choice).
const componentItems = computed(() => rows.value.map(p => ({ title: p.name, value: p.id })))

async function openComboConfig() {
    comboTiers.value = []
    comboSlots.value = []
    try {
        const { data } = await service.getComboConfig()
        const cfg = (data as any).data
        comboTiers.value = (cfg.tiers ?? []).map((t: any) => ({
            name: t.name, sizeLabel: t.sizeLabel, priceDollars: t.priceCents / 100,
        }))
        comboSlots.value = (cfg.slots ?? []).map((s: any) => ({
            name: s.name, isRequired: s.isRequired,
            options: s.options.map((o: any) => ({ componentProductId: o.componentProductId, isDefault: o.isDefault })),
        }))
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load the combo setup. Please try again.', 'error')
    }
}
function addComboTier() { comboTiers.value.push({ name: '', sizeLabel: '', priceDollars: 0 }) }
function addComboSlot() { comboSlots.value.push({ name: '', isRequired: true, options: [] }) }
function addComboOption(slot: ComboSlotRow) {
    const first = componentItems.value[0]?.value ?? ''
    slot.options.push({ componentProductId: first, isDefault: slot.options.length === 0 })
}
function setComboDefault(si: number, oi: number) {
    comboSlots.value[si].options.forEach((o, i) => o.isDefault = i === oi)
}
async function saveComboConfig() {
    savingCombo.value = true
    try {
        const payload = {
            tiers: comboTiers.value
                .filter(t => t.name.trim())
                .map(t => ({
                    name: t.name.trim(),
                    sizeLabel: t.sizeLabel?.trim() || null,
                    priceCents: Math.round((t.priceDollars || 0) * 100),
                })),
            slots: comboSlots.value
                .filter(s => s.name.trim() && s.options.length)
                .map(s => ({
                    name: s.name.trim(), isRequired: s.isRequired,
                    options: s.options.filter(o => o.componentProductId)
                        .map(o => ({ componentProductId: o.componentProductId, isDefault: o.isDefault })),
                })),
        }
        await service.setComboConfig(payload)
        flash('Combo setup saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not save the combo setup. Please try again.', 'error')
    } finally {
        savingCombo.value = false
    }
}

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')
const saving86 = ref(false)
const seeding = ref(false)
// Whether this tenant has already loaded the starter catalog; hides the "Load starter content" buttons.
const starterSeeded = ref(false)

// Load the editable starter catalog (idempotent by name, so existing items stay and nothing duplicates).
async function loadStarter() {
    if (!await confirm({
        title: 'Load starter content?',
        message: 'Adds sample categories, stations, modifier groups, and products you can edit or delete. Your existing items are kept and nothing is duplicated.',
        confirmText: 'Load',
    })) return
    seeding.value = true
    try {
        await service.seedStarter()
        starterSeeded.value = true
        await load()
        flash('Starter content added.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load starter content. Please try again.', 'error')
    } finally {
        seeding.value = false
    }
}

// 86 / un-86 from the item editor (marks sold out for today only; auto-clears tomorrow).
async function toggleEditor86() {
    if (!editing.value) return
    const p = editing.value
    const next = !p.manuallySoldOut
    saving86.value = true
    try {
        await service.setSoldOut(p.id, next)
        p.manuallySoldOut = next   // reflect in the open editor immediately
        await load()
        flash(next ? `"${p.name}" marked sold out for today.` : `"${p.name}" is back on.`, 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || `Could not update "${p.name}". Please try again.`, 'error')
    } finally {
        saving86.value = false
    }
}

onMounted(async () => {
    if (!branding.loaded) await loadBranding()
    await load()
})

// Tabbed sections. Each management tab lazy-loads its editor data when first shown.
const tab = ref('items')
watch(tab, (t) => {
    if (t === 'categories') openCategories()
    else if (t === 'stations') { openStations(); loadMenuStyle() }   // menuStyle backs the cook-screen targets here
    else if (t === 'modifiers') openGroups()
    else if (t === 'combos') openComboConfig()
    else if (t === 'settings') openMenuStyle()
})

function absoluteUrl(u: string): string {
    return u.startsWith('http') ? u : `${import.meta.env.VITE_API_ENDPOINT?.replace(/\/api$/, '') ?? ''}${u}`
}

async function load() {
    loading.value = true
    try {
        const [r, st, mg, cat, inv, ms, tx] = await Promise.all([
            service.listForAdmin(), service.listStations(), service.listModifierGroups(), service.categoriesAdmin(),
            service.inventoryItems(), service.menuSettings(), service.taxCategories(),
        ])
        rows.value = (r.data as any).data
        stations.value = (st.data as any).data
        modifierGroups.value = (mg.data as any).data
        categories.value = (cat.data as any).data
        inventoryItems.value = (inv.data as any).data
        starterSeeded.value = (ms.data as any).data.starterSeeded
        taxCategories.value = (tx.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load items.', 'error')
    } finally {
        loading.value = false
    }
}

function openCreate() {
    editing.value = null
    form.value = { name: '', categoryId: null, priceDollars: 0, description: '', imageUrl: null, isActive: true, stationId: null, taxCategoryId: null, modifierGroupIds: [], inventory: null, showInCarousel: true, defaultOptionIds: [], comboAvailable: false }
    recipeRows.value = []
    productDialog.value = true
}

async function openEdit(p: ConcessionProduct) {
    editing.value = p
    form.value = {
        name: p.name, categoryId: p.categoryId, priceDollars: p.priceCents / 100,
        description: p.description, imageUrl: p.imageUrl, isActive: p.isActive,
        stationId: p.stationId, taxCategoryId: p.taxCategoryId, modifierGroupIds: p.modifierGroups.map(g => g.id),
        inventory: p.inventory, showInCarousel: p.showInCarousel,
        defaultOptionIds: [...(p.defaultModifierOptionIds ?? [])],
        comboAvailable: p.comboAvailable,
    }
    recipeRows.value = []
    productDialog.value = true
    try {
        const { data } = await service.getRecipe(p.id)
        recipeRows.value = (data as any).data.map((l: any) => ({ inventoryItemId: l.inventoryItemId, quantity: l.quantity }))
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load the recipe for this item.', 'error')
    }
}

async function onImageSelected(v: File | File[] | null) {
    const file = Array.isArray(v) ? (v[0] ?? null) : v
    if (!file) return
    uploading.value = true
    try {
        const r = await service.uploadImage(file)
        form.value.imageUrl = (r.data as any).data.imageUrl
    } catch (err: any) {
        flash(err.response?.data?.error || 'Image upload failed.', 'error')
    } finally {
        uploading.value = false
    }
}

async function saveProduct() {
    const name = form.value.name.trim()
    if (!name) { flash('Name is required.', 'error'); return }
    saving.value = true
    try {
        const payload = {
            name,
            categoryId: form.value.categoryId,
            priceCents: Math.round((form.value.priceDollars || 0) * 100),
            description: form.value.description?.trim() || null,
            imageUrl: form.value.imageUrl,
            showInCarousel: form.value.showInCarousel,
            isActive: form.value.isActive,
            sortOrder: editing.value?.sortOrder ?? rows.value.length * 10 + 10,
            stationId: form.value.stationId,
            taxCategoryId: form.value.taxCategoryId,
            modifierGroupIds: form.value.modifierGroupIds,
            defaultModifierOptionIds: form.value.defaultOptionIds,
            inventory: form.value.inventory != null && (form.value.inventory as any) !== ''
                ? Math.trunc(form.value.inventory) : null,
            comboAvailable: form.value.comboAvailable,
        }
        let productId: string
        if (editing.value) { await service.update(editing.value.id, payload); productId = editing.value.id }
        else { const res = await service.create(payload); productId = (res.data as any).data.id }
        const recipe = recipeRows.value
            .filter(r => r.inventoryItemId && Number(r.quantity) > 0)
            .map(r => ({ inventoryItemId: r.inventoryItemId, quantity: Number(r.quantity) }))
        await service.setRecipe(productId, recipe)
        productDialog.value = false
        flash('Item saved.', 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function remove(p: ConcessionProduct) {
    if (!await confirm({
        title: 'Delete item?',
        message: `Delete "${p.name}"? If it has sales on file, set it inactive instead.`,
        confirmText: 'Delete', confirmColor: 'error',
    })) return
    try {
        await service.remove(p.id)
        flash('Item deleted.', 'success')
        await load()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    }
}

async function onReorderEnd(evt: { oldIndex?: number; newIndex?: number }) {
    if (evt.oldIndex === evt.newIndex) return
    rows.value.forEach((r, i) => { r.sortOrder = (i + 1) * 10 })
    try {
        await service.reorder(rows.value.map(r => ({ id: r.id, sortOrder: r.sortOrder })))
        flash('Order saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to save order — refreshing.', 'error')
        await load()
    }
}

// ── Variants ──────────────────────────────────────────────────────────────
function openVariants(p: ConcessionProduct) {
    variantProduct.value = p
    variantRows.value = p.variants.map(toVariantRow)
    variantDialog.value = true
}

function toVariantRow(v: ConcessionVariant): VariantRow {
    return {
        id: v.id, size: v.size, color: v.color,
        priceDollars: v.priceCents != null ? v.priceCents / 100 : null,
        inventory: v.inventory, isActive: v.isActive,
    }
}

function addVariantRow() {
    variantRows.value.push({ id: null, size: '', color: '', priceDollars: null, inventory: null, isActive: true })
}

// Batch-save every row in the dialog in one action (matching the Extras variant editor),
// so a filled-but-not-individually-saved row can no longer be silently dropped on close.
// New rows keep their returned id, so a mid-way failure won't duplicate them on retry.
async function saveAllVariants() {
    if (!variantProduct.value) return
    savingVariants.value = true
    try {
        const productId = variantProduct.value.id
        for (let i = 0; i < variantRows.value.length; i++) {
            const v = variantRows.value[i]
            const payload = {
                size: (v.size ?? '').toString().trim() || null,
                color: (v.color ?? '').toString().trim() || null,
                priceCents: v.priceDollars != null && v.priceDollars !== ('' as any) ? Math.round(v.priceDollars * 100) : null,
                imageUrl: null,
                inventory: v.inventory != null && v.inventory !== ('' as any) ? Math.trunc(v.inventory) : null,
                isActive: v.isActive,
                sortOrder: i * 10,
            }
            if (v.id) await service.updateVariant(productId, v.id, payload)
            else {
                const r = await service.createVariant(productId, payload)
                v.id = (r.data as any).data.id
            }
        }
        flash('Variants saved.', 'success')
        await load()
        syncVariantProduct()
        variantDialog.value = false
    } catch (err: any) {
        flash(err.response?.data?.error || 'Variant save failed.', 'error')
    } finally {
        savingVariants.value = false
    }
}

async function removeVariant(v: VariantRow, i: number) {
    if (!variantProduct.value) return
    if (!v.id) { variantRows.value.splice(i, 1); return }   // unsaved row
    if (!await confirm({
        title: 'Delete variant?', message: 'Delete this variant? If it has sales, set it inactive instead.',
        confirmText: 'Delete', confirmColor: 'error',
    })) return
    try {
        await service.removeVariant(variantProduct.value.id, v.id)
        variantRows.value.splice(i, 1)
        flash('Variant deleted.', 'success')
        await load()
        syncVariantProduct()
    } catch (err: any) {
        flash(err.response?.data?.error || 'Variant delete failed.', 'error')
    }
}

// Keep the open variant dialog pointed at the freshly-loaded product row.
function syncVariantProduct() {
    if (!variantProduct.value) return
    const fresh = rows.value.find(r => r.id === variantProduct.value!.id)
    if (fresh) variantProduct.value = fresh
}

// ── Stations manager ────────────────────────────────────────────────────────
interface StationRow { id: string | null; name: string; sortOrder: number; isActive: boolean; uid: number }
const savingStations = ref(false)
const stationRows = ref<StationRow[]>([])
let stnUidSeq = 0
// Stable key for vuedraggable (new rows have no id yet, so fall back to a client uid).
const stationKey = (s: StationRow) => s.id ?? `new-${s.uid}`

function openStations() {
    stationRows.value = stations.value.map(s => ({ id: s.id, name: s.name, sortOrder: s.sortOrder, isActive: s.isActive, uid: ++stnUidSeq }))
}

function addStationRow() {
    stationRows.value.push({ id: null, name: '', sortOrder: stationRows.value.length * 10, isActive: true, uid: ++stnUidSeq })
}

async function removeStationRow(s: StationRow, i: number) {
    if (!s.id) { stationRows.value.splice(i, 1); return }
    if (!await confirm({ title: 'Delete station?', message: `Delete "${s.name}"? Items assigned to it fall back to the default queue.`, confirmText: 'Delete', confirmColor: 'error' })) return
    try {
        await service.removeStation(s.id)
        stationRows.value.splice(i, 1)
        await load()
        flash('Station deleted.', 'success')
    } catch (err: any) { flash(err.response?.data?.error || 'Delete failed.', 'error') }
}

async function saveStations() {
    savingStations.value = true
    try {
        // sortOrder is driven by drag position now, so persist it from each row's index.
        for (let i = 0; i < stationRows.value.length; i++) {
            const s = stationRows.value[i]
            const name = s.name.trim()
            if (!name) continue
            const payload = { name, sortOrder: i * 10, isActive: s.isActive }
            if (s.id) await service.updateStation(s.id, payload)
            else await service.createStation(payload)
        }
        await load()
        flash('Stations saved.', 'success')
    } catch (err: any) { flash(err.response?.data?.error || 'Save failed.', 'error') }
    finally { savingStations.value = false }
}

// ── Modifier groups manager ───────────────────────────────────────────────────
interface OptionRow { id: string | null; name: string; priceDollars: number; isActive: boolean; sortOrder: number }
interface GroupRow { id: string | null; name: string; minSelect: number; maxSelect: number | null; isRequired: boolean; isActive: boolean; sortOrder: number; options: OptionRow[] }
const savingGroupId = ref<string | null>(null)
const groupRows = ref<GroupRow[]>([])

function openGroups() {
    groupRows.value = modifierGroups.value.map(g => ({
        id: g.id, name: g.name, minSelect: g.minSelect, maxSelect: g.maxSelect,
        isRequired: g.isRequired, isActive: g.isActive, sortOrder: g.sortOrder,
        options: g.options.map(o => ({ id: o.id, name: o.name, priceDollars: o.priceDeltaCents / 100, isActive: o.isActive, sortOrder: o.sortOrder })),
    }))
}

async function saveGroup(g: GroupRow) {
    const name = g.name.trim()
    if (!name) { flash('Group name is required.', 'error'); return }
    savingGroupId.value = g.id ?? 'new'
    try {
        const payload = {
            name, minSelect: g.minSelect || 0,
            maxSelect: g.maxSelect && g.maxSelect > 0 ? g.maxSelect : null,
            isRequired: g.isRequired, sortOrder: g.sortOrder || 0, isActive: g.isActive,
        }
        if (g.id) await service.updateModifierGroup(g.id, payload)
        else g.id = ((await service.createModifierGroup(payload)).data as any).data.id
        for (const o of g.options) {
            const oname = o.name.trim()
            if (!oname) continue
            const op = { name: oname, priceDeltaCents: Math.round((o.priceDollars || 0) * 100), sortOrder: o.sortOrder || 0, isActive: o.isActive }
            if (o.id) await service.updateOption(g.id!, o.id, op)
            else o.id = ((await service.createOption(g.id!, op)).data as any).data.id
        }
        await load()
        flash('Modifier group saved.', 'success')
    } catch (err: any) { flash(err.response?.data?.error || 'Save failed.', 'error') }
    finally { savingGroupId.value = null }
}

async function removeGroup(g: GroupRow, gi: number) {
    if (!g.id) { groupRows.value.splice(gi, 1); return }
    if (!await confirm({ title: 'Delete group?', message: `Delete "${g.name}" and its options? Items using it lose these choices.`, confirmText: 'Delete', confirmColor: 'error' })) return
    try {
        await service.removeModifierGroup(g.id)
        groupRows.value.splice(gi, 1)
        await load()
        flash('Modifier group deleted.', 'success')
    } catch (err: any) { flash(err.response?.data?.error || 'Delete failed.', 'error') }
}

async function removeOptionRow(g: GroupRow, o: OptionRow, oi: number) {
    if (o.id && g.id) {
        try { await service.removeOption(g.id, o.id) }
        catch (err: any) { flash(err.response?.data?.error || 'Delete failed.', 'error'); return }
    }
    g.options.splice(oi, 1)
}

// ── Categories manager ────────────────────────────────────────────────────────
interface CategoryRow { id: string | null; name: string; sortOrder: number; isActive: boolean; uid: number }
const savingCategories = ref(false)
const categoryRows = ref<CategoryRow[]>([])
let catUidSeq = 0
// Stable key for vuedraggable (new rows have no id yet, so fall back to a client uid).
const catKey = (c: CategoryRow) => c.id ?? `new-${c.uid}`

function openCategories() {
    categoryRows.value = categories.value.map(c => ({ id: c.id, name: c.name, sortOrder: c.sortOrder, isActive: c.isActive, uid: ++catUidSeq }))
}

function addCategoryRow() {
    categoryRows.value.push({ id: null, name: '', sortOrder: categoryRows.value.length * 10, isActive: true, uid: ++catUidSeq })
}

async function removeCategoryRow(i: number) {
    const c = categoryRows.value[i]
    if (!c.id) { categoryRows.value.splice(i, 1); return }
    if (!await confirm({ title: 'Delete category?', message: `Delete "${c.name}"? Items in it become uncategorized.`, confirmText: 'Delete', confirmColor: 'error' })) return
    try {
        await service.removeCategory(c.id)
        categoryRows.value.splice(i, 1)
        await load()
        flash('Category deleted.', 'success')
    } catch (err: any) { flash(err.response?.data?.error || 'Delete failed.', 'error') }
}

async function saveCategories() {
    savingCategories.value = true
    try {
        // sortOrder is driven by drag position now, so persist it from each row's index.
        for (let i = 0; i < categoryRows.value.length; i++) {
            const c = categoryRows.value[i]
            const name = c.name.trim()
            if (!name) continue
            const payload = { name, sortOrder: i * 10, isActive: c.isActive }
            if (c.id) await service.updateCategory(c.id, payload)
            else await service.createCategory(payload)
        }
        await load()
        flash('Categories saved.', 'success')
    } catch (err: any) { flash(err.response?.data?.error || 'Save failed.', 'error') }
    finally { savingCategories.value = false }
}

// ── Menu board style + preview ──────────────────────────────────────────────────
const savingMenuStyle = ref(false)
const uploadingLogo = ref(false)
const menuStyle = ref<ConcessionMenuSettings>({
    logoUrl: null, backgroundColor: null, textColor: null, accentColor: null, showCarousel: true, carouselSeconds: 5, tipsEnabled: false,
    prepWarnMinutes: 5, prepLateMinutes: 10, orderingHours: null, orderingSeasons: null, requireEventDay: true, pricesIncludeTax: false,
    seasonPassDiscountEnabled: false, seasonPassDiscountKind: 'percent', seasonPassDiscountValue: 0,
    loampassDiscountEnabled: false, loampassDiscountKind: 'percent', loampassDiscountValue: 0,
    requireManagerForManualDiscount: false,
    starterSeeded: false, orderingOpenNow: true,
})

// Online order capacity (throttle + quote). onlinePaused is server-managed; not edited here.
const capacity = ref<ConcessionOrderingCapacity>({
    capacityEnabled: false, basePrepMinutes: 10, maxActiveOrders: 0, showQuoteTimes: true, onlinePaused: false,
})
async function saveOrderingCapacity() {
    await service.updateOrderingCapacity({
        capacityEnabled: capacity.value.capacityEnabled,
        basePrepMinutes: Math.max(0, Math.min(240, Math.trunc(capacity.value.basePrepMinutes || 0))),
        maxActiveOrders: Math.max(0, Math.min(1000, Math.trunc(capacity.value.maxActiveOrders || 0))),
        showQuoteTimes: capacity.value.showQuoteTimes,
    })
}

// Discounts and comps: a 'percent' value is basis points (1500 = 15%); an 'amount' value is cents (200 = $2.00).
// The UI works in whole percents and dollars, so display = stored / 100 and stored = round(display * 100).
const discountKindItems: { title: string; value: 'percent' | 'amount' }[] =
    [{ title: 'Percent', value: 'percent' }, { title: 'Amount', value: 'amount' }]
const compKindItems: { title: string; value: 'full' | 'percent' | 'amount' }[] =
    [{ title: 'Full comp', value: 'full' }, { title: 'Percent', value: 'percent' }, { title: 'Amount', value: 'amount' }]

// Member-perk discount value fields. Writable computeds keep menuStyle (in bps/cents) as the source of truth
// while the inputs show whole percents or dollars.
const seasonPassValueDisplay = computed({
    get: () => (menuStyle.value.seasonPassDiscountValue || 0) / 100,
    set: (v: number) => { menuStyle.value.seasonPassDiscountValue = Math.max(0, Math.round((v || 0) * 100)) },
})
const loampassValueDisplay = computed({
    get: () => (menuStyle.value.loampassDiscountValue || 0) / 100,
    set: (v: number) => { menuStyle.value.loampassDiscountValue = Math.max(0, Math.round((v || 0) * 100)) },
})

// Discount-preset editor rows (displayValue is the whole percent or dollars; converted to bps/cents on save).
interface DiscountRow { id: string; name: string; kind: 'percent' | 'amount'; displayValue: number; isActive: boolean; sortOrder: number }
const discountRows = ref<DiscountRow[]>([])
const removedDiscountIds = ref<string[]>([])

function loadDiscountRows(list: ConcessionDiscountPreset[]) {
    discountRows.value = list.map(d => ({
        id: d.id, name: d.name, kind: d.kind, displayValue: +(d.value / 100).toFixed(2),
        isActive: d.isActive, sortOrder: d.sortOrder,
    }))
    removedDiscountIds.value = []
}
function addDiscountRow() {
    discountRows.value.push({ id: '', name: '', kind: 'percent', displayValue: 0, isActive: true, sortOrder: discountRows.value.length })
}
function removeDiscountRow(i: number) {
    const row = discountRows.value[i]
    if (row.id) removedDiscountIds.value.push(row.id)   // deleted from the server on Save
    discountRows.value.splice(i, 1)
}
// Persists removed/created/updated presets, then refetches so new rows pick up their ids. Errors bubble to saveSettings.
async function saveDiscountPresets() {
    for (const id of removedDiscountIds.value) await service.removeDiscountPreset(id)
    removedDiscountIds.value = []
    for (const [i, row] of discountRows.value.entries()) {
        const name = (row.name || '').trim()
        if (!name) continue
        const payload = {
            name, kind: row.kind,
            value: Math.max(0, Math.round((row.displayValue || 0) * 100)),
            isActive: row.isActive, sortOrder: i,
        }
        if (row.id) await service.updateDiscountPreset(row.id, payload)
        else await service.createDiscountPreset(payload)
    }
    loadDiscountRows((await service.discountPresets() as any).data.data)
}

// Comp-reason editor rows (displayValue ignored when defaultKind is 'full').
interface CompRow { id: string; name: string; defaultKind: 'full' | 'percent' | 'amount'; displayValue: number; isActive: boolean; sortOrder: number }
const compRows = ref<CompRow[]>([])
const removedCompIds = ref<string[]>([])

function loadCompRows(list: ConcessionCompReason[]) {
    compRows.value = list.map(c => ({
        id: c.id, name: c.name, defaultKind: c.defaultKind, displayValue: +(c.defaultValue / 100).toFixed(2),
        isActive: c.isActive, sortOrder: c.sortOrder,
    }))
    removedCompIds.value = []
}
function addCompRow() {
    compRows.value.push({ id: '', name: '', defaultKind: 'full', displayValue: 0, isActive: true, sortOrder: compRows.value.length })
}
function removeCompRow(i: number) {
    const row = compRows.value[i]
    if (row.id) removedCompIds.value.push(row.id)   // deleted from the server on Save
    compRows.value.splice(i, 1)
}
// Persists removed/created/updated comp reasons, then refetches. Errors bubble to saveSettings.
async function saveCompReasons() {
    for (const id of removedCompIds.value) await service.removeCompReason(id)
    removedCompIds.value = []
    for (const [i, row] of compRows.value.entries()) {
        const name = (row.name || '').trim()
        if (!name) continue
        const payload = {
            name, defaultKind: row.defaultKind,
            defaultValue: row.defaultKind === 'full' ? 0 : Math.max(0, Math.round((row.displayValue || 0) * 100)),
            isActive: row.isActive, sortOrder: i,
        }
        if (row.id) await service.updateCompReason(row.id, payload)
        else await service.createCompReason(payload)
    }
    loadCompRows((await service.compReasons() as any).data.data)
}

// Manager PIN: the current user sets/clears their own POS PIN. Empty clears it; we never preload the hash.
const managerPin = ref('')
const savingPin = ref(false)
async function saveManagerPin() {
    const pin = managerPin.value.trim()
    if (pin && !/^\d{4,8}$/.test(pin)) {
        flash('Enter a 4 to 8 digit numeric PIN, or leave the field blank to clear it.', 'error')
        return
    }
    savingPin.value = true
    try {
        await service.setManagerPin(pin)
        flash(pin ? 'Manager PIN updated.' : 'Manager PIN cleared.', 'success')
        managerPin.value = ''
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not update the manager PIN. Please try again.', 'error')
    } finally {
        savingPin.value = false
    }
}

// Tax-category editor rows (rate kept as a percent for the input; converted to basis points on save).
interface TaxRow { id: string; name: string; ratePct: number; isDefault: boolean; sortOrder: number; isActive: boolean }
const taxRows = ref<TaxRow[]>([])
const removedTaxIds = ref<string[]>([])

function loadTaxRows() {
    taxRows.value = taxCategories.value.map(t => ({
        id: t.id, name: t.name, ratePct: +(t.rateBps / 100).toFixed(3), isDefault: t.isDefault,
        sortOrder: t.sortOrder, isActive: t.isActive,
    }))
    if (taxRows.value.length === 0)
        taxRows.value = [{ id: '', name: 'Sales tax', ratePct: 0, isDefault: true, sortOrder: 0, isActive: true }]
    removedTaxIds.value = []
}

function addTaxRow() {
    taxRows.value.push({ id: '', name: '', ratePct: 0, isDefault: false, sortOrder: taxRows.value.length, isActive: true })
}

function removeTaxRow(i: number) {
    const row = taxRows.value[i]
    if (row.isDefault) return   // the default is never removable
    if (row.id) removedTaxIds.value.push(row.id)
    taxRows.value.splice(i, 1)
}

async function saveTaxCategories() {
    for (const id of removedTaxIds.value) await service.removeTaxCategory(id)
    removedTaxIds.value = []
    for (const [i, row] of taxRows.value.entries()) {
        const name = (row.name || '').trim() || (row.isDefault ? 'Sales tax' : 'Tax')
        const payload = {
            name,
            rateBps: Math.max(0, Math.min(10000, Math.round((row.ratePct || 0) * 100))),
            isDefault: row.isDefault,
            sortOrder: i,
            isActive: row.isActive,
        }
        if (row.id) await service.updateTaxCategory(row.id, payload)
        else await service.createTaxCategory(payload)
    }
    const tx = await service.taxCategories()
    taxCategories.value = (tx.data as any).data
    loadTaxRows()
}

// Operating-hours editor state (HH:MM strings per day; index 0 = Sunday).
const dayLabels = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
const useOrderingHours = ref(false)
const hoursRows = ref(Array.from({ length: 7 }, () => ({ open: true, openStr: '09:00', closeStr: '17:00' })))
function minToHHMM(m: number) { return `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}` }
function hhmmToMin(s: string) { const [h, m] = (s || '0:0').split(':').map(Number); return (h || 0) * 60 + (m || 0) }

// Open-season editor state (yyyy-MM-dd date strings per range).
const useSeason = ref(false)
const seasonRows = ref<{ startStr: string; endStr: string }[]>([{ startStr: '', endStr: '' }])

const previewDialog = ref(false)
const previewSettings = computed(() => menuStyle.value)

async function loadMenuStyle() {
    try {
        menuStyle.value = (await service.menuSettings() as any).data.data
        starterSeeded.value = menuStyle.value.starterSeeded
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load the menu board style.', 'error')
    }
    const oh = menuStyle.value.orderingHours
    useOrderingHours.value = !!(oh && oh.length === 7)
    hoursRows.value = oh && oh.length === 7
        ? oh.map(d => ({ open: d.open, openStr: minToHHMM(d.openMinute), closeStr: minToHHMM(d.closeMinute) }))
        : Array.from({ length: 7 }, () => ({ open: true, openStr: '09:00', closeStr: '17:00' }))
    const seasons = menuStyle.value.orderingSeasons
    useSeason.value = !!(seasons && seasons.length)
    seasonRows.value = seasons && seasons.length
        ? seasons.map(s => ({ startStr: s.startDate, endStr: s.endDate }))
        : [{ startStr: '', endStr: '' }]
}

async function openMenuStyle() {
    await loadMenuStyle()
    try {
        const tx = await service.taxCategories()
        taxCategories.value = (tx.data as any).data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load tax categories.', 'error')
    }
    loadTaxRows()

    // Online order capacity (throttle + quote) config.
    try {
        capacity.value = (await service.orderingCapacity() as any).data.data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load online order capacity settings.', 'error')
    }

    // Discount presets + comp reasons for the discounts/comps cards.
    try {
        const [dp, cr] = await Promise.all([service.discountPresets(), service.compReasons()])
        loadDiscountRows((dp.data as any).data)
        loadCompRows((cr.data as any).data)
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not load discounts and comps.', 'error')
    }
    managerPin.value = ''
}

// Opened from inside the settings modal, so menuStyle is already loaded; preview the live (possibly
// unsaved) edits rather than reloading from the server.
function openPreview() {
    previewDialog.value = true
}

async function onLogoSelected(v: File | File[] | null) {
    const file = Array.isArray(v) ? (v[0] ?? null) : v
    if (!file) return
    uploadingLogo.value = true
    try {
        const r = await service.uploadImage(file)
        menuStyle.value.logoUrl = (r.data as any).data.imageUrl
    } catch (err: any) {
        flash(err.response?.data?.error || 'Logo upload failed.', 'error')
    } finally {
        uploadingLogo.value = false
    }
}

async function saveMenuStyle() {
    savingMenuStyle.value = true
    try {
        const s = menuStyle.value
        await service.updateMenuSettings({
            logoUrl: s.logoUrl,
            backgroundColor: s.backgroundColor,
            textColor: s.textColor,
            accentColor: s.accentColor,
            showCarousel: s.showCarousel,
            carouselSeconds: Math.min(60, Math.max(2, Math.trunc(s.carouselSeconds || 5))),
            tipsEnabled: s.tipsEnabled,
            prepWarnMinutes: Math.min(240, Math.max(1, Math.trunc(s.prepWarnMinutes || 5))),
            prepLateMinutes: Math.min(240, Math.max(1, Math.trunc(s.prepLateMinutes || 10))),
            orderingHours: useOrderingHours.value
                ? hoursRows.value.map(d => ({ open: d.open, openMinute: hhmmToMin(d.openStr), closeMinute: hhmmToMin(d.closeStr) }))
                : null,
            orderingSeasons: useSeason.value
                ? seasonRows.value.filter(s => s.startStr && s.endStr).map(s => ({ startDate: s.startStr, endDate: s.endStr }))
                : null,
            requireEventDay: s.requireEventDay,
            pricesIncludeTax: s.pricesIncludeTax,
            seasonPassDiscountEnabled: s.seasonPassDiscountEnabled,
            seasonPassDiscountKind: s.seasonPassDiscountKind,
            seasonPassDiscountValue: Math.max(0, Math.round(s.seasonPassDiscountValue || 0)),
            loampassDiscountEnabled: s.loampassDiscountEnabled,
            loampassDiscountKind: s.loampassDiscountKind,
            loampassDiscountValue: Math.max(0, Math.round(s.loampassDiscountValue || 0)),
            requireManagerForManualDiscount: s.requireManagerForManualDiscount,
        })
        flash('Menu board style saved.', 'success')
    } catch (err: any) { flash(err.response?.data?.error || 'Save failed.', 'error') }
    finally { savingMenuStyle.value = false }
}

// The Settings-tab Save persists the menu settings, tax categories, discount presets, and comp reasons.
// The manager PIN is saved separately by its own button so an empty field never clears an existing PIN.
async function saveSettings() {
    savingMenuStyle.value = true
    try {
        await saveTaxCategories()
        await saveDiscountPresets()
        await saveCompReasons()
        await saveOrderingCapacity()
        await saveMenuStyle()   // shows its own success flash; also persists member discounts + the manual-discount toggle
    } catch (err: any) {
        flash(err.response?.data?.error || 'Could not save the Food and Beverage settings. Please try again.', 'error')
    } finally {
        savingMenuStyle.value = false
    }
}

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}
</script>

<style scoped>
.drag-handle-cell { padding-left: 4px !important; padding-right: 0 !important; }
.drag-handle { cursor: grab; }
.drag-handle:active { cursor: grabbing; }
.drag-ghost { opacity: 0.35; background: rgba(25, 118, 210, 0.08); }
.hours-input { border: 1px solid rgba(128, 128, 128, 0.4); border-radius: 6px; padding: 2px 6px; font-size: 0.85rem; }

/* Settings sections pack into columns so they fill the width and minimize scrolling. Cards keep their
   mb-4 for vertical spacing within a column; break-inside keeps each card whole. */
.settings-sections { column-gap: 16px; }
.settings-sections > * { break-inside: avoid; }
@media (min-width: 960px)  { .settings-sections { column-count: 2; } }
@media (min-width: 1600px) { .settings-sections { column-count: 3; } }
</style>
