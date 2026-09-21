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
    'TrackSsoIdentityRequired',
    'SsoProviderNotFound',
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
    'TrackSsoIdentityRequired',
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
    'TrackSsoIdentityRequired',
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
    const trackView = await sourceFile(new URL('../app/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdTracksPageView.vue', import.meta.url)).text()
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
    expect(admin).toContain('requiredSsoProviderId: track.requiredSsoProviderId ?? null')
    expect(admin).toContain('updateRequiredSsoProvider')
    expect(admin).toContain('configureSsoProviders')
    expect(admin).toContain('data.ssoProviders ?? []')
    expect(admin).toContain('error.value = competitionTrackErrorMessage')
    expect(trackView.indexOf('v-model="track.name"')).toBeLessThan(
      trackView.indexOf(':model-value="track.isDefault"'),
    )
    expect(trackView.indexOf(':model-value="track.requiredSsoProviderId ?? \'none\'"')).toBeGreaterThan(
      trackView.indexOf(':id="`track-invitation-${track.clientId}`"'),
    )
    expect(trackView).toContain('sso.trackGateNoProviders')
    expect(trackView).toContain('sso.trackGateConfigurationHint')
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
    expect(overview).toContain('trackInvitationCode: null')
    expect(overview).not.toContain('createTrackInvitationCode')
    expect(overview).toContain('track.isPublicSelectable')
    expect(overview).not.toContain(':disabled="track.meetsSsoRequirement === false"')
    expect(overview).toContain('track.requiredSsoProviderId')
    expect(overview).not.toContain('ssoSettingsTarget')
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
    expect(leaderboard).toContain('showLeaderboardHiddenTeams.value')
    expect(leaderboard).toContain('publiclyVisibleTrackKeys.value.has(team.trackKey)')
    expect(leaderboard).toContain("$t('ui.showLeaderboardHiddenTeams')")
    expect(leaderboard).toContain('!track.isInternal && (track.isViewerTrack || track.visibleOnLeaderboard)')
    expect(leaderboard).toContain("const allTracksKey = '__all_tracks__'")
    expect(leaderboard).toContain("<SelectItem :value=\"allTracksKey\">{{ $t('ui.allTracks') }}</SelectItem>")
    expect(leaderboard).toContain('const displayRanks = computed')
    expect(leaderboard).toContain("(right.totalScore ?? 0) - (left.totalScore ?? 0)")
    expect(leaderboard).toContain('displayRank(team)')
    expect(leaderboard).not.toContain("selectedAllTracks.value ? translate(\"ui.trackRank\")")
    expect(leaderboard).toContain('trackName(team.trackKey)')
  })

  test('lets captains update team profile and track while reflecting review state', async () => {
    const overview = await sourceFile(new URL('../app/features/competitions/CompetitionOverview.vue', import.meta.url)).text()
    const myTeam = await sourceFile(new URL('../app/pages/competitions/[id]/my/team.vue', import.meta.url)).text()
    const navigator = await sourceFile(new URL('../app/features/competition/CompetitionChallengeNavigator.vue', import.meta.url)).text()
    const adminTeams = await sourceFile(new URL('../app/pages/admin/competitions/[id]/teams.vue', import.meta.url)).text()

    expect(myTeam).toContain('listCompetitionTracks')
    expect(myTeam).toContain('trackKey: tracksEnabled.value ? renameTrackKey.value : null')
    expect(myTeam).toContain('trackInvitationCode: null')
    expect(overview).toContain('trackInvitationCode: registrationTrack.value?.requiresInvitationCode')
    expect(overview).toContain('registrationTrack.value?.meetsSsoRequirement !== false')
    expect(overview).toContain('<template v-else-if="!myTeam">')
    expect(overview).toContain("practiceOpen ? $t('ui.createPracticeTeam') : $t('ui.signUpNow')")
    expect(overview).toContain("$t('ui.joinWithInvitationCode')")
    expect(overview).toContain('&& myTeam.value')
    expect(overview).toContain('v-if="canSubmitRegistration"')
    expect(overview.indexOf("$t('ui.submitRegistration')")).toBeLessThan(overview.indexOf("$t('ui.myTeam')"))
    expect(myTeam).not.toContain('@click="openRegistration"')
    expect(myTeam).not.toContain('<Dialog :open="registrationOpen"')
    expect(myTeam).toContain('teamAvatarReplace')
    expect(myTeam).toContain('teamAvatarClear')
    expect(myTeam).toContain("team.registrationStatus === 'Pending'")
    expect(myTeam).toContain("updatedTeam.registrationStatus === 'Unregistered'")
    expect(myTeam).toContain("event.kind === 'TeamRegistrationChanged'")
    expect(myTeam).toContain('ui.pendingTeamCannotAccessCompetition')
    expect(navigator).toContain("kind === 'TeamRegistrationChanged'")
    expect(navigator).toContain('if (response?.status === 404) items.value = []')
    expect(adminTeams).toContain('registrationStatusOptions')
    expect(adminTeams).toContain('@select="setRegistrationStatusValue(t, option.value)"')
  })
})
