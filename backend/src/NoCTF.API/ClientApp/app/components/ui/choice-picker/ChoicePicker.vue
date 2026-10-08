<script setup lang="ts">
import { computed, ref } from 'vue'
import { ChevronsUpDown } from '@lucide/vue'
const props = defineProps<{ items: { value: string; label: string }[]; modelValue: string | null; label: string; searchLabel: string; emptyLabel: string; disabled?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
const open = ref(false), search = ref('')
const selected = computed(() => props.items.find(x => x.value === props.modelValue))
const filtered = computed(() => props.items.filter(x => x.label.toLocaleLowerCase().includes(search.value.trim().toLocaleLowerCase())))
function select(value: string) { emit('update:modelValue', value); open.value = false; search.value = '' }
</script>
<template>
  <Popover v-model:open="open"><PopoverTrigger as-child><Button type="button" role="combobox" variant="outline" class="w-full justify-between" :aria-label="label" :aria-expanded="open" :disabled="disabled"><span class="truncate">{{ selected?.label ?? label }}</span><ChevronsUpDown class="size-4 shrink-0" /></Button></PopoverTrigger><PopoverContent align="start" class="w-[var(--reka-popover-trigger-width)]"><Input v-model="search" :aria-label="searchLabel" :placeholder="searchLabel" /><ScrollSurface class="max-h-64"><SelectionList v-if="filtered.length" :items="filtered" :model-value="modelValue" :label="label" @update:model-value="select" /><Empty v-else><EmptyHeader><EmptyTitle>{{ emptyLabel }}</EmptyTitle></EmptyHeader></Empty></ScrollSurface></PopoverContent></Popover>
</template>
