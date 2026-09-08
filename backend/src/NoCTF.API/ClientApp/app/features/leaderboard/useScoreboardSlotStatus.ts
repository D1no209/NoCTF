import { toRefs } from 'vue'

import { Flag, Shield, ShieldCheck, ShieldX } from '@lucide/vue'
import type { NoCtfapiEndpointsCompetitionsGameModeProtocol, NoCtfapiEndpointsCompetitionsScoreboardSlotResponse } from '../../api'
import { scoreboardSlotSignals } from '../../utils/scoreboard'

/** Owns state, effects and commands for ScoreboardSlotStatus. */
export function useScoreboardSlotStatus(props: Readonly<{
  mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol | null
  slot: NoCtfapiEndpointsCompetitionsScoreboardSlotResponse
}>) {
  const signals = computed(() => scoreboardSlotSignals(props.slot, props.mode))

  const combinedSuccess = computed(() =>
    signals.value.flagState === 'Succeeded' && signals.value.shieldState === 'Succeeded')

  const combinedFailure = computed(() =>
    signals.value.flagState === 'Failed' && signals.value.shieldState === 'Failed')

  const noOperation = computed(() =>
    signals.value.flagState === 'None'
    && (!signals.value.showShield || signals.value.shieldState === 'None'))

  const flagLabel = computed(() => {
    if (signals.value.flagState === 'None') {
      if (props.mode === 'Awd' || props.mode === 'Awdp') return translate("ui.noAttackThisRound")
      if (props.mode === 'Koh') return translate("ui.noControlActionThisRound")
      return translate("ui.noSolveAttemptThisRound")
    }
    if (props.mode === 'Awd' || props.mode === 'Awdp')
      return signals.value.flagSucceeded ? translate("ui.attackSucceeded") : translate("ui.attackFailed")
    if (props.mode === 'Koh')
      return signals.value.flagSucceeded ? translate("ui.controlAcquired") : translate("ui.controlNotAcquired")
    return signals.value.flagSucceeded ? translate("ui.solved") : translate("ui.notSolved")
  })

  const shieldLabel = computed(() => {
    if (signals.value.shieldState === 'None') return translate("ui.noDefenseThisRound")
    return signals.value.shieldSucceeded ? translate("ui.defenseSucceeded") : translate("ui.defenseFailed")
  })

  const accessibleLabel = computed(() => [
    combinedSuccess.value ? translate("ui.attackAndDefenseSucceeded") : null,
    combinedFailure.value ? translate("ui.attackFailedAndDefenseWasAbnormal") : null,
    !combinedSuccess.value && !combinedFailure.value && signals.value.showFlag ? flagLabel.value : null,
    !combinedSuccess.value && !combinedFailure.value && signals.value.showShield ? shieldLabel.value : null,
  ].filter(Boolean).join('，'))

  return {
      ...toRefs(props),
      Flag,
      Shield,
      ShieldCheck,
      ShieldX,
      signals,
      combinedSuccess,
      combinedFailure,
      noOperation,
      accessibleLabel
    }
}

export type ScoreboardSlotStatusViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useScoreboardSlotStatus>>>
