import { markRaw, toRefs } from 'vue'

import { listRuntimeTargetsEndpoint } from '../../../api'
import { publicGatewayFailure } from '../../../utils/public-gateway'
import type { NoCtfapiEndpointsChallengesChallengeResponse, NoCtfapiEndpointsCompetitionsCompetitionResponse, NoCtfapiEndpointsRuntimeRuntimeTargetResponse } from '../../../api'
import FlagSubmitComponent from '../FlagSubmit.vue'
import RuntimeAccessUrlComponent from '../RuntimeAccessUrl.vue'
import RuntimeCardComponent from '../RuntimeCard.vue'

type Events = { submitted: [] }

/** Owns state, effects and commands for AwdPanel. */
export function useAwdPanel(props: Readonly<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
}>,
emit: { (event: "submitted", ...args: []): void }) {
  const targets = ref<NoCtfapiEndpointsRuntimeRuntimeTargetResponse[]>([])

  const targetsError = ref<string | null>(null)

  const targetsLoaded = ref(false)

  onMounted(async () => {
    const { data, error } = await listRuntimeTargetsEndpoint({
      path: { competitionId: props.competition.id!, competitionChallengeId: props.challenge.id! },
    })
    targetsLoaded.value = true
    if (error || !data) {
      targetsError.value = parseApiError(error, translate("ui.failedToLoadAttackTarget")).message
      return
    }
    targets.value = data.items ?? []
    if (data.publicAccessFailure) targetsError.value = publicGatewayFailure(data.publicAccessFailure)
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
