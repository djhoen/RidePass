<template>
    <v-container>
        <div class="d-flex align-center mb-6">
            <h1 class="text-h4">Coupons</h1>
            <v-spacer></v-spacer>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">Add Coupon</v-btn>
        </div>

        <v-card>
            <v-table>
                <thead>
                    <tr>
                        <th>Code</th>
                        <th>Discount</th>
                        <th>Scope</th>
                        <th style="width: 110px">Uses</th>
                        <th>Validity</th>
                        <th style="width: 90px">Active</th>
                        <th style="width: 160px" class="text-right"></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="c in rows" :key="c.id">
                        <td><code>{{ c.code }}</code><div v-if="c.description" class="text-caption text-medium-emphasis">{{ c.description }}</div></td>
                        <td>{{ formatDiscount(c) }}</td>
                        <td>{{ scopeLabel(c.applicableScope) }}</td>
                        <td>
                            {{ c.redemptionCount }}<span v-if="c.maxTotalUses"> / {{ c.maxTotalUses }}</span>
                        </td>
                        <td class="text-caption">
                            <div v-if="c.validFromUtc">From {{ formatDate(c.validFromUtc) }}</div>
                            <div v-if="c.validToUtc">Until {{ formatDate(c.validToUtc) }}</div>
                            <div v-if="!c.validFromUtc && !c.validToUtc" class="text-medium-emphasis">No expiry</div>
                        </td>
                        <td>
                            <v-icon v-if="c.isActive" color="success">mdi-check</v-icon>
                            <v-icon v-else color="grey">mdi-close</v-icon>
                        </td>
                        <td class="text-right">
                            <v-btn variant="text" size="small" @click="openEdit(c)">Edit</v-btn>
                            <v-btn variant="text" size="small" color="error" @click="remove(c)">Delete</v-btn>
                        </td>
                    </tr>
                    <tr v-if="!loading && rows.length === 0">
                        <td colspan="7" class="text-center text-medium-emphasis py-8">No coupons yet.</td>
                    </tr>
                </tbody>
            </v-table>
        </v-card>

        <v-dialog v-model="dialog" max-width="640">
            <v-card>
                <v-card-title class="d-flex align-center">
                    <span>{{ editing ? 'Edit Coupon' : 'Add Coupon' }}</span>
                    <v-spacer></v-spacer>
                    <v-btn icon="mdi-close" variant="text" size="small" @click="dialog = false"></v-btn>
                </v-card-title>
                <v-card-text>
                    <v-text-field v-model="form.code" label="Code" placeholder="SUMMER25"
                        hint="Letters, numbers, dashes, underscores. Case-insensitive at the gate."
                        persistent-hint :hide-details="false"></v-text-field>
                    <v-text-field v-model="form.description" label="Description (optional)"
                        placeholder="20% off summer passes" class="mt-3"></v-text-field>

                    <v-row class="mt-2">
                        <v-col cols="12" md="4">
                            <v-select v-model="form.discountKind" :items="discountKindOptions"
                                item-title="label" item-value="value" label="Discount type"></v-select>
                        </v-col>
                        <v-col cols="12" md="4">
                            <v-text-field v-if="form.discountKind === 'percent'" v-model.number="form.discountPercent"
                                type="number" min="1" max="100" suffix="%" label="Percent off"></v-text-field>
                            <v-text-field v-else v-model.number="form.discountDollars"
                                type="number" min="0.5" step="0.5" prefix="$" label="Amount off"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="4">
                            <v-select v-model="form.applicableScope" :items="scopeOptions"
                                item-title="label" item-value="value" label="Applies to"></v-select>
                        </v-col>
                    </v-row>

                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="form.validFrom" type="date" label="Valid from (optional)"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model="form.validTo" type="date" label="Valid until (optional)"></v-text-field>
                        </v-col>
                    </v-row>

                    <v-row>
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="form.maxTotalUses" type="number" min="1"
                                label="Max total uses (blank = unlimited)"></v-text-field>
                        </v-col>
                        <v-col cols="12" md="6">
                            <v-text-field v-model.number="form.maxUsesPerUser" type="number" min="1"
                                label="Max uses per user (blank = unlimited)"></v-text-field>
                        </v-col>
                    </v-row>

                    <v-switch v-model="form.isActive" label="Active" hide-details color="primary"></v-switch>
                </v-card-text>
                <v-card-actions>
                    <v-spacer></v-spacer>
                    <v-btn @click="dialog = false">Cancel</v-btn>
                    <v-btn color="primary" :loading="saving" @click="save">Save</v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-snackbar v-model="snackbar" :color="snackbarColor" :timeout="4000" location="top">{{ snackbarText }}</v-snackbar>
    </v-container>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import dayjs from 'dayjs'
import { CouponService, type Coupon, type UpsertCoupon } from '@/services/CouponService'
import { useConfirm } from '@/composables/useConfirm'

const service = new CouponService()
const confirm = useConfirm()

const rows = ref<Coupon[]>([])
const loading = ref(false)
const dialog = ref(false)
const editing = ref<Coupon | null>(null)
const saving = ref(false)

const discountKindOptions = [
    { value: 'percent', label: 'Percent off' },
    { value: 'amount',  label: 'Amount off' },
]
const scopeOptions = [
    { value: 'all',           label: 'Any purchase' },
    { value: 'pass',      label: 'Day passes only' },
    { value: 'event_ticket',  label: 'Event tickets only' },
    { value: 'season_pass',   label: 'Season passes only' },
]

const form = ref({
    code: '',
    description: '' as string | null,
    discountKind: 'percent' as 'percent' | 'amount',
    discountPercent: 10,
    discountDollars: 5,
    applicableScope: 'all' as 'all' | 'pass' | 'event_ticket' | 'season_pass',
    validFrom: '',
    validTo: '',
    maxTotalUses: null as number | null,
    maxUsesPerUser: null as number | null,
    isActive: true,
})

const snackbar = ref(false)
const snackbarText = ref('')
const snackbarColor = ref<'success' | 'error'>('success')

function flash(text: string, color: 'success' | 'error') {
    snackbarText.value = text
    snackbarColor.value = color
    snackbar.value = true
}

function formatDiscount(c: Coupon): string {
    return c.discountKind === 'percent'
        ? `${(c.discountValue / 100).toFixed(0)}% off`
        : `$${(c.discountValue / 100).toFixed(2)} off`
}
function scopeLabel(s: string): string {
    return scopeOptions.find(o => o.value === s)?.label ?? s
}
function formatDate(iso: string): string {
    return dayjs(iso).format('MMM D, YYYY')
}

async function load() {
    loading.value = true
    try {
        const r = await service.list()
        rows.value = r.data.data
    } catch (err: any) {
        flash(err.response?.data?.error || 'Failed to load coupons.', 'error')
    } finally {
        loading.value = false
    }
}

function openCreate() {
    editing.value = null
    form.value = {
        code: '', description: '',
        discountKind: 'percent', discountPercent: 10, discountDollars: 5,
        applicableScope: 'all',
        validFrom: '', validTo: '',
        maxTotalUses: null, maxUsesPerUser: null,
        isActive: true,
    }
    dialog.value = true
}

function openEdit(c: Coupon) {
    editing.value = c
    form.value = {
        code: c.code,
        description: c.description ?? '',
        discountKind: c.discountKind,
        discountPercent: c.discountKind === 'percent' ? Math.round(c.discountValue / 100) : 10,
        discountDollars: c.discountKind === 'amount' ? c.discountValue / 100 : 5,
        applicableScope: c.applicableScope,
        validFrom: c.validFromUtc ? dayjs(c.validFromUtc).format('YYYY-MM-DD') : '',
        validTo: c.validToUtc ? dayjs(c.validToUtc).format('YYYY-MM-DD') : '',
        maxTotalUses: c.maxTotalUses,
        maxUsesPerUser: c.maxUsesPerUser,
        isActive: c.isActive,
    }
    dialog.value = true
}

async function save() {
    if (!form.value.code.trim()) { flash('Code is required.', 'error'); return }
    const discountValue = form.value.discountKind === 'percent'
        ? Math.round(form.value.discountPercent * 100)   // bps
        : Math.round(form.value.discountDollars * 100)   // cents
    if (discountValue <= 0) { flash('Discount must be > 0.', 'error'); return }
    if (form.value.validFrom && form.value.validTo && form.value.validFrom > form.value.validTo) {
        flash('"Valid from" must be on or before "Valid until".', 'error'); return
    }

    const body: UpsertCoupon = {
        code: form.value.code.trim(),
        description: form.value.description?.trim() || null,
        discountKind: form.value.discountKind,
        discountValue,
        applicableScope: form.value.applicableScope,
        applicableEventId: null,
        validFromUtc: form.value.validFrom ? dayjs(form.value.validFrom).startOf('day').utc().toISOString() : null,
        validToUtc: form.value.validTo ? dayjs(form.value.validTo).endOf('day').utc().toISOString() : null,
        maxTotalUses: form.value.maxTotalUses || null,
        maxUsesPerUser: form.value.maxUsesPerUser || null,
        isActive: form.value.isActive,
    }
    try {
        saving.value = true
        if (editing.value) await service.update(editing.value.id, body)
        else await service.create(body)
        dialog.value = false
        await load()
        flash('Coupon saved.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Save failed.', 'error')
    } finally {
        saving.value = false
    }
}

async function remove(c: Coupon) {
    if (!await confirm({ message: `Delete coupon "${c.code}"? This also removes its redemption history.`, confirmText: 'Delete', confirmColor: 'error' })) return
    try {
        await service.delete(c.id)
        await load()
        flash('Coupon deleted.', 'success')
    } catch (err: any) {
        flash(err.response?.data?.error || 'Delete failed.', 'error')
    }
}

onMounted(load)
</script>
