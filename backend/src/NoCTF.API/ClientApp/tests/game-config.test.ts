import { describe, expect, test } from 'bun:test'
import {
  BLOOD_REWARD_POLICIES,
  BloodRewardPolicy,
  ScoreDecayMode,
  FlagSource,
  DEFAULT_RUNTIME_MEMORY_BYTES,
  DEFAULT_RUNTIME_NANO_CPUS,
  DEFAULT_RUNTIME_OPERATION_TIMEOUT_SECONDS,
  DEFAULT_RUNTIME_PIDS_LIMIT,
  DEFAULT_RUNTIME_TTL_SECONDS,
  challengeRuleFields,
  competitionConfigFields,
  ctfPointsAtSolveCount,
  emptyFlagTemplate,
  emptyDefinition,
  emptyRuntimeTemplate,
  parseDefinition,
  serializeDefinition,
} from '../app/utils/game-config'

describe('CTF blood reward policies', () => {
  test('offers the current challenge points percentage policy', () => {
    expect(BloodRewardPolicy.CurrentPointsPercentage).toBe(3)
    expect(BLOOD_REWARD_POLICIES).toContainEqual({
      value: BloodRewardPolicy.CurrentPointsPercentage,
      label: '当前分值百分比',
    })
  })
})

describe('shared dynamic score curves', () => {
  test('offers independent CTF and AWDP score curves with all preset modes', () => {
    expect(competitionConfigFields('Ctf').find(field => field.key === 'defaultScoreCurve')?.type).toBe('pointsCurve')
    expect(competitionConfigFields('Awdp').filter(field => field.type === 'pointsCurve').map(field => field.key))
      .toEqual(['break', 'fix'])
    expect(challengeRuleFields('Awdp').filter(field => field.type === 'pointsCurve').map(field => field.key))
      .toEqual(['break', 'fix'])
  })

  test('matches backend preset formulas and integer rounding', () => {
    const base = { initialPoints: 1000, minimumPoints: 100, decayTeamCount: 10, customExpression: null }
    expect(ctfPointsAtSolveCount({ ...base, decayMode: ScoreDecayMode.Linear }, 5)).toBe(600)
    expect(ctfPointsAtSolveCount({ ...base, decayMode: ScoreDecayMode.Quadratic }, 5)).toBe(822)
    expect(ctfPointsAtSolveCount({ ...base, decayMode: ScoreDecayMode.Exponential }, 5)).toBe(238)
    expect(ctfPointsAtSolveCount({ ...base, decayMode: ScoreDecayMode.Logarithmic }, 5)).toBe(371)
  })
})

describe('container port drafts', () => {
  test('omits empty draft rows while serializing valid ports', () => {
    const model = emptyDefinition('Ctf')
    model.runtime = emptyRuntimeTemplate('Ctf')
    if (model.runtime.definition.kind !== 'container') throw new Error('Expected container definition')
    model.runtime.definition.containerPorts = [8080, null]
    model.runtime.definition.internalPorts = [null, 9090]

    const json = JSON.parse(serializeDefinition('Ctf', model))

    expect(json.runtime.definition.portMappings).toEqual({ 8080: 0 })
    expect(json.runtime.definition.internalPorts).toEqual([9090])
  })
})

describe('dynamic flag templates', () => {
  test('exposes the CTF competition dynamic flag template with the random UUID default', () => {
    expect(emptyFlagTemplate()).toEqual({
      header: 'flag',
      bodyTemplate: '[GUID]',
      leetLiteralText: false,
    })
    expect(competitionConfigFields('Ctf').some(field => field.key === 'flagTemplate')).toBeTrue()
  })

  test('keeps CTF dynamic flag overrides in competition challenge rules', () => {
    const model = emptyDefinition('Ctf')
    model.runtime = emptyRuntimeTemplate('Ctf')
    model.runtime.flagSource = FlagSource.PerTeam
    model.flagTemplate = { header: 'NOCTF', bodyTemplate: '[TEAMHASH]', leetLiteralText: false }

    const dynamic = JSON.parse(serializeDefinition('Ctf', model))
    expect(dynamic.flagTemplate).toBeUndefined()
    expect(challengeRuleFields('Ctf').some(field => field.key === 'flagTemplate')).toBeTrue()
    expect(challengeRuleFields('Awd').some(field => field.key === 'flagTemplate')).toBeTrue()
  })

  test('exposes AWDP Break Flag templates only in competition configuration and rules', () => {
    const model = emptyDefinition('Awdp')
    model.flagTemplate = { header: 'NOCTF', bodyTemplate: '[GUID]', leetLiteralText: false }

    expect(competitionConfigFields('Awdp').some(field => field.key === 'flagTemplate')).toBeTrue()
    expect(challengeRuleFields('Awdp').some(field => field.key === 'flagTemplate')).toBeTrue()
    expect(JSON.parse(serializeDefinition('Awdp', model)).flagTemplate).toBeUndefined()
  })

  test('creates CTF runtimes with per-team environment injection by default', () => {
    const runtime = emptyRuntimeTemplate('Ctf')

    expect(runtime.flagSource).toBe(FlagSource.PerTeam)
    expect(runtime.definition.kind).toBe('container')
    if (runtime.definition.kind !== 'container') throw new Error('Expected container definition')
    expect(runtime.definition.flagEnvironmentVariableName).toBe('FLAG')
  })
})

describe('container security drafts', () => {
  test('fills the platform security defaults when the definition omits security', () => {
    const parsed = parseDefinition(JSON.stringify({
      schemaVersion: 2,
      runtime: {
        allocation: 1,
        definition: { kind: 'container', image: 'example/image:latest' },
        flagSource: 0,
      },
    }))

    expect(parsed?.runtime?.definition.kind).toBe('container')
    if (parsed?.runtime?.definition.kind !== 'container') throw new Error('Expected parsed container definition')
    expect(parsed.runtime.definition.security).toEqual({
      noNewPrivileges: false,
      readonlyRootfs: false,
      runAsNonRoot: false,
      capDrop: [],
      capAdd: [],
    })
    expect(JSON.parse(serializeDefinition('Ctf', parsed)).runtime.definition.security).toBeUndefined()
  })

  test('applies the safe capability baseline to new container drafts', () => {
    const model = emptyDefinition('Ctf')
    model.runtime = emptyRuntimeTemplate('Ctf')

    const json = JSON.parse(serializeDefinition('Ctf', model))

    expect(json.runtime.definition.security).toEqual({
      noNewPrivileges: true,
      capDrop: ['ALL'],
    })
  })

  test('serializes and parses explicit hardening values', () => {
    const model = emptyDefinition('Ctf')
    model.runtime = emptyRuntimeTemplate('Ctf')
    if (model.runtime.definition.kind !== 'container') throw new Error('Expected container definition')
    model.runtime.definition.security = {
      noNewPrivileges: true,
      readonlyRootfs: true,
      runAsNonRoot: true,
      capDrop: ['ALL'],
      capAdd: [],
    }

    const json = serializeDefinition('Ctf', model)
    const serialized = JSON.parse(json)

    expect(serialized.runtime.definition.security).toEqual({
      noNewPrivileges: true,
      readonlyRootfs: true,
      runAsNonRoot: true,
      capDrop: ['ALL'],
    })
    const parsed = parseDefinition(json)
    expect(parsed?.runtime?.definition.kind).toBe('container')
    if (parsed?.runtime?.definition.kind !== 'container') throw new Error('Expected parsed container definition')
    expect(parsed.runtime.definition.security).toEqual(model.runtime.definition.security)
  })

  test('adds the required capability baseline to explicit security drafts', () => {
    const model = emptyDefinition('Ctf')
    model.runtime = emptyRuntimeTemplate('Ctf')
    if (model.runtime.definition.kind !== 'container') throw new Error('Expected container definition')
    model.runtime.definition.security.noNewPrivileges = true

    const serialized = JSON.parse(serializeDefinition('Ctf', model))

    expect(serialized.runtime.definition.security).toEqual({
      noNewPrivileges: true,
      capDrop: ['ALL'],
    })
  })
})

describe('AWDP patch upload limits', () => {
  test('keeps the visible default and serializes challenge-managed bytes', () => {
    const model = emptyDefinition('Awdp')
    expect(model.maximumPatchUploadBytes).toBe(256 * 1024 * 1024)
    const runtime = emptyRuntimeTemplate('Awdp')
    expect(runtime.flagSource).toBe(FlagSource.PerTeam)
    expect(runtime.definition.kind).toBe('container')
    if (runtime.definition.kind !== 'container') throw new Error('Expected AWDP container')
    expect(runtime.definition.flagEnvironmentVariableName).toBe('FLAG')
    model.maximumPatchUploadBytes = 512 * 1024 * 1024

    const json = serializeDefinition('Awdp', model)
    expect(JSON.parse(json).maximumPatchUploadBytes).toBe(512 * 1024 * 1024)
    expect(parseDefinition(json)?.maximumPatchUploadBytes).toBe(512 * 1024 * 1024)
    expect(JSON.parse(json)).toMatchObject({
      schemaVersion: 4,
    })
    expect(JSON.parse(json).flagInjection).toBeUndefined()
    expect(competitionConfigFields('Awdp').some(field => field.key === 'requireBreakBeforeFix')).toBeTrue()
    expect(challengeRuleFields('Awdp').some(field => field.key === 'requireBreakBeforeFix')).toBeTrue()
  })

  test('ignores legacy AWDP flag injection fields', () => {
    const model = parseDefinition(JSON.stringify({
      schemaVersion: 4,
      flagInjection: { kind: 0, environmentVariableName: 'OLD_FLAG' },
    }))

    expect(model?.flagInjection).toBeNull()
    expect(JSON.parse(serializeDefinition('Awdp', model!)).flagInjection).toBeUndefined()
  })
})

describe('runtime resource and lifecycle defaults', () => {
  test('creates new runtime forms with the platform defaults', () => {
    const runtime = emptyRuntimeTemplate('Ctf')

    expect(runtime.limits).toEqual({
      memoryBytes: DEFAULT_RUNTIME_MEMORY_BYTES,
      nanoCpus: DEFAULT_RUNTIME_NANO_CPUS,
      pidsLimit: DEFAULT_RUNTIME_PIDS_LIMIT,
    })
    expect(runtime.ttlSeconds).toBe(DEFAULT_RUNTIME_TTL_SECONDS)
    expect(runtime.operationTimeoutSeconds).toBe(DEFAULT_RUNTIME_OPERATION_TIMEOUT_SECONDS)
  })

  test('fills and persists defaults for legacy definitions with unset values', () => {
    const parsed = parseDefinition(JSON.stringify({
      schemaVersion: 2,
      runtime: {
        allocation: 1,
        definition: { kind: 'container', image: 'example/image:latest' },
        limits: {},
        ttlSeconds: null,
        operationTimeoutSeconds: null,
        flagSource: 0,
      },
    }))
    if (!parsed?.runtime) throw new Error('Expected parsed runtime')

    const serialized = JSON.parse(serializeDefinition('Ctf', parsed))
    expect(serialized.runtime.limits).toEqual({
      memoryBytes: DEFAULT_RUNTIME_MEMORY_BYTES,
      nanoCpus: DEFAULT_RUNTIME_NANO_CPUS,
      pidsLimit: DEFAULT_RUNTIME_PIDS_LIMIT,
    })
    expect(serialized.runtime.ttlSeconds).toBe(DEFAULT_RUNTIME_TTL_SECONDS)
    expect(serialized.runtime.operationTimeoutSeconds).toBe(DEFAULT_RUNTIME_OPERATION_TIMEOUT_SECONDS)
  })

  test('uses defaults when cleared values are serialized', () => {
    const model = emptyDefinition('Ctf')
    model.runtime = emptyRuntimeTemplate('Ctf')
    model.runtime.limits = { memoryBytes: null, nanoCpus: null, pidsLimit: null }
    model.runtime.ttlSeconds = null
    model.runtime.operationTimeoutSeconds = null

    const serialized = JSON.parse(serializeDefinition('Ctf', model))
    expect(serialized.runtime.limits).toEqual({
      memoryBytes: DEFAULT_RUNTIME_MEMORY_BYTES,
      nanoCpus: DEFAULT_RUNTIME_NANO_CPUS,
      pidsLimit: DEFAULT_RUNTIME_PIDS_LIMIT,
    })
    expect(serialized.runtime.ttlSeconds).toBe(DEFAULT_RUNTIME_TTL_SECONDS)
    expect(serialized.runtime.operationTimeoutSeconds).toBe(DEFAULT_RUNTIME_OPERATION_TIMEOUT_SECONDS)
  })
})
