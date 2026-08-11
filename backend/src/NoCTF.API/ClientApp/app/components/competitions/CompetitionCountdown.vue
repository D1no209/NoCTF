<script setup lang="ts">
import type { NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol } from '~/api'

const props = defineProps<{
  startTime?: string
  endTime?: string
  status?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol
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
  if (props.status === 'Finished') return translate("已结束")
  if (start !== null && now.value < start) return translate('距开始 {duration}', { duration: formatDuration(start - now.value) })
  if (end !== null && now.value < end) return translate('距结束 {duration}', { duration: formatDuration(end - now.value) })
  if (end !== null) return translate("已结束")
  return ''
})
</script>

<template>
  <span v-if="text" class="tabular-nums">{{ text }}</span>
</template>
