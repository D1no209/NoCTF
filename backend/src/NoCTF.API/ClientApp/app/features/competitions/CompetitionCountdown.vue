<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol } from '~/api'
import { useCompetitionCountdown } from './useCompetitionCountdown'
import View from '~/components/views/competitions/CompetitionCountdownView.vue'

const props = defineProps<{
  startTime?: string
  endTime?: string
  status?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol
}>()
const state = bindViewState(useCompetitionCountdown(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
