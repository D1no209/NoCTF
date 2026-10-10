import { expect, test } from 'bun:test'
import { computed, effectScope, markRaw, ref, shallowRef, watch } from 'vue'
import { settingsDraft, settingFields, validSettings } from '../app/features/live-solo/settings-draft'
import { message } from '../app/utils/i18n'
import { canJudgeLiveSolo, canLockLiveSoloRoster, canPlayLiveSolo, formatRoundClock, matchStateKey, roundRemainingSeconds } from '../app/features/live-solo/live-solo-state'
import { canManageLiveSolo } from '../app/features/live-solo/settings-draft'

const source = await Bun.file(new URL('../app/features/live-solo/useLiveSoloMatch.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')

function fixture(policy: boolean, prepared?: boolean) {
  let mediaReads = 0, mediaPreparations = 0, disconnected = 0
  const config = { platformStreamingEnabled: policy }
  const round = { id: 'round', state: 'Preparing', platformStreamingEnabled: prepared }
  const match = { id: 'match', state: 'Preparing', concurrencyStamp: 'match-stamp', currentRoundId: prepared === undefined ? null : 'round',
    rosters: [{ teamId: 'left', userIds: ['player'], locked: true }, { teamId: 'right', userIds: ['opponent'], locked: true }] }
  const screen = { connected: ref(false), connecting: ref(false), publishing: ref(false), capturePending: ref(false),
    error: ref(null), localStream: ref(null), remoteStreams: ref(new Map()),
    disconnect: async () => { disconnected++ } }
  const deps = { computed, markRaw, ref, shallowRef, watch, message, canJudgeLiveSolo, canLockLiveSoloRoster,
    canPlayLiveSolo, formatRoundClock, matchStateKey, roundRemainingSeconds, canManageLiveSolo,
    competitionContextKey: Symbol(), inject: () => ({ competition: ref({ administrationRole: 'Owner' }) }),
    useRoute: () => ({ params: { id: 'contest', matchId: 'match' } }), useRouter: () => ({ push: async () => {} }),
    useAuth: () => ({ user: ref({ userId: 'player', userName: 'Synthetic player' }) }),
    useLiveSoloHub: () => {}, onMounted: () => {}, onBeforeRouteLeave: () => {}, onScopeDispose: () => {},
    useLiveSoloScreen: () => screen, parseLiveSoloError: () => ({ displayMessage: message('liveSolo.error.load') }),
    getLiveSoloMatch: async () => ({ data: match }), getLiveSoloPlayerPolicy: async () => ({ data: config }),
    getMyTeamEndpoint: async () => ({ data: { id: 'left', captainId: 'player', memberIds: ['player'] } }),
    getLiveSoloRound: async () => ({ data: { ...round } }), userProfileGet: async () => ({ data: null }),
    getLiveSoloMedia: async () => { mediaReads++; return { data: { state: 'Ready', generation: 'generation', members: [] } } },
    prepareLiveSoloMedia: async () => { mediaPreparations++; return {} },
    LiveSoloQuestions: {}, CompetitionParticipantWorkspace: {}, LiveSoloProgramControl: {},
  }
  const factory = new Function('deps', `const { ${Object.keys(deps).join(',')} } = deps; ${compiled}; return useLiveSoloMatch;`)(deps)
  const scope = effectScope(), state = scope.run(() => factory())!
  return { state, config, round, match, mediaReads: () => mediaReads, mediaPreparations: () => mediaPreparations,
    disconnected: () => disconnected, stop: () => scope.stop() }
}

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
  const deps = { ref, computed, effectScope, message, settingsDraft, settingFields, validSettings, canManageLiveSolo,
    useRoute: () => ({ params: { id: 'contest' } }), useRouter: () => ({ push: async () => {} }),
    competitionContextKey: Symbol(), inject: () => ({ competition: ref({ administrationRole: 'Owner' }), refresh: async () => {} }),
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
