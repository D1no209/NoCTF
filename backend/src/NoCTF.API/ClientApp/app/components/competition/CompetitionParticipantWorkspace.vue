<script setup lang="ts">
import { competitionWorkspaceNavigationKey } from '~/components/app/workspace-nav'

const props = withDefaults(defineProps<{
  competitionId: string
  selectedChallengeId?: string | null
  challengeSelectionMode?: 'inline' | 'navigate'
  showChallengeNavigator?: boolean
}>(), {
  selectedChallengeId: null,
  challengeSelectionMode: 'navigate',
  showChallengeNavigator: false,
})

const emit = defineEmits<{
  selectChallenge: [challengeId: string]
}>()

const router = useRouter()
const workspaceNavGroups = inject(competitionWorkspaceNavigationKey, computed(() => []))

async function selectChallenge(challengeId: string): Promise<void> {
  if (props.challengeSelectionMode === 'inline') {
    emit('selectChallenge', challengeId)
    return
  }
  await router.push({
    path: `/competitions/${props.competitionId}/challenges`,
    query: { challenge: challengeId },
  })
}

function handleReady(challengeId: string | null): void {
  if (
    props.challengeSelectionMode === 'inline'
    && challengeId
    && challengeId !== props.selectedChallengeId
  ) {
    emit('selectChallenge', challengeId)
  }
}
</script>

<template>
  <div
    class="grid min-h-[calc(100svh-12rem)] items-stretch gap-4"
    :class="showChallengeNavigator
      ? 'xl:grid-cols-[15rem_minmax(0,1fr)_19rem]'
      : 'xl:grid-cols-[minmax(0,1fr)_19rem]'"
  >
    <CompetitionChallengeNavigator
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
      <CompetitionWorkspaceNavigation :groups="workspaceNavGroups" />
      <CompetitionBroadcastPanel
        class="min-h-0 xl:static xl:flex xl:h-full xl:flex-col"
        :competition-id="competitionId"
        fill
      />
    </aside>
  </div>
</template>
