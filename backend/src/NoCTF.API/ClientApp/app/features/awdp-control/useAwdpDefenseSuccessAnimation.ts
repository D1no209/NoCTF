import { toRefs } from 'vue'

import { Box, ScanLine, ShieldCheck } from '@lucide/vue'
import type { AwdpControlEvent } from '../../utils/awdp-control-screen'

/** Owns state, effects and commands for AwdpDefenseSuccessAnimation. */
export function useAwdpDefenseSuccessAnimation(props: Readonly<{ event: AwdpControlEvent }>) {
  const hexes = Array.from({ length: 18 }, (_, index) => index)

  return {
      ...toRefs(props),
      Box,
      ScanLine,
      ShieldCheck,
      hexes
    }
}

export type AwdpDefenseSuccessAnimationViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAwdpDefenseSuccessAnimation>>>
