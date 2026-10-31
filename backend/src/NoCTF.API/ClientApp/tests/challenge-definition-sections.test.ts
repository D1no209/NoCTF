import { describe, expect, test } from 'bun:test'
import { isReactive, reactive, ref } from 'vue'
import {
  challengeRuntimeDefinitionsEqual,
  mergeChallengeModeDefinition,
  mergeChallengeRuntimeDefinition,
} from '../app/features/admin/challenge-definition-sections'
import { emptyDefinition, emptyRuntimeTemplate } from '../app/utils/game-config'

describe('challenge definition saves from reactive editors', () => {
  test('saves multiple access entries after editing one entry without changing the others', () => {
    const persisted = reactive(emptyDefinition('Ctf'))
    persisted.runtime = emptyRuntimeTemplate('Ctf')
    const draft = ref(emptyDefinition('Ctf'))
    draft.value.runtime = emptyRuntimeTemplate('Ctf')
    const runtime = draft.value.runtime
    runtime.urlBindings = [47001, 47002, 47003].map(containerPort => ({
      urlTemplate: 'UDP {host}:{port}', exposure: 'OwnerOnly', serviceName: 'main', containerPort,
    }))
    runtime.urlBindings = runtime.urlBindings.map((binding, index) =>
      index === 1 ? { ...binding, urlTemplate: 'CH2: UDP {HOST}:{PORT}' } : binding)

    const saved = mergeChallengeRuntimeDefinition(persisted, draft.value)
    expect(saved.runtime?.urlBindings).toEqual(runtime.urlBindings)
    expect(saved.runtime?.urlBindings[0]?.containerPort).toBe(47001)
    expect(saved.runtime?.urlBindings[1]?.urlTemplate).toBe('CH2: UDP {HOST}:{PORT}')
    expect(isReactive(saved.runtime?.urlBindings[0])).toBeFalse()
    runtime.urlBindings[0]!.containerPort = 48001
    expect(saved.runtime?.urlBindings[0]?.containerPort).toBe(47001)

    const savedMode = mergeChallengeModeDefinition('Ctf', saved, 'Ctf', draft.value)
    expect(savedMode.runtime).toEqual(saved.runtime)
  })

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
