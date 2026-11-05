import { describe, expect, test } from 'bun:test'

import type { NoCtfapiEndpointsAdministrationChallengeBankCreateChallengeTemplateRequest } from '../app/api'

import {
  buildConfigValues,
  challengeRuleFields,
  competitionConfigFields,
  defaultDefinition,
  definitionContractToModel,
  definitionModelToContract,
  emptyContainerDefinition,
  emptyDefinition,
  emptyOvaDefinition,
  emptyRuntimeTemplate,
  readConfigValues,
  setChallengeCapability,
} from '../app/utils/game-config'
import {
  challengeRuntimeDefinitionsEqual,
  mergeChallengeCapabilityDefinition,
  mergeChallengeRuntimeDefinition,
} from '../app/features/admin/challenge-definition-sections'

describe('typed game configuration contracts', () => {
  test('new environments default to one named service with portable resource units', () => {
    expect(emptyContainerDefinition().services).toHaveLength(1)
    expect(emptyContainerDefinition().services[0]).toMatchObject({ name: 'main', cpuCores: 0.5, memoryMiB: 512 })
    expect('security' in emptyContainerDefinition()).toBeFalse()
  })

  test.each(['Break', 'Fix', 'Check', 'Control', 'Questionnaire'] as const)('%s selects its independent typed configuration', (kind) => {
    const model = emptyDefinition()
    model.capabilities = []
    setChallengeCapability(model, kind, true)
    const definition = definitionModelToContract(model)
    expect(definition.capabilities).toHaveLength(1)
    const capability = definition.capabilities![0]!
    expect(capability.kind).toBe(kind)
    expect(['break', 'fix', 'check', 'control', 'questionnaire'].filter(key => capability[key as keyof typeof capability] != null)).toEqual([kind.toLowerCase()])
    expect('mode' in definition).toBeFalse()
  })

  test('serialized runtime selects its typed construction branch', () => {
    const model = emptyDefinition()
    model.runtime = emptyRuntimeTemplate()
    const payload = JSON.parse(JSON.stringify(definitionModelToContract(model))) as {
      runtime?: { kind?: string, container?: object }
    }
    expect(payload.runtime?.kind).toBe('Container')
    expect(payload.runtime?.container).toBeDefined()
  })

  test('Ova runtimes serialize only their matching branch', () => {
    for (const definition of [emptyOvaDefinition()]) {
      const model = emptyDefinition()
      model.runtime = emptyRuntimeTemplate()
      model.runtime.definition = definition
      const payload = JSON.parse(JSON.stringify(definitionModelToContract(model))) as {
        runtime?: Record<string, unknown>
      }
      expect(payload.runtime?.[String(payload.runtime.kind).toLowerCase()]).toBeDefined()
      expect(['container', 'ova'].filter(key => payload.runtime?.[key] != null)).toHaveLength(1)
      const restored = definitionContractToModel(payload as ReturnType<typeof definitionModelToContract>)
      expect(restored.runtime?.definition.kind).toBe(definition.kind)
    }
  })

  test('definition reader rejects absent, mismatched and duplicate capability branches', () => {
    const current = defaultDefinition()
    expect(() => definitionContractToModel({ ...current, capabilities: [] })).toThrow()
    expect(() => definitionContractToModel({ ...current, capabilities: [{ kind: 'Break', fix: {} }] })).toThrow()
    expect(() => definitionContractToModel({ ...current, capabilities: [{ kind: 'Break', break: {}, fix: {} }] })).toThrow()
    expect(() => definitionContractToModel({ ...current, capabilities: [...current.capabilities!, ...current.capabilities!] })).toThrow()
  })

  test('runtime reader rejects a branch that conflicts with kind', () => {
    const model = emptyDefinition()
    model.runtime = emptyRuntimeTemplate()
    const current = definitionModelToContract(model)
    expect(() => definitionContractToModel({ ...current, runtime: {
      ...current.runtime!, kind: 'Container', container: null,
      ova: { sourceUrl: 'https://example.test/a.ova', sha256: 'abc' },
    } })).toThrow()
  })

  test('create-template payload retains its runtime and capabilities through serialization', () => {
    const model = emptyDefinition()
    model.runtime = emptyRuntimeTemplate()
    const request: NoCtfapiEndpointsAdministrationChallengeBankCreateChallengeTemplateRequest = {
        visibility: 'Private',
        title: 'Current template',
        direction: 'Web',
        definition: definitionModelToContract(model),
    }
    const payload = JSON.parse(JSON.stringify(request)) as typeof request
    expect(payload.definition.capabilities?.map(capability => capability.kind)).toEqual(['Break'])
    expect(payload.definition.runtime?.container).toBeDefined()
    expect('mode' in payload).toBeFalse()
  })

  test('named services, resources and the single public entry list round-trip', () => {
    const model = emptyDefinition()
    model.runtime = emptyRuntimeTemplate()
    model.runtime.definition = emptyContainerDefinition(true)
    model.runtime.definition.services[0]!.image = 'challenge:latest'
    model.runtime.definition.services[0]!.cpuCores = 0.501
    model.runtime.urlBindings = [{ serviceName: 'main', containerPort: 8080, urlTemplate: 'nc {HOST} {PORT}', exposure: 'OwnerOnly' }]
    const contract = definitionModelToContract(model)
    const restored = definitionContractToModel(contract)
    expect(restored.runtime?.definition).toEqual(model.runtime.definition)
    expect(restored.runtime?.urlBindings).toEqual(model.runtime.urlBindings)
    expect(contract.runtime?.limits).toBeNull()
    expect('portMappings' in contract.runtime!.container!).toBeFalse()
  })

  test('definition flag template is not lost while editing', () => {
    const model = emptyDefinition()
    model.flagTemplate = { header: 'FLAG', bodyTemplate: '[TEAM]', leetLiteralText: true }
    const contract = definitionModelToContract(model)

    expect(definitionContractToModel(contract).flagTemplate).toEqual(model.flagTemplate)
  })

  test('runtime access exposure survives typed protocol conversion', () => {
    const model = emptyDefinition()
    model.runtime = emptyRuntimeTemplate()
    model.runtime.urlBindings = [
      { urlTemplate: 'http://{HOST}:{PORT}', exposure: 'OwnerOnly', containerPort: 80, serviceName: '' },
      { urlTemplate: 'tcp://{HOST}:{PORT}', exposure: 'Participants', containerPort: 81, serviceName: '' },
    ]

    const contract = definitionModelToContract(model)
    const restored = definitionContractToModel(contract)
    expect(restored.runtime?.urlBindings.map(binding => binding.exposure)).toEqual([
      'OwnerOnly',
      'Participants',
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
    expect(parsed?.values.defaultScoreCurve).toMatchObject({ decayMode: 'Custom' })
    expect(parsed?.values.bloodRewards).toEqual([{ policy: 'CurrentPointsPercentage', value: 10 }])

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
    expect(parsed?.values.evaluationDispatchMode).toBe('Manual')
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
    expect(rules).toEqual({ mode: 'Awdp', awdp: { runtime: { allocation: 'PerTeam', flagSource: 'PerTeam', ttlSeconds: 900 } } })
  })

  test('section merges preserve untouched typed state', () => {
    const persisted = emptyDefinition()
    persisted.runtime = emptyRuntimeTemplate()
    persisted.checkerFixInput = true
    const runtimeDraft = structuredClone(persisted)
    if (!runtimeDraft.runtime) throw new Error('Expected Runtime defaults.')
    runtimeDraft.runtime.ttlSeconds = 120

    const runtimeMerged = mergeChallengeRuntimeDefinition(persisted, runtimeDraft)
    expect(runtimeMerged.checkerFixInput).toBeTrue()
    expect(runtimeMerged.runtime?.ttlSeconds).toBe(120)

    const capabilityDraft = structuredClone(runtimeMerged)
    capabilityDraft.checkerFixInput = true
    const merged = mergeChallengeCapabilityDefinition(runtimeMerged, capabilityDraft)
    expect(merged.checkerFixInput).toBeTrue()
    expect(challengeRuntimeDefinitionsEqual(merged, runtimeMerged)).toBeTrue()
  })

  test('a repairable template retains Break, Fix and Check together', () => {
    const model = emptyDefinition()
    setChallengeCapability(model, 'Fix', true)
    setChallengeCapability(model, 'Check', true)
    const contract = definitionModelToContract(model)
    expect(contract.capabilities?.map(capability => capability.kind)).toEqual(['Break', 'Fix', 'Check'])
    expect(definitionContractToModel(contract).capabilities).toEqual(model.capabilities)
    expect('mode' in contract).toBeFalse()
  })
})
