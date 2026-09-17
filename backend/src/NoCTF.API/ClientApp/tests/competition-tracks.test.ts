import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import type {
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionTrackFailureCodeProtocol,
  NoCtfapiEndpointsTeamsTeamMembershipFailureCodeProtocol,
  NoCtfapiEndpointsTeamsTeamRegistrationFailureCodeProtocol,
} from '../app/api'
import {
  competitionTrackErrorMessage,
  duplicateCompetitionTrackKey,
  nextCompetitionTrackOrdinal,
  teamMembershipErrorMessage,
  teamRegistrationErrorMessage,
} from '../app/lib/competition-track'

describe('competition track error presentation', () => {
  const trackCodes = [
    'CompetitionNotFound',
    'InvalidConfiguration',
    'CompetitionFinished',
    'TracksDisabled',
    'TrackReassignmentRequired',
    'InvalidTrackReassignment',
    'TeamNotFound',
    'TrackNotFound',
    'TrackNotPublicSelectable',
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
  test('allocates generated keys without reusing an existing ordinal', () => {
    expect(nextCompetitionTrackOrdinal(['track-2', 'track-3', 'track-4'])).toBe(5)
    expect(nextCompetitionTrackOrdinal(['default', 'track-2', 'track-4'])).toBe(5)
    expect(nextCompetitionTrackOrdinal(['default'])).toBe(2)
  })

  test('detects duplicate keys with the same server normalization', () => {
    expect(duplicateCompetitionTrackKey(['track-2', ' TRACK-2 '])).toBe('track-2')
    expect(duplicateCompetitionTrackKey(['track-2', 'track-3'])).toBeNull()
  })

  test('uses generated SDK operations and keeps pending/error state explicit', async () => {
    const admin = await sourceFile(new URL('../app/pages/admin/competitions/[id]/tracks.vue', import.meta.url)).text()
    const teams = await sourceFile(new URL('../app/pages/admin/competitions/[id]/teams.vue', import.meta.url)).text()

    expect(admin).toContain('adminGetCompetition')
    expect(admin).toContain('adminListTeams')
    expect(admin).toContain('adminPatchCompetition')
    expect(admin).toContain('if (saving.value || !canUpdate.value || !canWrite.value) return')
    expect(admin).toContain('enabled: enabled.value')
    expect(admin).toContain('removedTrackReassignments: enabled.value ? removedTrackReassignments.value : []')
    expect(admin).toContain('affectedTeamCount')
    expect(admin).toContain('requestRemoveTrack')
    expect(admin).toContain('confirmRemoveTrack')
    expect(admin).not.toContain('if (affectedTeamCount === 0)')
    expect(admin).toContain('{ fromTrackKey: removal.key, toTrackKey: removal.toTrackKey }')
    expect(admin).toContain('confirmDisable')
    expect(admin).toContain('track.existingKey !== null')
    expect(admin).not.toContain('frozen')
    expect(admin).toContain('setInvitationRequired')
    expect(admin).toContain('clearInvitationRequirement')
    expect(admin).toContain('nextCompetitionTrackOrdinal')
    expect(admin).toContain('duplicateCompetitionTrackKey')
    expect(admin).toContain('clientId: crypto.randomUUID()')
    expect(admin).toContain(':key="track.clientId"')
    expect(admin).toContain('<Switch')
    expect(admin).not.toContain(':key="`${track.key}-${index}`"')
    expect(admin).toContain("ui.trackRequiresAnInvitationCode")
    expect(admin).toContain('track.invitationCode.trim() || null')
    expect(admin).toContain('error.value = competitionTrackErrorMessage')
    expect(teams).toContain('patchCompetitionTeam')
    expect(teams).toContain('tracksEnabled')
    expect(teams).toContain('body: { administration: { trackKey, registrationStatus: team.registrationStatus } }')
    expect(teams).not.toContain('tracksFrozen.value || team.trackKey === trackKey')
  })

  test('exposes participant selection and per-track leaderboard switching', async () => {
    const overview = await sourceFile(new URL('../app/features/competitions/CompetitionOverview.vue', import.meta.url)).text()
    const leaderboard = await sourceFile(new URL('../app/pages/competitions/[id]/leaderboard.vue', import.meta.url)).text()

    expect(overview).toContain('listCompetitionTracks')
    expect(overview).toContain('tracksEnabled.value && !createTrackKey.value')
    expect(overview).toContain('...(tracksEnabled.value')
    expect(overview).toContain('v-if="tracksEnabled && !tracksLoaded"')
    expect(overview).toContain('trackKey: createTrackKey.value')
    expect(overview).toContain('trackInvitationCode: selectedCreateTrack.value?.requiresInvitationCode')
    expect(overview).toContain('createTrackInvitationCode')
    expect(overview).toContain('track.isPublicSelectable')
    expect(overview).toContain('selectedCreateTrack?.requiresInvitationCode')
    expect(overview).toContain('v-else-if="tracksEnabled && selectableTracks.length > 0"')
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
    expect(leaderboard).toContain('tracksEnabled && availableTracks.length > 1')
    expect(leaderboard).toContain('board.snapshot.value?.tracksEnabled ?? true')
    expect(leaderboard).toContain('tracksEnabled.value && selectedAllTracks.value')
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
