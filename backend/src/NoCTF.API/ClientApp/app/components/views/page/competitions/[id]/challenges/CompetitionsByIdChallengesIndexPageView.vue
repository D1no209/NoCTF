<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdChallengesIndexPageViewState } from '~/features/routes/competitions/[id]/challenges/useCompetitionsByIdChallengesIndexPage'

const viewProps = defineProps<{ state: CompetitionsByIdChallengesIndexPageViewState }>()
const { competitionId, selectedChallengeId, selectChallenge, CompetitionChallengeDetail, CompetitionParticipantWorkspace } = toRefs(viewProps.state)
</script>

<template>
  <component :is="CompetitionParticipantWorkspace"
    :competition-id="competitionId"
    :selected-challenge-id="selectedChallengeId"
    challenge-selection-mode="inline"
    show-challenge-navigator
    @select-challenge="selectChallenge"
  >
    <component :is="CompetitionChallengeDetail"
      v-if="selectedChallengeId"
      :competition-id="competitionId"
      :competition-challenge-id="selectedChallengeId"
    />
    <Empty v-else class="h-full min-h-80 border-0">
      <EmptyHeader>
        <EmptyTitle>{{ $t('ui.selectAChallengeToViewDetails') }}</EmptyTitle>
      </EmptyHeader>
    </Empty>
  </component>
</template>
