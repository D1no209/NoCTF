import { translate } from '../utils/i18n'
import type { NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentDetailResponse } from '../api'

type Incident = Pick<NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentDetailResponse, 'failureCode' | 'ownerTeamName'>

export function cheatOwnerTeamLabel(incident: Incident): string {
  return incident.ownerTeamName ?? translate(incident.failureCode === 'ForeignTeamFlagDetected'
    ? 'administration.label.multipleTeamsUndetermined' : 'cheats.label.notApplicable')
}

export function cheatEvidenceSourceLabel(source?: 'Recorded' | 'LegacySubmission'): string {
  return translate(source === 'LegacySubmission' ? 'cheats.label.legacy' : 'cheats.label.recorded')
}
