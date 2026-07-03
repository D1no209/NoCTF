<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'

const props = withDefaults(defineProps<{
  minWidth?: string
  dense?: boolean
  class?: HTMLAttributes['class']
}>(), {
  minWidth: '720px',
})
</script>

<template>
  <div :class="cn('noctf-table-shell', props.class)">
    <div class="noctf-scrollbar w-full overflow-x-auto">
      <div :style="{ minWidth }" :class="dense ? 'text-sm' : ''">
        <slot />
      </div>
    </div>
  </div>
</template>
