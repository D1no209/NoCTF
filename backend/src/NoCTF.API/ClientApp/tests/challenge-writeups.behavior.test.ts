import { describe, expect, test } from 'bun:test'
import { computed, effectScope, markRaw, nextTick, reactive, ref, watch } from 'vue'
import { challengeWriteUpPath, latestWriteUpVersion, needsWriteUpConfirmation, writeUpPublicationVersion, writeUpPublicationLabel, writeUpStatusKey } from '../app/features/writeups/writeup-state'

async function compile(path: string, name: string) {
  const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(await Bun.file(new URL(path, import.meta.url)).text())
    .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
    .replace(/export (async )?function /g, '$1function ')
  return (deps: Record<string, unknown>) => new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return ${name};`)(deps)
}
const readerFactory = await compile('../app/features/writeups/useChallengeWriteUpPage.ts', 'useChallengeWriteUpPage')
const rawEditorFactory = await compile('../app/features/writeups/useChallengeWriteUpEditor.ts', 'useChallengeWriteUpEditor')
const rawReviewFactory = await compile('../app/features/writeups/useChallengeWriteUpReview.ts', 'useChallengeWriteUpReview')
const publicationFactory = await compile('../app/features/writeups/writeup-publication.ts', '({ matchesWriteUpTarget, publishWriteUpVersion, writeUpActionTarget })')
function publicationDeps(deps: Record<string, unknown>) {
  return { ...deps, latestWriteUpVersion, writeUpPublicationVersion, writeUpPublicationLabel, ...publicationFactory(deps) }
}
const editorFactory = (deps: Record<string, unknown>) => rawEditorFactory(publicationDeps(deps))
const reviewFactory = (deps: Record<string, unknown>) => rawReviewFactory(publicationDeps(deps))
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }
const baseDeps = { getCompetitionEndpoint: async () => ({ data: { status: 'Running' } }), ref, computed, watch, markRaw, nextTick, onMounted: () => {}, onBeforeUnmount: () => {},
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
    inject: () => ({ competition: ref({ mode: 'Ctf', singleWriteUpsEnabled: true }), standing: ref(null), refreshStanding: async () => {} }), useRoute: () => route,
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
  const row = { id: 'root', concurrencyStamp: 'stamp', competitionChallengeId: 'challenge', publishedVersionId: 'old', published: { id: 'old' }, submitted: { id: 'new', state: 'Submitted' } }
  const state = scope.run(() => reviewFactory({ ...baseDeps, useRoute: () => route, useRouter: () => ({ replace: async (next: unknown) => moves.push(next) }),
    useMediaQuery: () => ref(true), SingleWriteUpSettingsComponent: {}, ChallengeWriteUpEditorComponent: {}, writeUpStatusKey,
    adminListChallengeWriteUpReviews: async () => ({ data: { items: [row], totalCount: 30, canManage: true, canJudge: true } }),
    getChallengeWriteUpSettings: async () => ({ data: { settings: { enabled: true, deductionPercent: 25 } } }),
    getChallengeWriteUpContent: async () => ({ data: { format: 'Markdown', markdown: '# Review' } }),
  })({ competitionId: 'competition' }))!
  try {
    expect(state.filter.value).toBe('Published'); expect(state.source.value).toBe('Official'); expect(state.page.value).toBe(2)
    await state.load(); expect(state.selected.value.id).toBe('root'); await state.load(); expect(state.selected.value.id).toBe('root')
    expect(state.panel.value).toBe('list'); await state.select('root'); expect(state.panel.value).toBe('preview')
    state.setPanel('actions'); expect(state.panel.value).toBe('actions')
    await state.requestPublish(); expect(state.confirmationSettings.value.deductionPercent).toBe(25)
    state.officialOpen.value = true; state.bindOfficialEditor({ confirmDiscard: async () => false }); await state.setOfficialOpen(false)
    expect(state.officialOpen.value).toBeTrue()
    state.bindOfficialEditor({ confirmDiscard: async () => true }); await state.setOfficialOpen(false); expect(state.officialOpen.value).toBeFalse()
    expect(Array.from((moves[0] as { query: { tag: string[] } }).query.tag)).toEqual(['Web'])
  } finally { scope.stop() }
})

test('official saved drafts can publish directly while the server still seals a submitted version first', async () => {
  const scope = effectScope(), calls: string[] = []
  const draft = { id: 'draft', number: 2, format: 'Markdown', state: 'Draft' }
  const root = { id: 'official', competitionChallengeId: 'challenge', source: 'Official', concurrencyStamp: 'saved', draft, submitted: null, versions: [draft] }
  const state = scope.run(() => editorFactory({ ...baseDeps, latestWriteUpVersion, writeUpStatusKey,
    useNow: () => ref(new Date()), onBeforeRouteLeave: () => {}, onBeforeRouteUpdate: () => {},
    listChallengeWriteUps: async () => ({ data: { items: [root], access: { canManage: true, settings: { enabled: true } } } }),
    getChallengeWriteUpContent: async () => ({ data: { format: 'Markdown', markdown: '# Official' } }),
    submitChallengeWriteUp: async () => { calls.push('seal'); return { data: { ...root, draft: null, concurrencyStamp: 'sealed', submitted: { ...draft, state: 'Submitted' } } } },
    reviewChallengeWriteUp: async ({ body }: { body: { expectedStamp: string; versionId: string } }) => {
      expect(body.expectedStamp).toBe('sealed'); expect(body.versionId).toBe('draft'); calls.push('publish')
      return { data: { ...root, draft: null, publishedVersionId: 'draft', published: { ...draft, state: 'Approved' } } }
    },
  })({ competitionId: 'competition', competitionChallengeId: 'challenge', official: true }, () => {}))!
  try {
    await state.reload(); expect(state.canPublish.value).toBeTrue()
    await state.requestPublish(); expect(state.publishOpen.value).toBeTrue(); await state.publish()
    expect(calls).toEqual(['seal', 'publish']); expect(state.root.value.publishedVersionId).toBe('draft')
  } finally { scope.stop() }
})

function publicationApp(entry: 'editor' | 'review', format = 'Markdown', source = 'Official', canManage = true) {
  const scope = effectScope(), calls: Array<{ action: string; body: any }> = []
  const old = { id: 'old', number: 1, state: 'Approved', format, concurrencyStamp: 'old-stamp' }
  const draft = { id: 'draft', number: 2, state: 'Draft', format, concurrencyStamp: 'draft-stamp' }
  let root: any = { id: 'official', competitionChallengeId: 'challenge', challengeTitle: 'Challenge', source,
    concurrencyStamp: 'saved', draft, submitted: old, published: old, publishedVersionId: 'old', versions: [draft, old] }
  let failPublication = false, failSubmission = false, changedSubmission = false, hold: (() => Promise<void>) | undefined
  const deps = { ...baseDeps, useNow: () => ref(new Date()), onBeforeRouteLeave: () => {}, onBeforeRouteUpdate: () => {},
    useRoute: () => ({ query: {} }), useRouter: () => ({ replace: async () => {} }), useMediaQuery: () => ref(false),
    SingleWriteUpSettingsComponent: {}, ChallengeWriteUpEditorComponent: {},
    listChallengeWriteUps: async () => ({ data: { items: [structuredClone(root)], access: { canManage, canSubmit: source === 'Team', settings: { enabled: true } } } }),
    adminListChallengeWriteUpReviews: async () => ({ data: { items: [structuredClone(root)], totalCount: 1, canManage, canJudge: true } }),
    getChallengeWriteUpSettings: async () => ({ data: { settings: { enabled: true, deductionPercent: 20 } } }),
    getChallengeWriteUpContent: async ({ path }: any) => ({ data: { versionId: path.versionId, format, markdown: '# Answer', fileName: 'answer.pdf' } }),
    prepareChallengeWriteUpBrowserAccess: async () => ({ data: { previewUrl: '/pdf' } }),
    submitChallengeWriteUp: async ({ body }: any) => {
      calls.push({ action: 'submit', body }); await hold?.()
      if (failSubmission) return { error: { code: 'Conflict' } }
      root = { ...root, concurrencyStamp: 'sealed', draft: null, submitted: { ...draft, id: changedSubmission ? 'unexpected' : draft.id, state: 'Submitted' } }
      return { data: structuredClone(root) }
    },
    reviewChallengeWriteUp: async ({ body }: any) => {
      calls.push({ action: body.action, body })
      if (failPublication) return { error: { code: 'Conflict' } }
      root = { ...root, concurrencyStamp: 'published-stamp', publishedVersionId: body.versionId, published: { ...root.submitted, state: 'Approved' } }
      return { data: structuredClone(root) }
    },
  }
  const state = scope.run(() => entry === 'editor' ? editorFactory(deps)({ competitionId: 'competition', competitionChallengeId: 'challenge', official: true }, () => {})
    : reviewFactory(deps)({ competitionId: 'competition' }))!
  const start = () => entry === 'editor' ? state.reload() : state.load()
  const publish = async () => { if (entry === 'editor') await state.publish(); else { state.confirmReview(); await drain() } }
  return { state, calls, start, publish, stop: () => scope.stop(), root: () => root,
    fail: (value: boolean) => { failPublication = value }, failSubmission: () => { failSubmission = true },
    changedSubmission: () => { changedSubmission = true }, hold: (fn: () => Promise<void>) => { hold = fn } }
}

describe.each(['editor', 'review'] as const)('publication from %s', entry => {
  test.each(['Markdown', 'Pdf'])('an official %s draft publishes the displayed version, keeping the old version until success', async format => {
    const app = publicationApp(entry, format)
    try {
      await app.start(); expect(app.state.canPublish.value).toBeTrue()
      const displayed = entry === 'editor' ? app.state.currentVersion.value : app.state.selectedVersion.value
      expect(displayed.id).toBe('draft')
      await app.state.requestPublish()
      const target = entry === 'editor' ? app.state.publicationTarget.value : app.state.confirmationTarget.value
      expect(target.versionId).toBe(displayed.id); expect(target.number).toBe(2)
      await app.publish()
      expect(app.calls.map(x => x.action)).toEqual(['submit', 'Publish'])
      expect(app.calls[1]!.body).toEqual({ action: 'Publish', versionId: 'draft', expectedStamp: 'sealed' })
      expect(app.root().publishedVersionId).toBe('draft')
    } finally { app.stop() }
  })
  test('a publication failure retains the sealed version and old publication; retry never submits again', async () => {
    const app = publicationApp(entry)
    try {
      await app.start(); await app.state.requestPublish(); app.fail(true); await app.publish()
      expect(app.state.publicationNotice.value).toBeTrue(); expect(app.root().publishedVersionId).toBe('old')
      expect(app.state.canPublish.value).toBeTrue()
      app.fail(false); await app.state.requestPublish(); await app.publish()
      expect(app.calls.map(x => x.action)).toEqual(['submit', 'Publish', 'Publish'])
      expect(app.root().publishedVersionId).toBe('draft'); expect(app.state.publicationNotice.value).toBeFalse()
    } finally { app.stop() }
  })
  test('a submission conflict retains the draft and old publication without claiming submission succeeded', async () => {
    const app = publicationApp(entry)
    try {
      await app.start(); await app.state.requestPublish(); app.failSubmission(); await app.publish()
      expect(app.calls.map(x => x.action)).toEqual(['submit'])
      expect(app.root().draft.id).toBe('draft'); expect(app.root().publishedVersionId).toBe('old')
      expect(app.state.publicationNotice.value).toBeFalse(); expect(app.state.pending.value).toBeFalse()
    } finally { app.stop() }
  })
  test('a changed confirmation never publishes a replacement version', async () => {
    const app = publicationApp(entry)
    try {
      await app.start(); await app.state.requestPublish()
      const row = entry === 'editor' ? app.state.root.value : app.state.selected.value
      row.concurrencyStamp = 'another-editor'; row.draft.id = 'replacement'
      await app.publish(); expect(app.calls).toHaveLength(0)
      expect(app.state.error.value.key).toBe('challengeWriteUp.publicationChanged')
    } finally { app.stop() }
  })
  test('an unexpected submitted version never becomes the publish target', async () => {
    const app = publicationApp(entry)
    try {
      await app.start(); await app.state.requestPublish(); app.changedSubmission(); await app.publish()
      expect(app.calls.map(x => x.action)).toEqual(['submit'])
      expect(app.root().publishedVersionId).toBe('old')
      const displayed = entry === 'editor' ? app.state.currentVersion.value : app.state.selectedVersion.value
      expect(displayed.id).toBe('draft'); expect(app.state.publicationNotice.value).toBeFalse()
    } finally { app.stop() }
  })
  test('judges and observers cannot publish official drafts', async () => {
    const app = publicationApp(entry, 'Markdown', 'Official', false)
    try { await app.start(); expect(app.state.canPublish.value).toBeFalse(); await app.state.requestPublish(); await app.publish(); expect(app.calls).toHaveLength(0) }
    finally { app.stop() }
  })
})

test('review never publishes team drafts or silently substitutes historical approved versions', async () => {
  const app = publicationApp('review', 'Markdown', 'Team')
  try {
    await app.start(); expect(app.state.canPublish.value).toBeFalse()
    expect(app.state.publicationBlocked.value).toBe('challengeWriteUp.teamDraftNotPublishable')
    await app.state.requestPublish(); await app.publish(); expect(app.calls).toHaveLength(0)
    const row = app.state.selected.value
    row.source = 'Official'; row.publishedVersionId = null; row.published = null
    expect(app.state.publishVersion.value.id).toBe('draft')
    row.draft = null; row.submitted = null; row.versions = [app.root().published]
    expect(app.state.publishVersion.value.id).toBe('old')
    expect(app.state.publicationLabel.value).toBe('challengeWriteUp.republishVersion')
    app.state.setDisplay('published'); expect(app.state.canPublish.value).toBeFalse()
  } finally { app.stop() }
})

test.each(['editor', 'review'] as const)('duplicate publication clicks from %s produce one submission and one publication', async entry => {
  const app = publicationApp(entry)
  try {
    let finish!: () => void
    app.hold(() => new Promise<void>(resolve => { finish = resolve }))
    await app.start(); await app.state.requestPublish()
    const first = app.publish(); await drain(); await app.publish()
    expect(app.calls).toHaveLength(1); expect(app.state.pending.value).toBeTrue()
    if (entry === 'editor') expect(await app.state.confirmDiscard()).toBeFalse()
    finish(); await first; await drain(); expect(app.calls.map(x => x.action)).toEqual(['submit', 'Publish'])
  } finally { app.stop() }
})
