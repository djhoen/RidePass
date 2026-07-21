<template>
    <!-- Store-credit tender lookup, shared by the POS screens (shop register, F&B POS). The
         cashier types the customer's email or phone, sees the name + balance to verify the
         person, and the parent gets the account via v-model to attach to the sale. -->
    <div>
        <div v-if="!modelValue" class="d-flex ga-2 align-center">
            <v-text-field :model-value="query" @update:model-value="query = $event"
                :label="label" density="compact" hide-details @keyup.enter="lookup"></v-text-field>
            <v-btn variant="tonal" :loading="loading" @click="lookup">Find</v-btn>
        </div>
        <div v-else class="d-flex align-center ga-2 text-body-2">
            <v-icon size="18" color="success">mdi-wallet-giftcard</v-icon>
            <span>{{ modelValue.displayName || 'Store credit' }}:
                <strong>{{ money(modelValue.balanceCents) }}</strong> (applied up to the total)</span>
            <v-spacer></v-spacer>
            <v-btn icon="mdi-close" size="x-small" variant="text" @click="clear"></v-btn>
        </div>
        <div v-if="error" class="text-error text-caption mt-1">{{ error }}</div>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { CreditService, type CreditLookupResult } from '@/services/CreditService'

const props = withDefaults(defineProps<{
    modelValue: CreditLookupResult | null
    label?: string
}>(), { label: 'Store credit (email or phone)' })

const emit = defineEmits<{ (e: 'update:modelValue', v: CreditLookupResult | null): void }>()

const service = new CreditService()
const query = ref('')
const loading = ref(false)
const error = ref('')

function money(cents: number): string { return `$${(cents / 100).toFixed(2)}` }

async function lookup() {
    error.value = ''
    const q = query.value.trim()
    if (!q) { error.value = "Enter the customer's email or phone."; return }
    loading.value = true
    try {
        const found = (await service.lookup(q)).data.data
        if (found.balanceCents <= 0) {
            error.value = `${found.displayName || 'That customer'} has no credit available right now.`
        } else {
            emit('update:modelValue', found)
        }
    } catch (e: any) {
        error.value = e.response?.data?.error || 'No store credit account matches that email or phone.'
    } finally { loading.value = false }
}

function clear() {
    emit('update:modelValue', null)
    query.value = ''
    error.value = ''
}
</script>
