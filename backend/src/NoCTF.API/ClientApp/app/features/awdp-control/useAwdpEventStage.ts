import { toRefs } from 'vue'

import { Activity, Crosshair, RadioTower, Shield } from '@lucide/vue'
import type { AwdpResolvedControlEvent } from '../../utils/awdp-control-screen'

/** Owns state, effects and commands for AwdpEventStage. */
export function useAwdpEventStage(props: Readonly<{
  event: AwdpResolvedControlEvent | null
  queueLength: number
  progress: number
}>) {
  const animationComponent = computed(() => {
    if (!props.event) return null
    if (props.event.action === 'attack')
      return props.event.outcome === 'success' ? resolveComponent('AwdpAttackSuccessAnimation') : resolveComponent('AwdpAttackFailureAnimation')
    return props.event.outcome === 'success' ? resolveComponent('AwdpDefenseSuccessAnimation') : resolveComponent('AwdpDefenseFailureAnimation')
  })

  return {
      ...toRefs(props),
      Activity,
      Crosshair,
      RadioTower,
      Shield,
      animationComponent
    }
}

export type AwdpEventStageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAwdpEventStage>>>
