import type { UiMessage } from '../utils/i18n'
import type { NoCTFAPIEndpointsAdministrationCompetitionsCompetitionTrackFailureCodeProtocol, NoCTFAPIEndpointsAdministrationCompetitionsCompetitionTrackFailureResponse, NoCTFAPIEndpointsTeamsTeamRegistrationFailureCodeProtocol, NoCTFAPIEndpointsTeamsTeamRegistrationFailureResponse, NoCTFAPIEndpointsTeamsTeamMembershipFailureCodeProtocol, NoCTFAPIEndpointsTeamsTeamMembershipFailureResponse } from '../api/models'
import { parseApiError } from '../utils/api-error'
import { translate } from '../utils/i18n'

const trackMessages = {
  CompetitionNotFound: "common.label.competitionFound",
  InvalidConfiguration: "common.competitionTrack.error.trackConfigurationCheckInvalid",
  CompetitionFinished: "administration.competitionTrack.label.finishedCompetitionTracksRead",
  TracksDisabled: "common.competitionTrack.description.teamTrackAssignmentRequires",
  TrackReassignmentRequired: "common.validation.trackReassignmentRequired",
  InvalidTrackReassignment: "common.error.trackReassignmentInvalid",
  TeamNotFound: "common.label.teamFound",
  TrackNotFound: "common.competitionTrack.description.trackExist",
  TrackNotPublicSelectable: "common.competitionTrack.validation.participantsSelectFormat",
  TrackSsoIdentityRequired: "sso.trackIdentityRequired",
  SsoProviderNotFound: "sso.trackProviderNotFound",
} satisfies Record<NoCTFAPIEndpointsAdministrationCompetitionsCompetitionTrackFailureCodeProtocol, string>

const registrationMessages = {
  InvalidTeamName: "common.competitionTrack.error.teamNameInvalid",
  CompetitionNotFound: "common.label.competitionFound",
  RegistrationClosed: "common.competitionTrack.validation.teamsCreatedFormat",
  UserAlreadyRegistered: "common.competitionTrack.description.alreadyBelongAnotherTeam",
  TeamNameOrMembershipConflict: "common.competitionTrack.description.teamNameAlreadyExists",
  TeamNotFound: "common.label.teamFound",
  CompetitionFinished: "common.label.competitionFinished",
  TeamLocked: "common.label.teamLocked",
  TeamBanned: "common.competitionTrack.validation.bannedTeamFormat",
  TeamConflict: "common.competitionTrack.description.teamWasUpdatedTry",
  TeamReviewConflict: "common.competitionTrack.label.teamReviewStatusChanged",
  CompetitionActive: "common.competitionTrack.error.teamWhileCompetitionUnavailable",
  TrackNotFound: "common.competitionTrack.description.trackExist",
  TrackNotPublicSelectable: "common.competitionTrack.validation.participantsSelectFormat",
  TrackInvitationRequired: "teams.track.invitationRequired",
  TrackInvitationInvalid: "teams.track.invitationInvalid",
  TrackSsoIdentityRequired: "sso.trackIdentityRequired",
} satisfies Record<NoCTFAPIEndpointsTeamsTeamRegistrationFailureCodeProtocol, string>

const membershipMessages = {
  CompetitionNotFound: "common.label.competitionFound",
  TeamNotFound: "common.competitionTrack.error.invitationCodeRotatedInvalid",
  TeamForbidden: "common.competitionTrack.description.allowedPerformTeam",
  TeamBanned: "common.competitionTrack.validation.bannedTeamFormat",
  MembershipLocked: "common.competitionTrack.validation.teamsJoinedFormat",
  UserAlreadyRegistered: "common.competitionTrack.description.alreadyBelongAnotherTeam",
  TeamFull: "common.label.teamFull",
  MembershipConflict: "common.competitionTrack.description.teamMembershipChangedTry",
  CaptainCannotBeRemoved: "common.competitionTrack.validation.captainRemovedFormat",
  MemberNotFound: "common.competitionTrack.label.userExist",
  MembershipNotFound: "common.competitionTrack.description.teamMembershipWasFound",
  CaptainMustTransfer: "common.competitionTrack.validation.captainTransferFormat",
  CaptainOnly: "common.competitionTrack.description.captainPerform",
  TrackSsoIdentityRequired: "sso.trackIdentityRequired",
} satisfies Record<NoCTFAPIEndpointsTeamsTeamMembershipFailureCodeProtocol, string>

export function nextCompetitionTrackOrdinal(trackKeys: readonly string[]): number {
  const normalizedKeys = new Set(trackKeys.map(key => key.trim().toLowerCase()))
  const greatestGeneratedOrdinal = trackKeys.reduce((greatest, key) => {
    const match = /^track-(\d+)$/i.exec(key.trim())
    if (!match?.[1]) return greatest
    return Math.max(greatest, Number.parseInt(match[1], 10))
  }, 0)
  let ordinal = Math.max(trackKeys.length + 1, greatestGeneratedOrdinal + 1)
  while (normalizedKeys.has(`track-${ordinal}`)) ordinal += 1
  return ordinal
}

export function duplicateCompetitionTrackKey(trackKeys: readonly string[]): string | null {
  const keys = new Set<string>()
  for (const key of trackKeys) {
    const normalized = key.trim().toLowerCase()
    if (keys.has(normalized)) return normalized
    keys.add(normalized)
  }
  return null
}

export function competitionTrackErrorMessage(error: unknown, fallback: string): UiMessage {
  const payload = error as Partial<NoCTFAPIEndpointsAdministrationCompetitionsCompetitionTrackFailureResponse> | null
  if (payload?.code === 'TrackReassignmentRequired' && payload.affectedTeamCount)
    return translate('competitions.competitionTrack.validation.trackReassignmentRequired', { count: payload.affectedTeamCount })
  return payload?.code
    ? translate(trackMessages[payload.code])
    : parseApiError(error, fallback).displayMessage
}

export function teamRegistrationErrorMessage(error: unknown, fallback: string): UiMessage {
  const payload = error as Partial<NoCTFAPIEndpointsTeamsTeamRegistrationFailureResponse> | null
  return payload?.code ? translate(registrationMessages[payload.code]) : parseApiError(error, fallback).displayMessage
}

export function teamMembershipErrorMessage(error: unknown, fallback: string): UiMessage {
  const payload = error as Partial<NoCTFAPIEndpointsTeamsTeamMembershipFailureResponse> | null
  return payload?.code ? translate(membershipMessages[payload.code]) : parseApiError(error, fallback).displayMessage
}
