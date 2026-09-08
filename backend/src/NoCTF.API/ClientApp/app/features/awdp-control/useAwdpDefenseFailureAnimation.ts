import { toRefs } from 'vue'

import { Box, ScanLine, ShieldAlert } from '@lucide/vue'
import type { AwdpControlEvent } from '../../utils/awdp-control-screen'

/** Owns state, effects and commands for AwdpDefenseFailureAnimation. */
export function useAwdpDefenseFailureAnimation(props: Readonly<{ event: AwdpControlEvent }>) {
  const cracks = Array.from({ length: 10 }, (_, index) => index)

  return {
      ...toRefs(props),
      Box,
      ScanLine,
      ShieldAlert,
      cracks
    }
}

export type AwdpDefenseFailureAnimationViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAwdpDefenseFailureAnimation>>>
