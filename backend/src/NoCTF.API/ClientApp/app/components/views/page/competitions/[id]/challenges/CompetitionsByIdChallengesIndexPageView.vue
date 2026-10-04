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
    <Card v-if="selectedChallengeId" class="challenge-detail-card overflow-hidden gap-0 py-0">
      <div id="challenge-flag-dock" data-slot="challenge-flag-dock" />
      <MotionSwap :identity="selectedChallengeId" preset="film-up">
        <ScrollSurface axis="y" class="h-full" :aria-label="$t('common.label.challenge.pageTitle')">
        <div class="p-5 lg:p-6">
          <component :is="CompetitionChallengeDetail"
            :key="`${competitionId}:${selectedChallengeId}`"
            :competition-id="competitionId"
            :competition-challenge-id="selectedChallengeId"
            flag-dock-target="#challenge-flag-dock"
          />
        </div>
        </ScrollSurface>
      </MotionSwap>
    </Card>
    <Empty v-else class="h-full min-h-80 border-0">
      <EmptyHeader>
        <EmptyTitle>{{ $t('challenges.competitionsBy.description.selectChallengeViewDetails') }}</EmptyTitle>
      </EmptyHeader>
    </Empty>
  </component>
</template>
