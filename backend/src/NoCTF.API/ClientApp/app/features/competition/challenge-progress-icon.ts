import type { StatusIconName } from '../../components/ui/icons/status-icons'

export interface ChallengeProgressIconState {
  solvedByMyTeam: boolean
  attackSucceeded: boolean
  defenseSucceeded: boolean
}

export function challengeProgressIcon(progress: ChallengeProgressIconState | null, awdp: boolean): StatusIconName | null {
  if (!progress) return null
  if (!awdp) return progress.solvedByMyTeam ? 'solved' : null
  if (progress.attackSucceeded && progress.defenseSucceeded) return 'attack-defense-success'
  if (progress.attackSucceeded) return 'attack-success'
  if (progress.defenseSucceeded) return 'defense-success'
  return null
}
