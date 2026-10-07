import { describe, expect, test } from 'bun:test'
import { computed, effectScope, nextTick, onScopeDispose, reactive, ref, watch } from 'vue'
import { validateAppealStatement } from '../app/lib/participant-form-validation'

const source = await Bun.file(new URL('../app/features/competition/useCompetitionTeamBanScreen.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }

function harness() {
  const reads: Array<{ options: any; resolve: (value: any) => void; reject: (error: unknown) => void }> = []
  const appeals: any[] = []
  let teamRefreshes = 0
  const props = reactive({ competitionId: 'competition', teamId: 'team', teamName: undefined as string | undefined, teamAvatarUrl: null as string | null })
  const dependencies = { computed, ref, watch, onScopeDispose, logoUrl: '/logo.svg', validateAppealStatement,
    message: (key: string) => ({ key }), parseApiError: (_: unknown, fallback: any) => ({ displayMessage: fallback }),
    getMyTeamBanCase: (options: any) => new Promise((resolve, reject) => reads.push({ options, resolve, reject })),
    submitTeamBanAppeal: async (options: any) => { appeals.push(options); return {} } }
  const factory = new Function('dependencies', `const { ${Object.keys(dependencies).join(', ')} } = dependencies; ${compiled}; return useCompetitionTeamBanScreen;`)(dependencies)
  const scope = effectScope()
  const state = scope.run(() => factory(props, () => { teamRefreshes++ }))!
  return { props, state, reads, appeals, teamRefreshes: () => teamRefreshes, stop: () => scope.stop() }
}
const ban = (source = 'CheatIncident', canAppeal = true) => ({ teamId: 'team', isCurrentlyBanned: true, source, canAppeal })

describe('team ban screen', () => {
  test('uses the banned team avatar and responds to refreshed team identity', async () => {
    const app = harness()
    app.props.teamName = '  Alpha squad  '
    app.props.teamAvatarUrl = '/api/v1/teams/team/avatar?revision=first'
    expect(app.state.teamAvatarUrl.value).toBe('/api/v1/teams/team/avatar?revision=first')
    expect(app.state.teamInitials.value).toBe('AL')
    app.props.teamName = '星月'
    app.props.teamAvatarUrl = '/api/v1/teams/team/avatar?revision=second'
    expect(app.state.teamName.value).toBe('星月')
    expect(app.state.teamInitials.value).toBe('星月')
    expect(app.state.teamAvatarUrl.value).toContain('revision=second')
    app.stop()
  })

  test('uses the case team name as fallback when no avatar or parent name is available', async () => {
    const app = harness()
    app.reads[0]!.resolve({ data: { ...ban(), teamName: '🦖 Squad' } }); await drain()
    expect(app.state.teamAvatarUrl.value).toBe(undefined)
    expect(app.state.teamInitials.value).toBe('🦖 ')
    app.props.teamName = 'Beta'
    expect(app.state.teamInitials.value).toBe('BE')
    app.stop()
  })

  test('preserves the ban source when refreshing manual and cheating bans', async () => {
    const app = harness()
    app.reads[0]!.resolve({ data: ban() }); await drain()
    expect(app.state.isCheatingBan.value).toBe(true)
    const refreshed = app.state.refresh()
    app.reads[1]!.resolve({ data: ban('ManualModeration') }); await refreshed
    expect(app.state.isCheatingBan.value).toBe(false)
    app.stop()
  })

  test('keeps captain authorization and validates appeal length', async () => {
    const app = harness()
    app.reads[0]!.resolve({ data: ban('CheatIncident', false) }); await drain()
    app.state.statement.value = 'A valid statement about the contest.'
    await app.state.submitAppeal()
    expect(app.appeals).toHaveLength(0)
    const refreshed = app.state.refresh(); app.reads[1]!.resolve({ data: ban() }); await refreshed
    app.state.statement.value = 'short'
    await app.state.submitAppeal()
    expect(app.state.appealError.value.key).toBe('teamBanScreen.statementRequired')
    app.state.statement.value = '   A valid statement about the contest.   '
    app.state.setAppealOpen(true)
    const submitted = app.state.submitAppeal(); await drain()
    app.reads[2]!.resolve({ data: { ...ban('CheatIncident', false), appeal: { status: 'Submitted' } } }); await submitted
    expect(app.appeals[0].body.statement).toBe('A valid statement about the contest.')
    expect(app.state.appealOpen.value).toBe(false)
    expect(app.state.appealStatus.value).toBe('Submitted')
    app.stop()
  })

  test('aborts stale team reads and does not reuse another team ban', async () => {
    const app = harness()
    app.props.teamId = 'new-team'; await drain()
    expect(app.reads[0]!.options.signal.aborted).toBe(true)
    app.reads[1]!.resolve({ data: ban() }); await drain()
    expect(app.state.isCheatingBan.value).toBe(false)
    app.stop()
    expect(app.reads[1]!.options.signal.aborted).toBe(true)
  })

  test('manual refresh rechecks the parent team so missed unban events can recover', async () => {
    const app = harness()
    app.reads[0]!.resolve({ data: ban() }); await drain()
    const refreshed = app.state.refreshScreen()
    expect(app.teamRefreshes()).toBe(1)
    app.reads[1]!.resolve({ data: { ...ban(), isCurrentlyBanned: false } }); await refreshed
    app.stop()
  })

  test('a network failure leaves the confirmed notice visible and allows retry', async () => {
    const app = harness()
    app.reads[0]!.resolve({ data: ban() }); await drain()
    const refreshed = app.state.refresh()
    app.reads[1]!.reject(new Error('offline')); await refreshed
    expect(app.state.loading.value).toBe(false)
    expect(app.state.error.value.key).toBe('teamBanScreen.loadFailed')
    expect(app.state.isCheatingBan.value).toBe(true)
    const retry = app.state.refresh()
    app.reads[2]!.resolve({ data: ban() }); await retry
    expect(app.state.error.value).toBe(null)
    app.stop()
  })
})
