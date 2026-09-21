<script setup lang="ts">
import type { ScrollbarsBinding } from './scrollbars'
import { createScrollbars } from './scrollbars'

const props = withDefaults(defineProps<{
  as?: 'div' | 'section' | 'main' | 'nav' | 'ul' | 'ol' | 'pre' | 'code' | 'aside' | 'article'
  axis?: 'x' | 'y' | 'both'
  resetKey?: string | number | null
}>(), { as: 'div', axis: 'both', resetKey: null })
const target = ref<HTMLElement | null>(null)
let scrollbars: ScrollbarsBinding | undefined

onMounted(() => {
  if (target.value)
    scrollbars = createScrollbars(target.value, props.axis)
})
watch(() => props.axis, axis => scrollbars?.update(axis))
watch(() => props.resetKey, async (value, previous) => {
  if (value === previous) return
  await nextTick()
  if (!target.value) return
  if (props.axis !== 'x') target.value.scrollTop = 0
  if (props.axis !== 'y') target.value.scrollLeft = 0
  scrollbars?.update(props.axis)
})
onBeforeUnmount(() => scrollbars?.dispose())
</script>

<template>
  <component :is="as" ref="target" tabindex="0" data-slot="scroll-surface" data-scroll-surface :data-scroll-axis="axis"><slot /></component>
</template>
