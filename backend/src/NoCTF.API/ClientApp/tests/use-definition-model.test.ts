import { describe, expect, test } from 'bun:test'
import { effectScope, nextTick, reactive } from 'vue'

import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract } from '../app/api'
import { useDefinitionModel } from '../app/composables/useDefinitionModel'
import { defaultDefinition } from '../app/utils/game-config'
import type { GameModeValue } from '../app/utils/game-config'

describe('useDefinitionModel', () => {
  test('loading an external definition does not emit it back or recurse', async () => {
    const scope = effectScope()
    const form = reactive({ mode: 'Ctf' as const, definition: defaultDefinition('Ctf') })
    const emissions: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract[] = []
    const editor = scope.run(() => useDefinitionModel(
      () => form.definition,
      () => form.mode,
      definition => { form.definition = definition; emissions.push(definition) },
    ))!

    try {
      const loaded = defaultDefinition('Ctf')
      form.definition = loaded
      await nextTick()

      expect(editor.model.value).not.toBeNull()
      expect(emissions).toHaveLength(0)
    }
    finally {
      scope.stop()
    }
  })

  test('a nested editor change emits once without replacing the active model', async () => {
    const scope = effectScope()
    const form = reactive({ mode: 'Ctf' as const, definition: defaultDefinition('Ctf') })
    const emissions: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract[] = []
    const editor = scope.run(() => useDefinitionModel(
      () => form.definition,
      () => form.mode,
      definition => { form.definition = definition; emissions.push(definition) },
    ))!

    try {
      const activeModel = editor.model.value
      expect(activeModel).not.toBeNull()
      activeModel!.patchCommand.push('echo ready')
      await nextTick()

      expect(emissions).toHaveLength(1)
      expect(form.definition.patchCommand).toEqual(['echo ready'])
      expect(Object.keys(form.definition)[0]).toBe('mode')
      expect(editor.model.value).toBe(activeModel)
    }
    finally {
      scope.stop()
    }
  })

  test('external replacement after editing resets the draft without an echo', async () => {
    const scope = effectScope()
    const form = reactive({ mode: 'Ctf' as GameModeValue, definition: defaultDefinition('Ctf') })
    const emissions: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract[] = []
    const editor = scope.run(() => useDefinitionModel(
      () => form.definition,
      () => form.mode,
      definition => { form.definition = definition; emissions.push(definition) },
    ))!

    try {
      editor.model.value!.patchTimeoutSeconds = 42
      expect(emissions).toHaveLength(1)

      form.definition = defaultDefinition('Ctf')
      await nextTick()
      expect(editor.model.value?.patchTimeoutSeconds).toBeNull()
      expect(emissions).toHaveLength(1)

      form.definition = defaultDefinition('Awdp')
      form.mode = 'Awdp'
      await nextTick()
      expect(editor.model.value?.maximumPatchUploadBytes).toBe(256 * 1024 * 1024)
      expect(emissions).toHaveLength(1)
    }
    finally {
      scope.stop()
    }
  })
})
