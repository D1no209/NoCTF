import { toRefs } from 'vue'

import { Crosshair, ShieldX } from '@lucide/vue'
import type { AwdpControlEvent } from '../../utils/awdp-control-screen'

/** Owns state, effects and commands for AwdpAttackFailureAnimation. */
export function useAwdpAttackFailureAnimation(props: Readonly<{ event: AwdpControlEvent }>) {
  const bolts = Array.from({ length: 12 }, (_, index) => index)

  return {
      ...toRefs(props),
      Crosshair,
      ShieldX,
      bolts
    }
}

export type AwdpAttackFailureAnimationViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAwdpAttackFailureAnimation>>>
