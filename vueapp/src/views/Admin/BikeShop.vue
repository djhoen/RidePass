<template>
    <v-container>
        <div class="d-flex align-center mb-4 ga-3 flex-wrap">
            <h1 class="text-h4">Inventory</h1>
            <v-spacer></v-spacer>
            <v-btn variant="tonal" prepend-icon="mdi-cog" to="/Admin/BikeShop/Settings">Shop settings</v-btn>
            <v-btn color="primary" variant="tonal" prepend-icon="mdi-cash-register" to="/Admin/BikeShop/Register">
                Open register
            </v-btn>
        </div>

        <v-alert v-if="!branding.bikeShopEnabled" type="info" variant="tonal" class="mb-4">
            The bike shop is turned off. Enable it under Settings &rarr; Features to start selling.
        </v-alert>

        <v-tabs v-model="tab" class="mb-4">
            <v-tab value="products">Products</v-tab>
            <v-tab value="supply">Supply Chain</v-tab>
            <v-tab value="stocktakes">Stock takes</v-tab>
        </v-tabs>

        <!-- ── Products ─────────────────────────────────────────────────── -->
        <!-- Retail catalog: everything flagged sellable. A bike that is BOTH sellable and rentable
             belongs here too (you still price and stock it as retail); the Rentals page lists it
             again as fleet. Rent-only fleet units never appear here. -->
        <div v-if="tab === 'products'">
            <v-tabs v-model="productsTab" :height="40" class="mb-4 sub-tabs"
                hide-slider selected-class="sub-tab-active">
                <v-tab value="catalog" class="sub-tab">Catalog</v-tab>
                <v-tab value="categories" class="sub-tab">Categories</v-tab>
            </v-tabs>

            <div v-if="productsTab === 'catalog'">
            <!-- Came here from the register with a scanned barcode. The product form can't hold the
                 barcode (it lives on the variant, which is created afterwards), so keep it on
                 screen rather than making the user walk back to the register and scan again. -->
            <v-alert v-if="scannedPrefill?.barcode" type="info" variant="tonal" density="compact" closable
                class="mb-3" @click:close="scannedPrefill = null">
                Save the product, then add a variant to it: the scanned barcode
                (<strong>{{ scannedPrefill.barcode }}</strong>) and the manufacturer's name are
                filled in for you, so the scanner finds it next time.
            </v-alert>
            <div class="d-flex mb-3 ga-2 flex-wrap align-center">
                <v-text-field v-model="search" density="compact" hide-details clearable
                    prepend-inner-icon="mdi-magnify" label="Search name or SKU"
                    style="max-width: 320px" @update:model-value="onFilterChanged"></v-text-field>
                <v-select v-model="filterCategoryId" :items="categoryFilterItems" item-title="title"
                    item-value="value" density="compact" hide-details clearable label="Category"
                    style="max-width: 200px" @update:model-value="onFilterChanged"></v-select>
                <v-select v-model="filterSupplierId" :items="supplierFilterItems" item-title="title"
                    item-value="value" density="compact" hide-details clearable label="Supplier"
                    style="max-width: 200px" @update:model-value="onFilterChanged"></v-select>
                <v-btn-toggle v-model="stockFilter" density="compact" variant="outlined" divided
                    @update:model-value="onFilterChanged">
                    <v-btn value="all" size="small">All</v-btn>
                    <v-btn value="low" size="small">Low stock</v-btn>
                </v-btn-toggle>
                <v-switch v-model="activeOnly" density="compact" hide-details color="primary"
                    label="Active only" @update:model-value="onFilterChanged"></v-switch>
                <v-spacer></v-spacer>
                <v-btn variant="tonal" prepend-icon="mdi-file-delimited" @click="importDialog = true">Import CSV</v-btn>
                <v-btn color="primary" prepend-icon="mdi-plus" @click="openProduct()">New product</v-btn>
            </div>

            <!-- Totals for the whole filtered set, not just this page. Narrowing the filters
                 re-values that subset, so this doubles as a "what is my bike stock worth" answer. -->
            <div class="d-flex ga-6 flex-wrap mb-3 px-1">
                <div>
                    <div class="text-caption text-medium-emphasis">Stock at retail</div>
                    <div class="text-h6">{{ money(totals.stockRetailValueCents) }}</div>
                </div>
                <div>
                    <div class="text-caption text-medium-emphasis">Stock at cost</div>
                    <div class="text-h6">{{ money(totals.stockCostValueCents) }}</div>
                </div>
                <div>
                    <div class="text-caption text-medium-emphasis">Margin if sold</div>
                    <div class="text-h6">
                        {{ money(totals.stockRetailValueCents - totals.stockCostValueCents) }}
                        <span v-if="totalMarginPct !== null" class="text-caption text-medium-emphasis">
                            ({{ totalMarginPct }}%)
                        </span>
                    </div>
                </div>
                <div>
                    <div class="text-caption text-medium-emphasis">Low stock</div>
                    <div class="text-h6">
                        <a v-if="totals.lowStockCount > 0" href="#" class="text-warning"
                            style="text-decoration: underline" @click.prevent="showLowStock">
                            {{ totals.lowStockCount }}
                        </a>
                        <span v-else>0</span>
                    </div>
                </div>
                <div>
                    <div class="text-caption text-medium-emphasis">Units on order</div>
                    <div class="text-h6">{{ totals.unitsOnPo }}</div>
                </div>
            </div>

            <v-card>
                <v-table density="compact">
                    <thead>
                        <tr>
                            <th style="width: 40px"></th>
                            <th>Product</th>
                            <th style="width: 200px">Category</th>
                            <th style="width: 120px" class="text-right">Inventory</th>
                            <th style="width: 110px">Variants</th>
                            <th style="width: 90px"></th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-if="loadingProducts">
                            <td colspan="6" class="text-center py-6">
                                <v-progress-circular indeterminate size="24"></v-progress-circular>
                            </td>
                        </tr>
                        <tr v-else-if="products.length === 0">
                            <td colspan="6" class="text-center text-medium-emphasis py-6">
                                {{ hasActiveFilter
                                    ? 'No products match those filters.'
                                    : 'No products yet. Add your first bike, part, or piece of gear.' }}
                            </td>
                        </tr>
                        <template v-for="p in products" :key="p.id">
                            <tr class="product-row" @click="toggleExpand(p.id)">
                                <td>
                                    <v-icon size="small">
                                        {{ expanded.has(p.id) ? 'mdi-chevron-down' : 'mdi-chevron-right' }}
                                    </v-icon>
                                </td>
                                <td>
                                    <div class="d-flex align-center ga-3">
                                        <v-avatar size="36" rounded="lg" color="grey-lighten-3">
                                            <v-img v-if="p.imageUrl" :src="absoluteUrl(p.imageUrl)" cover></v-img>
                                            <v-icon v-else size="20" color="grey">mdi-image-outline</v-icon>
                                        </v-avatar>
                                        <div>
                                            <div class="d-flex align-center ga-2">
                                                <strong>{{ p.name }}</strong>
                                                <v-chip v-if="!p.isActive" size="x-small" color="warning">Inactive</v-chip>
                                                <v-tooltip v-if="p.isSellable && !p.isPublished" text="Sellable at the counter but not listed online" location="top">
                                                    <template #activator="{ props }">
                                                        <v-chip v-bind="props" size="x-small" variant="tonal">Not online</v-chip>
                                                    </template>
                                                </v-tooltip>
                                                <v-chip v-if="p.isRentable" size="x-small" variant="tonal">Also rented</v-chip>
                                            </div>
                                            <div v-if="p.brand" class="text-caption text-medium-emphasis">{{ p.brand }}</div>
                                        </div>
                                    </div>
                                </td>
                                <td class="text-caption">{{ categoryName(p.categoryId) || '—' }}</td>
                                <td class="text-right">
                                    {{ totalInventory(p) }}
                                    <v-chip v-if="isLowStock(p)" size="x-small" color="warning" class="ml-1">Low</v-chip>
                                    <v-chip v-else-if="totalInventory(p) <= 0" size="x-small" color="error" class="ml-1">Out</v-chip>
                                </td>
                                <td class="text-caption text-medium-emphasis">{{ p.variants.length }}</td>
                                <td class="text-right">
                                    <v-tooltip text="Edit product" location="top">
                                        <template #activator="{ props }">
                                            <v-btn v-bind="props" size="x-small" variant="text" icon="mdi-pencil"
                                                @click.stop="openProduct(p)"></v-btn>
                                        </template>
                                    </v-tooltip>
                                </td>
                            </tr>
                            <tr v-if="expanded.has(p.id)" class="expanded-row">
                                <td colspan="6" class="pa-0">
                                    <div class="pa-4">
                                        <div class="d-flex ga-2 mb-3">
                                            <v-spacer></v-spacer>
                                            <v-btn size="small" variant="tonal" prepend-icon="mdi-grid" @click="openMatrix(p)">Size matrix</v-btn>
                                            <v-btn size="small" color="primary" variant="tonal" prepend-icon="mdi-plus" @click="openVariant(p)">Add variant</v-btn>
                                        </div>
                                        <v-table density="compact">
                                            <thead>
                                                <tr>
                                                    <th>Variant</th><th>SKU</th><th>Barcode</th>
                                                    <th class="text-right">Cost</th>
                                                    <th class="text-right">Price</th>
                                                    <th class="text-right">Margin</th>
                                                    <th class="text-right">Available</th><th>Type</th><th></th>
                                                </tr>
                                            </thead>
                                            <tbody>
                                                <tr v-for="v in p.variants" :key="v.id">
                                                    <td>{{ variantLabel(v) || '(default)' }}</td>
                                                    <td class="text-caption">{{ v.sku || '—' }}</td>
                                                    <td class="text-caption text-medium-emphasis">{{ v.barcode || '—' }}</td>
                                                    <td class="text-right text-medium-emphasis">{{ v.costCents != null ? money(v.costCents) : '—' }}</td>
                                                    <td class="text-right">
                                                        {{ v.salePriceCents != null ? money(v.salePriceCents) : '—' }}
                                                        <div v-if="v.msrpCents && v.salePriceCents != null && v.msrpCents > v.salePriceCents"
                                                            class="text-caption text-medium-emphasis" style="text-decoration: line-through">
                                                            {{ money(v.msrpCents) }}
                                                        </div>
                                                    </td>
                                                    <td class="text-right">
                                                        <span v-if="marginPct(v) !== null"
                                                            :class="marginPct(v)! < 0 ? 'text-error' : ''">
                                                            {{ marginPct(v) }}%
                                                        </span>
                                                        <span v-else class="text-medium-emphasis">—</span>
                                                    </td>
                                                    <td class="text-right">
                                                        {{ v.availableCount }}
                                                        <v-chip v-if="v.availableCount <= 0" size="x-small" color="error" class="ml-1">Out</v-chip>
                                                    </td>
                                                    <td>
                                                        <v-chip size="x-small" :color="v.trackingKind === 'serialized' ? 'indigo' : 'blue-grey'">
                                                            {{ v.trackingKind === 'serialized' ? 'Serialized' : 'Pool' }}
                                                        </v-chip>
                                                    </td>
                                                    <td class="text-right">
                                                        <v-btn size="x-small" variant="text" icon="mdi-pencil" @click="openVariant(p, v)"></v-btn>
                                                        <v-tooltip v-if="v.barcode || v.sku" text="Print barcode label" location="top">
                                                            <template #activator="{ props }">
                                                                <v-btn v-bind="props" size="x-small" variant="text"
                                                                    icon="mdi-barcode" @click="openLabel(p, v)"></v-btn>
                                                            </template>
                                                        </v-tooltip>
                                                        <v-tooltip v-if="v.trackingKind === 'pool'" text="Adjust stock" location="top">
                                                            <template #activator="{ props }">
                                                                <v-btn v-bind="props" size="x-small" variant="text"
                                                                    icon="mdi-plus-minus-variant" @click="openAdjust(v)"></v-btn>
                                                            </template>
                                                        </v-tooltip>
                                                        <v-tooltip v-else text="Manage units" location="top">
                                                            <template #activator="{ props }">
                                                                <v-btn v-bind="props" size="x-small" variant="text" icon="mdi-format-list-numbered"
                                                                    @click="openItems(v)"></v-btn>
                                                            </template>
                                                        </v-tooltip>
                                                    </td>
                                                </tr>
                                            </tbody>
                                        </v-table>
                                    </div>
                                </td>
                            </tr>
                        </template>
                    </tbody>
                </v-table>

                <div v-if="totalProducts > pageSize" class="d-flex align-center pa-3">
                    <span class="text-caption text-medium-emphasis">
                        {{ pageRangeLabel }} of {{ totalProducts }}
                    </span>
                    <v-spacer></v-spacer>
                    <v-pagination v-model="page" :length="pageCount" :total-visible="5"
                        density="compact" @update:model-value="reloadProducts"></v-pagination>
                </div>
            </v-card>
            </div>

            <!-- Categories only exist to organize products, so they live beside them. -->
            <div v-else-if="productsTab === 'categories'">
                <SimpleCrud label="category" :rows="categories" @new="openCategory()" @edit="openCategory">
                    <template #cols="{ row }"><td>{{ row.name }}</td></template>
                </SimpleCrud>
            </div>
        </div>

        <!-- ── Supply chain: who you buy from, and what's on order ──────── -->
        <div v-else-if="tab === 'supply'">
            <!-- Pill styling so these read as a level BELOW the page tabs rather than
                 competing with them. -->
            <v-tabs v-model="supplyTab" :height="40" class="mb-4 sub-tabs"
                hide-slider selected-class="sub-tab-active">
                <v-tab value="purchasing" class="sub-tab">Purchasing</v-tab>
                <v-tab value="reorder" class="sub-tab">Reorder</v-tab>
                <v-tab value="suppliers" class="sub-tab">Suppliers</v-tab>
            </v-tabs>

            <div v-if="supplyTab === 'purchasing'">
                <PurchasingTab :products="allProducts" :suppliers="suppliers"
                    @flash="flash" @stock-changed="onStockChanged" />
            </div>

            <div v-else-if="supplyTab === 'reorder'">
                <ReorderTab @flash="flash" @created="onStockChanged" />
            </div>

            <div v-else-if="supplyTab === 'suppliers'">
                <SimpleCrud label="supplier" :rows="suppliers" @new="openSupplier()" @edit="openSupplier">
                    <template #cols="{ row }">
                        <td>{{ row.name }}</td>
                        <td class="text-caption text-medium-emphasis">{{ row.contactName || '' }} {{ row.phone || '' }}</td>
                    </template>
                </SimpleCrud>
            </div>
        </div>

        <!-- ── Stock takes ──────────────────────────────────────────────── -->
        <div v-else-if="tab === 'stocktakes'">
            <StockTakesTab @flash="flash" @stock-changed="onStockChanged" />
        </div>

        <!-- ── Dialogs ──────────────────────────────────────────────────── -->
        <ProductDialog v-model="productDialog" :product="editingProduct" :categories="categories"
            :suppliers="suppliers" :prefill="newProductPrefill" @saved="onStockChanged" @flash="flash" />
        <VariantDialog v-model="variantDialog" :product="variantProduct" :variant="editingVariant"
            :prefill="scannedPrefill" @saved="onStockChanged" @flash="flash" />
        <AdjustStockDialog v-model="adjustDialog" :variant="adjustVariant" @saved="onStockChanged" @flash="flash" />
        <BarcodeLabelDialog v-model="labelDialog" :variant="labelVariant" :product-name="labelProductName" />
        <ItemsDialog v-model="itemsDialog" :variant="itemsVariant" @changed="onStockChanged" @flash="flash" />
        <CategoryDialog v-model="categoryDialog" :category="editingCategory" @saved="reloadCategories" @flash="flash" />
        <SupplierDialog v-model="supplierDialog" :supplier="editingSupplier" @saved="reloadSuppliers" @flash="flash" />
        <ImportCsvDialog v-model="importDialog" @imported="onImported" @flash="flash" />
        <MatrixDialog v-model="matrixDialog" :product="matrixProduct" @saved="onStockChanged" @flash="flash" />

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="3500">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { branding } from '@/stores/branding'
import { BikeShopService, type ShopProduct, type ShopVariant, type ShopCategory, type ShopSupplier, type ShopTaxCategory, type ShopCatalogTotals } from '@/services/BikeShopService'
import SimpleCrud from '@/components/bikeshop/SimpleCrud.vue'
import ProductDialog from '@/components/bikeshop/ProductDialog.vue'
import VariantDialog from '@/components/bikeshop/VariantDialog.vue'
import BarcodeLabelDialog from '@/components/bikeshop/BarcodeLabelDialog.vue'
import AdjustStockDialog from '@/components/bikeshop/AdjustStockDialog.vue'
import ItemsDialog from '@/components/bikeshop/ItemsDialog.vue'
import CategoryDialog from '@/components/bikeshop/CategoryDialog.vue'
import SupplierDialog from '@/components/bikeshop/SupplierDialog.vue'
import PurchasingTab from '@/components/bikeshop/PurchasingTab.vue'
import ReorderTab from '@/components/bikeshop/ReorderTab.vue'
import StockTakesTab from '@/components/bikeshop/StockTakesTab.vue'
import ImportCsvDialog from '@/components/bikeshop/ImportCsvDialog.vue'
import MatrixDialog from '@/components/bikeshop/MatrixDialog.vue'

const service = new BikeShopService()

// Uploaded images come back as a relative /uploads/... path. That only resolves against the
// SPA origin, which is wrong whenever the API is a different origin (local dev, or a split
// deployment), so prefix it with the API host (matching the product dialog's preview).
function absoluteUrl(u: string): string {
    return u.startsWith('http') ? u : `${import.meta.env.VITE_API_ENDPOINT?.replace(/\/api$/, '') ?? ''}${u}`
}

// ?tab=tax lets other screens (e.g. Rentals -> Settings) deep-link straight to a section
// instead of telling the user to go hunt for it.
const route = useRoute()
const router = useRouter()
const validTabs = ['products','supply','stocktakes']
const requestedTab = String(route.query.tab ?? '')

// Tabs that used to live here. Suppliers/Purchasing merged into Supply Chain and Categories moved
// under Products, so those redirect within this page. Tax/Service/Agreements moved to Shop Settings
// and Reports into the Reporting hub, so those redirect off-page rather than dead-ending.
const inPageLegacy: Record<string, { tab: string; sub: string }> = {
    suppliers:  { tab: 'supply',   sub: 'suppliers' },
    purchasing: { tab: 'supply',   sub: 'purchasing' },
    categories: { tab: 'products', sub: 'categories' },
}
const movedAway: Record<string, string> = {
    tax:        '/Admin/BikeShop/Settings?tab=tax',
    service:    '/Admin/BikeShop/Settings?tab=service',
    agreements: '/Admin/BikeShop/Settings?tab=agreements',
    jobs:       '/Admin/BikeShop/WorkOrders?tab=templates',
    reports:    '/Admin/Reports?report=bike-shop',
}
const legacy = inPageLegacy[requestedTab]
const tab = ref(legacy?.tab ?? (validTabs.includes(requestedTab) ? requestedTab : 'products'))
// Catalog is the daily job; categories are reference data you touch occasionally.
const productsTab = ref(legacy?.tab === 'products' ? legacy.sub : 'catalog')
// Purchasing is the day-to-day job; suppliers are reference data you edit occasionally.
const requestedSub = String(route.query.sub ?? '')
const supplyTab = ref(legacy?.tab === 'supply' ? legacy.sub
    : (['purchasing','suppliers'].includes(requestedSub) ? requestedSub : 'purchasing'))

const products = ref<ShopProduct[]>([])
// The catalog list is paged and filtered to sellable, but Purchasing builds its PO-line picker
// from every variant in the shop (including rent-only fleet, and anything past page 1), so it
// gets its own unfiltered copy.
const allProducts = ref<ShopProduct[]>([])
const categories = ref<ShopCategory[]>([])
const suppliers = ref<ShopSupplier[]>([])

// ── Catalog list state (server-side search / filter / paging) ────────────────
const search = ref('')
const filterCategoryId = ref<string | null>(null)
const filterSupplierId = ref<string | null>(null)
const stockFilter = ref<'all' | 'low'>('all')
const activeOnly = ref(false)
const page = ref(1)
const pageSize = 25
const totalProducts = ref(0)
const loadingProducts = ref(false)
const expanded = ref<Set<string>>(new Set())
const totals = ref<ShopCatalogTotals>({
    stockRetailValueCents: 0, stockCostValueCents: 0, lowStockCount: 0, unitsOnPo: 0,
})

// Blended margin on the stock being valued. Null when nothing is priced, so the header shows a
// dollar figure without a meaningless "0%".
const totalMarginPct = computed(() => {
    const retail = totals.value.stockRetailValueCents
    if (retail <= 0) return null
    return Math.round(((retail - totals.value.stockCostValueCents) / retail) * 100)
})

// Margin on the sale price (not markup on cost). Null unless both sides are known and the item
// is actually priced.
function marginPct(v: ShopVariant): number | null {
    if (v.salePriceCents == null || v.costCents == null || v.salePriceCents <= 0) return null
    return Math.round(((v.salePriceCents - v.costCents) / v.salePriceCents) * 100)
}

// Header "Low stock" is a shortcut into the matching filter.
function showLowStock() {
    stockFilter.value = 'low'
    onFilterChanged()
}

const pageCount = computed(() => Math.max(1, Math.ceil(totalProducts.value / pageSize)))
const pageRangeLabel = computed(() => {
    if (totalProducts.value === 0) return '0'
    const from = (page.value - 1) * pageSize + 1
    return `${from}-${Math.min(from + products.value.length - 1, totalProducts.value)}`
})
const hasActiveFilter = computed(() =>
    !!search.value?.trim() || !!filterCategoryId.value || !!filterSupplierId.value
    || stockFilter.value === 'low' || activeOnly.value)

const categoryFilterItems = computed(() => categories.value.map(c => ({ value: c.id, title: c.name })))
const supplierFilterItems = computed(() => suppliers.value.map(s => ({ value: s.id, title: s.name })))

function toggleExpand(id: string) {
    const next = new Set(expanded.value)
    if (next.has(id)) next.delete(id); else next.add(id)
    expanded.value = next
}

// Inventory shown on the product row is the sum of what's actually available across variants
// (serialized variants count their free units; pool variants their stock on hand).
function totalInventory(p: ShopProduct): number {
    return p.variants.reduce((sum, v) => sum + (v.availableCount ?? 0), 0)
}
// Mirrors the server's low-stock predicate: pool variants only, threshold set, at or below it.
function isLowStock(p: ShopProduct): boolean {
    return p.variants.some(v => v.isActive && v.trackingKind === 'pool'
        && v.lowStockThreshold != null && (v.stockOnHand ?? 0) <= v.lowStockThreshold)
}

// Any filter change resets to page 1, otherwise you can land on an empty page.
let searchDebounce: ReturnType<typeof setTimeout> | null = null
function onFilterChanged() {
    page.value = 1
    if (searchDebounce) clearTimeout(searchDebounce)
    searchDebounce = setTimeout(reloadProducts, 250)
}

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')
function flash(text: string, color: 'success' | 'error' = 'success') {
    snackbarText.value = text; snackbarColor.value = color; snackbar.value = true
}

function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }
function categoryName(id: string | null): string {
    return id ? (categories.value.find(c => c.id === id)?.name ?? '') : ''
}
function variantLabel(v: ShopVariant): string {
    return [v.size, v.color, v.gender].filter(Boolean).join(' / ')
}

// Dialog state
const productDialog = ref(false); const editingProduct = ref<ShopProduct | null>(null)
const variantDialog = ref(false); const variantProduct = ref<ShopProduct | null>(null); const editingVariant = ref<ShopVariant | null>(null)
const adjustDialog = ref(false); const adjustVariant = ref<ShopVariant | null>(null)
const labelDialog = ref(false); const labelVariant = ref<ShopVariant | null>(null); const labelProductName = ref('')
const itemsDialog = ref(false); const itemsVariant = ref<ShopVariant | null>(null)
const categoryDialog = ref(false); const editingCategory = ref<ShopCategory | null>(null)
const supplierDialog = ref(false); const editingSupplier = ref<ShopSupplier | null>(null)
const importDialog = ref(false)
const matrixDialog = ref(false); const matrixProduct = ref<ShopProduct | null>(null)

function openProduct(p: ShopProduct | null = null) { editingProduct.value = p; productDialog.value = true }

// ── Arriving from the register with a barcode the shared parts library recognised ──────────
// The cashier scanned something this shop doesn't carry, the library knew what it was, and they
// clicked "Add to catalog". Open the new-product dialog already filled in rather than making them
// retype a name they were just shown. Only identity crosses over: price, category and stock are
// this shop's to set. The barcode rides along so it can be typed onto the variant afterwards.
// The product name is seeded from the manufacturer's, but it is the shop's OWN name from that
// point on: they can rename it freely and no other tenant ever sees it. The manufacturer's wording
// is kept separately and rides onto the variant, because that is the only field that is shared.
const newProductPrefill = ref<{ name?: string | null; brand?: string | null } | null>(null)
const scannedPrefill = ref<{ barcode?: string | null; manufacturerName?: string | null } | null>(null)
onMounted(() => {
    const name = typeof route.query.newName === 'string' ? route.query.newName : null
    if (!name) return
    newProductPrefill.value = {
        name,
        brand: typeof route.query.newBrand === 'string' ? route.query.newBrand : null,
    }
    scannedPrefill.value = {
        barcode: typeof route.query.newBarcode === 'string' ? route.query.newBarcode : null,
        // Same string as the seeded product name today, but they diverge the moment the shop
        // renames the product, which is exactly the point of keeping them apart.
        manufacturerName: name,
    }
    productDialog.value = true
    // Drop the params so a refresh (or a back-navigation) doesn't reopen the dialog.
    router.replace({ query: { ...route.query, newName: undefined, newBrand: undefined, newBarcode: undefined } })
})
function openMatrix(p: ShopProduct) { matrixProduct.value = p; matrixDialog.value = true }
// An import can create categories + suppliers alongside products; refresh all three lists.
function onImported() { onStockChanged(); reloadCategories(); reloadSuppliers() }
function openVariant(p: ShopProduct, v: ShopVariant | null = null) { variantProduct.value = p; editingVariant.value = v; variantDialog.value = true }
function openLabel(p: ShopProduct, v: ShopVariant) { labelVariant.value = v; labelProductName.value = p.name; labelDialog.value = true }
function openAdjust(v: ShopVariant) { adjustVariant.value = v; adjustDialog.value = true }
function openItems(v: ShopVariant) { itemsVariant.value = v; itemsDialog.value = true }
function openCategory(c: ShopCategory | null = null) { editingCategory.value = c; categoryDialog.value = true }
function openSupplier(s: ShopSupplier | null = null) { editingSupplier.value = s; supplierDialog.value = true }

async function reloadProducts() {
    loadingProducts.value = true
    try {
        // sellable: true keeps the rent-only fleet out of the retail catalog. A product flagged
        // both stays here AND shows on the Rentals page.
        const r = await service.searchProducts({
            search: search.value?.trim() || null,
            categoryId: filterCategoryId.value,
            supplierId: filterSupplierId.value,
            activeOnly: activeOnly.value,
            sellable: true,
            lowStockOnly: stockFilter.value === 'low',
            page: page.value,
            pageSize,
        })
        products.value = r.data.data.rows
        totalProducts.value = r.data.data.total
        totals.value = r.data.data.totals
    } catch (e: any) {
        flash(e.response?.data?.error || 'Could not load products. Check your connection and try again.', 'error')
    } finally {
        loadingProducts.value = false
    }
}
async function reloadAllProducts() {
    try { allProducts.value = (await service.listProducts()).data.data }
    catch (e: any) { flash(e.response?.data?.error || 'Could not load the full catalog for purchasing.', 'error') }
}

// Stock moved (receiving, a stock take): both the paged list and Purchasing's copy are stale.
async function onStockChanged() {
    await Promise.all([reloadProducts(), reloadAllProducts()])
}

async function reloadCategories() {
    try { categories.value = (await service.listCategories()).data.data }
    catch (e: any) { flash(e.response?.data?.error || 'Could not load categories.', 'error') }
}
async function reloadSuppliers() {
    try { suppliers.value = (await service.listSuppliers()).data.data }
    catch (e: any) { flash(e.response?.data?.error || 'Could not load suppliers.', 'error') }
}

onMounted(async () => {
    const moved = movedAway[requestedTab]
    if (moved) { router.replace(moved); return }

    await Promise.all([reloadProducts(), reloadAllProducts(), reloadCategories(), reloadSuppliers()])
})
</script>

<style scoped>
/* Sub-tabs: pills on a tinted rail, visually subordinate to the page tabs above. */
/* Height is set via the component's own :height prop (40 = 32px pills + 4px padding each side).
   Forcing min-height here instead clipped the pills, because the inner slide-group keeps its
   own height and simply overflowed. */
.sub-tabs {
    background: rgba(var(--v-theme-on-surface), 0.04);
    border-radius: 4px;              /* same radius as a button */
    padding: 4px;
    display: inline-flex;
    flex: 0 0 auto;
}
.sub-tabs :deep(.v-slide-group__content) {
    gap: 4px;
    align-items: center;
}
.sub-tabs :deep(.v-tab) {
    border-radius: 4px;
    height: 32px;
    min-height: 32px;
    min-width: 0;
    padding: 0 18px;
    font-size: 13px;
    letter-spacing: 0.01em;
    text-transform: none;
    opacity: 0.75;
}
.sub-tabs :deep(.v-tab.sub-tab-active),
.sub-tabs :deep(.v-tab--selected) {
    background: rgba(var(--v-theme-primary), 0.14);
    color: rgb(var(--v-theme-primary));
    opacity: 1;
    font-weight: 600;
}

/* The whole product row is the expand affordance, so make that legible. */
.product-row {
    cursor: pointer;
}
.product-row:hover {
    background: rgba(var(--v-theme-on-surface), 0.04);
}
.expanded-row > td {
    background: rgba(var(--v-theme-on-surface), 0.02);
}
</style>
