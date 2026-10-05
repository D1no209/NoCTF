import type {
  NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse,
} from '../api'

export type ScoreboardRefreshOutcome = 'accepted' | 'retrying' | 'failed' | 'superseded'

/** Anonymous viewers have no Hub; clock-driven settlement must still reach them. */
export function needsAwdpRoundRefresh(
  schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null,
  snapshot: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse | null,
): boolean {
  return schema?.mode === 'Awdp' && snapshot?.dataScope === 'Live'
    && (schema.rounds ?? []).some(round => round.state === 'Running')
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

export function isCoherentScoreboardBundle(
  catalog: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse | null,
  schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null,
  snapshot: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse | null,
): boolean {
  if (!catalog || !schema || !snapshot) return false
  return schema.challengeCatalogRevision === catalog.revision
    && snapshot.schemaRevision === schema.revision
}
