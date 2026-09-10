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
      ? 'challenge-workspace'
      : 'xl:grid-cols-[minmax(0,1fr)_19rem]'"
  >
    <div v-if="showChallengeNavigator" class="min-w-0">
      <component :is="CompetitionChallengeNavigator"
        :competition-id="competitionId"
        :selected-challenge-id="selectedChallengeId"
        @ready="handleReady"
        @select="selectChallenge"
      />
    </div>

    <main id="challenge-workspace-detail" class="min-w-0" :class="!showChallengeNavigator && 'px-1 py-4 md:px-3 md:py-5'">
      <slot />
    </main>

    <aside class="grid min-h-0 content-start gap-4 sm:grid-cols-2 xl:sticky xl:top-20 xl:max-h-[calc(100svh-6rem)] xl:grid-cols-1 xl:grid-rows-[auto_minmax(0,1fr)]">
      <ScrollSurface axis="y" class="col-span-full h-full" :aria-label="$t('ui.competitions')">
      <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-1">
      <component :is="CompetitionWorkspaceNavigation" :groups="workspaceNavGroups" />
      <component :is="CompetitionBroadcastPanel"
        class="min-h-0 xl:static xl:flex xl:h-full xl:flex-col"
        :competition-id="competitionId"
        fill
      />
      </div>
      </ScrollSurface>
    </aside>
  </div>
</template>
