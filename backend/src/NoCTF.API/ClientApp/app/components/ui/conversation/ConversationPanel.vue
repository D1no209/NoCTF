<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import { Card } from '../card'
import ScrollSurface from '../scroll-area/ScrollSurface.vue'

const props = defineProps<{ historyLabel: string; identity?: string; itemCount?: number }>()
const history = ref<InstanceType<typeof ScrollSurface> | null>(null)
let atEnd = true
function trackScroll(event: Event) {
  const viewport = event.target as HTMLElement
  atEnd = viewport.scrollHeight - viewport.scrollTop - viewport.clientHeight < 48
}
watch([() => props.identity, () => props.itemCount], async ([identity], previous) => {
  if (identity === previous?.[0] && !atEnd) return
  await nextTick()
  const root = history.value?.$el as HTMLElement | undefined
  const viewport = root?.querySelector<HTMLElement>('[data-overlayscrollbars-viewport]') ?? root
  if (viewport) viewport.scrollTop = viewport.scrollHeight
}, { immediate: true, flush: 'post' })
</script>

<template>
  <Card class="conversation-panel flex h-full min-h-0 flex-col gap-0 overflow-hidden py-0">
    <div class="shrink-0 px-5 py-4"><slot name="header" /></div>
    <div data-slot="conversation-body">
      <ScrollSurface ref="history" axis="y" class="min-h-0" :aria-label="historyLabel" @scroll.capture="trackScroll">
        <ul class="flex min-h-full flex-col gap-4 p-5"><slot name="history" /></ul>
      </ScrollSurface>
      <div data-slot="conversation-composer"><slot name="composer" /></div>
    </div>
  </Card>
</template>
