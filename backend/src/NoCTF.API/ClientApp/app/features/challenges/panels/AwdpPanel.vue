<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCTFAPIEndpointsChallengesChallengeResponse, NoCTFAPIEndpointsCompetitionsCompetitionResponse } from '~/api/models'
import { useAwdpPanel } from './useAwdpPanel'
import View from '~/components/views/challenges/panels/AwdpPanelView.vue'

const props = defineProps<{
  competition: NoCTFAPIEndpointsCompetitionsCompetitionResponse
  challenge: NoCTFAPIEndpointsChallengesChallengeResponse
  flagDockTarget?: string
  runtimeDockTarget?: string
}>()
const emit = defineEmits<{ submitted: []; remainingChanged: [remaining: number | null] }>()
const state = bindViewState(useAwdpPanel(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
