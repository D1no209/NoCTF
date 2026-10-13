<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
const props = defineProps<{ label: string; mount: (element: HTMLElement) => () => void }>()
const root = ref<HTMLElement | null>(null)
let dispose: (() => void) | undefined
onMounted(() => { if (root.value) dispose = props.mount(root.value) })
onBeforeUnmount(() => dispose?.())
</script>

<template>
  <div ref="root" class="relative h-full w-full" role="img" :aria-label="label" />
</template>
