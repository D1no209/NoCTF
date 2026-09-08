<script setup lang="ts">
import { toRefs } from 'vue'
import type { ScoreTrendChartViewState } from '~/features/leaderboard/useScoreTrendChart'

const viewProps = defineProps<{ state: ScoreTrendChartViewState }>()
const { setElRef, height } = toRefs(viewProps.state)
</script>

<template>
  <div :ref="setElRef" class="w-full text-foreground" :style="{ height }" />
</template>
