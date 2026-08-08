import { describe, expect, test } from 'bun:test'
import {
  BLOOD_REWARD_POLICIES,
  BloodRewardPolicy,
  emptyDefinition,
  emptyRuntimeTemplate,
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
