import type {
  NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse,
} from '~/api'

export type ScoreboardRefreshOutcome = 'accepted' | 'retrying' | 'failed' | 'superseded'

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
