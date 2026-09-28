<script setup lang="ts">
import type { ScrollbarsBinding } from './scrollbars'
import { createScrollbars } from './scrollbars'

const props = withDefaults(defineProps<{
  as?: 'div' | 'section' | 'main' | 'nav' | 'ul' | 'ol' | 'pre' | 'code' | 'aside' | 'article'
  axis?: 'x' | 'y' | 'both'
  enabled?: boolean
  resetKey?: string | number | null
}>(), { as: 'div', axis: 'both', enabled: true, resetKey: null })
const target = ref<HTMLElement | null>(null)
let scrollbars: ScrollbarsBinding | undefined

function updateEnabled(enabled: boolean) {
  if (!target.value) return
  if (enabled) scrollbars ??= createScrollbars(target.value, props.axis)
  else {
    scrollbars?.dispose()
    scrollbars = undefined
  }
}

onMounted(() => updateEnabled(props.enabled))
watch(() => props.enabled, updateEnabled, { flush: 'post' })
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
  <component :is="as" ref="target" :tabindex="enabled ? 0 : undefined" data-slot="scroll-surface"
    :data-scroll-surface="enabled ? '' : undefined" :data-scroll-axis="enabled ? axis : undefined"><slot /></component>
</template>
