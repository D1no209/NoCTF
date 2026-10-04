<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCTFAPIEndpointsCompetitionsGameModeProtocol, NoCTFAPIEndpointsCompetitionsScoreboardSlotResponse } from '~/api/models'

import { useScoreboardSlotStatus } from './useScoreboardSlotStatus'
import View from '~/components/views/leaderboard/ScoreboardSlotStatusView.vue'

const props = defineProps<{
  mode?: NoCTFAPIEndpointsCompetitionsGameModeProtocol | null
  slot: NoCTFAPIEndpointsCompetitionsScoreboardSlotResponse
}>()
const state = bindViewState(useScoreboardSlotStatus(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
