import { markRaw, toRefs } from 'vue'

import { Activity, Crosshair, RadioTower, Shield } from '@lucide/vue'
import type { AwdpResolvedControlEvent } from '../../utils/awdp-control-screen'
import AwdpAttackFailureAnimationComponent from './AwdpAttackFailureAnimation.vue'
import AwdpAttackSuccessAnimationComponent from './AwdpAttackSuccessAnimation.vue'
import AwdpDefenseFailureAnimationComponent from './AwdpDefenseFailureAnimation.vue'
import AwdpDefenseSuccessAnimationComponent from './AwdpDefenseSuccessAnimation.vue'

/** Owns state, effects and commands for AwdpEventStage. */
export function useAwdpEventStage(props: Readonly<{
  event: AwdpResolvedControlEvent | null
  queueLength: number
  progress: number
}>) {
  const AwdpAttackFailureAnimation = markRaw(AwdpAttackFailureAnimationComponent)
  const AwdpAttackSuccessAnimation = markRaw(AwdpAttackSuccessAnimationComponent)
  const AwdpDefenseFailureAnimation = markRaw(AwdpDefenseFailureAnimationComponent)
  const AwdpDefenseSuccessAnimation = markRaw(AwdpDefenseSuccessAnimationComponent)

  const animationComponent = computed(() => {
    if (!props.event) return null
    if (props.event.action === 'attack')
      return props.event.outcome === 'success' ? AwdpAttackSuccessAnimation : AwdpAttackFailureAnimation
    return props.event.outcome === 'success' ? AwdpDefenseSuccessAnimation : AwdpDefenseFailureAnimation
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
