<script setup lang="ts">
import { inject } from 'vue'

defineProps<{
  value: string
}>()

const activeValue = inject('tabs-value', { value: undefined as string | undefined })
const setValue = inject<(value: string) => void>('tabs-set-value', () => {})
</script>

<template>
  <button
    type="button"
    class="inline-flex items-center justify-center whitespace-nowrap rounded-sm px-3 py-1 text-sm font-medium transition-all disabled:pointer-events-none disabled:opacity-50"
    :class="activeValue?.value === value ? 'bg-background text-foreground shadow-sm' : 'hover:text-foreground'"
    @click="setValue(value)"
  >
    <slot />
  </button>
</template>
