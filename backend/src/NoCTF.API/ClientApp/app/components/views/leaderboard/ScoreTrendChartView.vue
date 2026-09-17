<script setup lang="ts">
import { toRefs } from 'vue'
import type { ScoreTrendChartViewState } from '~/features/leaderboard/useScoreTrendChart'

const viewProps = defineProps<{ state: ScoreTrendChartViewState }>()
const { setElRef, height, releaseWheelToScrollSurface } = toRefs(viewProps.state)
</script>

<template>
  <div
    :ref="setElRef"
    class="w-full text-foreground"
    :style="{ height }"
    @wheel.capture="releaseWheelToScrollSurface"
  />
</template>
