<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsCompetitionsGameModeProtocol, NoCtfapiEndpointsCompetitionsScoreboardSlotResponse } from '~/api'

import { useScoreboardSlotStatus } from './useScoreboardSlotStatus'
import View from '~/components/views/leaderboard/ScoreboardSlotStatusView.vue'

const props = defineProps<{
  mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol | null
  slot: NoCtfapiEndpointsCompetitionsScoreboardSlotResponse
}>()
const state = bindViewState(useScoreboardSlotStatus(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
