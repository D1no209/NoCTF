<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'

const props = withDefaults(defineProps<{
  variant?: 'default' | 'destructive' | 'warning' | 'success'
  class?: HTMLAttributes['class']
}>(), {
  variant: 'default',
})
</script>

<template>
  <div
    data-slot="alert"
    :class="cn(
      'relative w-full rounded-md border px-4 py-3 text-sm',
      variant === 'destructive' && 'border-destructive/35 bg-destructive/10 text-destructive',
      variant === 'warning' && 'border-[var(--semantic-warning-border)] bg-[var(--semantic-warning-soft)] text-[var(--semantic-warning)]',
      variant === 'success' && 'border-[var(--semantic-success-border)] bg-[var(--semantic-success-soft)] text-[var(--semantic-success)]',
      variant === 'default' && 'border-border bg-muted/40 text-foreground',
      props.class,
    )"
  >
    <slot />
  </div>
</template>
