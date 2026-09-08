import { toRefs } from 'vue'

import type { NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol } from '../../api'
import { competitionStatusLabel } from '../../utils/labels'

/** Owns state, effects and commands for LifecycleBadge. */
export function useLifecycleBadge(props: Readonly<{ status?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol }>) {
  const label = computed(() => competitionStatusLabel(props.status))

  const variant = computed(() => {
    switch (props.status) {
      case 'Running':
        return 'default' as const
      case 'Published':
      case 'Visible':
        return 'secondary' as const
      case 'Paused':
      case 'Finished':
        return 'outline' as const
      default:
        return 'secondary' as const
    }
  })

  return {
      ...toRefs(props),
      label,
      variant
    }
}

export type LifecycleBadgeViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useLifecycleBadge>>>
