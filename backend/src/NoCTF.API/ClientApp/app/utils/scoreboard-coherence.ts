import type {
  NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse,
} from '~/api'

export function isCoherentScoreboardBundle(
  catalog: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse | null,
  schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null,
  snapshot: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse | null,
): boolean {
  if (!catalog || !schema || !snapshot) return false
  return schema.challengeCatalogRevision === catalog.revision
    && snapshot.schemaRevision === schema.revision
}
