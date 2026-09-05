import { describe, expect, test } from 'bun:test'
import {
  emptyDefinition,
  normalizeDefinitionJson,
  parseDefinition,
  serializeDefinition,
} from '../app/utils/game-config'
import { validateChallengeTemplateDraft } from '../app/lib/challenge-template-validation'

describe('AWDP Checker Fix input', () => {
  test('loads old schemaVersion 4 JSON as disabled without normalized dirty drift', () => {
    const oldJson = JSON.stringify({ schemaVersion: 4 })
    const model = parseDefinition(oldJson, 'Awdp')

    expect(model?.checkerFixInput).toBeFalse()
    expect(serializeDefinition('Awdp', model!)).toBe(JSON.stringify({
      schemaVersion: 4,
      maximumPatchUploadBytes: 256 * 1024 * 1024,
      checkerFixInput: false,
    }, null, 2))
    expect(serializeDefinition('Awdp', parseDefinition(oldJson, 'Awdp')!))
      .toBe(serializeDefinition('Awdp', model!))
    expect(normalizeDefinitionJson('Awdp', oldJson))
      .toBe(serializeDefinition('Awdp', model!))
  })

  test('round-trips true and false while keeping schemaVersion 4', () => {
    for (const enabled of [false, true]) {
      const model = emptyDefinition('Awdp')
      model.checkerFixInput = enabled
      const serialized = serializeDefinition('Awdp', model)

      expect(JSON.parse(serialized)).toMatchObject({
        schemaVersion: 4,
        checkerFixInput: enabled,
      })
      expect(parseDefinition(serialized, 'Awdp')?.checkerFixInput).toBe(enabled)
    }
  })

  test('does not serialize the AWDP-only field in other modes', () => {
    for (const mode of ['Ctf', 'Awd', 'Koh'] as const) {
      const model = emptyDefinition(mode)
      model.checkerFixInput = true
      expect(JSON.parse(serializeDefinition(mode, model)).checkerFixInput).toBeUndefined()
    }
  })

  test('requires an enabled Checker and exposes localized fixed-path guidance', async () => {
    const model = emptyDefinition('Awdp')
    model.checkerFixInput = true
    const issues = validateChallengeTemplateDraft({
      mode: 'Awdp',
      title: 'Checker input',
      direction: 'Pwn',
      definitionJson: serializeDefinition('Awdp', model),
    })
    const component = await Bun.file(
      new URL('../app/components/admin/DefinitionCheckerSection.vue', import.meta.url),
    ).text()
    const translations = await Bun.file(
      new URL('../app/locales/en.ts', import.meta.url),
    ).text()

    expect(issues).toContain('向 Checker 提供 Fix 包前必须启用 Checker')
    expect(component).toContain('v-model="model.checkerFixInput"')
    expect(component).toContain('/noctf/fix')
    expect(translations).toContain('Provide the Fix package to the Checker')
  })
})
