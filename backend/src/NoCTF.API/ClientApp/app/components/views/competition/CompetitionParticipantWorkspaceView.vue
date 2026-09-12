<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionParticipantWorkspaceViewState } from '~/features/competition/useCompetitionParticipantWorkspace'

const viewProps = defineProps<{ state: CompetitionParticipantWorkspaceViewState }>()
const { workspaceNavGroups, selectChallenge, handleReady, CompetitionBroadcastPanel, CompetitionChallengeNavigator, CompetitionWorkspaceNavigation, competitionId, selectedChallengeId, showChallengeNavigator, contentScroll } = toRefs(viewProps.state)
</script>

<template>
  <div
    class="competition-participant-workspace grid min-h-0 items-stretch gap-4"
    :class="showChallengeNavigator
      ? 'challenge-workspace'
      : 'xl:grid-cols-[minmax(0,1fr)_clamp(16rem,20vw,21rem)]'"
  >
    <div v-if="showChallengeNavigator" class="min-w-0">
      <component :is="CompetitionChallengeNavigator"
        :competition-id="competitionId"
        :selected-challenge-id="selectedChallengeId"
        @ready="handleReady"
        @select="selectChallenge"
      />
    </div>

    <main id="challenge-workspace-detail" class="min-h-0 min-w-0">
      <slot v-if="showChallengeNavigator || !contentScroll" />
      <ScrollSurface v-else axis="y" class="h-full" :aria-label="$t('ui.competitions')">
        <div class="px-1 py-4 md:px-3 md:py-5">
          <slot />
        </div>
      </ScrollSurface>
    </main>

    <aside
      class="grid h-full min-h-0 grid-cols-2 grid-rows-[minmax(0,1fr)] gap-4"
      :class="showChallengeNavigator
        ? 'min-[1440px]:grid-cols-1 min-[1440px]:grid-rows-[fit-content(50%)_minmax(0,1fr)]'
        : 'xl:grid-cols-1 xl:grid-rows-[fit-content(50%)_minmax(0,1fr)]'"
    >
      <component :is="CompetitionWorkspaceNavigation"
        class="h-full min-h-0"
        :class="showChallengeNavigator ? 'min-[1440px]:h-auto' : 'xl:h-auto'"
        :groups="workspaceNavGroups"
      />
      <component :is="CompetitionBroadcastPanel"
        class="h-full min-h-0 min-[1440px]:static min-[1440px]:flex min-[1440px]:flex-col"
        :competition-id="competitionId"
        fill
      />
    </aside>
  </div>
</template>
