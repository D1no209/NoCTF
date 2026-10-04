
import { api } from '../../../lib/api'
import { message as describeMessage } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
import { markRaw, toRefs } from 'vue'


import type { NoCTFAPIEndpointsChallengesChallengeResponse, NoCTFAPIEndpointsCompetitionsCompetitionResponse, NoCTFAPIEndpointsRuntimeRuntimeTargetResponse } from '../../../api/models'
import FlagSubmitComponent from '../FlagSubmit.vue'
import RuntimeAccessUrlComponent from '../RuntimeAccessUrl.vue'
import RuntimeCardComponent from '../RuntimeCard.vue'

/** Owns state, effects and commands for AwdPanel. */
export function useAwdPanel(props: Readonly<{
  competition: NoCTFAPIEndpointsCompetitionsCompetitionResponse
  challenge: NoCTFAPIEndpointsChallengesChallengeResponse
  flagDockTarget?: string | null
  runtimeDockTarget?: string | null
}>,
emit: { (event: "submitted", ...args: []): void; (event: "remainingChanged", ...args: [remaining: number | null]): void }) {
  const targets = ref<NoCTFAPIEndpointsRuntimeRuntimeTargetResponse[]>([])

  const targetsError = ref<UiMessage | null>(null)

  const targetsLoaded = ref(false)

  onMounted(async () => {
    let error: unknown;
    const data = await api.api.v1.competitions.byCompetitionId(props.competition.id!).challenges.byCompetitionChallengeId(props.challenge.id!).targets.get().catch(cause => { error = cause; return undefined });
    targetsLoaded.value = true
    if (error || !data) {
      targetsError.value = parseApiError(error, describeMessage("challenges.awdPanel.error.loadAttackTargetFailed")).displayMessage
      return
    }
    targets.value = data.items ?? []
  })

  const FlagSubmit = markRaw(FlagSubmitComponent)

  const RuntimeAccessUrl = markRaw(RuntimeAccessUrlComponent)

  const RuntimeCard = markRaw(RuntimeCardComponent)

  return {
      ...toRefs(props),
      emit,
      targets,
      targetsError,
      targetsLoaded,
      FlagSubmit,
      RuntimeAccessUrl,
      RuntimeCard
    }
}

export type AwdPanelViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAwdPanel>>>
