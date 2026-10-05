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
      if (props.mode === 'Awd' || props.mode === 'Awdp') return translate("leaderboard.label.attackRound")
      if (props.mode === 'Koh') return translate("leaderboard.scoreboardSlot.label.controlActionRound")
      return translate("leaderboard.scoreboardSlot.label.solveAttemptRound")
    }
    if (props.mode === 'Awd' || props.mode === 'Awdp')
      return signals.value.flagSucceeded ? translate("common.label.attackSucceeded") : translate("leaderboard.error.attackFailed")
    if (props.mode === 'Koh')
      return signals.value.flagSucceeded ? translate("leaderboard.label.controlAcquired") : translate("leaderboard.label.controlAcquired.scoreboardSlotStatus")
    return signals.value.flagSucceeded ? translate("common.label.solved.competitionChallengeNavigator") : translate("common.label.solved")
  })

  const shieldLabel = computed(() => {
    if (signals.value.shieldState === 'None') return translate("leaderboard.label.defenseRound")
    return signals.value.shieldSucceeded ? translate("common.label.defenseSucceeded") : translate("leaderboard.error.defenseFailed")
  })

  const accessibleLabel = computed(() => [
    combinedSuccess.value ? translate("common.label.attackDefenseSucceeded") : null,
    combinedFailure.value ? translate("leaderboard.scoreboardSlot.error.attackDefenseWasFailed") : null,
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
