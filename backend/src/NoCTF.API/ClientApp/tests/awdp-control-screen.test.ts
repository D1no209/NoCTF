import { describe, expect, test } from 'bun:test'
import type {
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
  NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse,
} from '../app/api'
import {
  awdpControlEvents,
  awdpCurrentRoundEvents,
  awdpCurrentRoundOperationMetrics,
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

const roundId = '00000000-0000-0000-0000-000000000005'
const catalog: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse = {
  competitionId,
  revision: 1,
  items: [{ id: challengeId, title: 'Pwn-02', direction: 'PWN', category: 'PWN', order: 1, published: true }],
}
const schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse = {
  competitionId,
  mode: 'Awdp',
  revision: 1,
  challengeCatalogRevision: 1,
  rounds: [{
    id: roundId,
    number: 12,
    startAt: '2026-08-19T11:57:00Z',
    endAt: '2026-08-19T12:02:00Z',
    settledAt: '2026-08-19T12:02:00Z',
    state: 'Settled',
  }],
  columns: [{ index: 0, competitionChallengeId: challengeId, roundId }],
}
const snapshot: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse = {
  competitionId,
  version: 1,
  schemaRevision: 1,
  generatedAt: '2026-08-19T12:02:00Z',
  currentRoundId: roundId,
  tracks: [
    { key: 'open', name: 'Open', isInternal: false, visibleOnLeaderboard: true },
    { key: 'staff', name: 'Staff', isInternal: true, visibleOnLeaderboard: true },
  ],
  teams: [
    {
      rank: 2,
      teamId,
      teamName: 'BlueWhale',
      trackKey: 'open',
      rankingState: 'Eligible',
      totalScore: 900,
      attackScore: 5_500,
      defenseScore: 4_400,
      challengeScores: [{ competitionChallengeId: challengeId, attackScore: 3_300, defenseScore: 2_200 }],
      slots: [{
        columnIndex: 0,
        scoreState: 'Settled',
        earnedPoints: 900,
        deductedPoints: 0,
        netPoints: 900,
        entryCount: 2,
        breakdown: [
          { kind: 'Attack', successfulCount: 1, attemptCount: 1, earnedPoints: 500, deductedPoints: 0, netPoints: 500 },
          { kind: 'Defense', successfulCount: 1, attemptCount: 1, earnedPoints: 400, deductedPoints: 0, netPoints: 400 },
        ],
      }],
    },
    {
      rank: 1,
      teamId: '00000000-0000-0000-0000-000000000004',
      teamName: 'Internal',
      trackKey: 'staff',
      rankingState: 'Eligible',
      totalScore: 99_999,
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
    expect(awdpPublicEntries(snapshot).map(entry => entry.teamName)).toEqual(['BlueWhale'])
    expect(awdpRankedEntries(snapshot, new Map([[teamId, 3]]))[0])
      .toMatchObject({ rank: 2, attackScore: 5_500, defenseScore: 4_400, trend: 'up' })

    const events = awdpControlEvents([
      event('AwdpBreakResolved', { gameplayFactState: 'Completed', gameplayFactResult: 'Correct' }),
      event('AwdpFixResolved', { gameplayFactState: 'Completed', gameplayFactResult: 'Wrong' }),
    ])
    expect(awdpTeamChallengeStates(catalog, schema, awdpPublicEntries(snapshot)[0]!, events)[0])
      .toMatchObject({
        title: 'Pwn-02',
        attackScore: 3_300,
        defenseScore: 2_200,
        attackOutcome: 'success',
        defenseOutcome: 'failure',
      })
  })

  test('preserves null ranks for banned or disqualified teams instead of inventing public places', () => {
    const ineligibleTeamId = '00000000-0000-0000-0000-000000000006'
    const ineligibleSnapshot: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse = {
      ...snapshot,
      teams: [
        ...(snapshot.teams ?? []),
        {
          rank: null,
          teamId: ineligibleTeamId,
          teamName: 'Banned team',
          trackKey: 'open',
          rankingState: 'Banned',
          totalScore: 0,
          slots: [],
        },
      ],
    }

    expect(awdpRankedEntries(ineligibleSnapshot, new Map([[ineligibleTeamId, 1]])).at(-1))
      .toMatchObject({ rank: null, rankingState: 'Banned', trend: 'steady' })
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

  test('reads current-round operation metrics from the authoritative matrix', () => {
    const previousRoundId = '00000000-0000-0000-0000-000000000006'
    const twoRoundSchema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse = {
      ...schema,
      rounds: [
        {
          id: previousRoundId,
          number: 11,
          startAt: '2026-08-19T11:52:00Z',
          endAt: '2026-08-19T11:57:00Z',
          settledAt: '2026-08-19T11:57:00Z',
          state: 'Settled',
        },
        ...schema.rounds!,
      ],
      columns: [
        { index: 1, competitionChallengeId: challengeId, roundId: previousRoundId },
        ...schema.columns!,
      ],
    }
    const currentSnapshot: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse = {
      ...snapshot,
      teams: [{
        ...snapshot.teams![0]!,
        slots: [
          {
            columnIndex: 1,
            scoreState: 'Settled',
            earnedPoints: 10_000,
            deductedPoints: 0,
            netPoints: 10_000,
            entryCount: 40,
            breakdown: [
              { kind: 'Attack', successfulCount: 10, attemptCount: 20, earnedPoints: 5_000, deductedPoints: 0, netPoints: 5_000 },
              { kind: 'Defense', successfulCount: 10, attemptCount: 20, earnedPoints: 5_000, deductedPoints: 0, netPoints: 5_000 },
            ],
          },
          {
            columnIndex: 0,
            scoreState: 'Pending',
            earnedPoints: null,
            deductedPoints: null,
            netPoints: null,
            entryCount: 3,
            breakdown: [
              { kind: 'Attack', successfulCount: 1, attemptCount: 2, earnedPoints: 0, deductedPoints: 0, netPoints: 0 },
              { kind: 'Defense', successfulCount: 0, attemptCount: 1, earnedPoints: 0, deductedPoints: 0, netPoints: 0 },
            ],
          },
        ],
      }],
    }

    expect(awdpCurrentRoundOperationMetrics(currentSnapshot, twoRoundSchema)).toEqual({
      attack: { success: 1, total: 2 },
      defense: { success: 0, total: 1 },
    })
    expect(awdpCurrentRoundOperationMetrics(currentSnapshot, twoRoundSchema, teamId)).toEqual({
      attack: { success: 1, total: 2 },
      defense: { success: 0, total: 1 },
    })
    expect(awdpCurrentRoundOperationMetrics(currentSnapshot, twoRoundSchema, crypto.randomUUID())).toEqual({
      attack: { success: 0, total: 0 },
      defense: { success: 0, total: 0 },
    })
  })

  test('keeps current challenge status isolated from earlier rounds', () => {
    const current = awdpControlEvents([
      event('AwdpBreakResolved', {
        occurredAt: '2026-08-19T11:56:59Z',
        gameplayFactState: 'Completed',
        gameplayFactResult: 'Correct',
      }),
      event('AwdpFixResolved', {
        occurredAt: '2026-08-19T12:01:59Z',
        gameplayFactState: 'Completed',
        gameplayFactResult: 'Wrong',
      }),
      event('AwdpBreakResolved', {
        occurredAt: '2026-08-19T12:02:00Z',
        gameplayFactState: 'Completed',
        gameplayFactResult: 'Correct',
      }),
    ])

    expect(awdpCurrentRoundEvents(current, snapshot, schema).map(item => item.action))
      .toEqual(['defense'])
  })

  test('advances the displayed round clock from the generated leaderboard snapshot', () => {
    const timedSchema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse = {
      ...schema,
      rounds: [{
        id: roundId,
        number: 12,
        startAt: '2026-08-19T11:57:00Z',
        endAt: '2026-08-19T12:02:00Z',
        settledAt: null,
        state: 'Running',
      }],
    }
    const timedSnapshot = { ...snapshot, generatedAt: '2026-08-19T12:00:00Z' }

    expect(awdpRoundClock(timedSnapshot, timedSchema, Date.parse('2026-08-19T12:00:30Z'), true))
      .toEqual({ currentRound: 12, remainingSeconds: 90 })
    expect(awdpRoundClock(timedSnapshot, timedSchema, Date.parse('2026-08-19T12:02:30Z'), true))
      .toEqual({ currentRound: 12, remainingSeconds: 0 })
    expect(awdpRoundClock(timedSnapshot, timedSchema, Date.parse('2026-08-19T12:02:30Z'), false))
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
    expect(page).toContain('useScoreboardMatrix(competitionId)')
    expect(page).toContain('board.refresh({ catalog: true, schema: true, snapshot: true })')
    expect(page).toContain('listCompetitionEvents({')
    expect(page).toContain('competitionEventChanged: () => void refreshLatest()')
    expect(page).toContain('playbackQueue')
    expect(page).toContain('PLAYBACK_DURATION_MS = 5_400')
    expect(page).toContain('carouselTimer = setInterval(selectNextTeam, 8_000)')
    expect(page).toContain("directionIcon(challenge.direction)")
    expect(page).toContain('operationMetrics.attack.success')
    expect(page).toContain('operationMetrics.defense.success')
    expect(page).toContain("isChallengeFocused(challenge.competitionChallengeId)")
    expect(page).toContain('class="team-tab-list"')
    expect(page).toContain('grid-template-columns:30px minmax(0,1fr) 30px')
    expect(page).toContain(':disabled="rankedEntries.length <= 1"')
    expect(page).toContain('grid-template-rows:36px 45px 150px')
    expect(page).toContain('.challenge-strip{height:150px}')
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
