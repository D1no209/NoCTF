<script setup lang="ts">
const props = defineProps<{ value?: string | null }>()

const formatted = computed(() => {
  if (!props.value) return '—'
  const date = new Date(props.value)
  if (Number.isNaN(date.getTime())) return props.value
  return date.toLocaleString('zh-CN', { hour12: false })
})
</script>

<template>
  <time :datetime="value ?? undefined" :title="value ?? undefined" class="whitespace-nowrap">{{ formatted }}</time>
</template>
