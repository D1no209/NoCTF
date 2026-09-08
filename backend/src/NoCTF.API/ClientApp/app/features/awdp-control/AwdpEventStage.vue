<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { AwdpResolvedControlEvent } from '~/utils/awdp-control-screen'
import { useAwdpEventStage } from './useAwdpEventStage'
import View from '~/components/views/awdp-control/AwdpEventStageView.vue'

const props = defineProps<{
  event: AwdpResolvedControlEvent | null
  queueLength: number
  progress: number
}>()
const state = bindViewState(useAwdpEventStage(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
