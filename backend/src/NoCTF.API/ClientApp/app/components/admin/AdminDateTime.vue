<script setup lang="ts">
const props = defineProps<{ value?: string | null }>()

const formatted = computed(() => {
  if (!props.value) return '—'
  const date = new Date(props.value)
  if (Number.isNaN(date.getTime())) return props.value
  // 后端以 epoch(1970)表示“从未更新”,不展示无意义的时间。
  if (date.getTime() <= 0) return '—'
  return date.toLocaleString(localeTag(), { hour12: false })
})
</script>

<template>
  <time :datetime="value ?? undefined" :title="value ?? undefined" class="whitespace-nowrap">{{ formatted }}</time>
</template>
