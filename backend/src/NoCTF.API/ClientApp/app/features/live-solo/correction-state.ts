import type { NoCtfapiEndpointsLiveSoloLiveSoloCorrectionImpactResponse as Impact } from '~/api'
export function replayConsents(impacts: Impact[], selected: Set<string>) {
  return impacts.filter(row => row.requiresReplay && row.matchId && row.concurrencyStamp && selected.has(row.matchId))
    .map(row => ({ matchId: row.matchId!, concurrencyStamp: row.concurrencyStamp! }))
}
export function allReplaysConfirmed(impacts: Impact[], selected: Set<string>) {
  return impacts.every(row => !row.requiresReplay || !!row.matchId && !!row.concurrencyStamp && selected.has(row.matchId))
}
export function correctionScoreValid(winner: string, left: string | null | undefined, right: string | null | undefined, wins: number, leftWins: number, rightWins: number) {
  return Number.isInteger(leftWins) && Number.isInteger(rightWins) && leftWins >= 0 && rightWins >= 0
    && (winner === left && leftWins === wins && rightWins < wins || winner === right && rightWins === wins && leftWins < wins)
}
