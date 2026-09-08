import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import { emptyDefinition, emptyRunnerJob, parseDefinition, serializeDefinition } from '../app/utils/game-config'
import { validateChallengeTemplateDraft } from '../app/lib/challenge-template-validation'

describe('Checker root permission', () => {
  for (const mode of ['Awd', 'Awdp'] as const) {
    test(`${mode} defaults to disabled and round-trips explicit opt-in`, () => {
      expect(parseDefinition('{"schemaVersion":4}', mode)?.checkerAllowRoot).toBeFalse()
      const model = emptyDefinition(mode)
      if (mode === 'Awd') model.checker = { job: emptyRunnerJob(), targetServiceName: '' }
      else model.checkerJob = emptyRunnerJob()
      for (const enabled of [true, false]) {
        model.checkerAllowRoot = enabled
        const serialized = serializeDefinition(mode, model)
        expect(JSON.parse(serialized).checkerAllowRoot).toBe(enabled)
        expect(parseDefinition(serialized, mode)?.checkerAllowRoot).toBe(enabled)
      }
    })

    test(`${mode} explains invalid root permission without an enabled Checker`, () => {
      const model = emptyDefinition(mode)
      model.checkerAllowRoot = true
      expect(validateChallengeTemplateDraft({ mode, title: 'Test', direction: 'Pwn', definitionJson: serializeDefinition(mode, model) }))
        .toContain("允许 Checker 以 root 运行前必须启用 Checker")
    })
  }

  test('does not leak Checker permission into CTF or KoH definitions', () => {
    for (const mode of ['Ctf', 'Koh'] as const) {
      const model = emptyDefinition(mode)
      model.checkerAllowRoot = true
      expect(JSON.parse(serializeDefinition(mode, model)).checkerAllowRoot).toBeUndefined()
      expect(parseDefinition('{"schemaVersion":4,"checkerAllowRoot":true}', mode)?.checkerAllowRoot).toBeFalse()
    }
  })

  test('the setting is labeled, disabled for read-only users, and cleared when Checker is disabled', async () => {
    const source = await sourceFile(new URL('../app/features/admin/DefinitionCheckerSection.vue', import.meta.url)).text()
    expect(source).toContain('v-model="model.checkerAllowRoot" :disabled="disabled"')
    expect(source).toContain('for="def-checker-allow-root"')
    expect(source.match(/if \(!enabled\) props.model.checkerAllowRoot = false/g)).toHaveLength(2)
  })
})
