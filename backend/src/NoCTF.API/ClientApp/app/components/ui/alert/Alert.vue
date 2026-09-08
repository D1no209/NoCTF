<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import type { AlertVariants } from '~/components/ui/alert'
import { cn } from '~/lib/utils'
import { alertVariants } from '~/components/ui/alert'

const props = defineProps<{
  class?: HTMLAttributes['class']
  variant?: AlertVariants['variant']
}>()
</script>

<template>
  <div
    data-slot="alert"
    :class="cn(alertVariants({ variant }), props.class)"
    role="alert"
  >
    <slot />
  </div>
</template>
