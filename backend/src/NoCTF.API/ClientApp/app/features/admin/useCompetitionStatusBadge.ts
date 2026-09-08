import { toRefs } from 'vue'

import type { NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol } from '../../api'
import type { BadgeVariants } from '../../components/ui/badge'

/** Owns state, effects and commands for CompetitionStatusBadge. */
export function useCompetitionStatusBadge(props: Readonly<{ status?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol | null }>) {
  const label = computed(() => enumLabel(CompetitionStatusLabel, props.status))

  const variant = computed<BadgeVariants['variant']>(() => {
    switch (props.status) {
      case 'Running': return 'default'
      case 'Paused': return 'secondary'
      case 'Finished': return 'outline'
      case 'Published': return 'secondary'
      default: return 'outline'
    }
  })

  return {
      ...toRefs(props),
      label,
      variant
    }
}

export type CompetitionStatusBadgeViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionStatusBadge>>>
