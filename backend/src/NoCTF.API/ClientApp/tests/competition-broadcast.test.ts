import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import type { NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse } from '../app/api'
import {
  competitionBroadcastKinds,
  competitionBroadcastQueryWindow,
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
    }))).toBe('/competitions/competition-1/challenges/challenge-1')
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

  test('keeps a server-timestamped invalidation inside the latest REST window', () => {
    const startAt = Date.parse('2026-09-01T00:00:00Z')
    const clientNow = Date.parse('2026-09-14T10:00:00Z')
    const serverEventAt = Date.parse('2026-09-14T10:00:08Z')

    expect(competitionBroadcastQueryWindow(startAt, clientNow, serverEventAt)).toEqual({
      from: '2026-09-01T00:00:00.000Z',
      to: '2026-09-14T10:05:00.000Z',
    })
  })

  test('uses a newer notification timestamp beyond the reconnect clock margin', () => {
    const startAt = Date.parse('2026-09-01T00:00:00Z')
    const clientNow = Date.parse('2026-09-14T10:00:00Z')
    const serverEventAt = Date.parse('2026-09-14T10:08:00Z')

    expect(competitionBroadcastQueryWindow(startAt, clientNow, serverEventAt)).toEqual({
      from: '2026-09-01T00:00:00.000Z',
      to: '2026-09-14T10:08:00.000Z',
    })
  })

  test('continues querying current announcements after a competition has finished', () => {
    const startAt = Date.parse('2026-08-01T00:00:00Z')
    const clientNow = Date.parse('2026-09-14T10:00:00Z')

    expect(competitionBroadcastQueryWindow(startAt, clientNow)).toEqual({
      from: '2026-08-15T10:05:00.000Z',
      to: '2026-09-14T10:05:00.000Z',
    })
  })

  test('mounts the compact panel beside challenges and removes the overlapping tab', async () => {
    const challengePage = await sourceFile(
      new URL('../app/pages/competitions/[id]/challenges/[[ccId]].vue', import.meta.url),
    ).text()
    const participantWorkspace = await sourceFile(
      new URL('../app/features/competition/CompetitionParticipantWorkspace.vue', import.meta.url),
    ).text()
    const challengeNavigator = await sourceFile(
      new URL('../app/features/competition/CompetitionChallengeNavigator.vue', import.meta.url),
    ).text()
    const shell = await sourceFile(
      new URL('../app/pages/competitions/[id].vue', import.meta.url),
    ).text()
    const [panel, workspaceNavigation, theme] = await Promise.all([
      sourceFile(new URL('../app/features/competition/CompetitionBroadcastPanel.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/features/competition/CompetitionWorkspaceNavigation.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/assets/css/main.css', import.meta.url)).text(),
    ])

    expect(challengePage).toContain("<component :is=\"CompetitionParticipantWorkspace\"")
    expect(participantWorkspace).toContain("<component :is=\"CompetitionBroadcastPanel\"")
    expect(participantWorkspace).toContain('challenge-workspace')
    expect(participantWorkspace).toContain("<component :is=\"CompetitionWorkspaceNavigation\"")
    expect(participantWorkspace).not.toContain('<ScrollSurface axis="y" class="col-span-full h-full"')
    expect(participantWorkspace).toContain('min-[900px]:grid-cols-1')
    expect(participantWorkspace).toContain('min-[900px]:grid-rows-[fit-content(50%)_minmax(0,1fr)]')
    expect(participantWorkspace).toContain('xl:grid-rows-[fit-content(50%)_minmax(0,1fr)]')
    expect(participantWorkspace).not.toContain('18rem]')
    expect(participantWorkspace).not.toContain('content-start')
    expect(participantWorkspace).toContain("showChallengeNavigator ? 'min-[900px]:h-auto' : 'xl:h-auto'")
    expect(workspaceNavigation).toContain('<Card as="nav"')
    expect(workspaceNavigation).toContain('<ScrollSurface axis="y"')
    expect(panel).toContain('<Card')
    expect(panel).toContain('as="aside"')
    expect(panel).toContain("fill ? 'min-h-0 flex-1' : 'max-h-[50dvh]'")
    expect(panel).not.toContain('max-h-[32rem]')
    expect(theme).toContain("[data-slot='default-layout-foreground']:has(> main [data-contained-workspace-page])")
    expect(theme).toContain("> [data-slot='competition-page-route'] { height: 100%;")
    expect(theme).not.toContain(":has(> main > [data-contained-workspace-page])")
    expect(challengePage).toContain("<component :is=\"CompetitionChallengeDetail\"")
    expect(challengeNavigator).toContain("@update:model-value=\"selectChallenge\"")
    expect(shell).not.toContain("<component :is=\"AppWorkspaceNav\"")
    expect(shell).toContain('v-else-if="competition"')
    expect(shell).not.toContain("label: '公告/通知'")
    expect(panel).toContain('kinds: competitionBroadcastKinds')
    expect(panel).toContain('competitionEventChanged: notification => {')
    expect(panel).toContain('if (!isCompetitionBroadcastKind(notification.kind)) return')
    expect(panel).toContain('onReconnected: () => void refreshLatest()')
    expect(panel).toContain('const refreshLatest = createTrailingRefresh(load)')
    expect(panel).toContain('mergeCompetitionBroadcasts(items.value, data.items ?? [])')
    expect(panel).not.toContain('<TransitionGroup')
    expect(panel).toContain('v-bind="broadcastMotionAttributes(event)"')
    expect(panel).toContain("motionAttributes('list-enter')")
    expect(panel).toContain(':key="competitionBroadcastIdentity(event)"')
    expect(panel).not.toContain('.broadcast-move')
    expect(panel).toContain('const initialLoad = !initialized.value')
    expect(panel).toContain("now < startAt || status === 'Draft' || status === 'Visible' || status === 'Published'")
    expect(panel).not.toContain("status === 'Finished' && initialized.value")
    expect(panel).not.toContain('competition.endTime')
    expect(panel).toContain('latestNotifiedAt = Math.max(latestNotifiedAt, notifiedAt)')
    expect(panel).toContain('competitionBroadcastQueryWindow(')
  })
})

describe('competition administration entry', () => {
  test('loads the administrator list inside the public competition browser without role probing', async () => {
    const [page, overview] = await Promise.all([
      sourceFile(new URL('../app/pages/competitions/[id]/index.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/features/competitions/CompetitionOverview.vue', import.meta.url)).text(),
    ])

    expect(page).toContain('adminListCompetitions({ query: { includeDeleted: true } })')
    expect(page).toContain("v-if=\"isAdministrator\"")
    expect(page).toContain('@click="openCreateDialog"')
    expect(page).toContain(':is="CreateCompetitionDialog"')
    expect(page).toContain('@created="handleCompetitionCreated"')
    expect(page).toContain('const { create: _create, ...query } = route.query')
    expect(page).not.toContain('to="/admin/competitions/new"')
    expect(overview).toContain("$t('competitions.label.manageCompetition')")
    expect(overview.indexOf("$t('competitions.label.myTeam')")).toBeLessThan(overview.indexOf("$t('competitions.label.manageCompetition')"))
    expect(page).not.toContain('adminListCheatIncidents')
    expect(page).not.toContain('probeRole')
  })

  test('creates competitions in the canonical dialog flow', async () => {
    const dialog = await sourceFile(
      new URL('../app/features/competitions/CreateCompetitionDialog.vue', import.meta.url),
    ).text()
    expect(dialog).toContain('<Dialog :open="open" @update:open="setOpen">')
    expect(dialog).toContain('sm:max-w-3xl')
    expect(dialog).toContain('<FileUpload')
    expect(dialog).toContain('accept="image/jpeg,image/png,image/webp"')
    expect(dialog).toContain('adminCompetitionPosterReplace({')
    expect(dialog).toContain('createdCompetition.value = data')
    expect(dialog).toContain('completeCreation()')
    expect(dialog).toContain('<ScrollSurface axis="y"')
    expect(dialog).not.toContain('navigateTo(`/admin/competitions/')
  })
})
