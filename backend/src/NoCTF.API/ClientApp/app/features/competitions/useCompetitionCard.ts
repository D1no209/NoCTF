import { markRaw, toRefs } from 'vue'

import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '../../api'
import CompetitionCountdownComponent from './CompetitionCountdown.vue'
import LifecycleBadgeComponent from './LifecycleBadge.vue'
import ModeBadgeComponent from './ModeBadge.vue'

/** Owns state, effects and commands for CompetitionCard. */
export function useCompetitionCard(props: Readonly<{ competition: NoCtfapiEndpointsCompetitionsCompetitionResponse }>) {
  const CompetitionCountdown = markRaw(CompetitionCountdownComponent)

  const LifecycleBadge = markRaw(LifecycleBadgeComponent)

  const ModeBadge = markRaw(ModeBadgeComponent)

  return {
      ...toRefs(props),
      CompetitionCountdown,
      LifecycleBadge,
      ModeBadge
    }
}

export type CompetitionCardViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionCard>>>
