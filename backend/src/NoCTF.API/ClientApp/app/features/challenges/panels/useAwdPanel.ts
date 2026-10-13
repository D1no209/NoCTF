import { message as describeMessage } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
import { markRaw, toRefs, ref, watch, onMounted, onBeforeUnmount } from 'vue'
import { watchCompetition } from '../../../composables/useCompetitionHub'

import { listRuntimeTargetsEndpoint } from '../../../api'
import type { NoCtfapiEndpointsChallengesChallengeResponse, NoCtfapiEndpointsCompetitionsCompetitionResponse, NoCtfapiEndpointsRuntimeRuntimeTargetResponse } from '../../../api'
import FlagSubmitComponent from '../FlagSubmit.vue'
import AwdPublicRulesComponent from '../awd/AwdPublicRules.vue'
import AwdAttackInfoComponent from '../awd/AwdAttackInfo.vue'
import RuntimeAccessUrlComponent from '../RuntimeAccessUrl.vue'
import RuntimeCardComponent from '../RuntimeCard.vue'

/** Owns state, effects and commands for AwdPanel. */
export function useAwdPanel(props: Readonly<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
  flagDockTarget?: string
  runtimeDockTarget?: string
}>,
emit: { (event: "submitted", ...args: []): void; (event: "remainingChanged", ...args: [remaining: number | null]): void }) {
  const targets = ref<NoCtfapiEndpointsRuntimeRuntimeTargetResponse[]>([])
  const attackTarget = ref<NoCtfapiEndpointsRuntimeRuntimeTargetResponse | null>(null)
  function openAttackInfo(target: NoCtfapiEndpointsRuntimeRuntimeTargetResponse) { attackTarget.value = target }
  function closeAttackInfo() { attackTarget.value = null }

  const targetsError = ref<UiMessage | null>(null)

  const targetsLoaded = ref(false)

  let generation = 0, disposed = false
  let abort: AbortController | undefined, stopHub: (() => void) | undefined, timer: ReturnType<typeof setInterval> | undefined
  async function loadTargets() {
    const ticket = ++generation
    abort?.abort(); abort = new AbortController()
    try {
      const { data, error } = await listRuntimeTargetsEndpoint({
        path: { competitionId: props.competition.id!, competitionChallengeId: props.challenge.id! }, signal: abort.signal,
      })
      if (disposed || ticket !== generation) return
      targetsLoaded.value = true
      if (error || !data) {
        targets.value = []; attackTarget.value = null
        targetsError.value = parseApiError(error, describeMessage('challenges.awdPanel.error.loadAttackTargetFailed')).displayMessage
        return
      }
      targetsError.value = null; targets.value = data.items ?? []
      if (attackTarget.value && !targets.value.some(target => target.teamId === attackTarget.value?.teamId)) attackTarget.value = null
    } catch {
      if (!disposed && ticket === generation) {
        targetsLoaded.value = true; targets.value = []; attackTarget.value = null
        targetsError.value = describeMessage('challenges.awdPanel.error.loadAttackTargetFailed')
      }
    }
  }
  function subscribe() {
    stopHub?.()
    stopHub = watchCompetition(props.competition.id!, { scoreboardUpdated: loadTargets,
      competitionLifecycleChanged: loadTargets, competitionEventChanged: loadTargets, onReconnected: loadTargets })
  }
  watch(() => [props.competition.id, props.challenge.id], () => {
    targets.value = []; attackTarget.value = null; targetsError.value = null; targetsLoaded.value = false
    void loadTargets(); subscribe()
  })
  const refresh = () => { if (!document.hidden) void loadTargets() }
  onMounted(() => { void loadTargets(); subscribe(); timer = setInterval(refresh, 30000); document.addEventListener('visibilitychange', refresh) })
  onBeforeUnmount(() => { disposed = true; generation++; abort?.abort(); stopHub?.(); clearInterval(timer); document.removeEventListener('visibilitychange', refresh) })

  const FlagSubmit = markRaw(FlagSubmitComponent)

  const RuntimeAccessUrl = markRaw(RuntimeAccessUrlComponent)

  const RuntimeCard = markRaw(RuntimeCardComponent)

  return {
      ...toRefs(props),
      emit,
      targets,
      attackTarget,
      openAttackInfo,
      closeAttackInfo,
      AwdAttackInfo: markRaw(AwdAttackInfoComponent),
      targetsError,
      targetsLoaded,
      FlagSubmit,
      AwdPublicRules: markRaw(AwdPublicRulesComponent),
      RuntimeAccessUrl,
      RuntimeCard
    }
}

export type AwdPanelViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAwdPanel>>>
