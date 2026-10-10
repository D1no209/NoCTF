<script setup lang="ts">
import { onScopeDispose, ref, watch } from 'vue'
import { Empty, EmptyHeader, EmptyTitle } from '../empty'
const props = defineProps<{ stream: MediaStream | null; label: string; emptyLabel: string }>()
const video = ref<HTMLVideoElement | null>(null)
watch([video, () => props.stream], ([element, stream]) => {
  if (!element) return
  element.srcObject = stream
  if (stream) void element.play().catch(() => { /* Browser controls allow an explicit play retry. */ })
}, { flush: 'post' })
onScopeDispose(() => { if (video.value) video.value.srcObject = null })
</script>
<template>
  <div class="relative aspect-video min-w-0 rounded-xl bg-muted">
    <video v-show="stream" ref="video" class="h-full w-full rounded-xl object-contain" autoplay muted playsinline controls :aria-label="label" />
    <Empty v-if="!stream" class="h-full"><EmptyHeader><EmptyTitle class="text-sm">{{ emptyLabel }}</EmptyTitle></EmptyHeader></Empty>
  </div>
</template>
