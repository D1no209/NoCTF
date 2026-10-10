import { describe, expect, test } from 'bun:test'
import { computed, effectScope, markRaw, nextTick, proxyRefs, reactive, ref, toRefs, watch } from 'vue'
import { gameModeOptions, isGameMode } from '../app/utils/game-modes'
import { applyCtfInteraction, CtfInteraction, defaultDefinition, definitionContractToModel, definitionModelToContract,
  emptyDefinition, emptyRuntimeTemplate, FlagSource, RuntimeAllocation, UrlExposure } from '../app/utils/game-config'
import { useDefinitionModel } from '../app/composables/useDefinitionModel'
import { validateChallengeTemplateDraft } from '../app/lib/challenge-template-validation'
import { translate } from '../app/utils/i18n'
import { localeDomainsForPath } from '../app/locales/route-domains'
import { challengeRuntimeDefinitionsEqual, mergeChallengeModeDefinition, mergeChallengeRuntimeDefinition } from '../app/features/admin/challenge-definition-sections'

async function harness(path: string, name: string, props?: object) {
  const source = await Bun.file(new URL(`../app/${path}`, import.meta.url)).text()
  const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
    .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
  const writes: any[] = []
  const mounted: Array<() => unknown> = []
  const competition = ref<any>({ id: 'contest', mode: 'LiveSolo', administrationRole: 'Owner' })
  const events: any[] = []
  const deps: Record<string, any> = {
    computed, effectScope, markRaw, proxyRefs, reactive, ref, toRefs, watch,
    gameModeOptions, isGameMode, applyCtfInteraction, CtfInteraction, defaultDefinition, definitionContractToModel,
    definitionModelToContract, FlagSource, RuntimeAllocation, UrlExposure, useDefinitionModel, validateChallengeTemplateDraft,
    challengeRuntimeDefinitionsEqual, mergeChallengeModeDefinition, mergeChallengeRuntimeDefinition,
    bytesToMib: (x: number) => x, cpuMillicoresToCores: (x: number) => x,
    translate, gameModeLabel: (x: string) => x, describeMessage: (key: string) => key,
    useRoute: () => ({ params: { id: 'contest' }, path: '/admin/competitions/contest', query: {} }),
    useAuth: () => ({ canOrganize: ref(true), isAdministrator: ref(true), user: ref({ userId: 'admin' }) }),
    usePlatform: () => ({ configuration: ref({}) }),
    onMounted: (callback: () => unknown) => mounted.push(callback),
    provide: () => {}, CompetitionAdminKey: Symbol(), adminWorkspacePath: (x: string) => x,
    localInputToIso: (x: string) => new Date(x).toISOString(),
    toast: { success: () => {}, error: () => {} }, parseApiError: () => ({ displayMessage: 'error' }),
    challengeDirectionOptions: ['Misc'], directionLabel: (x: string) => x,
    adminCreateCompetition: async (options: any) => { writes.push(options.body); return { data: { id: 'new-contest', mode: options.body.mode } } },
    adminChallengeBankCreateTemplate: async (options: any) => { writes.push(options.body); return { data: { id: 'new-template' } } },
    adminChallengeBankPatchTemplate: async (options: any) => {
      writes.push(options.body)
      return { data: { id: 'template', ...options.body.content } }
    },
    adminGetCompetition: async () => ({ data: { competition: competition.value } }),
  }
  for (const match of source.matchAll(/import (\w+) from '[^']+\.vue'/g)) deps[match[1]!] = {}
  for (const icon of ['Paperclip', 'RotateCcw', 'Trash2', 'Upload', 'Activity', 'ChartNoAxesCombined', 'ClipboardCheck',
    'Container', 'Download', 'FileCheck', 'GitBranch', 'KeyRound', 'LayoutDashboard', 'Mail', 'Network', 'Orbit', 'Puzzle', 'Settings', 'ShieldAlert', 'Trophy', 'Users', 'Webhook']) deps[icon] = {}
  const factory = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return ${name};`)(deps)
  const scope = effectScope()
  const state = scope.run(() => factory(props ? reactive(props) : undefined, (...args: any[]) => events.push(args)))!
  return { state, writes, events, mounted, competition, stop: () => scope.stop() }
}

describe('LiveSolo authoring entry points', () => {
  test('all five modes are selectable and unknown protocol values are rejected', () => {
    expect(gameModeOptions.map(x => x.value)).toEqual(['Ctf', 'Awd', 'Awdp', 'Koh', 'LiveSolo'])
    expect(isGameMode('LiveSolo')).toBe(true)
    for (const value of ['livesolo', 'toString', 4, null]) expect(isGameMode(value)).toBe(false)
  })

  test('competition creation submits LiveSolo without CTF practice', async () => {
    const app = await harness('features/competitions/useCreateCompetitionDialog.ts', 'useCreateCompetitionDialog', { open: true })
    try {
      app.state.title.value = 'Synthetic LiveSolo'
      app.state.mode.value = 'LiveSolo'
      app.state.startTime.value = '2026-10-10T08:00:00Z'
      app.state.endTime.value = '2026-10-10T12:00:00Z'
      app.state.practiceModeEnabled.value = true
      await app.state.submit()
      expect(app.state.modeOptions).toEqual(gameModeOptions)
      expect(app.writes).toHaveLength(1)
      expect(app.writes[0]).toMatchObject({ mode: 'LiveSolo', practiceModeEnabled: false })
      expect(app.events.find(x => x[0] === 'created')[1].mode).toBe('LiveSolo')
    } finally { app.stop() }
  })

  test('template creation changes the definition branch and submits LiveSolo', async () => {
    const app = await harness('features/admin/useChallengeTemplateCreateDialog.ts', 'useChallengeTemplateCreateDialog', { open: true })
    try {
      app.state.changeMode('Awdp')
      app.state.changeMode('LiveSolo')
      app.state.title = 'Synthetic template'
      expect(app.state.mode).toBe('LiveSolo')
      expect(app.state.definition).toMatchObject({ mode: 'LiveSolo', liveSolo: {} })
      expect(app.state.definition.awdp).toBeUndefined()
      app.state.changeMode('unknown')
      expect(app.state.mode).toBe('LiveSolo')
      await app.state.submit()
      expect(app.writes).toHaveLength(1)
      expect(app.writes[0]).toMatchObject({ mode: 'LiveSolo', definition: { mode: 'LiveSolo', liveSolo: {}, checker: null } })
    } finally { app.stop() }
  })

  test('template editing supports LiveSolo dynamic flag settings and static regex flags', async () => {
    const app = await harness('features/routes/admin/challenges/useAdminChallengesByIdPage.ts', 'useAdminChallengesByIdPage')
    try {
      app.state.changeMode('LiveSolo')
      expect(app.state.form.mode).toBe('LiveSolo')
      expect(app.state.form.definition.liveSolo).toBeDefined()
      expect(app.state.showInteractionKind.value).toBe(false)
      app.state.template.value = { id: 'template', mode: 'LiveSolo', definition: defaultDefinition('LiveSolo') }
      expect(app.state.supportsRegularExpression.value).toBe(true)
      const definition = emptyDefinition('LiveSolo')
      definition.runtime = emptyRuntimeTemplate('LiveSolo')
      app.state.form.definition = definitionModelToContract('LiveSolo', definition)
      app.state.template.value.definition = app.state.form.definition
      await nextTick()
      expect(app.state.hasModeDefinition.value).toBe(true)
      expect(app.state.usesRuntimeFlagInjection.value).toBe(true)
      expect(app.state.supportsRegularExpression.value).toBe(false)
    } finally { app.stop() }
  })

  test('LiveSolo runtime offers independent flags and only own-team access', async () => {
    const model = emptyDefinition('LiveSolo'); model.runtime = emptyRuntimeTemplate('LiveSolo')
    const app = await harness('features/admin/useDefinitionRuntime.ts', 'useDefinitionRuntime',
      { runtime: model.runtime, model, mode: 'LiveSolo', interactionKind: CtfInteraction.FlagSubmission, disabled: false })
    try {
      expect(app.state.runtime.value.allocation).toBe(RuntimeAllocation.PerTeam)
      expect(app.state.showDynamicFlagInjection.value).toBe(true)
      expect(app.state.exposureOptions.value.map((x: any) => x.value)).toEqual([UrlExposure.OwnerOnly])
      app.state.setDynamicFlagInjection(false)
      expect(app.state.runtime.value.flagSource).toBe(FlagSource.Static)
      expect(app.state.runtime.value.definition.services[0].flagEnvironmentVariableName).toBe('')
      app.state.setDynamicFlagInjection(true)
      expect(app.state.runtime.value.definition.services[0].flagEnvironmentVariableName).toBe('FLAG')
    } finally { app.stop() }
  })

  test('saving the runtime updates mode capabilities without overwriting an unsaved flag template', async () => {
    const app = await harness('features/routes/admin/challenges/useAdminChallengesByIdPage.ts', 'useAdminChallengesByIdPage')
    try {
      app.state.changeMode('LiveSolo')
      app.state.template.value = { id: 'template', title: 'Synthetic', direction: 'Misc', mode: 'LiveSolo', definition: defaultDefinition('LiveSolo') }
      app.state.definitionModel.value.flagTemplate = { header: 'flag', bodyTemplate: 'draft', leetLiteralText: false }
      const runtime = emptyDefinition('LiveSolo'); runtime.runtime = emptyRuntimeTemplate('LiveSolo')
      runtime.runtime.definition.services[0]!.image = 'nginx:alpine'
      runtime.runtime.urlBindings[0]!.containerPort = 80
      app.state.runtimeDefinitionModel.value = runtime
      await app.state.saveRuntimeDefinition()
      expect(app.writes).toHaveLength(1)
      expect(app.state.runtimeSaveErrors.value).toEqual([])
      expect(app.state.hasModeDefinition.value).toBe(true)
      expect(app.state.definitionModel.value.flagTemplate.bodyTemplate).toBe('draft')
    } finally { app.stop() }
  })

  test('LiveSolo template validation catches shared runtimes and checker interactions', () => {
    const model = emptyDefinition('LiveSolo')
    const draft = { mode: 'LiveSolo' as const, title: 'Synthetic', direction: 'Misc', definition: model }
    expect(validateChallengeTemplateDraft(draft)).toEqual([])
    model.runtime = emptyRuntimeTemplate('LiveSolo')
    model.runtime.allocation = RuntimeAllocation.Shared
    expect(validateChallengeTemplateDraft(draft)).toContain(translate('liveSolo.templates.independentRuntime'))
    model.runtime = null; model.checkerFixInput = true
    expect(validateChallengeTemplateDraft(draft)).toContain(translate('liveSolo.templates.flagSubmissionOnly'))
  })

  test('LiveSolo badge and administration navigation lead to independent workspaces', async () => {
    const badge = await harness('features/admin/useAdminGameModeBadge.ts', 'useAdminGameModeBadge', { mode: 'LiveSolo' })
    const admin = await harness('features/routes/admin/competitions/useAdminCompetitionsByIdPage.ts', 'useAdminCompetitionsByIdPage')
    try {
      expect(badge.state.label.value).toBe('LiveSolo')
      await admin.mounted[0]!()
      const links = admin.state.navGroups.value.flatMap((x: any) => x.items.map((item: any) => item.to))
      for (const suffix of ['', '/settings', '/groups', '/bracket']) expect(links).toContain(`/competitions/contest/live-solo${suffix}`)
      expect(links).not.toContain('/competitions/contest/live')
      expect(links).not.toContain('/competitions/contest/leaderboard')
      expect(links).not.toContain('/admin/competitions/contest/progression')
      expect(localeDomainsForPath('/')).toContain('live-solo')
      for (const mode of ['Ctf', 'Awd', 'Awdp', 'Koh']) {
        admin.state.competition.value.mode = mode
        const modeLinks = admin.state.navGroups.value.flatMap((x: any) => x.items.map((item: any) => item.to))
        expect(modeLinks.includes('/competitions/contest/live')).toBe(mode === 'Ctf')
        expect(modeLinks.includes('/competitions/contest/awdp-live')).toBe(mode === 'Awdp')
        expect(modeLinks).toContain('/admin/competitions/contest/leaderboard')
        expect(modeLinks).not.toContain('/competitions/contest/live-solo')
      }
    } finally { badge.stop(); admin.stop() }
  })
})
