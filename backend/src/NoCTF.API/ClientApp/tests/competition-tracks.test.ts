import { describe, expect, test } from 'bun:test'
import type {
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionTrackFailureCodeProtocol,
  NoCtfapiEndpointsTeamsTeamRegistrationFailureCodeProtocol,
} from '../app/api'
import {
  competitionTrackErrorMessage,
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

  test('maps every generated track failure code', () => {
    for (const code of trackCodes)
      expect(competitionTrackErrorMessage({ code }, 'fallback')).not.toBe('fallback')
  })

  test('maps every generated team-registration failure code', () => {
    for (const code of registrationCodes)
      expect(teamRegistrationErrorMessage({ code }, 'fallback')).not.toBe('fallback')
  })
})

describe('competition track pages', () => {
  test('uses generated SDK operations and keeps pending/error state explicit', async () => {
    const admin = await Bun.file(new URL('../app/pages/admin/competitions/[id]/tracks.vue', import.meta.url)).text()
    const teams = await Bun.file(new URL('../app/pages/admin/competitions/[id]/teams.vue', import.meta.url)).text()

    expect(admin).toContain('adminCompetitionTracksGet')
    expect(admin).toContain('adminCompetitionTracksUpdate')
    expect(admin).toContain('if (saving.value || frozen.value || !canWrite.value) return')
    expect(admin).toContain('setInvitationRequired')
    expect(admin).toContain('clearInvitationRequirement')
    expect(admin).toContain('需要填写邀请码')
    expect(admin).toContain('error.value = competitionTrackErrorMessage')
    expect(teams).toContain('adminTeamTrackAssign')
    expect(teams).toContain('body: { trackKey }')
    expect(teams).not.toContain('tracksFrozen.value || team.trackKey === trackKey')
  })

  test('exposes participant selection and per-track leaderboard switching', async () => {
    const overview = await Bun.file(new URL('../app/pages/competitions/[id]/index.vue', import.meta.url)).text()
    const leaderboard = await Bun.file(new URL('../app/pages/competitions/[id]/leaderboard.vue', import.meta.url)).text()

    expect(overview).toContain('listCompetitionTracks')
    expect(overview).toContain('trackKey: createTrackKey.value')
    expect(overview).toContain('trackInvitationCode: selectedCreateTrack.value?.requiresInvitationCode')
    expect(overview).toContain('createTrackInvitationCode')
    expect(overview).toContain('track.isPublicSelectable')
    expect(overview).toContain('selectedCreateTrack?.requiresInvitationCode')
    expect(overview).toContain('v-if="selectableTracks.length > 0"')
    expect(overview).not.toContain('createTrackKey.value = selectableTracks.value.find')
    expect(leaderboard).toContain("all.filter(team => team.trackKey === selectedTrackKey.value)")
    expect(leaderboard).toContain('availableTracks.length > 1')
    expect(leaderboard).toContain('canObserveAllTracks.value')
    expect(leaderboard).toContain('Boolean(ctx.competition.value?.administrationRole)')
    expect(leaderboard).toContain('isAdministrator.value')
    expect(leaderboard).toContain('track.visibleOnLeaderboard && !track.isInternal')
    expect(leaderboard).toContain('tracks.find(track => track.isViewerTrack)?.key')
  })
})
