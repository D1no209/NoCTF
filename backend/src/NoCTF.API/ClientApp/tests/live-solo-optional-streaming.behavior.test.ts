import * as soloNavigation from '../app/features/live-solo/navigation'
import { expect, test } from 'bun:test'
import { computed, effectScope, markRaw, ref, shallowRef, watch } from 'vue'
import { settingsDraft, settingFields, validSettings } from '../app/features/live-solo/settings-draft'
import { message, translate } from '../app/utils/i18n'
import { canJudgeLiveSolo, canLockLiveSoloRoster, canPlayLiveSolo, formatRoundClock, matchStateKey, roundRemainingSeconds } from '../app/features/live-solo/live-solo-state'
import { canManageLiveSolo } from '../app/features/live-solo/settings-draft'
import { resultPresentation } from '../app/features/live-solo/result-presentation'

const source = await Bun.file(new URL('../app/features/live-solo/useLiveSoloMatch.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')

function fixture(policy: boolean, prepared?: boolean, captainId = 'player', management = true) {
  let mediaReads = 0, mediaPreparations = 0, disconnected = 0, readyCalls = 0
  const preparationWrites: any[] = []
  const candidates: any[] = [{ id: 'question-list', name: 'Synthetic list', available: true, concurrencyStamp: 'group-stamp', selectionRevision: 'selection-stamp', risks: [] }]
  const config = { platformStreamingEnabled: policy }
  const round = { id: 'round', state: 'Preparing', platformStreamingEnabled: prepared }
  const match = { id: 'match', state: 'Preparing', concurrencyStamp: 'match-stamp', currentRoundId: prepared === undefined ? null : 'round',
    rosters: [{ teamId: 'left', userIds: ['player'], locked: true }, { teamId: 'right', userIds: ['opponent'], locked: true }] }
  const screen = { connected: ref(false), connecting: ref(false), publishing: ref(false), capturePending: ref(false),
    error: ref(null), localStream: ref(null), remoteStreams: ref(new Map()),
    disconnect: async () => { disconnected++ } }
  const workspaceQueries: boolean[] = []
  const deps = { ...soloNavigation, translate, computed, markRaw, ref, shallowRef, watch, message, canJudgeLiveSolo, canLockLiveSoloRoster,
    canPlayLiveSolo, formatRoundClock, matchStateKey, roundRemainingSeconds, canManageLiveSolo,
    competitionContextKey: Symbol(), inject: () => ({ competition: ref({ administrationRole: 'Owner', status: 'Running' }) }),
    useRoute: () => ({ path: management ? '/admin/competitions/contest/live-solo/matches/match' : '/competitions/contest/live-solo/matches/match', params: { id: 'contest', matchId: 'match' } }), useRouter: () => ({ push: async () => {} }),
    useAuth: () => ({ user: ref({ userId: 'player', userName: 'Synthetic player' }) }),
    useLiveSoloHub: () => {}, onMounted: () => {}, onBeforeRouteLeave: () => {}, onScopeDispose: () => {},
    useLiveSoloScreen: () => screen, parseLiveSoloError: () => ({ displayMessage: message('liveSolo.error.load') }),
    getLiveSoloWorkspace: async ({ query }: any) => { workspaceQueries.push(query.staff); return { data: { match, round: prepared === undefined ? null : { ...round }, allowedOperations: [], missingConditions: [] } } }, getLiveSoloPlayerPolicy: async () => ({ data: config }),
    resultPresentation, LiveSoloReview: {},
    getMyTeamEndpoint: async () => ({ data: { id: 'left', captainId, memberIds: ['player'] } }),
    listLiveSoloQuestionGroupCandidates: async () => ({ data: { items: candidates } }),
    listLiveSoloQuestionGroups: async () => ({ data: { items: [{ id: 'question-list', questions: [{ competitionChallengeId: 'entry' }] }] } }),
    listLiveSoloQuestionPool: async () => ({ data: { items: [{ id: 'entry', title: 'Synthetic preview', description: 'Staff only' }] } }),
    prepareLiveSoloRound: async (options: any) => { preparationWrites.push(options.body); return {} },
    confirmLiveSoloReady: async () => { readyCalls++; return {} },
    getLiveSoloRound: async () => ({ data: { ...round } }), userProfileGet: async () => ({ data: null }),
    getLiveSoloMedia: async () => { mediaReads++; return { data: { state: 'Ready', generation: 'generation', members: [] } } },
    prepareLiveSoloMedia: async () => { mediaPreparations++; return {} },
    LiveSoloQuestions: {}, CompetitionParticipantWorkspace: {}, LiveSoloProgramControl: {},
  }
  const factory = new Function('deps', `const { ${Object.keys(deps).join(',')} } = deps; ${compiled}; return useLiveSoloMatch;`)(deps)
  const scope = effectScope(), state = scope.run(() => factory())!
  return { state, config, round, match, workspaceQueries, mediaReads: () => mediaReads, mediaPreparations: () => mediaPreparations,
    candidates, preparationWrites, readyCalls: () => readyCalls, disconnected: () => disconnected, stop: () => scope.stop() }
}

test('an organizer opening the player workspace requests player projection and sees no judging controls', async () => {
  const f = fixture(false, false, 'player', false)
  try {
    await f.state.load()
    expect(f.state.staff.value).toBe(false)
    expect(f.state.judge.value).toBe(false)
    expect(f.state.canCorrect.value).toBe(false)
    expect(f.workspaceQueries).toEqual([false])
  } finally { f.stop() }
})

test('non-streaming preparation skips media APIs and cannot prepare a room', async () => {
  const f = fixture(false)
  try {
    await f.state.load(); await f.state.prepareMedia()
    expect(f.state.platformStreaming.value).toBe(false)
    expect(f.state.error.value).toBeNull()
    expect(f.state.media.value).toBeNull()
    expect(f.mediaReads()).toBe(0); expect(f.mediaPreparations()).toBe(0)
  } finally { f.stop() }
})

test('staff-selected lists submit both concurrency and selection revisions without silently choosing another list', async () => {
  const f = fixture(false)
  try {
    await f.state.load(); f.state.chooseGroup('question-list'); await f.state.prepareRound()
    expect(f.preparationWrites[0]).toMatchObject({ questionGroupId: 'question-list', expectedGroupStamp: 'group-stamp', selectionRevision: 'selection-stamp' })
    f.candidates.splice(0); await f.state.load()
    expect(f.state.groupChoice.value).toBe('question-list')
  } finally { f.stop() }
})

test('a locked roster member can confirm readiness without being the captain', async () => {
  const f = fixture(false, false, 'another-captain')
  try { await f.state.load(); expect(f.state.captain.value).toBe(false); expect(f.state.onRoster.value).toBe(true); await f.state.ready(); expect(f.readyCalls()).toBe(1) }
  finally { f.stop() }
})

test('unavailable browser storage disables automatic prompts while the confirmed result remains manually accessible', async () => {
  const f = fixture(false)
  Object.assign(f.match, { state: 'Completed', resultRevision: 1, endReason: 'Forfeit', resultLeftWins: 0, resultRightWins: 0,
    rightTeamId: 'right', rightTeamName: 'Synthetic Right', winnerTeamId: 'right' })
  try {
    await f.state.load()
    expect(f.state.resultOpen.value).toBe(false)
    expect(f.state.stableResult.value).toMatchObject({ winnerName: 'Synthetic Right', score: '0 : 0' })
    f.state.showResult(); expect(f.state.resultOpen.value).toBe(true)
  } finally { f.stop() }
})

test('staff can preview a candidate before preparation without silently specifying it or creating a round', async () => {
  const f = fixture(false)
  try {
    await f.state.load(); f.state.chooseGroup('question-list'); await f.state.previewCandidate()
    expect(f.state.candidatePreviewOpen.value).toBe(true)
    expect(f.state.candidatePreviewItems.value[0]).toMatchObject({ title: 'Synthetic preview', description: 'Staff only' })
    expect(f.preparationWrites).toHaveLength(0); expect(f.state.groupChoice.value).toBe('question-list')
    f.state.setCandidatePreviewOpen(false); expect(f.state.candidatePreviewItems.value).toEqual([])
  } finally { f.stop() }
})

test('prepared round policy wins over subsequent competition changes in both directions', async () => {
  for (const streaming of [false, true]) {
    const f = fixture(!streaming, streaming)
    try {
      await f.state.load()
      expect(f.state.platformStreaming.value).toBe(streaming)
      expect(f.mediaReads()).toBe(streaming ? 1 : 0)
    } finally { f.stop() }
  }
})

test('event refresh to a non-streaming round retires the screen state', async () => {
  const f = fixture(true, true)
  try {
    await f.state.load(); expect(f.state.media.value).not.toBeNull()
    f.round.platformStreamingEnabled = false
    await f.state.load(); await f.state.prepareMedia()
    expect(f.state.media.value).toBeNull()
    expect(f.disconnected()).toBeGreaterThan(0)
    expect(f.mediaReads()).toBe(1); expect(f.mediaPreparations()).toBe(0)
  } finally { f.stop() }
})

test('streaming toggles preserve recording, opponent and retention settings', () => {
  const draft = settingsDraft({ platformStreamingEnabled: true, recordingEnabled: true, participantsMayViewOpponents: true,
    recordingRetentionDays: 12, publicDelaySeconds: 90, maximumViewers: 70 })
  draft.platformStreamingEnabled = false
  const saved = settingsDraft(draft)
  expect(saved.platformStreamingEnabled).toBe(false)
  expect(saved.recordingEnabled).toBe(true); expect(saved.participantsMayViewOpponents).toBe(true)
  expect(saved.recordingRetentionDays).toBe(12); expect(saved.publicDelaySeconds).toBe(90); expect(saved.maximumViewers).toBe(70)
})

test('settings save both switch directions and reset previous save feedback', async () => {
  const settingsSource = await Bun.file(new URL('../app/features/live-solo/useLiveSoloSettings.ts', import.meta.url)).text()
  const settingsCompiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(settingsSource)
    .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
  let configuration = settingsDraft({ platformStreamingEnabled: false, recordingEnabled: true })
  const writes: boolean[] = []
  const deps = { ...soloNavigation, ref, computed, effectScope, message, settingsDraft, settingFields, validSettings, canManageLiveSolo,
    useRoute: () => ({ params: { id: 'contest' } }), useRouter: () => ({ push: async () => {} }),
    competitionContextKey: Symbol(), inject: () => ({ competition: ref({ administrationRole: 'Owner', status: 'Running' }), refresh: async () => {} }),
    onMounted: () => {}, onScopeDispose: () => {}, onBeforeRouteLeave: () => {},
    parseLiveSoloError: () => ({ displayMessage: message('liveSolo.error.load') }),
    getLiveSoloConfiguration: async () => ({ data: configuration }), getLiveSoloBracket: async () => ({ data: { matches: [] } }),
    saveLiveSoloConfiguration: async (input: { body: { configuration: typeof configuration } }) => {
      configuration = settingsDraft(input.body.configuration); writes.push(configuration.platformStreamingEnabled)
      return { data: configuration }
    },
  }
  const factory = new Function('deps', `const { ${Object.keys(deps).join(',')} } = deps; ${settingsCompiled}; return useLiveSoloSettings;`)(deps)
  const scope = effectScope(), state = scope.run(() => factory())!
  try {
    await state.load(); state.streaming(true); await state.save()
    expect(state.saved.value).toBe(true)
    state.streaming(false); expect(state.saved.value).toBe(false)
    expect(state.sections.value.find((x: { key: string }) => x.key === 'media').fields).toEqual([])
    await state.save(); expect(state.saved.value).toBe(true); expect(state.dirty.value).toBe(false)
    expect(writes).toEqual([true, false]); expect(configuration.recordingEnabled).toBe(true)
  } finally { scope.stop() }
})
