import type {
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
  ConfigurationLocked: "ui.theCompetitionHasStartedAndItsTrackConfigurationIsFrozen",
  ConfigurationConflict: "ui.anotherStaffMemberUpdatedTheTrackConfigurationRefreshAndTry",
  TrackInUse: "ui.thisTrackIsStillAssignedToATeamAndCannot",
  TeamNotFound: "ui.teamNotFound",
  TrackNotFound: "ui.theSelectedTrackDoesNotExist",
  TrackNotPublicSelectable: "ui.participantsCannotSelectThisTrack",
  AssignmentLocked: "ui.theCompetitionHasStartedAndTeamTrackAssignmentsAreFrozen",
  AssignmentConflict: "ui.theTeamWasUpdatedRefreshAndTryAgain",
} as const

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

export function competitionTrackErrorMessage(error: unknown, fallback: string): string {
  const payload = error as { code?: keyof typeof trackMessages } | null
  return payload?.code && payload.code in trackMessages
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
