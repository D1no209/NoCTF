<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionParticipantWorkspaceViewState } from '~/features/competition/useCompetitionParticipantWorkspace'

const viewProps = defineProps<{ state: CompetitionParticipantWorkspaceViewState }>()
const { workspaceNavGroups, selectChallenge, handleReady, CompetitionBroadcastPanel, CompetitionChallengeNavigator, CompetitionWorkspaceNavigation, competitionId, selectedChallengeId, showChallengeNavigator, contentScroll } = toRefs(viewProps.state)
</script>

<template>
  <div
    class="competition-participant-workspace grid min-h-[calc(100svh-12rem)] items-stretch gap-4"
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

    <main id="challenge-workspace-detail" class="min-h-0 min-w-0">
      <slot v-if="showChallengeNavigator || !contentScroll" />
      <ScrollSurface v-else axis="y" class="h-full" :aria-label="$t('ui.competitions')">
        <div class="px-1 py-4 md:px-3 md:py-5">
          <slot />
        </div>
      </ScrollSurface>
    </main>

    <aside
      class="grid h-full min-h-0 grid-cols-2 content-start gap-4"
      :class="showChallengeNavigator
        ? 'min-[1440px]:sticky min-[1440px]:top-20 min-[1440px]:max-h-[calc(100svh-6rem)] min-[1440px]:grid-cols-1 min-[1440px]:grid-rows-[auto_18rem]'
        : 'xl:sticky xl:top-20 xl:max-h-[calc(100svh-6rem)] xl:grid-cols-1 xl:grid-rows-[auto_18rem]'"
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
