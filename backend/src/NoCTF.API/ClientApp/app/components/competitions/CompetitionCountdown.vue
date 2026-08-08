<script setup lang="ts">
const props = defineProps<{
  startTime?: string
  endTime?: string
  status?: string
}>()

const now = ref(Date.now())
let timer: ReturnType<typeof setInterval> | undefined

onMounted(() => {
  timer = setInterval(() => {
    now.value = Date.now()
  }, 1000)
})

onUnmounted(() => {
  if (timer) clearInterval(timer)
})

const text = computed(() => {
  const start = props.startTime ? new Date(props.startTime).getTime() : null
  const end = props.endTime ? new Date(props.endTime).getTime() : null
  if (props.status === CompetitionStatus.Finished) return '已结束'
  if (start !== null && now.value < start) return `距开始 ${formatDuration(start - now.value)}`
  if (end !== null && now.value < end) return `距结束 ${formatDuration(end - now.value)}`
  if (end !== null) return '已结束'
  return ''
})
</script>

<template>
  <span v-if="text" class="text-sm tabular-nums">{{ text }}</span>
</template>
