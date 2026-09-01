import type { NoCtfapiEndpointsCompetitionsGameModeProtocol } from '../api'
import type { RunnerJobModel } from '../utils/game-config'
import {
  FlagSource,
  HARD_MAXIMUM_PATCH_UPLOAD_BYTES,
  parseDefinition,
  RuntimeAllocation,
  UrlExposure,
} from '../utils/game-config'
import { translate } from '../utils/i18n'

const environmentNamePattern = /^[A-Za-z_][A-Za-z0-9_]*$/
const awdpPatchEntrypointPlaceholder = '{entrypoint}'
const maximumAwdpPatchTimeoutSeconds = 300
const maximumAwdpPatchCommandArguments = 64
const maximumAwdpPatchCommandArgumentLength = 4096
const awdpFixExecutionOverheadSeconds = 120
const awdpFixHandlerTimeoutSeconds = 2400

function addIssue(issues: string[], issue: string): void {
  if (!issues.includes(issue)) issues.push(issue)
}

function validPort(value: number | null): value is number {
  return value !== null && Number.isInteger(value) && value >= 1 && value <= 65535
}

function validateEnvironment(
  issues: string[],
  environment: Record<string, string>,
  label: string,
): void {
  for (const name of Object.keys(environment)) {
    if (!environmentNamePattern.test(name)) {
      addIssue(issues, translate('{label}中的环境变量名“{name}”无效', { label, name }))
    }
    else if (name.toUpperCase().startsWith('NOCTF_')) {
      addIssue(issues, translate('{label}中的环境变量不能使用 NOCTF_ 前缀', { label }))
    }
  }
}

function validateRunnerJob(issues: string[], job: RunnerJobModel | null, label: string): void {
  if (!job) return
  if (!job.image.trim()) addIssue(issues, translate('{label}镜像不能为空', { label }))
  else if (job.image.length > 512) addIssue(issues, translate('{label}镜像最多 512 个字符', { label }))
  if (job.command.some(argument => !argument.trim()))
    addIssue(issues, translate('{label}启动参数不能包含空项', { label }))
  if (job.timeoutSeconds !== null && (job.timeoutSeconds < 1 || job.timeoutSeconds > 1800))
    addIssue(issues, translate('{label}超时必须在 1 到 1800 秒之间', { label }))
  validateEnvironment(issues, job.environment, label)
}

function validateAccessDisplayTemplate(issues: string[], template: string): void {
  if (!template.trim()) {
    addIssue(issues, translate('访问入口的显示模板不能为空'))
    return
  }
  const remaining = template
    .replaceAll('{HOST}', '')
    .replaceAll('{PORT}', '')
  if (remaining.includes('{') || remaining.includes('}')) {
    addIssue(issues, translate('访问入口的显示模板只能使用 {HOST} 和 {PORT} 占位符'))
  }
}

export interface ChallengeTemplateDraft {
  mode: NoCtfapiEndpointsCompetitionsGameModeProtocol
  title: string
  direction: string
  definitionJson: string
}

/** Mirrors the save-time invariants that can be checked without server state. */
export function validateChallengeTemplateDraft(draft: ChallengeTemplateDraft): string[] {
  const issues: string[] = []
  const title = draft.title.trim()
  const direction = draft.direction.trim()
  if (!title) addIssue(issues, translate('标题不能为空'))
  else if (title.length > 160) addIssue(issues, translate('标题最多 160 个字符'))
  if (!direction) addIssue(issues, translate('方向不能为空'))
  else if (direction.length > 96) addIssue(issues, translate('方向最多 96 个字符'))

  const model = parseDefinition(draft.definitionJson, draft.mode)
  if (!model) {
    addIssue(issues, translate('题目定义无法解析，请重置或修正后再保存'))
    return issues
  }
  const runtime = model.runtime
  if (!runtime) {
    validateRunnerJob(
      issues,
      draft.mode === 'Awd' ? model.checker?.job ?? null : model.checkerJob,
      translate('Checker'),
    )
    if (draft.mode === 'Awd' && model.checker)
      addIssue(issues, translate('启用 Checker 前必须先启用运行环境'))
    return issues
  }

  if (runtime.limits.memoryBytes === null || runtime.limits.memoryBytes <= 0
    || runtime.limits.nanoCpus === null || runtime.limits.nanoCpus <= 0
    || runtime.limits.pidsLimit === null || runtime.limits.pidsLimit <= 0) {
    addIssue(issues, translate('运行环境的内存、CPU 和进程数上限必须为正数'))
  }
  if (runtime.ttlSeconds !== null && (runtime.ttlSeconds < 1 || runtime.ttlSeconds > 604800))
    addIssue(issues, translate('实例存活时间必须在 1 到 604800 秒之间'))
  if (runtime.operationTimeoutSeconds !== null
    && (runtime.operationTimeoutSeconds < 1 || runtime.operationTimeoutSeconds > 300)) {
    addIssue(issues, translate('运行环境操作超时必须在 1 到 300 秒之间'))
  }

  const definition = runtime.definition
  let publicPorts: number[] = []
  let internalPorts: number[] = []
  if (definition.kind === 'container') {
    if (!definition.image.trim()) addIssue(issues, translate('容器镜像不能为空'))
    else if (definition.image.length > 512) addIssue(issues, translate('容器镜像最多 512 个字符'))
    publicPorts = [...new Set(definition.containerPorts.filter(validPort))]
    internalPorts = definition.internalPorts.filter(validPort)
    if (definition.containerPorts.some(port => port !== null && !validPort(port)))
      addIssue(issues, translate('对外端口必须是 1 到 65535 之间的整数'))
    if (definition.internalPorts.some(port => port !== null && !validPort(port)))
      addIssue(issues, translate('内部端口必须是 1 到 65535 之间的整数'))
    if (new Set(internalPorts).size !== internalPorts.length)
      addIssue(issues, translate('内部端口不能重复'))
    validateEnvironment(issues, definition.environment, translate('运行环境'))
    if (runtime.flagSource === FlagSource.PerTeam) {
      const flagVariable = definition.flagEnvironmentVariableName.trim()
      if (!flagVariable) addIssue(issues, translate('每队独立 Flag 必须填写 Flag 环境变量名'))
      else if (!environmentNamePattern.test(flagVariable))
        addIssue(issues, translate('Flag 环境变量名无效'))
      else if (flagVariable.toUpperCase().startsWith('NOCTF_'))
        addIssue(issues, translate('Flag 环境变量名不能使用 NOCTF_ 前缀'))
    }
  }
  else {
    if (!definition.composeYaml.trim()) addIssue(issues, translate('Docker Compose 内容不能为空'))
    validateEnvironment(issues, definition.environment, translate('运行环境'))
  }

  for (const binding of runtime.urlBindings) {
    validateAccessDisplayTemplate(issues, binding.urlTemplate)
    if (!validPort(binding.containerPort))
      addIssue(issues, translate('每个访问入口都必须填写有效的容器端口'))
    if (definition.kind === 'container' && validPort(binding.containerPort)
      && !publicPorts.includes(binding.containerPort)) {
      addIssue(issues, translate('访问入口端口必须同时存在于对外端口列表'))
    }
    if (definition.kind === 'compose' && !binding.serviceName.trim())
      addIssue(issues, translate('Compose 访问入口必须填写服务名'))
  }

  switch (draft.mode) {
    case 'Ctf':
      if (runtime.allocation !== RuntimeAllocation.PerTeam)
        addIssue(issues, translate('CTF 运行环境必须采用每队独立分配'))
      if (runtime.flagSource !== FlagSource.PerTeam)
        addIssue(issues, translate('CTF 容器题必须使用每队独立 Flag'))
      if (runtime.urlBindings.some(binding => binding.exposure !== UrlExposure.OwnerOnly))
        addIssue(issues, translate('CTF 访问入口必须设为仅队伍自己可见'))
      break
    case 'Awd':
      if (runtime.allocation !== RuntimeAllocation.PerTeam)
        addIssue(issues, translate('AWD 运行环境必须采用每队独立分配'))
      if (!model.flagInjection) {
        addIssue(issues, translate('启用 AWD 运行环境后必须配置 Flag 注入命令'))
      }
      else {
        if (!model.flagInjection.command.trim() || !model.flagInjection.command.includes('${FLAG}'))
          addIssue(issues, translate('AWD Flag 注入命令必须包含 ${FLAG}'))
        if (model.flagInjection.timeoutSeconds === null || model.flagInjection.timeoutSeconds <= 0)
          addIssue(issues, translate('AWD Flag 注入超时必须为正数'))
        if (definition.kind === 'compose' && !model.flagInjection.serviceName.trim())
          addIssue(issues, translate('Compose 运行环境必须填写 Flag 注入目标服务名'))
      }
      validateRunnerJob(issues, model.checker?.job ?? null, translate('Checker'))
      if (model.checker && definition.kind === 'compose' && !model.checker.targetServiceName.trim())
        addIssue(issues, translate('Compose Checker 必须填写目标服务名'))
      break
    case 'Awdp':
      if (runtime.allocation !== RuntimeAllocation.PerTeam)
        addIssue(issues, translate('AWDP 运行环境必须采用每队独立分配'))
      if (runtime.flagSource !== FlagSource.PerTeam)
        addIssue(issues, translate('AWDP 运行环境必须使用每队独立 Flag'))
      if (definition.kind !== 'container')
        addIssue(issues, translate('AWDP 只支持单容器运行环境'))
      if (internalPorts.length !== 1)
        addIssue(issues, translate('AWDP 必须且只能填写 1 个内部端口'))
      if (publicPorts.length !== 1)
        addIssue(issues, translate('AWDP 必须且只能填写 1 个对外端口'))
      if (internalPorts.length === 1 && publicPorts.length === 1 && internalPorts[0] !== publicPorts[0])
        addIssue(issues, translate('AWDP 的内部端口与对外端口必须相同'))
      if (runtime.urlBindings.length === 0)
        addIssue(issues, translate('AWDP 必须添加至少 1 个访问入口'))
      if (runtime.urlBindings.some(binding => binding.exposure !== UrlExposure.OwnerOnly))
        addIssue(issues, translate('AWDP 访问入口必须设为仅队伍自己可见'))
      if (internalPorts.length === 1
        && runtime.urlBindings.some(binding => binding.containerPort !== internalPorts[0]))
        addIssue(issues, translate('AWDP 访问入口端口必须与内部端口相同'))
      validateRunnerJob(issues, model.checkerJob, translate('Checker'))
      if (model.patchEntrypoint) {
        const segments = model.patchEntrypoint.split(/[\\/]/)
        if (model.patchEntrypoint.length > 256)
          addIssue(issues, translate('补丁入口最多 256 个字符'))
        else if (/^[A-Za-z]:[\\/]|^[\\/]/.test(model.patchEntrypoint)
          || segments.some(segment => segment === '.' || segment === '..'))
          addIssue(issues, translate('补丁入口必须是安全的相对路径'))
      }
      if (model.patchCommand.length > 0) {
        if (model.patchCommand.length > maximumAwdpPatchCommandArguments)
          addIssue(issues, translate('补丁应用命令最多包含 64 个参数'))
        if (model.patchCommand.some(argument => !argument.trim()))
          addIssue(issues, translate('补丁应用命令不能包含空参数'))
        if (model.patchCommand.some(argument => argument.length > maximumAwdpPatchCommandArgumentLength))
          addIssue(issues, translate('补丁应用命令的单个参数最多 4096 个字符'))
        if (model.patchCommand.filter(argument => argument === awdpPatchEntrypointPlaceholder).length !== 1)
          addIssue(issues, translate('非空补丁应用命令必须恰好包含一个独立的 {entrypoint} 参数'))
      }
      if (model.patchTimeoutSeconds !== null
        && (model.patchTimeoutSeconds < 1
          || model.patchTimeoutSeconds > maximumAwdpPatchTimeoutSeconds)) {
        addIssue(issues, translate('补丁超时必须在 1 到 300 秒之间'))
      }
      if (model.readyTimeoutSeconds !== null && model.readyTimeoutSeconds <= 0)
        addIssue(issues, translate('就绪超时必须为正数'))
      if (model.checkerJob) {
        const checkerTimeout = model.checkerJob.timeoutSeconds ?? 60
        const readyTimeout = model.readyTimeoutSeconds ?? 30
        const patchTimeout = model.patchTimeoutSeconds ?? 60
        if (readyTimeout > checkerTimeout)
          addIssue(issues, translate('就绪超时不能超过 Checker 超时'))
        if (patchTimeout > 0 && checkerTimeout > 0
          && patchTimeout + checkerTimeout + awdpFixExecutionOverheadSeconds
          >= awdpFixHandlerTimeoutSeconds) {
          addIssue(issues, translate('Fix 总执行预算必须小于专用处理器超时'))
        }
      }
      if (model.maximumPatchUploadBytes !== null
        && (model.maximumPatchUploadBytes <= 0
          || model.maximumPatchUploadBytes > HARD_MAXIMUM_PATCH_UPLOAD_BYTES)) {
        addIssue(issues, translate('Fix 包上传上限必须大于 0 且不超过 1024 MiB'))
      }
      break
    case 'Koh':
      if (runtime.allocation !== RuntimeAllocation.Shared)
        addIssue(issues, translate('KoH 运行环境必须采用共享分配'))
      break
  }
  return issues
}
