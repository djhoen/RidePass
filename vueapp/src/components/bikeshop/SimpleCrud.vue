<template>
    <div>
        <div class="d-flex mb-3">
            <v-spacer></v-spacer>
            <v-btn color="primary" prepend-icon="mdi-plus" @click="$emit('new')">New {{ label }}</v-btn>
        </div>
        <v-card v-if="rows.length === 0" class="pa-6 text-center text-medium-emphasis">
            No {{ label }} entries yet.
        </v-card>
        <v-table v-else density="compact">
            <tbody>
                <tr v-for="row in rows" :key="row.id">
                    <slot name="cols" :row="row"></slot>
                    <td class="text-right">
                        <v-chip v-if="row.isActive === false" size="x-small" color="warning" class="mr-2">Inactive</v-chip>
                        <v-btn size="x-small" variant="text" icon="mdi-pencil" @click="$emit('edit', row)"></v-btn>
                    </td>
                </tr>
            </tbody>
        </v-table>
    </div>
</template>

<script setup lang="ts">
// rows are heterogeneous (categories / suppliers / tax); the parent's `cols` slot reads the
// concrete fields, so expose them loosely here.
defineProps<{ label: string; rows: any[] }>()
defineEmits<{ (e: 'new'): void; (e: 'edit', row: any): void }>()
</script>
