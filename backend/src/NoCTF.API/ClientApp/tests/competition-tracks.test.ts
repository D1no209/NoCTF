import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import type {
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionTrackFailureCodeProtocol,
  NoCtfapiEndpointsTeamsTeamMembershipFailureCodeProtocol,
  NoCtfapiEndpointsTeamsTeamRegistrationFailureCodeProtocol,
} from '../app/api'
import {
  competitionTrackErrorMessage,
  teamMembershipErrorMessage,
  teamRegistrationErrorMessage,
} from '../app/lib/competition-track'

describe('competition track error presentation', () => {
  const trackCodes = [
    'CompetitionNotFound',
    'InvalidConfiguration',
    'ConfigurationLocked',
    'ConfigurationConflict',
    'TrackInUse',
    'TeamNotFound',
    'TrackNotFound',
    'TrackNotPublicSelectable',
    'AssignmentLocked',
    'AssignmentConflict',
  ] satisfies NoCtfapiEndpointsAdministrationCompetitionsCompetitionTrackFailureCodeProtocol[]

  const registrationCodes = [
    'InvalidTeamName',
    'CompetitionNotFound',
    'RegistrationClosed',
    'UserAlreadyRegistered',
    'TeamNameOrMembershipConflict',
    'TeamNotFound',
    'CompetitionFinished',
    'TeamLocked',
    'TeamConflict',
    'TeamReviewConflict',
    'CompetitionActive',
    'TrackNotFound',
    'TrackNotPublicSelectable',
    'TrackInvitationRequired',
    'TrackInvitationInvalid',
  ] satisfies NoCtfapiEndpointsTeamsTeamRegistrationFailureCodeProtocol[]

  const membershipCodes = [
    'CompetitionNotFound',
    'TeamNotFound',
    'TeamForbidden',
    'TeamBanned',
    'MembershipLocked',
    'UserAlreadyRegistered',
    'TeamFull',
    'MembershipConflict',
    'CaptainCannotBeRemoved',
    'MemberNotFound',
    'MembershipNotFound',
    'CaptainMustTransfer',
    'CaptainOnly',
  ] satisfies NoCtfapiEndpointsTeamsTeamMembershipFailureCodeProtocol[]

  test('maps every generated track failure code', () => {
    for (const code of trackCodes)
      expect(competitionTrackErrorMessage({ code }, 'fallback')).not.toBe('fallback')
  })

  test('maps every generated team-registration failure code', () => {
    for (const code of registrationCodes)
      expect(teamRegistrationErrorMessage({ code }, 'fallback')).not.toBe('fallback')
  })

  test('maps every generated team-membership failure code', () => {
    for (const code of membershipCodes)
      expect(teamMembershipErrorMessage({ code }, 'fallback')).not.toBe('fallback')
  })
})

describe('competition track pages', () => {
  test('uses generated SDK operations and keeps pending/error state explicit', async () => {
    const admin = await sourceFile(new URL('../app/pages/admin/competitions/[id]/tracks.vue', import.meta.url)).text()
    const teams = await sourceFile(new URL('../app/pages/admin/competitions/[id]/teams.vue', import.meta.url)).text()

    expect(admin).toContain('adminCompetitionTracksGet')
    expect(admin).toContain('adminCompetitionTracksUpdate')
    expect(admin).toContain('if (saving.value || frozen.value || !canWrite.value) return')
    expect(admin).toContain('setInvitationRequired')
    expect(admin).toContain('clearInvitationRequirement')
    expect(admin).toContain('clientId: crypto.randomUUID()')
    expect(admin).toContain(':key="track.clientId"')
    expect(admin).not.toContain(':key="`${track.key}-${index}`"')
    expect(admin).toContain("ui.trackRequiresAnInvitationCode")
    expect(admin).toContain('error.value = competitionTrackErrorMessage')
    expect(teams).toContain('adminTeamTrackAssign')
    expect(teams).toContain('body: { trackKey }')
    expect(teams).not.toContain('tracksFrozen.value || team.trackKey === trackKey')
  })

  test('exposes participant selection and per-track leaderboard switching', async () => {
    const overview = await sourceFile(new URL('../app/pages/competitions/[id]/index.vue', import.meta.url)).text()
    const leaderboard = await sourceFile(new URL('../app/pages/competitions/[id]/leaderboard.vue', import.meta.url)).text()

    expect(overview).toContain('listCompetitionTracks')
    expect(overview).toContain('trackKey: createTrackKey.value')
    expect(overview).toContain('trackInvitationCode: selectedCreateTrack.value?.requiresInvitationCode')
    expect(overview).toContain('createTrackInvitationCode')
    expect(overview).toContain('track.isPublicSelectable')
    expect(overview).toContain('selectedCreateTrack?.requiresInvitationCode')
    expect(overview).toContain('v-else-if="selectableTracks.length > 0"')
    expect(overview).not.toContain('createTrackKey.value = selectableTracks.value.find')
    expect(overview).toContain('response?.status === 404')
    expect(overview).toContain('teamLoadError')
    expect(overview).toContain('trackLoadError')
    expect(overview).toContain("translate(\"ui.selectACompetitionTrack\")")
    expect(overview).toContain('createValidationError')
    expect(overview).toContain('teamMembershipErrorMessage')
    expect(overview).toContain("invitationToken.length !== 32")
    expect(overview).toContain('joinValidationError')
    expect(overview).toContain('} finally {')
    expect(overview).toContain('joinPending.value = false')
    expect(overview).toContain('minlength="32" maxlength="32"')
    expect(overview).not.toContain(':disabled="createPending || !createName.trim() || !createTrackKey')
    expect(leaderboard).toContain("all.filter(team => team.trackKey === selectedTrackKey.value)")
    expect(leaderboard).toContain('availableTracks.length > 1')
    expect(leaderboard).toContain('canViewInternalTracks.value')
    expect(leaderboard).toContain("role === 'Owner' || role === 'Manager' || role === 'Judge'")
    expect(leaderboard).toContain('isAdministrator.value')
    expect(leaderboard).toContain('!track.isInternal && (track.isViewerTrack || track.visibleOnLeaderboard)')
    expect(leaderboard).toContain("const allTracksKey = '__all_tracks__'")
    expect(leaderboard).toContain("<SelectItem :value=\"allTracksKey\">{{ $t('ui.allTracks') }}</SelectItem>")
    expect(leaderboard).toContain('const displayRanks = computed')
    expect(leaderboard).toContain("(right.totalScore ?? 0) - (left.totalScore ?? 0)")
    expect(leaderboard).toContain('displayRank(team)')
    expect(leaderboard).not.toContain("selectedAllTracks.value ? translate(\"ui.trackRank\")")
    expect(leaderboard).toContain('trackName(team.trackKey)')
  })
})
