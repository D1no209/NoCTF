<script setup lang="ts">
import { computed, onScopeDispose, ref, watch } from 'vue'
import { Empty, EmptyHeader, EmptyTitle } from '../empty'
import { Button } from '../button'
const props = defineProps<{ source: string | null; label: string; emptyLabel: string; failedLabel: string; retryLabel: string }>()
const emit = defineEmits<{ retry: []; failed: [] }>()
const video = ref<HTMLVideoElement | null>(null), failed = ref(false)
const safeSource = computed(() => {
  if (!props.source) return null
  try { const url = new URL(props.source, window.location.origin); return url.origin === window.location.origin && ['http:', 'https:'].includes(url.protocol) ? url.href : null }
  catch { return null }
})
function clear() { if (video.value) { video.value.pause(); video.value.removeAttribute('src'); video.value.load() } }
watch([video, safeSource], ([element, source]) => {
  clear(); failed.value = false
  if (element && source) { element.src = source; element.load() }
}, { flush: 'post' })
function failure() { failed.value = true; clear(); emit('failed') }
onScopeDispose(clear)
</script>
<template>
  <div class="relative aspect-video min-w-0 rounded-xl bg-muted">
    <video v-show="safeSource && !failed" ref="video" class="h-full w-full rounded-xl object-contain" controls playsinline preload="metadata" :aria-label="label" @error="failure" />
    <Empty v-if="!safeSource || failed" class="h-full"><EmptyHeader><EmptyTitle class="text-sm">{{ failed ? failedLabel : emptyLabel }}</EmptyTitle></EmptyHeader><Button v-if="failed" variant="outline" @click="emit('retry')">{{ retryLabel }}</Button></Empty>
  </div>
</template>
