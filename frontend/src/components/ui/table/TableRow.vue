<script setup lang="ts">
import type { HTMLAttributes } from "vue"
import { cn } from "@/lib/utils"

const props = defineProps<{
  class?: HTMLAttributes["class"]
}>()
</script>

<template>
  <tr :class="cn('border-b transition-colors last:border-b-0 hover:bg-blue-50/60 data-[state=selected]:bg-muted', props.class)">
    <slot />
  </tr>
</template>
