import { toRefs } from 'vue'

import { Crosshair, Swords } from '@lucide/vue'
import type { AwdpControlEvent } from '../../utils/awdp-control-screen'

/** Owns state, effects and commands for AwdpAttackSuccessAnimation. */
export function useAwdpAttackSuccessAnimation(props: Readonly<{ event: AwdpControlEvent }>) {
  const rays = Array.from({ length: 18 }, (_, index) => index)

  const shards = Array.from({ length: 20 }, (_, index) => index)

  return {
      ...toRefs(props),
      Crosshair,
      Swords,
      rays,
      shards
    }
}

export type AwdpAttackSuccessAnimationViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAwdpAttackSuccessAnimation>>>
