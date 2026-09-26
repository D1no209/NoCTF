<script setup lang="ts" generic="T extends { value: string; label: string }">
import FloatingSidebar from './FloatingSidebar.vue'
import WaveSelectionList from '../selection-list/WaveSelectionList.vue'
import { Skeleton } from '../skeleton'
import { Empty, EmptyHeader, EmptyTitle } from '../empty'

defineProps<{ items: T[]; groups?: { value: string; label: string; items: T[] }[]; modelValue: string | null; label: string; loading?: boolean; loadingLabel: string; emptyLabel: string; controls?: string; compact?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
</script>

<template>
  <FloatingSidebar :label="label">
    <slot name="header" />
    <slot name="feedback" />
    <div v-if="loading" class="flex flex-col gap-3 pr-10" :aria-label="loadingLabel"><Skeleton v-for="i in 3" :key="i" class="h-24 w-full" /></div>
    <WaveSelectionList v-else-if="items.length" :items="items" :groups="groups" :model-value="modelValue" :label="label" :controls="controls" :compact="compact" @update:model-value="emit('update:modelValue', $event)">
      <template #default="{ item }"><slot name="item" :item="item"><span>{{ item.label }}</span></slot></template>
      <template #group="{ group }"><slot name="group" :group="group">{{ group.label }}</slot></template>
    </WaveSelectionList>
    <Empty v-else class="pr-10 py-8"><EmptyHeader><EmptyTitle>{{ emptyLabel }}</EmptyTitle></EmptyHeader></Empty>
    <slot name="footer" />
  </FloatingSidebar>
</template>
