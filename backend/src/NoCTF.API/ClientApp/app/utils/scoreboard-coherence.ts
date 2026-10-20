import type { NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse } from '../api'
import type { ScoreboardSnapshot } from './scoreboard-board'

export type ScoreboardRefreshOutcome = 'accepted' | 'retrying' | 'failed' | 'superseded'

/** Anonymous viewers have no Hub; clock-driven settlement must still reach them. */
export function needsAwdpRoundRefresh(snapshot: ScoreboardSnapshot | null): boolean {
  return snapshot?.layout.mode === 'Awdp' && snapshot.dataScope === 'Live'
    && snapshot.layout.rounds.some(round => round.state === 'Running')
}

function normalizeScoreboardVersion(value: string | null | undefined): string | null {
  if (!value || !/^\d+$/.test(value)) return null
  return value.replace(/^0+(?=\d)/, '')
}

function compareScoreboardVersions(left: string, right: string): number {
  if (left.length !== right.length) return left.length > right.length ? 1 : -1
  return left === right ? 0 : left > right ? 1 : -1
}

export function isScoreboardVersionAtLeast(
  candidate: string | null | undefined,
  minimum: string | null | undefined,
): boolean {
  const normalizedCandidate = normalizeScoreboardVersion(candidate)
  if (normalizedCandidate === null) return false
  const normalizedMinimum = normalizeScoreboardVersion(minimum)
  return normalizedMinimum === null
    || compareScoreboardVersions(normalizedCandidate, normalizedMinimum) >= 0
}

export function newestScoreboardVersion(
  current: string | null,
  candidate: string | null | undefined,
): string | null {
  const normalizedCandidate = normalizeScoreboardVersion(candidate)
  if (normalizedCandidate === null) return current
  const normalizedCurrent = normalizeScoreboardVersion(current)
  return normalizedCurrent === null
    || compareScoreboardVersions(normalizedCandidate, normalizedCurrent) > 0
    ? normalizedCandidate
    : normalizedCurrent
}

export function shouldRestoreRequestedRoundWindow(outcome: ScoreboardRefreshOutcome): boolean {
  return outcome === 'failed'
}

export function canReplaceScoreboardSnapshot(
  candidate: ScoreboardSnapshot | null,
  current: ScoreboardSnapshot | null,
): boolean {
  return candidate !== null && isScoreboardVersionAtLeast(candidate.version,
    candidate.dataScope === current?.dataScope ? current?.version : null)
}

/** The leaderboard carries its own columns; only the separately cached catalog titles must match it. */
export function isCoherentScoreboardBundle(
  catalog: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse | null,
  snapshot: ScoreboardSnapshot | null,
): boolean {
  if (!catalog || !snapshot) return false
  return snapshot.challengeCatalogRevision === catalog.revision
}
