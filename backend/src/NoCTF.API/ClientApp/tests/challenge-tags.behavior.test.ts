import { describe, expect, test } from 'bun:test'
import { computed, effectScope, nextTick, onScopeDispose, reactive, ref, toRefs, watch } from 'vue'
import { challengeTagOptions, matchesAllTags, tagKey, tagsFromQuery, uniqueTags, validChallengeTags } from '../app/lib/challenge-tags'
import { isChallengeVisible } from '../app/features/competition/useCompetitionChallengeNavigator'

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
