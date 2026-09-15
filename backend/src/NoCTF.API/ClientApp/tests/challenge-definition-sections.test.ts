import { describe, expect, test } from 'bun:test'
import {
  mergeChallengeModeDefinition,
  mergeChallengeRuntimeDefinition,
} from '../app/features/admin/challenge-definition-sections'
import {
  emptyDefinition,
  emptyRuntimeTemplate,
  parseDefinition,
  serializeDefinition,
} from '../app/utils/game-config'

function persistedAwdpDefinition() {
  const model = emptyDefinition('Awdp')
  model.runtime = emptyRuntimeTemplate('Awdp')
  if (model.runtime.definition.kind !== 'container') throw new Error('Expected container definition')
  model.runtime.definition.image = 'registry.example.com/runtime:persisted'
  model.patchEntrypoint = 'persisted.sh'
  model.checkerJob = {
    image: 'registry.example.com/checker:persisted',
    command: [],
    environment: {},
    timeoutSeconds: 60,
  }
  return model
}

describe('challenge definition section saves', () => {
  test('runtime saves retain the persisted mode definition', () => {
    const persisted = persistedAwdpDefinition()
    const persistedJson = serializeDefinition('Awdp', persisted)
    const runtimeDraft = parseDefinition(persistedJson, 'Awdp')!
    if (runtimeDraft.runtime?.definition.kind !== 'container') throw new Error('Expected container definition')
    runtimeDraft.runtime.definition.image = 'registry.example.com/runtime:draft'
    runtimeDraft.patchEntrypoint = 'unsaved.sh'
    runtimeDraft.checkerJob!.image = 'registry.example.com/checker:unsaved'

    const merged = parseDefinition(
      mergeChallengeRuntimeDefinition('Awdp', persistedJson, runtimeDraft)!,
      'Awdp',
    )!

    expect(merged.runtime?.definition.kind).toBe('container')
    if (merged.runtime?.definition.kind !== 'container') throw new Error('Expected container definition')
    expect(merged.runtime.definition.image).toBe('registry.example.com/runtime:draft')
    expect(merged.patchEntrypoint).toBe('persisted.sh')
    expect(merged.checkerJob?.image).toBe('registry.example.com/checker:persisted')
  })

  test('mode-definition saves retain the persisted Runtime', () => {
    const persisted = persistedAwdpDefinition()
    const persistedJson = serializeDefinition('Awdp', persisted)
    const modeDraft = parseDefinition(persistedJson, 'Awdp')!
    if (modeDraft.runtime?.definition.kind !== 'container') throw new Error('Expected container definition')
    modeDraft.runtime.definition.image = 'registry.example.com/runtime:unsaved'
    modeDraft.patchEntrypoint = 'saved.sh'
    modeDraft.checkerJob!.image = 'registry.example.com/checker:saved'

    const merged = parseDefinition(
      mergeChallengeModeDefinition('Awdp', persistedJson, 'Awdp', modeDraft)!,
      'Awdp',
    )!

    expect(merged.runtime?.definition.kind).toBe('container')
    if (merged.runtime?.definition.kind !== 'container') throw new Error('Expected container definition')
    expect(merged.runtime.definition.image).toBe('registry.example.com/runtime:persisted')
    expect(merged.patchEntrypoint).toBe('saved.sh')
    expect(merged.checkerJob?.image).toBe('registry.example.com/checker:saved')
  })
})
