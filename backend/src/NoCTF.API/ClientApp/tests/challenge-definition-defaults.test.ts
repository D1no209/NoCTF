import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import {
  challengeTemplateWriteErrorMessage,
  challengeTemplateWriteErrorMessages,
} from '../app/lib/challenge-template-error'
import { validateChallengeTemplateDraft } from '../app/lib/challenge-template-validation'
import { startGateErrorMessage } from '../app/lib/start-gate-error'
import {
  applyCtfInteraction,
  competitionConfigFields,
  CtfInteraction,
  ctfPointsAtSolveCount,
  defaultDefinitionJson,
  emptyDefinition,
  emptyRuntimeTemplate,
  fieldDefaultValue,
  normalizeDefinitionJson,
  serializeDefinition,
  serializeConfigValues,
  FlagSource,
  UrlExposure,
} from '../app/utils/game-config'

describe('challenge definition defaults', () => {
  test.each([
    ['Ctf', 3],
    ['Awd', 4],
    ['Awdp', 4],
    ['Koh', 1],
  ] as const)('creates a complete %s default definition', (mode, schemaVersion) => {
    expect(JSON.parse(defaultDefinitionJson(mode)).schemaVersion).toBe(schemaVersion)
    expect(validateChallengeTemplateDraft({
      mode,
      title: 'Example',
      direction: 'Misc',
      definitionJson: defaultDefinitionJson(mode),
    })).toEqual([])
  })

  test('accepts a custom runtime access display template', () => {
    const model = emptyDefinition('Ctf')
    const runtime = emptyRuntimeTemplate('Ctf')
    model.runtime = runtime
    if (runtime.definition.kind !== 'container') throw new Error('Expected container definition')
    runtime.definition.image = 'registry.example.com/challenge:latest'
    runtime.definition.containerPorts = [31337]
    runtime.urlBindings = [{
      urlTemplate: 'nc {HOST} {PORT}',
      exposure: UrlExposure.OwnerOnly,
      containerPort: 31337,
      serviceName: '',
    }]

    expect(validateChallengeTemplateDraft({
      mode: 'Ctf',
      title: 'Custom connection command',
      direction: 'Pwn',
      definitionJson: serializeDefinition('Ctf', model),
    })).toEqual([])
  })

  test('accepts a complete CTF PatchVerification definition without Flag injection', () => {
    const model = emptyDefinition('Ctf')
    applyCtfInteraction(model, CtfInteraction.PatchVerification)
    if (model.runtime?.definition.kind !== 'container') throw new Error('Expected container definition')
    model.runtime.definition.image = 'registry.example.com/challenge:latest'
    model.runtime.definition.internalPorts = [8080]
    model.runtime.definition.containerPorts = [8080]
    model.runtime.urlBindings[0]!.containerPort = 8080
    if (!model.checkerJob) throw new Error('Expected patch checker')
    model.checkerJob.image = 'registry.example.com/checker:latest'

    expect(validateChallengeTemplateDraft({
      mode: 'Ctf',
      title: 'Patch verification',
      direction: 'Pwn',
      definitionJson: serializeDefinition('Ctf', model),
    })).toEqual([])
  })

  test('initializes creation and resets the definition before a mode switch', async () => {
    const createPage = (await sourceFile(new URL('../app/features/admin/ChallengeTemplateCreateDialog.vue', import.meta.url)).text())
      .replaceAll('\r\n', '\n')
    const editPage = (await sourceFile(new URL('../app/pages/admin/challenges/[id].vue', import.meta.url)).text())
      .replaceAll('\r\n', '\n')

    expect(createPage).toContain('ref(defaultDefinitionJson(mode.value))')
    expect(createPage).toContain('definitionJson.value = defaultDefinitionJson(value)\n    mode.value = value')
    expect(editPage).toContain('form.definitionJson = value.definitionJson ??')
    expect(editPage).toContain("form.mode = value.mode ?? 'Ctf'")
    expect(createPage).toContain('normalizeDefinitionJson(mode.value, definitionJson.value)')
    expect(editPage).toContain('normalizeDefinitionJson(mode, definitionJson)')
    expect(createPage).toContain('<UiForm validation="feature"')
    expect(createPage).toContain('@submit.prevent="submit"')
    expect(editPage).toContain('<UiForm validation="feature" @submit.prevent="saveBasic">')
    expect(createPage).toContain('v-if="saveErrors.length"')
    expect(editPage).toContain('v-if="basicSaveErrors.length"')
    expect(editPage).toContain('v-if="runtimeSaveErrors.length"')
    expect(editPage).toContain('v-if="definitionSaveErrors.length"')
  })

  test('normalizes submission JSON with the active schema version', () => {
    expect(JSON.parse(normalizeDefinitionJson('Ctf', '{}')!).schemaVersion).toBe(3)
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
    })).toBe('题目定义版本或 JSON 格式无效')
    expect(challengeTemplateWriteErrorMessage({ status: 400, detail: 'Definition schemaVersion is missing.' }))
      .toBe('题目定义版本或 JSON 格式无效')
    expect(challengeTemplateWriteErrorMessage({
      status: 400,
      detail: 'CTF schemaVersion 1 has no registered upgrader.',
    })).toBe('题目定义版本或 JSON 格式无效')

    expect(challengeTemplateWriteErrorMessages({
      status: 400,
      detail: 'Runtime image is required. AWDP target Runtime must declare exactly one InternalPort. AWDP player Runtime must publish exactly one attack port.',
    })).toEqual([
      '容器镜像不能为空',
      'AWDP 必须且只能填写 1 个内部端口',
      'AWDP 必须且只能填写 1 个对外端口',
    ])
    expect(challengeTemplateWriteErrorMessages({
      status: 409,
      code: 'ActiveCompetitionModeConflict',
    })).toEqual(['该模板正被进行中的比赛引用，不能修改游戏模式'])
    expect(challengeTemplateWriteErrorMessages({
      status: 409,
      code: 'ActiveRuntimeDefinitionConflict',
    })).toEqual(['该模板仍有活动运行环境，请停止相关实例后再修改技术定义'])

    const createPage = await sourceFile(new URL('../app/features/admin/ChallengeTemplateCreateDialog.vue', import.meta.url)).text()
    const submit = createPage.slice(createPage.indexOf('async function submit'), createPage.indexOf('export type', createPage.indexOf('async function submit')))
    expect(submit).toContain('challengeTemplateWriteErrorMessages(error)')
    expect(submit).not.toContain("title.value = ''")
    expect(submit).not.toContain("definitionJson.value = ''")
  })

  test('offers explicit repair and in-place save actions for definition editors', async () => {
    const competitionEditor = await sourceFile(
      new URL('../app/features/admin/CompetitionModeConfigEditor.vue', import.meta.url),
    ).text()
    const templatePage = await sourceFile(
      new URL('../app/pages/admin/challenges/[id].vue', import.meta.url),
    ).text()

    expect(competitionEditor).toContain('function resetToCurrentDefaults()')
    expect(competitionEditor).toContain("$t('ui.resetToCurrentModeDefaults')")
    expect(competitionEditor).toContain("$t('ui.saveTheConfigurationAfterResettingToApplyIt')")
    expect(templatePage).toContain('function resetRuntimeDefinition(): void')
    expect(templatePage).toContain('function resetModeDefinition(): void')
    expect(templatePage).toContain('form.definitionJson = defaultDefinitionJson(form.mode)')
    expect(templatePage).toContain("$t('ui.resetToCurrentModeDefinition')")
    expect(templatePage).toContain('data-testid="runtime-definition-save"')
    expect(templatePage).toContain('data-testid="mode-definition-save"')
    expect(templatePage).toContain('mergeChallengeModeDefinition(')
    expect(templatePage).not.toContain("$t('ui.afterResettingReturnToBasicInformationAndSaveYourChanges')")
  })

  test('saves basic, runtime, and mode definition drafts independently', async () => {
    const templatePage = await sourceFile(
      new URL('../app/pages/admin/challenges/[id].vue', import.meta.url),
    ).text()

    expect(templatePage).toContain("type SaveSection = 'basic' | 'runtime' | 'definition'")
    expect(templatePage).toContain("updateContent('basic', contentFromTemplate(value, {")
    expect(templatePage).toContain('mergeChallengeRuntimeDefinition(')
    expect(templatePage).toContain("updateContent('runtime', contentFromTemplate(value, {")
    expect(templatePage).toContain("updateContent('definition', contentFromTemplate(value, {")
    expect(templatePage).toContain('mergeChallengeModeDefinition(')
    expect(templatePage).toContain('@click="saveRuntimeDefinition"')
    expect(templatePage).toContain('@click="saveModeDefinition"')
  })

  test('lists every blocking AWDP runtime field before sending the save request', () => {
    const model = emptyDefinition('Awdp')
    model.runtime = emptyRuntimeTemplate('Awdp')

    const issues = validateChallengeTemplateDraft({
      mode: 'Awdp',
      title: 'AWDP example',
      direction: 'Pwn',
      definitionJson: serializeDefinition('Awdp', model),
    })

    expect(issues).toContain("容器镜像不能为空")
    expect(issues).toContain("AWDP 必须且只能填写 1 个内部端口")
    expect(issues).toContain("AWDP 必须且只能填写 1 个对外端口")
    expect(issues).toContain("每个访问入口都必须填写有效的容器端口")
  })

  test('reports every unsafe AWDP Fix execution setting before save', async () => {
    const model = emptyDefinition('Awdp')
    model.runtime = emptyRuntimeTemplate('Awdp')
    model.patchCommand = ['/bin/sh', '{entrypoint']
    model.patchTimeoutSeconds = 301
    model.readyTimeoutSeconds = 121
    model.checkerJob = {
      image: 'checker:test',
      command: [],
      environment: {},
      timeoutSeconds: 120,
    }

    const issues = validateChallengeTemplateDraft({
      mode: 'Awdp',
      title: 'Unsafe AWDP Fix',
      direction: 'Pwn',
      definitionJson: serializeDefinition('Awdp', model),
    })

    expect(issues).toContain("非空补丁应用命令必须恰好包含一个独立的 {entrypoint} 参数")
    expect(issues).toContain("补丁超时必须在 1 到 300 秒之间")
    expect(issues).toContain("就绪超时不能超过 Checker 超时")

    const editor = await sourceFile(
      new URL('../app/features/admin/DefinitionPatchSection.vue', import.meta.url),
    ).text()
    expect(editor).toContain(':max="300"')
    expect(editor).toContain(':max="model.checkerJob?.timeoutSeconds ?? undefined"')
    expect(editor).toContain("ui.leaveBlankToExecuteTheEntrypointFileACustomCommand")
  })

  test('rejects AWD Flag injection timeouts beyond the dedicated handler budget', async () => {
    const model = emptyDefinition('Awd')
    model.runtime = emptyRuntimeTemplate('Awd')
    model.flagInjection = {
      command: 'printf %s ${FLAG}',
      timeoutSeconds: 301,
      serviceName: '',
    }

    const issues = validateChallengeTemplateDraft({
      mode: 'Awd',
      title: 'AWD timeout',
      direction: 'Pwn',
      definitionJson: serializeDefinition('Awd', model),
    })

    expect(issues).toContain("AWD Flag 注入超时必须在 1 到 300 秒之间")
    const editor = await sourceFile(
      new URL('../app/features/admin/DefinitionFlagInjectionSection.vue', import.meta.url),
    ).text()
    expect(editor).toContain(':max="300"')
  })

  test('rejects AWD definitions without rotation flags or participant access', () => {
    const model = emptyDefinition('Awd')
    const runtime = emptyRuntimeTemplate('Awd')
    runtime.flagSource = FlagSource.Static
    runtime.urlBindings = [{
      urlTemplate: 'nc {HOST} {PORT}',
      exposure: UrlExposure.OwnerOnly,
      containerPort: 8080,
      serviceName: '',
    }]
    model.runtime = runtime
    model.flagInjection = {
      command: "printf '%s' '${FLAG}' > /dev/shm/flag",
      timeoutSeconds: 30,
      serviceName: '',
    }

    const issues = validateChallengeTemplateDraft({
      mode: 'Awd',
      title: 'Invalid AWD runtime',
      direction: 'Pwn',
      definitionJson: serializeDefinition('Awd', model),
    })

    expect(issues).toContain("AWD 运行环境必须使用轮换 Flag")
    expect(issues).toContain("AWD 运行环境必须至少提供一个参赛队伍可见入口")
  })

  test.each([
    ['Ctf', 8080],
    ['Awd', 8081],
    ['Awdp', 9999],
    ['Koh', 8082],
  ] as const)('accepts a complete %s single-container save draft', (mode, port) => {
    const model = emptyDefinition(mode)
    const runtime = emptyRuntimeTemplate(mode)
    model.runtime = runtime
    if (runtime.definition.kind !== 'container') throw new Error('Expected container definition')
    runtime.definition.image = 'registry.example.com/challenge:latest'
    runtime.definition.containerPorts = [port]
    runtime.urlBindings = [{
      urlTemplate: 'http://{HOST}:{PORT}',
      exposure: mode === 'Ctf' || mode === 'Awdp'
        ? UrlExposure.OwnerOnly
        : UrlExposure.Participants,
      containerPort: port,
      serviceName: '',
    }]
    if (mode === 'Awd') {
      model.flagInjection = {
        command: 'printf %s ${FLAG}',
        timeoutSeconds: 30,
        serviceName: '',
      }
    }
    if (mode === 'Awdp') runtime.definition.internalPorts = [port]

    expect(validateChallengeTemplateDraft({
      mode,
      title: `${mode} container`,
      direction: 'Pwn',
      definitionJson: serializeDefinition(mode, model),
    })).toEqual([])
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

    const page = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/index.vue', import.meta.url),
    ).text()
    expect(page).toContain('startGateErrorMessage(ve)')
    expect(page).toContain('v-if="ve.competitionChallengeId"')
    expect(page).toContain("$t('ui.viewQuestions')")
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

    const page = await sourceFile(
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
    const source = await sourceFile(new URL('../app/features/admin/PointsDecayCurve.vue', import.meta.url)).text()
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
    expect(source).toContain("$t('ui.solvedTeams')")
    expect(source).toContain("$t('ui.solvedTeam'")
    expect(source).toContain('role="img"')
  })

  test('uses competition defaults while a challenge curve is inherited', async () => {
    const editor = await sourceFile(new URL('../app/features/admin/ChallengeRulesEditor.vue', import.meta.url)).text()
    const page = await sourceFile(new URL('../app/pages/admin/competitions/[id]/challenges/[ccId].vue', import.meta.url)).text()
    expect(editor).toContain('inheritedValues.value[field.key]')
    expect(editor).toContain(':model-value="displayedValue(field)"')
    expect(page).toContain(':inherited-json="inheritedConfigJson"')
    expect(editor).toContain(".filter(field => !props.hiddenKeys.includes(field.key))")
    expect(page).toContain(':hidden-keys="hiddenRuleKeys"')
    expect(page).toContain("return ['maxFlagAttempts', 'flagTemplate']")
    expect(page).toContain("const hidden = ['maxPatchAttempts']")
    expect(page).toContain('<ChoiceSidebar')
    expect(page).toContain(':items="sectionOptions"')
  })

  test('keeps runtime essentials visible and moves defaulted controls into advanced sections', async () => {
    const runtime = await sourceFile(new URL('../app/features/admin/DefinitionRuntime.vue', import.meta.url)).text()
    const container = await sourceFile(new URL('../app/features/admin/DefinitionContainer.vue', import.meta.url)).text()
    const containerController = await sourceFile(new URL('../app/features/admin/useDefinitionContainer.ts', import.meta.url)).text()
    const section = await sourceFile(new URL('../app/features/admin/DefinitionRuntimeSection.vue', import.meta.url)).text()

    expect(section).toContain("$t('ui.enableRuntimeEnvironment')")
    expect(container).toContain(":title=\"$t('ui.advancedSettings')\"")
    expect(container).toContain(':default-open="hasAdvanced"')
    expect(container).toContain(':default-open="hasMetadata"')
    expect(container).toContain(':default-open="hasSecurity"')
    expect(containerController).toContain('return security.noNewPrivileges || security.readonlyRootfs || security.runAsNonRoot')
    expect(containerController).toContain('|| security.capDrop.length > 0')
    expect(runtime).toContain(':default-open="hasCustomRuntimePolicy"')
    expect(runtime).toContain(":title=\"$t('ui.accessEntrance')\"")
    expect(runtime).toContain('accent-title')
    expect(container).toContain("containerPorts")
    expect(runtime).not.toContain('blankValuesUseThePlatformDefaultResourceLimitsAndLifecycle')
  })

  test('offers HTTP and netcat access display presets', async () => {
    const bindingList = await sourceFile(new URL('../app/features/admin/UrlBindingList.vue', import.meta.url)).text()

    expect(bindingList).toContain("'http://{HOST}:{PORT}'")
    expect(bindingList).toContain("'nc {HOST} {PORT}'")
    expect(bindingList).toContain('v-for="template in displayTemplateOptions"')
  })
})
