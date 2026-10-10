import { describe, expect, test } from 'bun:test'

import { adminChallengeBankCreateTemplate } from '../app/api'
import { createClient } from '../app/api/client'

import {
  buildConfigValues,
  BloodRewardPolicy,
  challengeRuleFields,
  competitionConfigFields,
  CtfInteraction,
  defaultDefinition,
  definitionContractToModel,
  definitionModelToContract,
  emptyContainerDefinition,
  emptyDefinition,
  emptyOvaDefinition,
  emptyRuntimeTemplate,
  FlagSource,
  readConfigValues,
  ScoreDecayMode,
  UrlExposure,
} from '../app/utils/game-config'
import {
  challengeRuntimeDefinitionsEqual,
  mergeChallengeModeDefinition,
  mergeChallengeRuntimeDefinition,
} from '../app/features/admin/challenge-definition-sections'

describe('typed game configuration contracts', () => {
  test('new environments default to one named service with portable resource units', () => {
    expect(emptyContainerDefinition().services).toHaveLength(1)
    expect(emptyContainerDefinition().services[0]).toMatchObject({ name: 'main', cpuCores: 0.5, memoryMiB: 512 })
    expect('security' in emptyContainerDefinition()).toBeFalse()
  })

  test.each(['Ctf', 'Awd', 'Awdp', 'Koh', 'LiveSolo'] as const)('%s defaults select one typed branch without a schema version', (mode) => {
    const definition = defaultDefinition(mode) as unknown as Record<string, unknown>
    expect(definition.mode).toBe(mode)
    expect(definition[mode === 'LiveSolo' ? 'liveSolo' : mode.toLowerCase()]).toBeDefined()
    expect(['ctf', 'awd', 'awdp', 'koh', 'liveSolo'].filter(key => definition[key] != null)).toHaveLength(1)
    expect('schemaVersion' in definition).toBeFalse()
  })

  test.each(['Ctf', 'Awd', 'Awdp', 'Koh', 'LiveSolo'] as const)('%s serialized runtime selects a typed branch', (mode) => {
    const model = emptyDefinition(mode)
    model.runtime = emptyRuntimeTemplate(mode)
    const payload = JSON.parse(JSON.stringify(definitionModelToContract(mode, model))) as {
      mode?: string
      runtime?: { kind?: string, container?: object }
    }
    expect(payload.mode).toBe(mode)
    expect(payload.runtime?.kind).toBe('Container')
    expect(payload.runtime?.container).toBeDefined()
  })

  test('Ova runtimes serialize only their matching branch', () => {
    for (const definition of [emptyOvaDefinition()]) {
      const model = emptyDefinition('Ctf')
      model.runtime = emptyRuntimeTemplate('Ctf')
      model.runtime.definition = definition
      const payload = JSON.parse(JSON.stringify(definitionModelToContract('Ctf', model))) as {
        runtime?: Record<string, unknown>
      }
      expect(payload.runtime?.[String(payload.runtime.kind).toLowerCase()]).toBeDefined()
      expect(['container', 'ova'].filter(key => payload.runtime?.[key] != null)).toHaveLength(1)
      const restored = definitionContractToModel(payload as ReturnType<typeof definitionModelToContract>, 'Ctf')
      expect(restored.runtime?.definition.kind).toBe(definition.kind)
    }
  })

  test('definition reader rejects absent, mismatched and multiple mode branches', () => {
    const current = defaultDefinition('Ctf')
    expect(() => definitionContractToModel({ ...current, ctf: null }, 'Ctf')).toThrow()
    expect(() => definitionContractToModel({ ...current, ctf: null, awd: { flagInjection: null } }, 'Ctf')).toThrow()
    expect(() => definitionContractToModel({ ...current, awd: { flagInjection: null } }, 'Ctf')).toThrow()
  })

  test('runtime reader rejects a branch that conflicts with kind', () => {
    const model = emptyDefinition('Ctf')
    model.runtime = emptyRuntimeTemplate('Ctf')
    const current = definitionModelToContract('Ctf', model)
    expect(() => definitionContractToModel({ ...current, runtime: {
      ...current.runtime!, kind: 'Container', container: null,
      ova: { sourceUrl: 'https://example.test/a.ova', sha256: 'abc' },
    } }, 'Ctf')).toThrow()
  })

  test('generated create-template SDK sends the branch regardless of property order', async () => {
    const sent: string[] = []
    const client = createClient({
      baseUrl: 'https://noctf.test',
      fetch: async (input) => {
        sent.push(await new Request(input).text())
        return new Response(JSON.stringify({ id: '00000000-0000-0000-0000-000000000001' }), {
          status: 201,
          headers: { 'Content-Type': 'application/json' },
        })
      },
    })
    const model = emptyDefinition('Ctf')
    model.runtime = emptyRuntimeTemplate('Ctf')
    await adminChallengeBankCreateTemplate({
      client,
      body: {
        mode: 'Ctf',
        visibility: 'Private',
        title: 'Current template',
        direction: 'Web',
        definition: definitionModelToContract('Ctf', model),
      },
    })
    expect(sent).toHaveLength(1)
    const payload = JSON.parse(sent[0]!) as { definition: { mode: string, ctf: object, runtime: { kind: string, container: object } } }
    expect(payload.definition.mode).toBe('Ctf')
    expect(payload.definition.ctf).toBeDefined()
    expect(payload.definition.runtime.container).toBeDefined()
  })

  test('named services, resources and the single public entry list round-trip', () => {
    const model = emptyDefinition('Ctf')
    model.runtime = emptyRuntimeTemplate('Ctf')
    model.runtime.definition = emptyContainerDefinition(true)
    model.runtime.definition.services[0]!.image = 'challenge:latest'
    model.runtime.definition.services[0]!.cpuCores = 0.501
    model.runtime.urlBindings = [{ serviceName: 'main', containerPort: 8080, urlTemplate: 'nc {HOST} {PORT}', exposure: UrlExposure.OwnerOnly }]
    const contract = definitionModelToContract('Ctf', model)
    const restored = definitionContractToModel(contract, 'Ctf')
    expect(restored.runtime?.definition).toEqual(model.runtime.definition)
    expect(restored.runtime?.urlBindings).toEqual(model.runtime.urlBindings)
    expect(contract.runtime?.limits).toBeNull()
    expect('portMappings' in contract.runtime!.container!).toBeFalse()
  })

  test('definition flag template is not lost while editing', () => {
    const model = emptyDefinition('Ctf')
    model.flagTemplate = { header: 'FLAG', bodyTemplate: '[TEAM]', leetLiteralText: true }
    const contract = definitionModelToContract('Ctf', model)

    expect(definitionContractToModel(contract, 'Ctf').flagTemplate).toEqual(model.flagTemplate)
  })

  test('runtime access exposure survives typed protocol conversion', () => {
    const model = emptyDefinition('Ctf')
    model.runtime = emptyRuntimeTemplate('Ctf')
    model.runtime.urlBindings = [
      { urlTemplate: 'http://{HOST}:{PORT}', exposure: UrlExposure.OwnerOnly, containerPort: 80, serviceName: '' },
      { urlTemplate: 'tcp://{HOST}:{PORT}', exposure: UrlExposure.Participants, containerPort: 81, serviceName: '' },
    ]

    const contract = definitionModelToContract('Ctf', model)
    const restored = definitionContractToModel(contract, 'Ctf')
    expect(restored.runtime?.urlBindings.map(binding => binding.exposure)).toEqual([
      UrlExposure.OwnerOnly,
      UrlExposure.Participants,
    ])
  })

  test('score and blood reward enums round-trip as current protocol strings', () => {
    const fields = competitionConfigFields('Ctf')
    const source = {
      mode: 'Ctf',
      flagTemplate: { header: 'flag', bodyTemplate: '[GUID]', leetLiteralText: false },
      ctf: {
        defaultScoreCurve: { initialPoints: 500, minimumPoints: 100, decayTeamCount: 10, decayMode: 'Custom', customExpression: '500-x' },
        bloodRewards: [{ policy: 'CurrentPointsPercentage', value: 10 }],
      },
    }
    const parsed = readConfigValues(source, fields, { rules: false })
    expect(parsed?.values.defaultScoreCurve).toMatchObject({ decayMode: ScoreDecayMode.Custom })
    expect(parsed?.values.bloodRewards).toEqual([{ policy: BloodRewardPolicy.CurrentPointsPercentage, value: 10 }])

    const written = buildConfigValues('Ctf', fields, parsed!.values, { rules: false })
    expect((written.ctf as Record<string, unknown>).defaultScoreCurve).toMatchObject({ decayMode: 'Custom', customExpression: '500-x' })
    expect((written.ctf as Record<string, unknown>).bloodRewards).toEqual([{ policy: 'CurrentPointsPercentage', value: 10 }])
  })

  test('AWDP evaluation dispatch round-trips as the current protocol string', () => {
    const fields = competitionConfigFields('Awdp')
    const parsed = readConfigValues({
      mode: 'Awdp',
      flagTemplate: { header: 'flag', bodyTemplate: '[GUID]', leetLiteralText: false },
      awdp: { evaluationDispatchMode: 'Manual' },
    }, fields, { rules: false })
    expect(parsed?.values.evaluationDispatchMode).toBe(1)
    const written = buildConfigValues('Awdp', fields, parsed!.values, { rules: false })
    expect((written.awdp as Record<string, unknown>).evaluationDispatchMode).toBe('Manual')
  })

  test('configuration reader rejects mismatched and multiple mode branches', () => {
    const fields = competitionConfigFields('Ctf')
    expect(readConfigValues({ mode: 'Ctf', awd: {} }, fields, { rules: false })).toBeNull()
    expect(readConfigValues({ mode: 'Ctf', ctf: {}, awd: {} }, fields, { rules: false })).toBeNull()
  })

  test('challenge rules retain AWDP curve names and nullable override semantics', () => {
    const fields = challengeRuleFields('Awdp')
    const parsed = readConfigValues({ mode: 'Awdp', awdp: {
      breakScoreCurve: { initialPoints: 300, minimumPoints: 100, decayTeamCount: 5, decayMode: 'Fixed' },
      fixScoreCurve: null,
    } }, fields, { rules: true })
    expect(parsed?.overridden.breakScoreCurve).toBeTrue()
    expect(parsed?.overridden.fixScoreCurve).toBeFalse()
    const written = buildConfigValues('Awdp', fields, parsed!.values, { rules: true, overridden: parsed!.overridden })
    expect((written.awdp as Record<string, unknown>).breakScoreCurve).toMatchObject({ initialPoints: 300 })
    expect((written.awdp as Record<string, unknown>).fixScoreCurve).toBeUndefined()
  })

  test('competition configuration and rules emit one mode branch', () => {
    const fields = competitionConfigFields('Koh')
    const configuration = buildConfigValues('Koh', fields, {
      pollIntervalSeconds: 5,
      controlPointsPerInterval: 10,
    }, { rules: false })
    expect(configuration).toEqual({
      mode: 'Koh',
      flagTemplate: { header: 'flag', bodyTemplate: '[GUID]', leetLiteralText: false },
      koh: { pollIntervalSeconds: 5, controlPointsPerInterval: 10 },
    })
    const rules = buildConfigValues('Awdp', challengeRuleFields('Awdp'), {}, { rules: true })
    expect(rules).toEqual({ mode: 'Awdp', awdp: {} })
  })

  test('section merges preserve untouched typed state', () => {
    const persisted = emptyDefinition('Awdp')
    persisted.runtime = emptyRuntimeTemplate('Awdp')
    persisted.checkerFixInput = true
    const runtimeDraft = structuredClone(persisted)
    if (!runtimeDraft.runtime) throw new Error('Expected Runtime defaults.')
    runtimeDraft.runtime.ttlSeconds = 120

    const runtimeMerged = mergeChallengeRuntimeDefinition(persisted, runtimeDraft)
    expect(runtimeMerged.checkerFixInput).toBeTrue()
    expect(runtimeMerged.runtime?.ttlSeconds).toBe(120)

    const modeDraft = structuredClone(runtimeMerged)
    modeDraft.checkerFixInput = true
    const modeMerged = mergeChallengeModeDefinition('Awdp', runtimeMerged, 'Awdp', modeDraft)
    expect(modeMerged.checkerFixInput).toBeTrue()
    expect(challengeRuntimeDefinitionsEqual('Awdp', modeMerged, runtimeMerged)).toBeTrue()
  })

  test('CTF interaction is represented by the typed branch', () => {
    const model = emptyDefinition('Ctf')
    model.interactionKind = CtfInteraction.PatchVerification
    const contract = definitionModelToContract('Ctf', model) as unknown as Record<string, unknown>
    expect(contract.mode).toBe('Ctf')
    expect((contract.ctf as Record<string, unknown>).interactionKind).toBe('PatchVerification')
  })
})
