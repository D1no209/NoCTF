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
        'flex flex-col gap-4 rounded-lg border border-border/90 bg-card py-4 text-card-foreground shadow-none',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
