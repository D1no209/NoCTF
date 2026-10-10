import { markRaw, toRefs } from 'vue'

import { competitionWorkspaceNavigationKey } from '../app/workspace-nav'
import CompetitionBroadcastPanelComponent from './CompetitionBroadcastPanel.vue'
import CompetitionChallengeNavigatorComponent from './CompetitionChallengeNavigator.vue'
import CompetitionWorkspaceNavigationComponent from './CompetitionWorkspaceNavigation.vue'
import { competitionChallengePath } from '../../utils/app-routes'

/** Owns state, effects and commands for CompetitionParticipantWorkspace. */
export function useCompetitionParticipantWorkspace(props: Readonly<Omit<{
  competitionId: string
  selectedChallengeId?: string | null
  challengeSelectionMode?: 'inline' | 'navigate'
  showChallengeNavigator?: boolean
  showSupportingPanels?: boolean
  contentScroll?: boolean
}, "selectedChallengeId" | "challengeSelectionMode" | "showChallengeNavigator" | "contentScroll" | "showSupportingPanels"> & Required<Pick<{
  competitionId: string
  selectedChallengeId?: string | null
  challengeSelectionMode?: 'inline' | 'navigate'
  showChallengeNavigator?: boolean
  showSupportingPanels?: boolean
  contentScroll?: boolean
}, "selectedChallengeId" | "challengeSelectionMode" | "showChallengeNavigator" | "contentScroll" | "showSupportingPanels">>>,
emit: { (event: "selectChallenge", ...args: [challengeId: string]): void }) {
  const router = useRouter()

  const workspaceNavGroups = inject(competitionWorkspaceNavigationKey, computed(() => []))
  const competitionContext = inject(competitionContextKey)!
  const canReadBroadcasts = computed(() => competitionContext.canReadBroadcasts.value)

  async function selectChallenge(challengeId: string): Promise<void> {
    if (props.challengeSelectionMode === 'inline') {
      emit('selectChallenge', challengeId)
      return
    }
    await router.push(competitionChallengePath(props.competitionId, challengeId))
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

  const CompetitionBroadcastPanel = markRaw(CompetitionBroadcastPanelComponent)

  const CompetitionChallengeNavigator = markRaw(CompetitionChallengeNavigatorComponent)

  const CompetitionWorkspaceNavigation = markRaw(CompetitionWorkspaceNavigationComponent)

  return {
      ...toRefs(props),
      workspaceNavGroups,
      canReadBroadcasts,
      selectChallenge,
      handleReady,
      CompetitionBroadcastPanel,
      CompetitionChallengeNavigator,
      CompetitionWorkspaceNavigation
    }
}

export type CompetitionParticipantWorkspaceViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionParticipantWorkspace>>>
