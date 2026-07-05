<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'

const props = defineProps<{
  class?: HTMLAttributes['class']
}>()
</script>

<template>
  <tr
    :class="
      cn(
        'border-b transition-colors duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] last:border-b-0 hover:bg-info-muted/45 data-[state=selected]:bg-muted',
        props.class,
      )
    "
  >
    <slot />
  </tr>
</template>
