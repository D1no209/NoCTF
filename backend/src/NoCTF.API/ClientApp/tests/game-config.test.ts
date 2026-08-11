import { describe, expect, test } from 'bun:test'
import {
  BLOOD_REWARD_POLICIES,
  BloodRewardPolicy,
  FlagSource,
  competitionConfigFields,
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
  test('exposes the CTF competition dynamic flag header with the stable team hash default', () => {
    expect(emptyFlagTemplate()).toEqual({
      header: 'flag',
      bodyTemplate: '[TEAMHASH]',
      leetLiteralText: false,
    })
    expect(competitionConfigFields('Ctf').some(field => field.key === 'flagTemplate')).toBeTrue()
  })

  test('serializes a CTF challenge override only for per-team runtime flags', () => {
    const model = emptyDefinition('Ctf')
    model.runtime = emptyRuntimeTemplate('Ctf')
    model.runtime.flagSource = FlagSource.PerTeam
    model.flagTemplate = { header: 'NOCTF', bodyTemplate: '[TEAMHASH]', leetLiteralText: false }

    const dynamic = JSON.parse(serializeDefinition('Ctf', model))
    expect(dynamic.flagTemplate.header).toBe('NOCTF')

    model.runtime.flagSource = FlagSource.Static
    const staticDefinition = JSON.parse(serializeDefinition('Ctf', model))
    expect(staticDefinition.flagTemplate).toBeUndefined()
  })
})

describe('AWDP patch upload limits', () => {
  test('keeps the visible default and serializes challenge-managed bytes', () => {
    const model = emptyDefinition('Awdp')
    expect(model.maximumPatchUploadBytes).toBe(256 * 1024 * 1024)
    model.maximumPatchUploadBytes = 512 * 1024 * 1024

    const json = serializeDefinition('Awdp', model)
    expect(JSON.parse(json).maximumPatchUploadBytes).toBe(512 * 1024 * 1024)
    expect(parseDefinition(json)?.maximumPatchUploadBytes).toBe(512 * 1024 * 1024)
  })
})
