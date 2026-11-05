import { describe, expect, test } from 'bun:test'
import { isReactive, reactive, ref } from 'vue'
import {
  challengeRuntimeDefinitionsEqual,
  mergeChallengeCapabilityDefinition,
  mergeChallengeRuntimeDefinition,
} from '../app/features/admin/challenge-definition-sections'
import { emptyDefinition, emptyRuntimeTemplate, setChallengeCapability } from '../app/utils/game-config'

describe('challenge definition saves from reactive editors', () => {
  test('saves multiple access entries after editing one entry without changing the others', () => {
    const persisted = reactive(emptyDefinition())
    persisted.runtime = emptyRuntimeTemplate()
    const draft = ref(emptyDefinition())
    draft.value.runtime = emptyRuntimeTemplate()
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

    const savedCapabilities = mergeChallengeCapabilityDefinition(saved, draft.value)
    expect(savedCapabilities.runtime).toEqual(saved.runtime)
  })

  test('saves Runtime edits from a ref without changing persisted capability fields', () => {
    const persisted = reactive(emptyDefinition())
    persisted.runtime = emptyRuntimeTemplate()
    persisted.checkerFixInput = true
    const draft = ref(emptyDefinition())
    draft.value.runtime = emptyRuntimeTemplate()
    draft.value.runtime.operationTimeoutSeconds = 120
    const definition = draft.value.runtime.definition
    if (definition.kind !== 'container') throw new Error('Expected container services')
    definition.services[0]!.image = 'challenge:latest'
    definition.services[0]!.environment = { GREETING: 'hello' }

    const saved = mergeChallengeRuntimeDefinition(persisted, draft.value)

    expect(saved.checkerFixInput).toBeTrue()
    expect(saved.runtime?.operationTimeoutSeconds).toBe(120)
    expect(challengeRuntimeDefinitionsEqual(saved, draft.value)).toBeTrue()
    expect(isReactive(saved.runtime)).toBeFalse()
    definition.services[0]!.environment.GREETING = 'changed'
    expect(saved.runtime?.definition).toMatchObject({
      services: [{ image: 'challenge:latest', environment: { GREETING: 'hello' } }],
    })
    expect(persisted.runtime.operationTimeoutSeconds).not.toBe(120)
  })

  test('saves capability edits as detached values and retains the persisted Runtime', () => {
    const persisted = reactive(emptyDefinition())
    persisted.runtime = emptyRuntimeTemplate()
    persisted.runtime.operationTimeoutSeconds = 120
    const draft = reactive(emptyDefinition())
    draft.runtime = emptyRuntimeTemplate()
    draft.runtime.operationTimeoutSeconds = 240
    draft.patchCommand = ['verify', '/patch']
    draft.checker = { targetServiceName: 'main', job: {
      image: 'checker:latest', command: ['check'], environment: { INPUT: 'patch' }, timeoutSeconds: 30 },
    }

    const saved = mergeChallengeCapabilityDefinition(persisted, draft)

    expect(saved.runtime?.operationTimeoutSeconds).toBe(120)
    expect(saved.checker?.job.image).toBe('checker:latest')
    expect(isReactive(saved.checker?.job)).toBeFalse()
    draft.checker.job.environment.INPUT = 'changed'
    draft.patchCommand.push('changed')
    expect(saved.checker?.job.environment.INPUT).toBe('patch')
    expect(saved.patchCommand).toEqual(['verify', '/patch'])
  })

  test('adding a capability retains the persisted Runtime and detaches configuration values', () => {
    const persisted = emptyDefinition()
    persisted.runtime = emptyRuntimeTemplate()
    const draft = reactive(emptyDefinition())
    setChallengeCapability(draft, 'Control', true)
    const saved = mergeChallengeCapabilityDefinition(persisted, draft)

    expect(saved.capabilities).toEqual(['Break', 'Control'])
    expect(saved.control).toEqual(draft.control)
    expect(saved.runtime).toEqual(persisted.runtime)
    expect(isReactive(saved)).toBeFalse()
    draft.runtime!.operationTimeoutSeconds = 120
    expect(saved.runtime?.operationTimeoutSeconds).not.toBe(120)
  })

  test('can remove a saved Runtime from a reactive draft', () => {
    const persisted = reactive(emptyDefinition())
    persisted.runtime = emptyRuntimeTemplate()
    const draft = reactive(emptyDefinition())

    expect(mergeChallengeRuntimeDefinition(persisted, draft).runtime).toBeNull()
    expect(persisted.runtime).not.toBeNull()
  })
})
