import { describe, expect, test } from 'bun:test'
import type { NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse } from '../app/api'
import {
  competitionBroadcastKinds,
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
      'AwdpBreakAttempted',
      'AwdpFixAttempted',
    ])
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
    expect(competitionBroadcastText(event('AwdpBreakAttempted')))
      .toBe('队伍「Alpha」对题目「Web 100」进行了一次攻击操作')
    expect(competitionBroadcastText(event('AwdpFixAttempted')))
      .toBe('队伍「Alpha」对题目「Web 100」提交了一次防御操作')
  })

  test('links challenge broadcasts to the matching challenge', () => {
    expect(competitionBroadcastTargetPath(event('HintPublished', {
      competitionChallengeId: 'challenge-1',
    }))).toBe('/competitions/competition-1/challenges?challenge=challenge-1')
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
    expect(shell).toContain('usesParticipantWorkspace')
    expect(shell).toContain('competition && usesParticipantWorkspace')
    expect(shell).not.toContain("label: '公告/通知'")
    expect(panel).toContain('kinds: competitionBroadcastKinds')
    expect(panel).toContain('competitionEventChanged: () => void load()')
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
