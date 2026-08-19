import { describe, expect, test } from 'bun:test'
import type {
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
  NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse,
} from '../app/api'
import {
  awdpControlEvents,
  awdpOperationMetrics,
  awdpPlaybackEvents,
  awdpPublicEntries,
  awdpRankedEntries,
  awdpRoundClock,
  awdpTeamChallengeStates,
  calculateAwdpCanvasScale,
  normalizeAwdpControlEvent,
  reconcileAwdpControlEvents,
} from '../app/utils/awdp-control-screen'

const competitionId = '00000000-0000-0000-0000-000000000001'
const challengeId = '00000000-0000-0000-0000-000000000002'
const teamId = '00000000-0000-0000-0000-000000000003'

function event(
  kind: NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse['kind'],
  values: Partial<NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse> = {},
): NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse {
  return {
    id: crypto.randomUUID(),
    competitionId,
    competitionChallengeId: challengeId,
    challengeTitle: 'Pwn-02',
    teamId,
    teamDisplayName: 'BlueWhale',
    occurredAt: '2026-08-19T12:00:00Z',
    gameplayFactId: crypto.randomUUID(),
    kind,
    ...values,
  }
}

const leaderboard: NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse = {
  competitionId,
  currentRound: 12,
  settledThroughRound: 11,
  tracks: [
    { key: 'open', name: 'Open', isInternal: false, visibleOnLeaderboard: true },
    { key: 'staff', name: 'Staff', isInternal: true, visibleOnLeaderboard: true },
  ],
  challenges: [{
    competitionChallengeId: challengeId,
    title: 'Pwn-02',
    direction: 'PWN',
    currentBreakScore: 480,
    currentFixScore: 420,
  }],
  entries: [
    {
      rank: 2,
      teamId,
      teamName: 'BlueWhale',
      trackKey: 'open',
      score: 900,
      attackScore: 500,
      defenseScore: 400,
      cells: [{ competitionChallengeId: challengeId, attackScore: 500, defenseScore: 400 }],
    },
    {
      rank: 1,
      teamId: '00000000-0000-0000-0000-000000000004',
      teamName: 'Internal',
      trackKey: 'staff',
      score: 99_999,
    },
  ],
}

describe('AWDP control screen data adapter', () => {
  test('normalizes the four public operation outcomes without inventing victim teams', () => {
    const pendingAttack = normalizeAwdpControlEvent(event('AwdpBreakAttempted'))
    const attackSuccess = normalizeAwdpControlEvent(event('AwdpBreakResolved', {
      gameplayFactState: 'Completed',
      gameplayFactResult: 'Correct',
    }))
    const defenseFailure = normalizeAwdpControlEvent(event('AwdpFixResolved', {
      gameplayFactState: 'Completed',
      gameplayFactResult: 'Wrong',
    }))
    const platformFailure = normalizeAwdpControlEvent(event('AwdpFixResolved', {
      gameplayFactState: 'PlatformFailed',
    }))

    expect(pendingAttack).toMatchObject({ action: 'attack', outcome: 'pending' })
    expect(attackSuccess).toMatchObject({ action: 'attack', outcome: 'success' })
    expect(defenseFailure).toMatchObject({ action: 'defense', outcome: 'failure' })
    expect(platformFailure).toMatchObject({ action: 'defense', outcome: 'failure' })
    expect(JSON.stringify([pendingAttack, attackSuccess, defenseFailure, platformFailure]))
      .not.toContain('victim')
  })

  test('baselines history and preserves FIFO order for a burst of twenty resolved events', () => {
    const historical = awdpControlEvents([event('AwdpBreakResolved', {
      id: '00000000-0000-0000-0000-000000000010',
      gameplayFactState: 'Completed',
      gameplayFactResult: 'Correct',
    })])
    const baseline = reconcileAwdpControlEvents(null, historical)
    expect(baseline.newEvents).toEqual([])

    const burst = Array.from({ length: 20 }, (_, index) => event(
      index % 2 === 0 ? 'AwdpBreakResolved' : 'AwdpFixResolved',
      {
        id: `00000000-0000-0000-0000-${(index + 20).toString().padStart(12, '0')}`,
        occurredAt: `2026-08-19T12:00:${index.toString().padStart(2, '0')}Z`,
        gameplayFactState: 'Completed',
        gameplayFactResult: index % 3 === 0 ? 'Wrong' : 'Correct',
      },
    ))
    const reconciliation = reconcileAwdpControlEvents(
      baseline.seenIds,
      awdpControlEvents([...burst].reverse().concat(historical)),
    )

    expect(reconciliation.newEvents).toHaveLength(20)
    expect(reconciliation.newEvents.map(item => item.occurredAt))
      .toEqual(burst.map(item => item.occurredAt))
    expect(awdpPlaybackEvents(reconciliation.newEvents)).toHaveLength(20)
    expect(reconcileAwdpControlEvents(reconciliation.seenIds, burst).newEvents).toEqual([])
  })

  test('uses settled public scores and excludes internal tracks', () => {
    expect(awdpPublicEntries(leaderboard).map(entry => entry.teamName)).toEqual(['BlueWhale'])
    expect(awdpRankedEntries(leaderboard, new Map([[teamId, 3]]))[0])
      .toMatchObject({ rank: 2, attackScore: 500, defenseScore: 400, trend: 'up' })

    const events = awdpControlEvents([
      event('AwdpBreakResolved', { gameplayFactState: 'Completed', gameplayFactResult: 'Correct' }),
      event('AwdpFixResolved', { gameplayFactState: 'Completed', gameplayFactResult: 'Wrong' }),
    ])
    expect(awdpTeamChallengeStates(leaderboard, awdpPublicEntries(leaderboard)[0]!, events)[0])
      .toMatchObject({
        title: 'Pwn-02',
        attackScore: 500,
        defenseScore: 400,
        attackOutcome: 'success',
        defenseOutcome: 'failure',
      })
  })

  test('counts each attack or defense operation once while pairing attempted and resolved events', () => {
    const attackFactId = crypto.randomUUID()
    const defenseFactId = crypto.randomUUID()
    const events = awdpControlEvents([
      event('AwdpBreakAttempted', { gameplayFactId: attackFactId }),
      event('AwdpBreakResolved', {
        gameplayFactId: attackFactId,
        occurredAt: '2026-08-19T12:00:01Z',
        gameplayFactState: 'Completed',
        gameplayFactResult: 'Correct',
      }),
      event('AwdpFixAttempted', { gameplayFactId: defenseFactId }),
      event('AwdpFixResolved', {
        gameplayFactId: defenseFactId,
        occurredAt: '2026-08-19T12:00:02Z',
        gameplayFactState: 'Completed',
        gameplayFactResult: 'Wrong',
      }),
    ])

    expect(awdpOperationMetrics(events)).toEqual({
      attack: { success: 1, total: 1 },
      defense: { success: 0, total: 1 },
    })
  })

  test('advances the displayed round clock from the generated leaderboard snapshot', () => {
    const timedLeaderboard = {
      ...leaderboard,
      generatedAt: '2026-08-19T12:00:00Z',
      currentRound: 12,
      roundDurationSeconds: 300,
      currentRoundRemainingSeconds: 120,
    }

    expect(awdpRoundClock(timedLeaderboard, Date.parse('2026-08-19T12:00:30Z'), true))
      .toEqual({ currentRound: 12, remainingSeconds: 90 })
    expect(awdpRoundClock(timedLeaderboard, Date.parse('2026-08-19T12:02:30Z'), true))
      .toEqual({ currentRound: 13, remainingSeconds: 270 })
    expect(awdpRoundClock(timedLeaderboard, Date.parse('2026-08-19T12:02:30Z'), false))
      .toEqual({ currentRound: 12, remainingSeconds: 120 })
  })

  test('keeps a fixed 1920 by 1080 virtual canvas with uniform letterboxed scaling', () => {
    expect(calculateAwdpCanvasScale(1920, 1080)).toBe(1)
    expect(calculateAwdpCanvasScale(2560, 1440)).toBeCloseTo(4 / 3)
    expect(calculateAwdpCanvasScale(1080, 1920)).toBeCloseTo(1080 / 1920)
    expect(calculateAwdpCanvasScale(0, 0)).toBe(1)
  })
})

describe('AWDP control screen implementation contract', () => {
  test('uses generated SDK data, FIFO playback, SignalR invalidation, and four code-native animations', async () => {
    const page = await Bun.file(
      new URL('../app/pages/competitions/[id]/awdp-live.vue', import.meta.url),
    ).text()
    const shell = await Bun.file(
      new URL('../app/pages/competitions/[id].vue', import.meta.url),
    ).text()
    const stage = await Bun.file(
      new URL('../app/components/awdp-control/AwdpEventStage.vue', import.meta.url),
    ).text()
    const ticker = await Bun.file(
      new URL('../app/components/awdp-control/AwdpEventTicker.vue', import.meta.url),
    ).text()

    expect(page).toContain('definePageMeta({ layout: false })')
    expect(page).toContain('getLeaderboardEndpoint({ path: { competitionId } })')
    expect(page).toContain('listCompetitionEvents({')
    expect(page).toContain('competitionEventChanged: () => void refreshLatest()')
    expect(page).toContain('playbackQueue')
    expect(page).toContain('PLAYBACK_DURATION_MS = 5_400')
    expect(page).toContain('carouselTimer = setInterval(selectNextTeam, 8_000)')
    expect(page).toContain("directionIcon(challenge.direction)")
    expect(page).toContain('operationMetrics.attack.success')
    expect(page).toContain('operationMetrics.defense.success')
    expect(page).toContain("isChallengeFocused(challenge.competitionChallengeId)")
    expect(page).not.toContain('totalAttackScore')
    expect(page).not.toContain('totalDefenseScore')
    expect(page).toContain('width:1920px')
    expect(page).toContain('height:1080px')
    expect(page).not.toContain('$fetch(')
    expect(page).not.toContain('/api/v1')
    expect(page).not.toContain('.mp4')
    expect(shell).toContain("competition.value?.mode === 'Awdp'")
    expect(shell).toContain('`${base}/awdp-live`')
    expect(stage).toContain('AwdpAttackSuccessAnimation')
    expect(stage).toContain('AwdpAttackFailureAnimation')
    expect(stage).toContain('AwdpDefenseSuccessAnimation')
    expect(stage).toContain('AwdpDefenseFailureAnimation')
    expect(ticker).toContain('requestAnimationFrame(tick)')
    expect(ticker).not.toContain('<marquee')
    expect(ticker).not.toContain('@keyframes marquee')
  })
})
