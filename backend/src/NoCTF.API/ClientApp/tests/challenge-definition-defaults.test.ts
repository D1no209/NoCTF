import { describe, expect, test } from 'bun:test'
import { challengeTemplateWriteErrorMessage } from '../app/lib/challenge-template-error'
import { ctfPointsAtSolveCount, defaultDefinitionJson, normalizeDefinitionJson } from '../app/utils/game-config'

describe('challenge definition defaults', () => {
  test.each([
    ['Ctf', 1],
    ['Awd', 4],
    ['Awdp', 1],
    ['Koh', 1],
  ] as const)('creates a complete %s default definition', (mode, schemaVersion) => {
    expect(JSON.parse(defaultDefinitionJson(mode)).schemaVersion).toBe(schemaVersion)
  })

  test('initializes creation and resets the definition before a mode switch', async () => {
    const createPage = (await Bun.file(new URL('../app/pages/admin/challenges/new.vue', import.meta.url)).text())
      .replaceAll('\r\n', '\n')
    const editPage = (await Bun.file(new URL('../app/pages/admin/challenges/[id].vue', import.meta.url)).text())
      .replaceAll('\r\n', '\n')

    expect(createPage).toContain('ref(defaultDefinitionJson(mode.value))')
    expect(createPage).toContain('definitionJson.value = defaultDefinitionJson(value)\n  mode.value = value')
    expect(editPage).toContain('form.definitionJson = value.definitionJson ??')
    expect(editPage).toContain("form.mode = value.mode ?? 'Ctf'")
    expect(createPage).toContain('normalizeDefinitionJson(mode.value, definitionJson.value)')
    expect(editPage).toContain('normalizeDefinitionJson(form.mode, form.definitionJson)')
  })

  test('normalizes submission JSON with the active schema version', () => {
    expect(JSON.parse(normalizeDefinitionJson('Ctf', '{}')!).schemaVersion).toBe(1)
    expect(normalizeDefinitionJson('Ctf', '{')).toBeNull()
  })

  test('localizes invalid definition failures without clearing the form', async () => {
    expect(challengeTemplateWriteErrorMessage({
      status: 400,
      errors: { DefinitionJson: ['Definition schemaVersion is missing.'] },
    })).toBe('题目定义格式无效,请检查题目定义配置')
    expect(challengeTemplateWriteErrorMessage({ status: 400, detail: 'Definition schemaVersion is missing.' }))
      .toBe('题目定义格式无效,请检查题目定义配置')

    const createPage = await Bun.file(new URL('../app/pages/admin/challenges/new.vue', import.meta.url)).text()
    const submit = createPage.slice(createPage.indexOf('async function submit'), createPage.indexOf('</script>'))
    expect(submit).toContain('challengeTemplateWriteErrorMessage(apiError)')
    expect(submit).not.toContain("title.value = ''")
    expect(submit).not.toContain("definitionJson.value = ''")
  })
})

describe('CTF score decay preview', () => {
  const curve = { initialPoints: 500, minimumPoints: 100, decayFactor: 10 }

  test('matches the authoritative quadratic scoring curve', () => {
    expect(ctfPointsAtSolveCount(curve, 1)).toBe(500)
    expect(ctfPointsAtSolveCount(curve, 5.5)).toBe(400)
    expect(ctfPointsAtSolveCount(curve, 10)).toBe(100)
    expect(ctfPointsAtSolveCount(curve, 20)).toBe(100)
  })

  test('renders the concrete curve as an accessible dashed plot', async () => {
    const source = await Bun.file(new URL('../app/components/admin/PointsDecayCurve.vue', import.meta.url)).text()
    expect(source).toContain('ctfPointsAtSolveCount')
    expect(source).toContain('stroke-dasharray="8 6"')
    expect(source).toContain('role="img"')
  })

  test('uses competition defaults while a challenge curve is inherited', async () => {
    const editor = await Bun.file(new URL('../app/components/admin/ChallengeRulesEditor.vue', import.meta.url)).text()
    const page = await Bun.file(new URL('../app/pages/admin/competitions/[id]/challenges/[ccId].vue', import.meta.url)).text()
    expect(editor).toContain('inheritedValues.value[field.key]')
    expect(editor).toContain(':model-value="displayedValue(field)"')
    expect(page).toContain(':inherited-json="inheritedConfigJson"')
  })
})
