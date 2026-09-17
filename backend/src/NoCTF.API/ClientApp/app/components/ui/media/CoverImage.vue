<script setup lang="ts">
import { ref, watch } from 'vue'
import { ImageIcon } from '@lucide/vue'
import { Skeleton } from '../skeleton'
import ScrollSurface from '../scroll-area/ScrollSurface.vue'

const props = withDefaults(defineProps<{
  src?: string | null
  alt: string
  pending?: boolean
  fallback?: string
  fit?: 'cover' | 'contain'
  aspectRatio?: number
  loading?: 'eager' | 'lazy'
  fetchpriority?: 'high' | 'low' | 'auto'
  width?: number
  height?: number
}>(), { fit: 'cover', loading: 'lazy', fetchpriority: 'auto' })
const failed = ref(false)
watch(() => props.src, () => { failed.value = false })
</script>

<template>
  <div data-slot="cover-image" class="relative isolate overflow-hidden bg-muted" :style="{ aspectRatio }" :aria-busy="pending">
    <Skeleton v-if="pending" class="pointer-events-none absolute inset-0 size-full rounded-none" />
    <img v-else-if="src && !failed" :src="src" :alt="alt" :style="{ objectFit: fit }" :loading="loading" :fetchpriority="fetchpriority" :width="width" :height="height" class="pointer-events-none absolute inset-0 size-full object-center" decoding="async" @error="failed = true">
    <div v-else-if="!$slots.default" class="pointer-events-none absolute inset-0 flex flex-col items-center justify-center gap-2 text-muted-foreground" role="img" :aria-label="fallback || alt">
      <ImageIcon class="size-8" aria-hidden="true" />
      <span v-if="fallback" class="text-sm">{{ fallback }}</span>
    </div>
    <div v-if="$slots.default && src && !failed" data-slot="cover-image-scrim" aria-hidden="true" />
    <div v-if="$slots.default" data-slot="cover-image-content" class="z-10" :class="aspectRatio ? 'absolute inset-0' : 'relative'">
      <ScrollSurface v-if="aspectRatio" axis="y" class="h-full"><slot /></ScrollSurface>
      <slot v-else />
    </div>
  </div>
</template>
