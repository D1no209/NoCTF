import { toRefs } from 'vue'

import type { NoCtfapiEndpointsCompetitionsGameModeProtocol } from '../../api'

/** Owns state, effects and commands for GameModeBadge. */
export function useGameModeBadge(props: Readonly<{ mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol | null }>) {
  const label = computed(() => enumLabel(GameModeLabel, props.mode))

  return {
      ...toRefs(props),
      label
    }
}

export type GameModeBadgeViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useGameModeBadge>>>
