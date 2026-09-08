import { toRefs } from 'vue'

import type { NoCtfapiEndpointsCompetitionsGameModeProtocol } from '../../api'
import { gameModeLabel } from '../../utils/labels'

/** Owns state, effects and commands for ModeBadge. */
export function useModeBadge(props: Readonly<{ mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol }>) {
  const label = computed(() => gameModeLabel(props.mode))

  return {
      ...toRefs(props),
      label
    }
}

export type ModeBadgeViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useModeBadge>>>
