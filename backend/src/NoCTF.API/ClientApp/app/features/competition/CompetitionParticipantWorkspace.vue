<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useCompetitionParticipantWorkspace } from './useCompetitionParticipantWorkspace'
import View from '~/components/views/competition/CompetitionParticipantWorkspaceView.vue'

const props = withDefaults(defineProps<{
  competitionId: string
  selectedChallengeId?: string | null
  challengeSelectionMode?: 'inline' | 'navigate'
  showChallengeNavigator?: boolean
  contentScroll?: boolean
}>(), {
  selectedChallengeId: null,
  challengeSelectionMode: 'navigate',
  showChallengeNavigator: false,
  contentScroll: true,
})
const emit = defineEmits<{
  selectChallenge: [challengeId: string]
}>()
const state = bindViewState(useCompetitionParticipantWorkspace(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
