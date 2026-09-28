<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '~/lib/utils'

const props = defineProps<{
  class?: HTMLAttributes['class']
  pinHorizontalScrollbar?: boolean
}>()
</script>

<template>
  <div
    data-slot="table-container"
    v-scroll-surface
    data-scroll-surface
    data-scroll-axis="x"
    :data-pin-horizontal-scrollbar="pinHorizontalScrollbar ? 'nearest-y' : undefined"
    class="relative w-full overflow-x-auto"
  >
    <table data-slot="table" :class="cn('w-full caption-bottom text-sm', props.class)">
      <slot />
    </table>
  </div>
</template>
