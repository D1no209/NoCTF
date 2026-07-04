<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'

const props = defineProps<{
  class?: HTMLAttributes['class']
}>()
</script>

<template>
  <div
    data-slot="card"
    :class="
      cn(
        'bg-card text-card-foreground flex flex-col gap-6 rounded-xl border border-slate-900/10 py-6 shadow-[0_12px_36px_rgb(15_23_42/0.045)]',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
