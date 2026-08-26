import { describe, expect, test } from 'bun:test'
import { challengeTemplateWriteErrorMessage } from '../app/lib/challenge-template-error'
import { startGateErrorMessage } from '../app/lib/start-gate-error'
import {
  competitionConfigFields,
  ctfPointsAtSolveCount,
  defaultDefinitionJson,
  fieldDefaultValue,
  normalizeDefinitionJson,
  serializeConfigValues,
} from '../app/utils/game-config'

describe('challenge definition defaults', () => {
  test.each([
    ['Ctf', 2],
    ['Awd', 4],
    ['Awdp', 4],
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
    expect(editPage).toContain('normalizeDefinitionJson(form.mode, currentDefinition)')
  })

  test('normalizes submission JSON with the active schema version', () => {
    expect(JSON.parse(normalizeDefinitionJson('Ctf', '{}')!).schemaVersion).toBe(2)
    expect(normalizeDefinitionJson('Ctf', '{')).toBeNull()
  })

  test('serializes saved AWDP competition defaults with schema version 4', () => {
    const fields = competitionConfigFields('Awdp')
    const values = Object.fromEntries(fields.map(field => [field.key, fieldDefaultValue(field)]))

    expect(JSON.parse(serializeConfigValues('Awdp', fields, values, { rules: false })).schemaVersion).toBe(4)
  })

  test('localizes invalid definition failures without clearing the form', async () => {
    expect(challengeTemplateWriteErrorMessage({
      status: 400,
      errors: { DefinitionJson: ['Definition schemaVersion is missing.'] },
    })).toBe('题目定义格式无效,请检查题目定义配置')
    expect(challengeTemplateWriteErrorMessage({ status: 400, detail: 'Definition schemaVersion is missing.' }))
      .toBe('题目定义格式无效,请检查题目定义配置')
    expect(challengeTemplateWriteErrorMessage({
      status: 400,
      detail: 'CTF schemaVersion 1 has no registered upgrader.',
    })).toBe('题目定义格式无效,请检查题目定义配置')

    const createPage = await Bun.file(new URL('../app/pages/admin/challenges/new.vue', import.meta.url)).text()
    const submit = createPage.slice(createPage.indexOf('async function submit'), createPage.indexOf('</script>'))
    expect(submit).toContain('challengeTemplateWriteErrorMessage(apiError)')
    expect(submit).not.toContain("title.value = ''")
    expect(submit).not.toContain("definitionJson.value = ''")
  })

  test('offers explicit repair and in-place save actions for definition editors', async () => {
    const competitionEditor = await Bun.file(
      new URL('../app/components/admin/CompetitionModeConfigEditor.vue', import.meta.url),
    ).text()
    const templatePage = await Bun.file(
      new URL('../app/pages/admin/challenges/[id].vue', import.meta.url),
    ).text()

    expect(competitionEditor).toContain('function resetToCurrentDefaults()')
    expect(competitionEditor).toContain("$t('重置为当前模式默认配置')")
    expect(competitionEditor).toContain("$t('重置后仍需保存配置才会生效')")
    expect(templatePage).toContain('function resetDefinitionToCurrentMode(): void')
    expect(templatePage).toContain('form.definitionJson = defaultDefinitionJson(form.mode)')
    expect(templatePage).toContain("$t('重置为当前模式默认题目定义')")
    expect(templatePage).toContain('data-testid="runtime-definition-save"')
    expect(templatePage).toContain('data-testid="mode-definition-save"')
    expect(templatePage).toContain('serializeDefinition(form.mode, definitionModel.value)')
    expect(templatePage).not.toContain("$t('重置后请返回基本信息保存修改')")
  })

  test('localizes legacy start-gate schema failures and retains challenge navigation', async () => {
    expect(startGateErrorMessage({
      code: 'CompetitionConfigurationInvalid',
      message: 'schemaVersion 1 is unsupported; supported versions are 4.',
    })).toBe('比赛模式配置版本过旧，请重新保存比赛配置。')
    expect(startGateErrorMessage({
      code: 'RuntimeDefinitionInvalid',
      message: 'schemaVersion 1 is unsupported; supported versions are 4.',
    })).toBe('题目运行环境定义版本过旧，请进入题目模板重新保存题目定义。')
    expect(startGateErrorMessage({
      code: 'ChallengeRulesInvalid',
      message: 'schemaVersion 1 is unsupported; supported versions are 4.',
    })).toBe('题目规则版本过旧，请重新保存题目规则。')

    const page = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/index.vue', import.meta.url),
    ).text()
    expect(page).toContain('startGateErrorMessage(ve)')
    expect(page).toContain('v-if="ve.competitionChallengeId"')
    expect(page).toContain("$t('查看题目')")
  })

  test('localizes every stable start-gate failure and only offers validation before start', async () => {
    const failures = [
      ['CompetitionNotPublished', '比赛必须处于已发布状态才能执行启动前检查。'],
      ['CompetitionConfigurationInvalid', '比赛模式配置无效，请检查比赛配置。'],
      ['PublishedChallengeRequired', '至少需要发布一道题目。'],
      ['ApprovedTeamRequired', '至少需要一支审核通过的队伍。'],
      ['RuntimeQuotaInsufficient', '每队并发运行环境上限不足以承载全部已发布的 AWD 题目。'],
      ['ChallengeModeMismatch', '题目模式与比赛模式不一致。'],
      ['ChallengeRulesInvalid', '题目规则无效，请检查比赛题目配置。'],
      ['RuntimeDefinitionInvalid', '题目运行环境定义无效，请检查题目模板。'],
      ['TrackConfigurationInvalid', '赛道配置无效，请检查赛道设置。'],
      ['TeamTrackInvalid', '存在队伍使用了已不存在的赛道，请调整队伍赛道。'],
    ] as const

    for (const [code, expected] of failures) {
      expect(startGateErrorMessage({ code, message: 'Unlocalized backend detail.' })).toBe(expected)
    }

    const page = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/index.vue', import.meta.url),
    ).text()
    expect(page).toContain('v-if="status === \'Published\'"')
    expect(page).not.toContain('{{ ve.code }}')
  })
})

describe('CTF score decay preview', () => {
  const curve = { initialPoints: 500, minimumPoints: 100, decayTeamCount: 10, decayMode: 2, customExpression: null }

  test('matches the authoritative quadratic scoring curve', () => {
    expect(ctfPointsAtSolveCount(curve, 1)).toBe(500)
    expect(ctfPointsAtSolveCount(curve, 5.5)).toBe(400)
    expect(ctfPointsAtSolveCount(curve, 10)).toBe(100)
    expect(ctfPointsAtSolveCount(curve, 20)).toBe(100)
  })

  test('renders a detailed accessible curve with hover inspection', async () => {
    const source = await Bun.file(new URL('../app/components/admin/PointsDecayCurve.vue', import.meta.url)).text()
    expect(source).toContain('ctfPointsAtSolveCount')
    expect(source).toContain('@pointermove="onPointerMove"')
    expect(source).toContain('hoverPointer.value')
    expect(source).toContain('updateHover(event.clientX, event.clientY)')
    expect(source).toContain('clientPointToSvg')
    expect(source).toContain('tooltipTransform')
    expect(source).toContain('tooltipBox.offsetX')
    expect(source).toContain('preview.active.score')
    expect(source).toContain('yTickCount = 8')
    expect(source).toContain('xTickCount')
    expect(source).toContain("$t('解题队伍数')")
    expect(source).toContain("$t('第 {count} 支解题队伍'")
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
