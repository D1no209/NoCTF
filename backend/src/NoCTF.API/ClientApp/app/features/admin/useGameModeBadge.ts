import { toRefs } from 'vue'

import type { NoCTFAPIEndpointsCompetitionsGameModeProtocol } from '../../api/models'

/** Owns state, effects and commands for GameModeBadge. */
export function useGameModeBadge(props: Readonly<{ mode?: NoCTFAPIEndpointsCompetitionsGameModeProtocol | null }>) {
  const label = computed(() => enumLabel(GameModeLabel, props.mode))

  return {
      ...toRefs(props),
      label
    }
}

export type GameModeBadgeViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useGameModeBadge>>>
