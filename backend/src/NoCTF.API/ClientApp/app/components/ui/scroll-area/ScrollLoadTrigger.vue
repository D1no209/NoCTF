<script setup lang="ts">
const props = withDefaults(defineProps<{
  enabled?: boolean
  loading?: boolean
}>(), { enabled: true, loading: false })
const emit = defineEmits<{ load: [] }>()
const target = ref<HTMLElement | null>(null)
let observer: IntersectionObserver | undefined
let visible = false
let requested = false

function requestVisiblePage() {
  if (!visible || requested || !props.enabled || props.loading) return
  requested = true
  emit('load')
}

onMounted(() => {
  if (!target.value) return
  observer = new IntersectionObserver(([entry]) => {
    visible = entry?.isIntersecting ?? false
    if (!visible) requested = false
    requestVisiblePage()
  }, {
    root: target.value.parentElement?.closest('[data-scroll-surface]') ?? null,
    rootMargin: '0px 0px 64px 0px',
  })
  observer.observe(target.value)
})
watch(() => [props.enabled, props.loading], () => {
  // Appending a page moves the sentinel. Re-observe its new geometry instead
  // of using the previous intersection and accidentally draining every page.
  visible = false
  requested = false
  observer?.disconnect()
  if (target.value && props.enabled && !props.loading) observer?.observe(target.value)
}, { flush: 'post' })
onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <div ref="target" data-slot="scroll-load-trigger"><slot /></div>
</template>
