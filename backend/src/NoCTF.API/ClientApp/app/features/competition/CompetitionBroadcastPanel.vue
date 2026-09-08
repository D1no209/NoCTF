<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse } from '~/api'

import { useCompetitionBroadcastPanel } from './useCompetitionBroadcastPanel'
import View from '~/components/views/competition/CompetitionBroadcastPanelView.vue'

const props = withDefaults(defineProps<{
  competitionId: string
  fill?: boolean
}>(), {
  fill: false,
})
const state = bindViewState(useCompetitionBroadcastPanel(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
