<script setup lang="ts">
import { ref } from 'vue'
import { useScrollDockMotion } from '~/motion/useScrollDockMotion'
import ScrollSurface from './ScrollSurface.vue'

defineProps<{ label: string }>()
const root = ref<HTMLElement | null>(null)
useScrollDockMotion(root)
</script>

<template>
  <div ref="root" class="h-full min-h-0">
    <ScrollSurface axis="y" data-scroll-dock-viewport :aria-label="label" class="h-full overflow-y-auto overscroll-contain">
      <div class="relative">
        <div v-if="$slots.backdrop" class="pointer-events-none absolute inset-x-0 top-0 overflow-hidden" :style="{ height: 'var(--scroll-dock-backdrop-height)' }" aria-hidden="true">
          <div :style="{ height: 'var(--scroll-dock-backdrop-natural-height)' }"><slot name="backdrop" /></div>
        </div>
        <div data-scroll-dock-intro class="relative"><slot name="intro" /></div>
        <div data-scroll-dock-header class="noctf-scroll-dock-header"><slot name="header" /></div>
        <div data-scroll-dock-body><slot /></div>
      </div>
    </ScrollSurface>
  </div>
</template>
