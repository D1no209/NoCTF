import { describe, expect, test } from 'bun:test'
import type { NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse } from '../app/api'
import {
  competitionBroadcastKinds,
  competitionBroadcastIdentity,
  deduplicateCompetitionBroadcasts,
  isCompetitionBroadcastKind,
  mergeCompetitionBroadcasts,
  competitionBroadcastTargetPath,
  competitionBroadcastText,
} from '../app/utils/competition-broadcast'

function event(
  kind: NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse['kind'],
  values: Partial<NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse> = {},
): NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse {
  return {
    id: 'event-1',
    competitionId: 'competition-1',
    kind,
    teamDisplayName: 'Alpha',
    challengeTitle: 'Web 100',
    ...values,
  }
}

describe('competition broadcast projection', () => {
  test('contains only the compact public broadcast event classes', () => {
    expect(competitionBroadcastKinds).toEqual([
      'FirstBloodAwarded',
      'SecondBloodAwarded',
      'ThirdBloodAwarded',
      'TeamBanned',
      'TeamBanCorrectionPublished',
      'HintPublished',
      'ChallengeDescriptionUpdated',
      'ChallengePublished',
      'AwdpBreakResolved',
      'AwdpFixResolved',
      'AnnouncementPublished',
    ])
    expect(isCompetitionBroadcastKind('FirstBloodAwarded')).toBe(true)
    expect(isCompetitionBroadcastKind('AwdpBreakAttempted')).toBe(false)
    expect(isCompetitionBroadcastKind('AwdpFixAttempted')).toBe(false)
    expect(isCompetitionBroadcastKind('AwdpBreakResolved')).toBe(true)
    expect(isCompetitionBroadcastKind('AwdpFixResolved')).toBe(true)
    expect(isCompetitionBroadcastKind('QuestionOpened')).toBe(false)
  })

  test('renders the requested concise messages', () => {
    expect(competitionBroadcastText(event('FirstBloodAwarded')))
      .toBe('队伍「Alpha」获得题目「Web 100」一血')
    expect(competitionBroadcastText(event('SecondBloodAwarded')))
      .toBe('队伍「Alpha」获得题目「Web 100」二血')
    expect(competitionBroadcastText(event('ThirdBloodAwarded')))
      .toBe('队伍「Alpha」获得题目「Web 100」三血')
    expect(competitionBroadcastText(event('TeamBanned')))
      .toBe('队伍「Alpha」由于「作弊」被封禁')
    expect(competitionBroadcastText(event('TeamBanCorrectionPublished')))
      .toBe('队伍「Alpha」申诉成功，封禁已撤销')
    expect(competitionBroadcastText(event('HintPublished')))
      .toBe('题目「Web 100」发布了新的提示')
    expect(competitionBroadcastText(event('ChallengeDescriptionUpdated')))
      .toBe('题目「Web 100」已更新描述')
    expect(competitionBroadcastText(event('ChallengePublished')))
      .toBe('题目「Web 100」已开放')
    expect(competitionBroadcastText(event('AwdpBreakResolved', {
      gameplayFactState: 'Completed',
      gameplayFactResult: 'Correct',
    }))).toBe('队伍「Alpha」攻击题目「Web 100」成功')
    expect(competitionBroadcastText(event('AwdpBreakResolved', {
      gameplayFactState: 'Completed',
      gameplayFactResult: 'Wrong',
    }))).toBe('队伍「Alpha」攻击题目「Web 100」失败')
    expect(competitionBroadcastText(event('AwdpFixResolved', {
      gameplayFactState: 'Completed',
      gameplayFactResult: 'Correct',
    }))).toBe('队伍「Alpha」防御题目「Web 100」成功')
    expect(competitionBroadcastText(event('AwdpFixResolved', {
      gameplayFactState: 'PlatformFailed',
      gameplayFactResult: null,
    }))).toBe('队伍「Alpha」防御题目「Web 100」失败')
    expect(competitionBroadcastText(event('AnnouncementPublished')))
      .toBe('赛事发布了新通知')
  })

  test('links challenge broadcasts to the matching challenge', () => {
    expect(competitionBroadcastTargetPath(event('HintPublished', {
      competitionChallengeId: 'challenge-1',
    }))).toBe('/competitions/competition-1/challenges?challenge=challenge-1')
  })

  test('links published notices to their notification detail', () => {
    expect(competitionBroadcastTargetPath(event('AnnouncementPublished', {
      questionId: 'notification-1',
    }))).toBe('/notifications?notification=notification-1')
  })

  test('collapses the staff and public copy of one ban broadcast', () => {
    const occurredAt = '2026-08-26T23:50:25Z'
    const duplicated = [
      event('TeamBanned', { id: 'staff', teamId: 'team-1', occurredAt }),
      event('TeamBanned', { id: 'public', teamId: 'team-1', occurredAt }),
    ]
    expect(deduplicateCompetitionBroadcasts(duplicated).map(item => item.id)).toEqual(['staff'])
  })

  test('preserves existing row identity while prepending a realtime event', () => {
    const existing = event('HintPublished', {
      id: 'existing',
      competitionChallengeId: 'challenge-1',
      occurredAt: '2026-08-27T14:01:00Z',
    })
    const next = event('FirstBloodAwarded', {
      id: 'next',
      competitionChallengeId: 'challenge-1',
      occurredAt: '2026-08-27T14:02:00Z',
    })
    const refreshedExisting = { ...existing, id: 'duplicate-copy' }

    const merged = mergeCompetitionBroadcasts([existing], [next, refreshedExisting])

    expect(merged.map(item => item.id)).toEqual(['next', 'existing'])
    expect(merged[1]).toBe(existing)
    expect(competitionBroadcastIdentity(refreshedExisting))
      .toBe(competitionBroadcastIdentity(existing))
  })

  test('mounts the compact panel beside challenges and removes the overlapping tab', async () => {
    const challengePage = await Bun.file(
      new URL('../app/pages/competitions/[id]/challenges/index.vue', import.meta.url),
    ).text()
    const participantWorkspace = await Bun.file(
      new URL('../app/components/competition/CompetitionParticipantWorkspace.vue', import.meta.url),
    ).text()
    const challengeNavigator = await Bun.file(
      new URL('../app/components/competition/CompetitionChallengeNavigator.vue', import.meta.url),
    ).text()
    const shell = await Bun.file(
      new URL('../app/pages/competitions/[id].vue', import.meta.url),
    ).text()
    const panel = await Bun.file(
      new URL('../app/components/competition/CompetitionBroadcastPanel.vue', import.meta.url),
    ).text()

    expect(challengePage).toContain('<CompetitionParticipantWorkspace')
    expect(participantWorkspace).toContain('<CompetitionBroadcastPanel')
    expect(participantWorkspace).toContain('xl:grid-cols-[15rem_minmax(0,1fr)_19rem]')
    expect(participantWorkspace).toContain('<CompetitionWorkspaceNavigation')
    expect(challengePage).toContain('<CompetitionChallengeDetail')
    expect(challengeNavigator).toContain("@click=\"emit('select', challenge.id!)\"")
    expect(shell).not.toContain('<AppWorkspaceNav')
    expect(shell).toContain('v-else-if="competition"')
    expect(shell).not.toContain("label: '公告/通知'")
    expect(panel).toContain('kinds: competitionBroadcastKinds')
    expect(panel).toContain('competitionEventChanged: notification => {')
    expect(panel).toContain('if (!isCompetitionBroadcastKind(notification.kind)) return')
    expect(panel).toContain('onReconnected: () => void refreshLatest()')
    expect(panel).toContain('const refreshLatest = createTrailingRefresh(load)')
    expect(panel).toContain('mergeCompetitionBroadcasts(items.value, data.items ?? [])')
    expect(panel).toContain('name="broadcast"')
    expect(panel).toContain(':key="competitionBroadcastIdentity(event)"')
    expect(panel).toContain('@media (prefers-reduced-motion: reduce)')
    expect(panel).toContain('const initialLoad = !initialized.value')
    expect(panel).toContain("now < startAt || status === 'Draft' || status === 'Visible' || status === 'Published'")
    expect(panel).toContain("status === 'Finished' && initialized.value && loadedStatus === status")
    expect(panel).toContain('const queryEnd = status === \'Finished\' && competition.endTime')
  })
})

describe('competition administration role label', () => {
  test('uses the strongly typed role returned by the list endpoint', async () => {
    const page = await Bun.file(
      new URL('../app/pages/admin/competitions/index.vue', import.meta.url),
    ).text()

    expect(page).toContain("return competition.administrationRole ?? 'Observer'")
    expect(page).not.toContain('adminListCheatIncidents')
    expect(page).not.toContain('probeRole')
  })
})
