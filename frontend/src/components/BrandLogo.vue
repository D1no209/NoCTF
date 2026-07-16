<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'

const props = defineProps<{
  class?: HTMLAttributes['class']
}>()
</script>

<template>
  <img
    src="/logo.png"
    alt="NoCTF"
    draggable="false"
    class="block w-auto max-h-full object-contain [image-rendering:pixelated]"
    :class="cn(props.class)"
  />
</template>
