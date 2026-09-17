import type {
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionTrackFailureCodeProtocol,
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionTrackFailureResponse,
  NoCtfapiEndpointsTeamsTeamRegistrationFailureCodeProtocol,
  NoCtfapiEndpointsTeamsTeamRegistrationFailureResponse,
  NoCtfapiEndpointsTeamsTeamMembershipFailureCodeProtocol,
  NoCtfapiEndpointsTeamsTeamMembershipFailureResponse,
} from '../api'
import { parseApiError } from '../utils/api-error'
import { translate } from '../utils/i18n'

const trackMessages = {
  CompetitionNotFound: "ui.competitionNotFound",
  InvalidConfiguration: "ui.theTrackConfigurationIsInvalidCheckTheDefaultTrackTrack",
  CompetitionFinished: "ui.finishedCompetitionTracksReadOnly",
  TracksDisabled: "ui.teamTrackAssignmentRequiresEnabledTracks",
  TrackReassignmentRequired: "ui.trackReassignmentRequired",
  InvalidTrackReassignment: "ui.invalidTrackReassignment",
  TeamNotFound: "ui.teamNotFound",
  TrackNotFound: "ui.theSelectedTrackDoesNotExist",
  TrackNotPublicSelectable: "ui.participantsCannotSelectThisTrack",
} satisfies Record<NoCtfapiEndpointsAdministrationCompetitionsCompetitionTrackFailureCodeProtocol, string>

const registrationMessages = {
  InvalidTeamName: "ui.theTeamNameIsInvalid",
  CompetitionNotFound: "ui.competitionNotFound",
  RegistrationClosed: "ui.teamsCannotBeCreatedDuringTheCurrentCompetitionPhase",
  UserAlreadyRegistered: "ui.youAlreadyBelongToAnotherTeamInThisCompetition",
  TeamNameOrMembershipConflict: "ui.theTeamNameAlreadyExistsOrYourMembershipConflictsWith",
  TeamNotFound: "ui.teamNotFound",
  CompetitionFinished: "ui.theCompetitionHasFinished",
  TeamLocked: "ui.theTeamIsLocked",
  TeamBanned: "ui.aBannedTeamCannotChangeItsOrganizationDetails",
  TeamConflict: "ui.theTeamWasUpdatedTryAgain",
  TeamReviewConflict: "ui.theTeamReviewStatusChanged",
  CompetitionActive: "ui.thisTeamOperationIsUnavailableWhileTheCompetitionIsRunning",
  TrackNotFound: "ui.theSelectedTrackDoesNotExist",
  TrackNotPublicSelectable: "ui.participantsCannotSelectThisTrack",
  TrackInvitationRequired: "ui.message7",
  TrackInvitationInvalid: "ui.message8",
} satisfies Record<NoCtfapiEndpointsTeamsTeamRegistrationFailureCodeProtocol, string>

const membershipMessages = {
  CompetitionNotFound: "ui.competitionNotFound",
  TeamNotFound: "ui.theInvitationCodeIsInvalidOrHasBeenRotated",
  TeamForbidden: "ui.youAreNotAllowedToPerformThisTeamOperation",
  TeamBanned: "ui.aBannedTeamCannotChangeItsOrganizationDetails",
  MembershipLocked: "ui.teamsCannotBeJoinedDuringTheCurrentCompetitionStage",
  UserAlreadyRegistered: "ui.youAlreadyBelongToAnotherTeamInThisCompetition",
  TeamFull: "ui.theTeamIsFull",
  MembershipConflict: "ui.teamMembershipHasChangedPleaseTryAgain",
  CaptainCannotBeRemoved: "ui.theCaptainCannotBeRemovedFromTheTeam",
  MemberNotFound: "ui.theUserDoesNotExist",
  MembershipNotFound: "ui.theTeamMembershipWasNotFound",
  CaptainMustTransfer: "ui.theCaptainMustTransferCaptaincyFirst",
  CaptainOnly: "ui.onlyTheCaptainCanPerformThisOperation",
} satisfies Record<NoCtfapiEndpointsTeamsTeamMembershipFailureCodeProtocol, string>

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

export function competitionTrackErrorMessage(error: unknown, fallback: string): string {
  const payload = error as Partial<NoCtfapiEndpointsAdministrationCompetitionsCompetitionTrackFailureResponse> | null
  if (payload?.code === 'TrackReassignmentRequired' && payload.affectedTeamCount)
    return translate('ui.trackReassignmentRequiredWithCount', { count: payload.affectedTeamCount })
  return payload?.code
    ? translate(trackMessages[payload.code])
    : parseApiError(error, fallback).message
}

export function teamRegistrationErrorMessage(error: unknown, fallback: string): string {
  const payload = error as Partial<NoCtfapiEndpointsTeamsTeamRegistrationFailureResponse> | null
  return payload?.code ? translate(registrationMessages[payload.code]) : parseApiError(error, fallback).message
}

export function teamMembershipErrorMessage(error: unknown, fallback: string): string {
  const payload = error as Partial<NoCtfapiEndpointsTeamsTeamMembershipFailureResponse> | null
  return payload?.code ? translate(membershipMessages[payload.code]) : parseApiError(error, fallback).message
}
