import { describe, expect, test } from 'bun:test'
import {
  BLOOD_REWARD_POLICIES,
  BloodRewardPolicy,
  ScoreDecayMode,
  FlagSource,
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

  test('creates CTF runtimes with per-team environment injection by default', () => {
    const runtime = emptyRuntimeTemplate('Ctf')

    expect(runtime.flagSource).toBe(FlagSource.PerTeam)
    expect(runtime.definition.kind).toBe('container')
    if (runtime.definition.kind !== 'container') throw new Error('Expected container definition')
    expect(runtime.definition.flagEnvironmentVariableName).toBe('FLAG')
  })
})

describe('container security drafts', () => {
  test('omits the trusted compatibility defaults', () => {
    const model = emptyDefinition('Ctf')
    model.runtime = emptyRuntimeTemplate('Ctf')

    const json = JSON.parse(serializeDefinition('Ctf', model))

    expect(json.runtime.definition.security).toBeUndefined()
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
    expect(model.awdpFlagInjection).toBeNull()
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
})
