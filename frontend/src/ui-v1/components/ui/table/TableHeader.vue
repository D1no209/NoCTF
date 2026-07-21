<script setup lang="ts">
import type { HTMLAttributes } from "vue"
import { cn } from "@/lib/utils"

const props = defineProps<{
  class?: HTMLAttributes["class"]
}>()
</script>

<template>
  <thead :class="cn('bg-muted/50 [&_tr]:border-b', props.class)">
    <slot />
  </thead>
</template>
