<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { TrendSeries } from '~/features/leaderboard/types'
import { useScoreTrendChart } from './useScoreTrendChart'
import View from '~/components/views/leaderboard/ScoreTrendChartView.vue'

const props = withDefaults(
  defineProps<{
    title?: string
    series: TrendSeries[]
    /** 时间轴范围(ISO);没有数据的队伍画一条 0 分平线。 */
    rangeStart?: string | null
    rangeEnd?: string | null
    height?: string
  }>(),
  { title: '', rangeStart: null, rangeEnd: null, height: '400px' },
)
const state = bindViewState(useScoreTrendChart(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
