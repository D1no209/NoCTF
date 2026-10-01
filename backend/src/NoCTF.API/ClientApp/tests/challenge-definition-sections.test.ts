import { describe, expect, test } from 'bun:test'
import { isReactive, reactive, ref } from 'vue'
import {
  challengeRuntimeDefinitionsEqual,
  mergeChallengeModeDefinition,
  mergeChallengeRuntimeDefinition,
} from '../app/features/admin/challenge-definition-sections'
import { emptyDefinition, emptyRuntimeTemplate } from '../app/utils/game-config'

describe('challenge definition saves from reactive editors', () => {
  test('saves Runtime edits from a ref without changing persisted mode fields', () => {
    const persisted = reactive(emptyDefinition('Awdp'))
    persisted.runtime = emptyRuntimeTemplate('Awdp')
    persisted.checkerFixInput = true
    const draft = ref(emptyDefinition('Awdp'))
    draft.value.runtime = emptyRuntimeTemplate('Awdp')
    draft.value.runtime.ttlSeconds = 120
    const definition = draft.value.runtime.definition
    if (definition.kind !== 'container') throw new Error('Expected container services')
    definition.services[0]!.image = 'challenge:latest'
    definition.services[0]!.environment = { GREETING: 'hello' }

    const saved = mergeChallengeRuntimeDefinition(persisted, draft.value)

    expect(saved.checkerFixInput).toBeTrue()
    expect(saved.runtime?.ttlSeconds).toBe(120)
    expect(challengeRuntimeDefinitionsEqual('Awdp', saved, draft.value)).toBeTrue()
    expect(isReactive(saved.runtime)).toBeFalse()
    definition.services[0]!.environment.GREETING = 'changed'
    expect(saved.runtime?.definition).toMatchObject({
      services: [{ image: 'challenge:latest', environment: { GREETING: 'hello' } }],
    })
    expect(persisted.runtime.ttlSeconds).not.toBe(120)
  })

  test('saves mode edits as detached values and retains the persisted Runtime', () => {
    const persisted = reactive(emptyDefinition('Awdp'))
    persisted.runtime = emptyRuntimeTemplate('Awdp')
    persisted.runtime.ttlSeconds = 120
    const draft = reactive(emptyDefinition('Awdp'))
    draft.runtime = emptyRuntimeTemplate('Awdp')
    draft.runtime.ttlSeconds = 240
    draft.patchCommand = ['verify', '/patch']
    draft.checkerJob = {
      image: 'checker:latest', command: ['check'], environment: { INPUT: 'patch' }, timeoutSeconds: 30,
    }

    const saved = mergeChallengeModeDefinition('Awdp', persisted, 'Awdp', draft)

    expect(saved.runtime?.ttlSeconds).toBe(120)
    expect(saved.checkerJob?.image).toBe('checker:latest')
    expect(isReactive(saved.checkerJob)).toBeFalse()
    draft.checkerJob.environment.INPUT = 'changed'
    draft.patchCommand.push('changed')
    expect(saved.checkerJob?.environment.INPUT).toBe('patch')
    expect(saved.patchCommand).toEqual(['verify', '/patch'])
  })

  test('saves a mode switch from a reactive draft', () => {
    const draft = reactive(emptyDefinition('Koh'))
    draft.runtime = emptyRuntimeTemplate('Koh')

    const saved = mergeChallengeModeDefinition('Ctf', emptyDefinition('Ctf'), 'Koh', draft)

    expect(saved).toEqual(draft)
    expect(isReactive(saved)).toBeFalse()
    draft.runtime.ttlSeconds = 120
    expect(saved.runtime?.ttlSeconds).not.toBe(120)
  })

  test('can remove a saved Runtime from a reactive draft', () => {
    const persisted = reactive(emptyDefinition('Ctf'))
    persisted.runtime = emptyRuntimeTemplate('Ctf')
    const draft = reactive(emptyDefinition('Ctf'))

    expect(mergeChallengeRuntimeDefinition(persisted, draft).runtime).toBeNull()
    expect(persisted.runtime).not.toBeNull()
  })
})
