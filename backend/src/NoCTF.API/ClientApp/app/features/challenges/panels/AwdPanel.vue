<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsChallengesChallengeResponse, NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'
import { useAwdPanel } from './useAwdPanel'
import View from '~/components/views/challenges/panels/AwdPanelView.vue'

const props = defineProps<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
  flagDockTarget?: string
  runtimeDockTarget?: string
}>()
const emit = defineEmits<{ submitted: []; remainingChanged: [remaining: number | null] }>()
const state = bindViewState(useAwdPanel(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
