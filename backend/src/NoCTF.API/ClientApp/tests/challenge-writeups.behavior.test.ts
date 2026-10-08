import { describe, expect, test } from 'bun:test'
import { computed, effectScope, markRaw, nextTick, reactive, ref, watch } from 'vue'
import { challengeWriteUpPath, latestWriteUpVersion, needsWriteUpConfirmation, writeUpStatusKey } from '../app/features/writeups/writeup-state'

async function compile(path: string, name: string) {
  const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(await Bun.file(new URL(path, import.meta.url)).text())
    .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
    .replace(/export function /g, 'function ')
  return (deps: Record<string, unknown>) => new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return ${name};`)(deps)
}
const readerFactory = await compile('../app/features/writeups/useChallengeWriteUpPage.ts', 'useChallengeWriteUpPage')
const editorFactory = await compile('../app/features/writeups/useChallengeWriteUpEditor.ts', 'useChallengeWriteUpEditor')
const reviewFactory = await compile('../app/features/writeups/useChallengeWriteUpReview.ts', 'useChallengeWriteUpReview')
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }
const baseDeps = { ref, computed, watch, markRaw, nextTick, onMounted: () => {}, onBeforeUnmount: () => {},
  translate: (key: string) => key, message: (key: string) => ({ key }),
  parseApiError: (error: unknown) => ({ displayMessage: error }), toast: { success: () => {}, error: () => {} } }
function reader() {
  const scope = effectScope(), calls: string[] = []
  const route = reactive({ params: { id: 'competition', competitionChallengeId: 'challenge', writeUpId: 'official' }, query: { tag: ['Web', 'SQL'], hideSolved: 'true' } })
  const items = [{ id: 'official', source: 'Official', publishedVersionId: 'v1', published: { id: 'v1' } },
    { id: 'other', source: 'Team', teamId: 'other-team', publishedVersionId: 'v2', published: { id: 'v2' } },
    { id: 'mine', source: 'Team', teamId: 'my-team', publishedVersionId: 'v3', published: { id: 'v3' } }]
  const access = { teamId: 'my-team', isUnlocked: false, isFree: false, deductionPercent: 20 }
  let nextQuote = { versionId: 'v1', policyStamp: 'policy', isUnlocked: false, isFree: false, canUnlock: true, deductionPercent: 20, grossPoints: 500 }
  let unlockError: unknown, bodyRead: (() => Promise<unknown>) | undefined, event: (value: { kind: string }) => void = () => {}
  const mounted: Array<() => void> = []
  const moves: unknown[] = []
  const deps = { ...baseDeps, ...Object.fromEntries(['ArrowLeft', 'BookOpen', 'Download', 'RefreshCw'].map(x => [x, {}])),
    useMediaQuery: () => ref(false), CompetitionParticipantWorkspaceComponent: {}, ChallengeWriteUpEditorComponent: {}, competitionContextKey: Symbol(),
    inject: () => ({ competition: ref({ mode: 'Ctf', singleWriteUpsEnabled: true }) }), useRoute: () => route,
    useRouter: () => ({ replace: async (value: unknown) => { moves.push(value) }, push: async (value: unknown) => { moves.push(value) } }),
    onMounted: (callback: () => void) => mounted.push(callback),
    watchCompetition: (_id: string, callbacks: { competitionEventChanged: typeof event }) => { event = callbacks.competitionEventChanged; return () => {} },
    challengeWriteUpPath, needsWriteUpConfirmation,
    listChallengeWriteUps: async () => { calls.push('metadata'); return { data: { items: structuredClone(items), access: { ...access } } } },
    getChallengeEndpoint: async () => ({ data: { title: 'Challenge' } }),
    getChallengeWriteUpQuote: async () => { calls.push('quote'); return { data: { ...nextQuote } } },
    unlockChallengeWriteUp: async (request: unknown) => { calls.push('unlock'); moves.push(request); return unlockError ? { error: unlockError } : { data: { deductionPercent: 20 } } },
    getChallengeWriteUpContent: async ({ path }: { path: { versionId: string } }) => { calls.push(`body:${path.versionId}`); return bodyRead ? bodyRead() : { data: { versionId: path.versionId, format: 'Markdown', markdown: '# Answer' } } },
    prepareChallengeWriteUpBrowserAccess: async () => { calls.push('grant'); return { data: { previewUrl: '/file', downloadUrl: '/file?download=true' } } },
    startWriteUpBrowserDownload: () => calls.push('download'),
  }
  const state = scope.run(() => readerFactory(deps)({}))!
  return { state, access, items, route, calls, moves, start: () => { mounted.forEach(fn => fn()) },
    event: (kind: string) => event({ kind }), quote: (patch: object) => { nextQuote = { ...nextQuote, ...patch } },
    rejectUnlock: () => { unlockError = { code: 'ConfirmationChanged' } }, deferredBody: (fn: typeof bodyRead) => { bodyRead = fn }, stop: () => scope.stop() }
}

describe('explicit team writeup access', () => {
  test('metadata, selection and cancellation never request body or create an unlock', async () => {
    const app = reader()
    try {
      await app.state.load(); await app.state.select('other')
      expect(app.calls).toEqual(['metadata'])
      expect(app.moves[0]).toEqual({ path: '/competitions/competition/challenge-writeups/challenge/other', query: app.route.query })
      await app.state.read(); app.state.setConfirmOpen(false)
      expect(app.calls).toEqual(['metadata', 'quote']); expect(app.state.content.value).toBeNull()
    } finally { app.stop() }
  })
  test('confirmation commits once before body and retains filters when returning to the challenge', async () => {
    const app = reader()
    try {
      await app.state.load(); await app.state.read()
      expect(app.state.confirmOpen.value).toBeTrue()
      await app.state.confirm()
      expect(app.calls).toEqual(['metadata', 'quote', 'unlock', 'body:v1'])
      expect(app.state.access.value.isUnlocked).toBeTrue()
      expect(app.state.content.value.markdown).toBe('# Answer')
      app.state.back()
      expect(app.moves.at(-1)).toEqual({ path: '/competitions/competition/challenges/challenge', query: app.route.query })
    } finally { app.stop() }
  })
  test.each([{ isUnlocked: true }, { isFree: true }])('shared or post-competition access does not reconfirm: %j', async quote => {
    const app = reader()
    try { app.quote(quote); await app.state.load(); await app.state.read(); expect(app.state.confirmOpen.value).toBeFalse(); expect(app.calls).toEqual(['metadata', 'quote', 'body:v1']) }
    finally { app.stop() }
  })
  test('own submission is free while zero-percent external reading still requires confirmation', async () => {
    const app = reader()
    try {
      app.route.params.writeUpId = 'mine'; app.quote({ versionId: 'v3', isFree: true }); await app.state.load(); await app.state.read()
      expect(app.state.own.value).toBeTrue(); expect(app.calls).toContain('body:v3'); expect(app.calls).not.toContain('unlock')
      app.route.params.writeUpId = 'official'; await drain(); app.quote({ versionId: 'v1', isFree: false, deductionPercent: 0 }); await app.state.read()
      expect(app.state.confirmOpen.value).toBeTrue()
    } finally { app.stop() }
  })
  test('changed policy prevents body; a new read fetches fresh confirmation', async () => {
    const app = reader()
    try {
      await app.state.load(); await app.state.read(); app.rejectUnlock(); await app.state.confirm()
      expect(app.calls.filter(x => x.startsWith('body:'))).toEqual([])
      app.quote({ policyStamp: 'new-policy', deductionPercent: 30 }); await app.state.read()
      expect(app.state.quote.value.policyStamp).toBe('new-policy'); expect(app.state.quote.value.deductionPercent).toBe(30)
    } finally { app.stop() }
  })
  test('late body responses cannot reveal the previously selected writeup; withdrawal refresh clears it', async () => {
    const app = reader()
    try {
      let resolve!: (value: unknown) => void
      app.quote({ isUnlocked: true }); await app.state.load()
      app.deferredBody(() => new Promise(done => { resolve = done }))
      const pending = app.state.read(); await drain()
      await app.state.select('other'); resolve({ data: { versionId: 'v1', format: 'Markdown', markdown: '# Old' } }); await pending
      expect(app.state.content.value).toBeNull()
      app.start(); await drain(); app.items.splice(0, 1); app.event('ChallengeWriteUpWithdrawn'); await drain()
      expect(app.state.selected.value).toBeNull(); expect(app.state.content.value).toBeNull()
    } finally { app.stop() }
  })
  test('paused first-time reads display the pause condition without opening confirmation', async () => {
    const app = reader()
    try { app.quote({ canUnlock: false }); await app.state.load(); await app.state.read(); expect(app.state.confirmOpen.value).toBeFalse(); expect(app.calls).not.toContain('unlock') }
    finally { app.stop() }
  })
})

test('saved draft, separate submission, conflict preservation and read-only history use one editor lifecycle', async () => {
  const scope = effectScope(), writes: unknown[] = []
  const root = { id: 'root', concurrencyStamp: 'stamp', teamId: 'team', draft: { id: 'draft', number: 2, format: 'Markdown', state: 'Draft' },
    versions: [{ id: 'draft', number: 2, format: 'Markdown', state: 'Draft' }, { id: 'old', number: 1, format: 'Markdown', state: 'Approved' }] }
  const state = scope.run(() => editorFactory({ ...baseDeps, latestWriteUpVersion, writeUpStatusKey,
    useNow: () => ref(new Date()), onBeforeRouteLeave: () => {}, onBeforeRouteUpdate: () => {},
    listChallengeWriteUps: async () => ({ data: { items: [structuredClone(root)], access: { teamId: 'team', canSubmit: true, settings: { deadlineAt: '2099-01-01T00:00:00Z' } } } }),
    getChallengeWriteUpContent: async ({ path }: { path: { versionId: string } }) => ({ data: { format: 'Markdown', markdown: path.versionId } }),
    saveChallengeWriteUpDraft: async (request: unknown) => { writes.push(request); return { error: { code: 'Conflict' } } },
    submitChallengeWriteUp: async (request: unknown) => { writes.push(request); return { data: { ...root, draft: null, submitted: { ...root.draft, state: 'Submitted' } } } },
  })({ competitionId: 'competition', competitionChallengeId: 'challenge' }, () => {}))!
  try {
    await state.reload(); state.markdown.value = '# Unsaved'
    expect(state.canSubmit.value).toBeFalse(); await state.save()
    expect(state.markdown.value).toBe('# Unsaved'); expect(state.dirty.value).toBeTrue()
    await state.viewHistory('old'); expect(state.historyContent.value.markdown).toBe('old'); expect(state.markdown.value).toBe('# Unsaved')
    const leave = state.confirmDiscard(); expect(state.leaveOpen.value).toBeTrue(); state.stay(); expect(await leave).toBeFalse()
    const retry = state.confirmDiscard(); state.discard(); expect(await retry).toBeTrue(); await state.submit()
    expect(writes).toHaveLength(2); expect(state.root.value.submitted.state).toBe('Submitted')
  } finally { scope.stop() }
})

test('review restores URL filters, preserves selection during new arrivals, and guards official editor dismissal', async () => {
  const scope = effectScope(), moves: unknown[] = []
  const route = reactive({ query: { reviewStatus: 'Published', reviewSource: 'Official', reviewPage: '2', tag: ['Web'] } })
  const row = { id: 'root', competitionChallengeId: 'challenge', publishedVersionId: 'old', published: { id: 'old' }, submitted: { id: 'new', state: 'Submitted' } }
  const state = scope.run(() => reviewFactory({ ...baseDeps, useRoute: () => route, useRouter: () => ({ replace: async (next: unknown) => moves.push(next) }),
    SingleWriteUpSettingsComponent: {}, ChallengeWriteUpEditorComponent: {}, writeUpStatusKey,
    listChallengeWriteUpReviews: async () => ({ data: { items: [row], totalCount: 30, canManage: true, canJudge: true } }),
    getChallengeWriteUpSettings: async () => ({ data: { settings: { enabled: true, deductionPercent: 25 } } }),
    getChallengeWriteUpContent: async () => ({ data: { format: 'Markdown', markdown: '# Review' } }),
  })({ competitionId: 'competition' }))!
  try {
    expect(state.filter.value).toBe('Published'); expect(state.source.value).toBe('Official'); expect(state.page.value).toBe(2)
    await state.load(); expect(state.selected.value.id).toBe('root'); await state.load(); expect(state.selected.value.id).toBe('root')
    await state.requestPublish(); expect(state.confirmationSettings.value.deductionPercent).toBe(25)
    state.officialOpen.value = true; state.bindOfficialEditor({ confirmDiscard: async () => false }); await state.setOfficialOpen(false)
    expect(state.officialOpen.value).toBeTrue()
    state.bindOfficialEditor({ confirmDiscard: async () => true }); await state.setOfficialOpen(false); expect(state.officialOpen.value).toBeFalse()
    expect(Array.from((moves[0] as { query: { tag: string[] } }).query.tag)).toEqual(['Web'])
  } finally { scope.stop() }
})
