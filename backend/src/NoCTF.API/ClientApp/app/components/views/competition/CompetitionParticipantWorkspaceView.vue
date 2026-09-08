<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionParticipantWorkspaceViewState } from '~/features/competition/useCompetitionParticipantWorkspace'

const viewProps = defineProps<{ state: CompetitionParticipantWorkspaceViewState }>()
const { workspaceNavGroups, selectChallenge, handleReady, CompetitionBroadcastPanel, CompetitionChallengeNavigator, CompetitionWorkspaceNavigation, competitionId, selectedChallengeId, showChallengeNavigator } = toRefs(viewProps.state)
</script>

<template>
  <div
    class="grid min-h-[calc(100svh-12rem)] items-stretch gap-4"
    :class="showChallengeNavigator
      ? 'xl:grid-cols-[15rem_minmax(0,1fr)_19rem]'
      : 'xl:grid-cols-[minmax(0,1fr)_19rem]'"
  >
    <component :is="CompetitionChallengeNavigator"
      v-if="showChallengeNavigator"
      :competition-id="competitionId"
      :selected-challenge-id="selectedChallengeId"
      @ready="handleReady"
      @select="selectChallenge"
    />

    <main class="min-w-0 border-y px-1 py-4 md:px-3 md:py-5">
      <slot />
    </main>

    <aside class="grid min-h-0 content-start gap-4 sm:grid-cols-2 xl:sticky xl:top-20 xl:max-h-[calc(100svh-6rem)] xl:grid-cols-1 xl:grid-rows-[auto_minmax(0,1fr)]">
      <component :is="CompetitionWorkspaceNavigation" :groups="workspaceNavGroups" />
      <component :is="CompetitionBroadcastPanel"
        class="min-h-0 xl:static xl:flex xl:h-full xl:flex-col"
        :competition-id="competitionId"
        fill
      />
    </aside>
  </div>
</template>
