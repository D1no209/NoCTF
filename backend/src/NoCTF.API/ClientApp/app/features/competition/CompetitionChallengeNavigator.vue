<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsChallengesChallengeResponse } from '~/api'

import { useCompetitionChallengeNavigator } from './useCompetitionChallengeNavigator'
import View from '~/components/views/competition/CompetitionChallengeNavigatorView.vue'

const props = defineProps<{
  competitionId: string
  selectedChallengeId?: string | null
}>()
const emit = defineEmits<{
  ready: [challengeId: string | null]
  select: [challengeId: string]
}>()
const state = bindViewState(useCompetitionChallengeNavigator(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
