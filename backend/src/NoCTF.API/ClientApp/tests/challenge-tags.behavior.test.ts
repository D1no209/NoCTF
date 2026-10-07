import { describe, expect, test } from 'bun:test'
import { computed, effectScope, nextTick, onScopeDispose, reactive, ref, toRefs, watch } from 'vue'
import { challengeTagOptions, matchesAllTags, tagKey, tagsFromQuery, uniqueTags, validChallengeTags } from '../app/lib/challenge-tags'
import { compileChallengeTitleSearch, isChallengeVisible } from '../app/features/competition/useCompetitionChallengeNavigator'

const source = await Bun.file(new URL('../app/features/competition/useCompetitionChallengeNavigator.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }
const challenge = (id: string, tags: string[], published = true) => ({ id, title: id, direction: 'Web', order: 1, isPublished: published, tags })

function harness(query: Record<string, any> = {}) {
  const route = reactive({ query })
  const props = reactive({ competitionId: 'competition', selectedChallengeId: 'sql' })
  let rows = [challenge('sql', ['Web', 'SQL']), challenge('http', ['web', 'HTTP']), challenge('plain', []), challenge('draft', ['Secret'], false)]
  let listener: any
  const ready: string[] = []
  const deps = {
    computed, ref, toRefs, watch, challengeTagOptions, matchesAllTags, tagsFromQuery, uniqueTags,
    ShieldCheck: {}, Swords: {}, Users: {}, directionGlyph: () => 'web',
    competitionContextKey: {}, inject: () => ({ competition: ref({ mode: 'Ctf' }) }),
    useAuth: () => ({ isLoggedIn: ref(false) }), useRoute: () => route,
    useRouter: () => ({ replace: async (next: any) => { route.query = next.query } }),
    useScoreboardMatrix: () => ({ snapshot: ref(null), catalog: ref(null), schema: ref(null) }),
    listChallengesEndpoint: async () => ({ data: { items: rows }, response: { status: 200 } }),
    watchCompetition: (_: string, callbacks: any) => { listener = callbacks; return () => {} },
    createTrailingRefresh: (callback: any) => callback,
    onMounted: (callback: () => void) => callback(), onUnmounted: onScopeDispose,
    translate: (key: string) => key, directionLabel: (value: string) => value,
    describeMessage: (key: string) => ({ key }),
    bloodRankLabel: () => '', scoreboardCurrentChallengeScore: () => null,
  }
  const factory = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useCompetitionChallengeNavigator;`)(deps)
  const scope = effectScope()
  const state = scope.run(() => factory(props, (event: string, id: string) => { if (event === 'ready') ready.push(id) }))!
  return { state, route, props, ready, stop: () => scope.stop(),
    refresh: (next: typeof rows) => { rows = next; listener.competitionEventChanged({ kind: 'ChallengeUpdated' }) } }
}

describe('competition challenge tags', () => {
  test('normalizes, deduplicates and enforces tag limits', () => {
    expect(uniqueTags([' Web ', 'web', 'SQL'])).toEqual(['Web', 'SQL'])
    expect(tagKey(' SQL ')).toBe('SQL')
    expect(validChallengeTags([' ', 'Web'])).toBe(false)
    expect(validChallengeTags(['x'.repeat(41)])).toBe(false)
    expect(validChallengeTags(Array.from({ length: 21 }, (_, index) => `t${index}`))).toBe(false)
    expect(validChallengeTags(Array(21).fill('Web'))).toBe(true)
  })

  test('uses intersections and combines search, solved and locked filters', () => {
    expect(matchesAllTags(['Web', 'SQL'], ['web', ' sql '])).toBe(true)
    expect(matchesAllTags(['Web'], ['Web', 'SQL'])).toBe(false)
    expect(matchesAllTags([], [])).toBe(true)
    expect(matchesAllTags([], ['Web'])).toBe(false)
    const row = { title: 'SQL injection', locked: false, tags: ['Web', 'SQL'] }
    const filters = { search: 'injection', hideSolved: false, hideLocked: false, solvedByMyTeam: false, tags: ['Web', 'SQL'] }
    expect(isChallengeVisible(row, filters)).toBe(true)
    expect(isChallengeVisible(row, { ...filters, search: 'crypto' })).toBe(false)
    expect(isChallengeVisible(row, { ...filters, hideSolved: true, solvedByMyTeam: true })).toBe(false)
    expect(isChallengeVisible({ ...row, locked: true }, { ...filters, hideLocked: true })).toBe(false)
  })

  test('keeps ordinary searches literal while regex mode supports anchors and alternatives', () => {
    const filters = { hideSolved: false, hideLocked: false, solvedByMyTeam: false }
    const literal = compileChallengeTitleSearch(' [day1] ', false)
    expect(isChallengeVisible({ title: '[day1]nc' }, { ...filters, search: literal })).toBe(true)
    expect(isChallengeVisible({ title: 'day1' }, { ...filters, search: literal })).toBe(false)
    const expression = compileChallengeTitleSearch(' ^\\[day1\\].*(fmt|nc)$ ', true)
    expect(isChallengeVisible({ title: '[DAY1]FMT' }, { ...filters, search: expression })).toBe(true)
    expect(isChallengeVisible({ title: '[day1]nc' }, { ...filters, search: expression })).toBe(true)
    expect(isChallengeVisible({ title: '[day2]nc' }, { ...filters, search: expression })).toBe(false)
    expect(isChallengeVisible({ title: '[day1]orw' }, { ...filters, search: expression })).toBe(false)
    expect(isChallengeVisible({ title: '[day1]nc' }, { ...filters, search: expression, hideSolved: true, solvedByMyTeam: true })).toBe(false)
    expect(isChallengeVisible({ title: '[day1]nc', locked: true }, { ...filters, search: expression, hideLocked: true })).toBe(false)
    expect(isChallengeVisible({ title: '[day1]nc', tags: ['Pwn'] }, { ...filters, search: expression, tags: ['Web'] })).toBe(false)
  })

  test('preserves case-sensitive regex escapes and rejects malformed expressions without throwing', () => {
    const expression = compileChallengeTitleSearch('^\\D+$', true) as RegExp
    expect(expression.test('NC')).toBe(true)
    expect(expression.test('123')).toBe(false)
    expect(expression.test('NC')).toBe(true)
    expect(compileChallengeTitleSearch('[', true)).toBeNull()
    expect(isChallengeVisible({ title: '[' }, { search: null, hideSolved: false, hideLocked: false, solvedByMyTeam: false })).toBe(false)
    expect(compileChallengeTitleSearch('   ', true)).toBe('')
    expect(compileChallengeTitleSearch(' FOO.* ', false)).toBe('foo.*')
  })

  test('combines regex mode with tag filters and recovers from invalid typing without changing selection', async () => {
    const app = harness({ tag: 'Web' })
    await drain()
    app.state.regexSearch.value = true
    app.state.search.value = '^(SQL|HTTP)$'
    await drain()
    expect(app.state.listOptions.value.map((item: any) => item.value)).toEqual(['sql', 'http'])
    expect(app.state.searchError.value).toBeNull()
    const readyCount = app.ready.length
    app.state.search.value = '('
    await drain()
    expect(app.state.listOptions.value).toEqual([])
    expect(app.state.searchError.value).toEqual({ key: 'challengeNavigator.invalidRegex' })
    expect(app.ready).toHaveLength(readyCount)
    app.state.search.value = '^http$'
    await drain()
    expect(app.state.listOptions.value.map((item: any) => item.value)).toEqual(['http'])
    expect(app.state.searchError.value).toBeNull()
    expect(app.state.selectedTags.value).toEqual(['Web'])
    app.state.regexSearch.value = false
    expect(app.state.listOptions.value).toEqual([])
    app.state.search.value = ' HTTP '
    expect(app.state.listOptions.value.map((item: any) => item.value)).toEqual(['http'])
    app.stop()
  })

  test('restores repeated query tags and preserves other query state', async () => {
    const app = harness({ tag: [' web ', 'WEB', 'SQL'], retained: 'yes' })
    await drain()
    expect(app.state.selectedTags.value).toEqual(['web', 'SQL'])
    expect(app.state.listOptions.value.map((item: any) => item.value)).toEqual(['sql'])
    expect(app.state.tagOptions.value).toEqual(['HTTP', 'SQL', 'Web'])
    app.state.search.value = 'nothing'
    expect(app.state.listOptions.value).toEqual([])
    expect(app.state.tagOptions.value).toEqual(['HTTP', 'SQL', 'Web'])
    app.state.search.value = ''
    app.state.updateSelectedTags(['HTTP'])
    await drain()
    expect(app.route.query).toEqual({ tag: ['HTTP'], retained: 'yes' })
    expect(app.ready.at(-1)).toBe('http')
    app.props.selectedChallengeId = 'http'
    await drain()
    expect(app.state.selectedTags.value).toEqual(['HTTP'])
    app.state.updateSelectedTags([])
    await drain()
    expect(app.route.query).toEqual({ retained: 'yes' })
    expect(app.state.listOptions.value).toHaveLength(3)
    app.stop()
  })

  test('retains vanished selected tags on event refresh with a removable no-match state', async () => {
    const app = harness({ tag: 'SQL' })
    await drain()
    app.refresh([challenge('sql', ['Web'])])
    await drain()
    expect(app.state.selectedTags.value).toEqual(['SQL'])
    expect(app.state.listOptions.value).toEqual([])
    expect(app.state.emptyLabel.value).toBe('challengeNavigator.noMatches')
    app.state.updateSelectedTags([])
    await drain()
    expect(app.state.listOptions.value).toHaveLength(1)
    app.stop()
    expect(tagsFromQuery([null, 'Web', 'web'])).toEqual(['Web'])
  })
})
