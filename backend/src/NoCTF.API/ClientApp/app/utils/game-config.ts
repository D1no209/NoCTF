import type { NoCtfapiEndpointsCompetitionsGameModeProtocol } from '../api'
import { translate } from './i18n'

/**
 * 游戏模式专属配置 JSON 的前端模型。
 * 对应后端 NoCTF.GameModes 各 *Configuration record:
 * 配置 JSON 使用 camelCase、枚举默认序列化为整数,唯一例外是 AWD 的
 * attackRewardMode(PascalCase 字符串)。schemaVersion 必须等于当前版本。
 * 参考:GameModeChallengeConfigurationCatalog / GameModeCompetitionConfigurationValidator。
 */

export type GameModeValue = NoCtfapiEndpointsCompetitionsGameModeProtocol

/** 各 JSON 区域当前的 schemaVersion(更高的版本或无 upgrader 的旧版本会被后端拒绝)。 */
export const DEFINITION_SCHEMA_VERSION = { Ctf: 2, Awd: 4, Awdp: 4, Koh: 1 } as const
export const COMPETITION_CONFIG_SCHEMA_VERSION: Record<GameModeValue, number> = { Ctf: 2, Awd: 2, Awdp: 4, Koh: 1 }
export const CHALLENGE_RULES_SCHEMA_VERSION: Record<GameModeValue, number> = { Ctf: 2, Awd: 4, Awdp: 4, Koh: 1 }

// ---------- 配置 JSON 内的整数枚举 ----------

export const RuntimeAllocation = { Shared: 0, PerTeam: 1 } as const
export const EgressPolicy = { Isolated: 0, InternetOnly: 1 } as const
export const FlagSource = { Static: 0, PerTeam: 1, AwdRotation: 2 } as const
export const UrlExposure = { OwnerOnly: 0, Participants: 1 } as const
export const BloodRewardPolicy = {
  FixedPoints: 0,
  InitialPointsPercentage: 1,
  SolveTimePointsPercentage: 2,
  CurrentPointsPercentage: 3,
} as const
export const ScoreDecayMode = {
  Fixed: 0,
  Linear: 1,
  Quadratic: 2,
  Exponential: 3,
  Logarithmic: 4,
  Custom: 5,
} as const
export const EvaluationDispatch = { Automatic: 0, ManualBatch: 1 } as const

export const ATTACK_REWARD_MODES = [
  { value: 'FixedPerAttack', label: "ui.fixedScoreForEachAttack" },
  { value: 'SplitVictimDefensePool', label: "ui.divideTheVictimDefensePool" },
] as const

export const BLOOD_REWARD_POLICIES = [
  { value: BloodRewardPolicy.FixedPoints, label: "ui.fixedPoints" },
  { value: BloodRewardPolicy.InitialPointsPercentage, label: "ui.initialScorePercentage" },
  { value: BloodRewardPolicy.SolveTimePointsPercentage, label: "ui.problemSolvingTimeScoreValuePercentage" },
  { value: BloodRewardPolicy.CurrentPointsPercentage, label: "ui.currentScorePercentage" },
] as const

export const SCORE_DECAY_MODES = [
  { value: ScoreDecayMode.Fixed, label: "ui.fixedPoints" },
  { value: ScoreDecayMode.Linear, label: "ui.linearDecay" },
  { value: ScoreDecayMode.Quadratic, label: "ui.quadraticDecay" },
  { value: ScoreDecayMode.Exponential, label: "ui.exponentialDecay" },
  { value: ScoreDecayMode.Logarithmic, label: "ui.logarithmicDecay" },
  { value: ScoreDecayMode.Custom, label: "ui.customFormula" },
] as const

export const EVALUATION_DISPATCH_MODES = [
  { value: EvaluationDispatch.Automatic, label: "ui.automaticAssessment" },
  { value: EvaluationDispatch.ManualBatch, label: "ui.manualBatchEvaluation" },
] as const

// ---------- 题目模板 Definition 模型 ----------

export interface RuntimeLimitsModel {
  memoryBytes: number | null
  nanoCpus: number | null
  pidsLimit: number | null
}

export interface SecurityModel {
  noNewPrivileges: boolean
  readonlyRootfs: boolean
  runAsNonRoot: boolean
  capDrop: string[]
  capAdd: string[]
}

export interface UrlBindingModel {
  urlTemplate: string
  exposure: number
  containerPort: number | null
  serviceName: string
}

export interface ContainerDefinitionModel {
  kind: 'container'
  image: string
  command: string[]
  environment: Record<string, string>
  labels: Record<string, string>
  /** 容器端口列表(host 端口恒为 0,由 Docker 随机分配)。 */
  containerPorts: Array<number | null>
  security: SecurityModel
  flagEnvironmentVariableName: string
  internalPorts: Array<number | null>
}

export interface ComposeServiceResourceModel {
  service: string
  memoryBytes: number | null
  nanoCpus: number | null
  pidsLimit: number | null
}

export interface ComposeDefinitionModel {
  kind: 'compose'
  composeYaml: string
  serviceResources: ComposeServiceResourceModel[]
  environment: Record<string, string>
  labels: Record<string, string>
  /** service -> 环境变量名。 */
  flagEnvironmentVariables: Record<string, string>
}

export type RuntimeDefinitionModel = ContainerDefinitionModel | ComposeDefinitionModel

export interface RuntimeTemplateModel {
  allocation: number
  definition: RuntimeDefinitionModel
  limits: RuntimeLimitsModel
  ttlSeconds: number | null
  operationTimeoutSeconds: number | null
  urlBindings: UrlBindingModel[]
  flagSource: number
  controlCheckUrlBinding: UrlBindingModel | null
}

export interface RunnerJobModel {
  image: string
  command: string[]
  environment: Record<string, string>
  timeoutSeconds: number | null
}

export interface FlagTemplateModel {
  header: string
  bodyTemplate: string
  leetLiteralText: boolean
}

export interface FlagInjectionModel {
  command: string
  timeoutSeconds: number | null
  serviceName: string
}

export interface DefinitionModel {
  runtime: RuntimeTemplateModel | null
  /** AWD:checker(包装 job + targetServiceName)。 */
  checker: { job: RunnerJobModel; targetServiceName: string } | null
  /** AWDP:checker 直接是 RunnerJobConfiguration。 */
  checkerJob: RunnerJobModel | null
  flagInjection: FlagInjectionModel | null
  flagTemplate: FlagTemplateModel | null
  patchEntrypoint: string
  patchCommand: string[]
  patchTimeoutSeconds: number | null
  readyTimeoutSeconds: number | null
  maximumPatchUploadBytes: number | null
  checkerFixInput: boolean
  checkerAllowRoot: boolean
}

export const DEFAULT_MAXIMUM_PATCH_UPLOAD_BYTES = 256 * 1024 * 1024
export const HARD_MAXIMUM_PATCH_UPLOAD_BYTES = 1024 * 1024 * 1024
export const DEFAULT_RUNTIME_MEMORY_BYTES = 256 * 1024 * 1024
export const DEFAULT_RUNTIME_NANO_CPUS = 500_000_000
export const DEFAULT_RUNTIME_PIDS_LIMIT = 128
export const DEFAULT_RUNTIME_TTL_SECONDS = 3600
export const DEFAULT_RUNTIME_OPERATION_TIMEOUT_SECONDS = 60

export function defaultContainerSecurity(): SecurityModel {
  return {
    noNewPrivileges: true,
    readonlyRootfs: false,
    runAsNonRoot: false,
    capDrop: ['ALL'],
    capAdd: [],
  }
}

function compatibilityContainerSecurity(): SecurityModel {
  return {
    noNewPrivileges: false,
    readonlyRootfs: false,
    runAsNonRoot: false,
    capDrop: [],
    capAdd: [],
  }
}

export function defaultRuntimeLimits(): RuntimeLimitsModel {
  return {
    memoryBytes: DEFAULT_RUNTIME_MEMORY_BYTES,
    nanoCpus: DEFAULT_RUNTIME_NANO_CPUS,
    pidsLimit: DEFAULT_RUNTIME_PIDS_LIMIT,
  }
}

export function emptyContainerDefinition(withFlagInjection = false): ContainerDefinitionModel {
  return {
    kind: 'container',
    image: '',
    command: [],
    environment: {},
    labels: {},
    containerPorts: [],
    security: defaultContainerSecurity(),
    flagEnvironmentVariableName: withFlagInjection ? 'FLAG' : '',
    internalPorts: [],
  }
}

export function emptyComposeDefinition(): ComposeDefinitionModel {
  return { kind: 'compose', composeYaml: '', serviceResources: [], environment: {}, labels: {}, flagEnvironmentVariables: {} }
}

export function emptyUrlBinding(): UrlBindingModel {
  return { urlTemplate: 'http://{HOST}:{PORT}', exposure: UrlExposure.Participants, containerPort: null, serviceName: '' }
}

export function emptyRuntimeTemplate(mode: GameModeValue): RuntimeTemplateModel {
  return {
    allocation: mode === 'Koh' ? RuntimeAllocation.Shared : RuntimeAllocation.PerTeam,
    definition: emptyContainerDefinition(mode === 'Ctf' || mode === 'Awdp'),
    limits: defaultRuntimeLimits(),
    ttlSeconds: DEFAULT_RUNTIME_TTL_SECONDS,
    operationTimeoutSeconds: DEFAULT_RUNTIME_OPERATION_TIMEOUT_SECONDS,
    urlBindings: [],
    flagSource: mode === 'Ctf' || mode === 'Awdp'
      ? FlagSource.PerTeam
      : mode === 'Awd'
        ? FlagSource.AwdRotation
        : FlagSource.Static,
    controlCheckUrlBinding: null,
  }
}

export function emptyRunnerJob(): RunnerJobModel {
  return { image: '', command: [], environment: {}, timeoutSeconds: null }
}

export function emptyFlagTemplate(): FlagTemplateModel {
  return { header: 'flag', bodyTemplate: '[GUID]', leetLiteralText: false }
}

export function emptyDefinition(mode: GameModeValue): DefinitionModel {
  return {
    runtime: null,
    checker: null,
    checkerJob: null,
    flagInjection: null,
    flagTemplate: null,
    patchEntrypoint: '',
    patchCommand: [],
    patchTimeoutSeconds: null,
    readyTimeoutSeconds: null,
    maximumPatchUploadBytes: mode === 'Awdp' ? DEFAULT_MAXIMUM_PATCH_UPLOAD_BYTES : null,
    checkerFixInput: false,
    checkerAllowRoot: false,
  }
}

// ---------- 通用 JSON 读取辅助 ----------

type JsonObject = Record<string, unknown>

function asObject(value: unknown): JsonObject | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? value as JsonObject : null
}

function asString(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

function asNumber(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null
}

function asBool(value: unknown): boolean {
  return value === true
}

function asStringArray(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((v): v is string => typeof v === 'string') : []
}

function asNumberArray(value: unknown): number[] {
  return Array.isArray(value) ? value.filter((v): v is number => typeof v === 'number' && Number.isFinite(v)) : []
}

function asStringMap(value: unknown): Record<string, string> {
  const obj = asObject(value)
  if (!obj) return {}
  const result: Record<string, string> = {}
  for (const [key, v] of Object.entries(obj)) {
    if (typeof v === 'string') result[key] = v
  }
  return result
}

export function parseJsonObject(json: string | null | undefined): JsonObject | null {
  if (!json) return {}
  try {
    return asObject(JSON.parse(json))
  }
  catch {
    return null
  }
}

// ---------- Definition 解析 ----------

function parseSecurity(raw: unknown): SecurityModel {
  const obj = asObject(raw)
  // Omitted security on a legacy template remains omitted after a read/write
  // cycle. Secure defaults are applied only when creating a new container draft.
  const defaults = compatibilityContainerSecurity()
  return {
    noNewPrivileges: typeof obj?.noNewPrivileges === 'boolean' ? obj.noNewPrivileges : defaults.noNewPrivileges,
    readonlyRootfs: typeof obj?.readonlyRootfs === 'boolean' ? obj.readonlyRootfs : defaults.readonlyRootfs,
    runAsNonRoot: typeof obj?.runAsNonRoot === 'boolean' ? obj.runAsNonRoot : defaults.runAsNonRoot,
    capDrop: Array.isArray(obj?.capDrop) ? asStringArray(obj.capDrop) : defaults.capDrop,
    capAdd: Array.isArray(obj?.capAdd) ? asStringArray(obj.capAdd) : defaults.capAdd,
  }
}

function parseUrlBinding(raw: unknown): UrlBindingModel {
  const obj = asObject(raw) ?? {}
  return {
    urlTemplate: asString(obj.urlTemplate),
    exposure: asNumber(obj.exposure) ?? UrlExposure.Participants,
    containerPort: asNumber(obj.containerPort),
    serviceName: asString(obj.serviceName),
  }
}

function parseRuntimeDefinition(raw: unknown): RuntimeDefinitionModel {
  const obj = asObject(raw) ?? {}
  if (obj.kind === 'compose') {
    const serviceResources: ComposeServiceResourceModel[] = []
    const resources = asObject(obj.serviceResources)
    if (resources) {
      for (const [service, value] of Object.entries(resources)) {
        const r = asObject(value) ?? {}
        serviceResources.push({
          service,
          memoryBytes: asNumber(r.memoryBytes),
          nanoCpus: asNumber(r.nanoCpus),
          pidsLimit: asNumber(r.pidsLimit),
        })
      }
    }
    return {
      kind: 'compose',
      composeYaml: asString(obj.composeYaml),
      serviceResources,
      environment: asStringMap(obj.environment),
      labels: asStringMap(obj.labels),
      flagEnvironmentVariables: asStringMap(obj.flagEnvironmentVariables),
    }
  }
  const portMappings = asObject(obj.portMappings) ?? {}
  return {
    kind: 'container',
    image: asString(obj.image),
    command: asStringArray(obj.command),
    environment: asStringMap(obj.environment),
    labels: asStringMap(obj.labels),
    containerPorts: Object.keys(portMappings).map(Number).filter(n => Number.isInteger(n) && n > 0),
    security: parseSecurity(obj.security),
    flagEnvironmentVariableName: asString(obj.flagEnvironmentVariableName),
    internalPorts: asNumberArray(obj.internalPorts),
  }
}

function parseRunnerJob(raw: unknown): RunnerJobModel {
  const obj = asObject(raw) ?? {}
  return {
    image: asString(obj.image),
    command: asStringArray(obj.command),
    environment: asStringMap(obj.environment),
    timeoutSeconds: asNumber(obj.timeoutSeconds),
  }
}

function parseFlagTemplate(raw: unknown): FlagTemplateModel {
  const obj = asObject(raw) ?? {}
  return {
    header: asString(obj.header) || 'flag',
    bodyTemplate: asString(obj.bodyTemplate) || '[GUID]',
    leetLiteralText: asBool(obj.leetLiteralText),
  }
}

function parseRuntimeTemplate(raw: unknown): RuntimeTemplateModel {
  const obj = asObject(raw) ?? {}
  const limits = asObject(obj.limits) ?? {}
  const defaultLimits = defaultRuntimeLimits()
  return {
    allocation: asNumber(obj.allocation) ?? RuntimeAllocation.PerTeam,
    definition: parseRuntimeDefinition(obj.definition),
    limits: {
      memoryBytes: asNumber(limits.memoryBytes) ?? defaultLimits.memoryBytes,
      nanoCpus: asNumber(limits.nanoCpus) ?? defaultLimits.nanoCpus,
      pidsLimit: asNumber(limits.pidsLimit) ?? defaultLimits.pidsLimit,
    },
    ttlSeconds: asNumber(obj.ttlSeconds) ?? DEFAULT_RUNTIME_TTL_SECONDS,
    operationTimeoutSeconds: asNumber(obj.operationTimeoutSeconds) ?? DEFAULT_RUNTIME_OPERATION_TIMEOUT_SECONDS,
    urlBindings: Array.isArray(obj.urlBindings) ? obj.urlBindings.map(parseUrlBinding) : [],
    flagSource: asNumber(obj.flagSource) ?? FlagSource.Static,
    controlCheckUrlBinding: obj.controlCheckUrlBinding ? parseUrlBinding(obj.controlCheckUrlBinding) : null,
  }
}

/** 解析模板 definitionJson 为当前模式的编辑模型;JSON 非法时返回 null。 */
export function parseDefinition(
  json: string | null | undefined,
  mode: GameModeValue = 'Ctf',
): DefinitionModel | null {
  const obj = parseJsonObject(json)
  if (!obj) return null
  const model = emptyDefinition(mode)
  if (mode === 'Awd' || mode === 'Awdp')
    model.checkerAllowRoot = obj.checkerAllowRoot === true
  model.runtime = obj.runtime ? parseRuntimeTemplate(obj.runtime) : null
  if (mode === 'Awd' && obj.checker) {
    const checker = asObject(obj.checker) ?? {}
    if ('job' in checker) {
      model.checker = {
        job: parseRunnerJob(checker.job),
        targetServiceName: asString(checker.targetServiceName),
      }
    }
  }
  if (mode === 'Awdp' && obj.checker) {
    const checker = asObject(obj.checker) ?? {}
    if (!('job' in checker)) model.checkerJob = parseRunnerJob(checker)
  }
  if (mode === 'Awd' && obj.flagInjection) {
    const injection = asObject(obj.flagInjection) ?? {}
    if (!('kind' in injection)) {
      model.flagInjection = {
        command: asString(injection.command),
        timeoutSeconds: asNumber(injection.timeoutSeconds),
        serviceName: asString(injection.serviceName),
      }
    }
  }
  if (mode === 'Ctf' || mode === 'Awd')
    model.flagTemplate = obj.flagTemplate ? parseFlagTemplate(obj.flagTemplate) : null
  if (mode === 'Awdp') {
    model.patchEntrypoint = asString(obj.patchEntrypoint)
    model.patchCommand = asStringArray(obj.patchCommand)
    model.patchTimeoutSeconds = asNumber(obj.patchTimeoutSeconds)
    model.readyTimeoutSeconds = asNumber(obj.readyTimeoutSeconds)
    model.maximumPatchUploadBytes = asNumber(obj.maximumPatchUploadBytes)
      ?? DEFAULT_MAXIMUM_PATCH_UPLOAD_BYTES
    model.checkerFixInput = obj.checkerFixInput === true
  }
  return model
}

// ---------- Definition 序列化 ----------

function putNumber(obj: JsonObject, key: string, value: number | null): void {
  if (value !== null && value !== undefined) obj[key] = value
}

function putString(obj: JsonObject, key: string, value: string): void {
  if (value.trim()) obj[key] = value.trim()
}

function putStringArray(obj: JsonObject, key: string, value: string[]): void {
  const items = value.map(v => v.trim()).filter(Boolean)
  if (items.length > 0) obj[key] = items
}

function putNumberArray(obj: JsonObject, key: string, value: Array<number | null>): void {
  const items = value.filter((item): item is number => item !== null)
  if (items.length > 0) obj[key] = items
}

function putStringMap(obj: JsonObject, key: string, value: Record<string, string>): void {
  const entries = Object.entries(value).filter(([k]) => k.trim())
  if (entries.length > 0) obj[key] = Object.fromEntries(entries)
}

function serializeUrlBinding(binding: UrlBindingModel): JsonObject {
  const obj: JsonObject = { urlTemplate: binding.urlTemplate.trim(), exposure: binding.exposure }
  putNumber(obj, 'containerPort', binding.containerPort)
  putString(obj, 'serviceName', binding.serviceName)
  return obj
}

function serializeSecurity(security: SecurityModel): JsonObject | null {
  const capDrop = security.capDrop.map(value => value.trim()).filter(Boolean)
  const capAdd = security.capAdd.map(value => value.trim()).filter(Boolean)
  const hasExplicitSecurity = security.noNewPrivileges
    || security.readonlyRootfs
    || security.runAsNonRoot
    || capDrop.length > 0
    || capAdd.length > 0
  if (!hasExplicitSecurity) return null

  // The backend accepts an explicit security object only when the complete
  // capability baseline is present. Keep the compatibility default omitted,
  // but make every explicitly configured security draft valid by construction.
  if (!capDrop.some(value => value.toUpperCase() === 'ALL')) capDrop.unshift('ALL')

  const obj: JsonObject = {}
  if (security.noNewPrivileges) obj.noNewPrivileges = true
  if (security.readonlyRootfs) obj.readonlyRootfs = true
  if (security.runAsNonRoot) obj.runAsNonRoot = true
  putStringArray(obj, 'capDrop', capDrop)
  putStringArray(obj, 'capAdd', capAdd)
  return obj
}

function serializeRuntimeDefinition(definition: RuntimeDefinitionModel): JsonObject {
  if (definition.kind === 'compose') {
    const obj: JsonObject = { kind: 'compose', composeYaml: definition.composeYaml }
    if (definition.serviceResources.length > 0) {
      const resources: JsonObject = {}
      for (const r of definition.serviceResources) {
        if (!r.service.trim()) continue
        const limits: JsonObject = {}
        putNumber(limits, 'memoryBytes', r.memoryBytes)
        putNumber(limits, 'nanoCpus', r.nanoCpus)
        putNumber(limits, 'pidsLimit', r.pidsLimit)
        resources[r.service.trim()] = limits
      }
      obj.serviceResources = resources
    }
    putStringMap(obj, 'environment', definition.environment)
    putStringMap(obj, 'labels', definition.labels)
    putStringMap(obj, 'flagEnvironmentVariables', definition.flagEnvironmentVariables)
    return obj
  }
  const obj: JsonObject = { kind: 'container', image: definition.image.trim() }
  putStringArray(obj, 'command', definition.command)
  putStringMap(obj, 'environment', definition.environment)
  putStringMap(obj, 'labels', definition.labels)
  const containerPorts = definition.containerPorts.filter((port): port is number => port !== null)
  if (containerPorts.length > 0) {
    obj.portMappings = Object.fromEntries(containerPorts.map(port => [String(port), 0]))
  }
  const security = serializeSecurity(definition.security)
  if (security) obj.security = security
  putString(obj, 'flagEnvironmentVariableName', definition.flagEnvironmentVariableName)
  // 可移植模板只声明隔离网络(0)；InternetOnly 会被后端拒绝。
  obj.egressPolicy = EgressPolicy.Isolated
  putNumberArray(obj, 'internalPorts', definition.internalPorts)
  return obj
}

function serializeRuntimeTemplate(runtime: RuntimeTemplateModel): JsonObject {
  const definition = serializeRuntimeDefinition(runtime.definition)
  // 后端仅允许 PerTeam/AwdRotation 配置 Flag 注入环境变量。
  if (runtime.flagSource === FlagSource.Static) {
    delete definition.flagEnvironmentVariableName
    delete definition.flagEnvironmentVariables
  }
  const obj: JsonObject = {
    allocation: runtime.allocation,
    definition,
  }
  const defaultLimits = defaultRuntimeLimits()
  const limits: JsonObject = {}
  putNumber(limits, 'memoryBytes', runtime.limits.memoryBytes ?? defaultLimits.memoryBytes)
  putNumber(limits, 'nanoCpus', runtime.limits.nanoCpus ?? defaultLimits.nanoCpus)
  putNumber(limits, 'pidsLimit', runtime.limits.pidsLimit ?? defaultLimits.pidsLimit)
  obj.limits = limits
  putNumber(obj, 'ttlSeconds', runtime.ttlSeconds ?? DEFAULT_RUNTIME_TTL_SECONDS)
  putNumber(obj, 'operationTimeoutSeconds', runtime.operationTimeoutSeconds ?? DEFAULT_RUNTIME_OPERATION_TIMEOUT_SECONDS)
  const bindings = runtime.urlBindings
    .filter(b => b.urlTemplate.trim())
    .map(serializeUrlBinding)
  if (bindings.length > 0) obj.urlBindings = bindings
  obj.flagSource = runtime.flagSource
  if (runtime.controlCheckUrlBinding && runtime.controlCheckUrlBinding.urlTemplate.trim()) {
    obj.controlCheckUrlBinding = serializeUrlBinding(runtime.controlCheckUrlBinding)
  }
  return obj
}

function serializeRunnerJob(job: RunnerJobModel): JsonObject {
  const obj: JsonObject = { image: job.image.trim() }
  putStringArray(obj, 'command', job.command)
  putStringMap(obj, 'environment', job.environment)
  putNumber(obj, 'timeoutSeconds', job.timeoutSeconds)
  return obj
}

/** 按模式白名单序列化编辑模型为 definitionJson。 */
export function serializeDefinition(mode: GameModeValue, model: DefinitionModel): string {
  const obj: JsonObject = { schemaVersion: DEFINITION_SCHEMA_VERSION[mode] }
  if (mode === 'Awd' || mode === 'Awdp')
    obj.checkerAllowRoot = model.checkerAllowRoot
  if (model.runtime) obj.runtime = serializeRuntimeTemplate(model.runtime)
  if (mode === 'Awd') {
    if (model.checker) {
      const checker: JsonObject = { job: serializeRunnerJob(model.checker.job) }
      putString(checker, 'targetServiceName', model.checker.targetServiceName)
      obj.checker = checker
    }
    if (model.flagInjection) {
      const injection: JsonObject = { command: model.flagInjection.command }
      putNumber(injection, 'timeoutSeconds', model.flagInjection.timeoutSeconds)
      putString(injection, 'serviceName', model.flagInjection.serviceName)
      obj.flagInjection = injection
    }
  }
  if (mode === 'Awdp') {
    putString(obj, 'patchEntrypoint', model.patchEntrypoint)
    putStringArray(obj, 'patchCommand', model.patchCommand)
    putNumber(obj, 'patchTimeoutSeconds', model.patchTimeoutSeconds)
    if (model.checkerJob) obj.checker = serializeRunnerJob(model.checkerJob)
    putNumber(obj, 'readyTimeoutSeconds', model.readyTimeoutSeconds)
    putNumber(obj, 'maximumPatchUploadBytes', model.maximumPatchUploadBytes)
    obj.checkerFixInput = model.checkerFixInput
  }
  return JSON.stringify(obj, null, 2)
}

/** 创建或切换模式时使用的完整、可提交默认定义。 */
export function defaultDefinitionJson(mode: GameModeValue): string {
  return serializeDefinition(mode, emptyDefinition(mode))
}

export function normalizeDefinitionJson(mode: GameModeValue, json: string): string | null {
  const model = parseDefinition(json, mode)
  return model ? serializeDefinition(mode, model) : null
}

// ---------- 通用「按字段描述」配置(竞赛配置 / 题目规则) ----------

export type ConfigFieldType =
  | 'int'
  | 'decimal'
  | 'string'
  | 'text'
  | 'bool'
  | 'select'
  | 'pointsCurve'
  | 'bloodRewards'
  | 'flagTemplate'

export interface ConfigFieldOption {
  value: string | number
  label: string
}

export interface ConfigFieldDef {
  /** JSON 属性名(camelCase)。 */
  key: string
  label: string
  type: ConfigFieldType
  description?: string
  placeholder?: string
  min?: number
  max?: number
  options?: readonly ConfigFieldOption[]
  /** 缺省值(竞赛配置缺失字段时回填)。 */
  defaultValue?: unknown
}

export interface PointsCurveValue {
  initialPoints: number | null
  minimumPoints: number | null
  decayTeamCount: number | null
  decayMode: number
  customExpression: string | null
}

export function ctfPointsAtSolveCount(curve: PointsCurveValue, solveCount: number): number | null {
  const { initialPoints, minimumPoints, decayTeamCount, decayMode } = curve
  if (initialPoints === null || minimumPoints === null || decayTeamCount === null
    || initialPoints < minimumPoints || minimumPoints < 0 || decayTeamCount <= 1)
    return null

  const normalizedCount = Math.max(1, solveCount)
  const progress = Math.min(1, Math.max(0, (normalizedCount - 1) / (decayTeamCount - 1)))
  let score: number
  switch (decayMode) {
    case ScoreDecayMode.Fixed:
      score = initialPoints
      break
    case ScoreDecayMode.Linear:
      score = initialPoints + (minimumPoints - initialPoints) * progress
      break
    case ScoreDecayMode.Quadratic:
      score = initialPoints + (minimumPoints - initialPoints) * progress * progress
      break
    case ScoreDecayMode.Exponential: {
      const steepness = 4
      const normalized = (Math.exp(-steepness * progress) - Math.exp(-steepness))
        / (1 - Math.exp(-steepness))
      score = minimumPoints + (initialPoints - minimumPoints) * normalized
      break
    }
    case ScoreDecayMode.Logarithmic:
      score = initialPoints + (minimumPoints - initialPoints) * Math.log10(1 + 9 * progress)
      break
    default:
      return null
  }
  return Math.round(Math.min(initialPoints, Math.max(minimumPoints, score)))
}

export interface BloodRewardValue {
  policy: number
  value: number | null
}

export type ConfigValues = Record<string, unknown>

const POINTS_CURVE_DEFAULT: PointsCurveValue = {
  initialPoints: 500,
  minimumPoints: 100,
  decayTeamCount: 10,
  decayMode: ScoreDecayMode.Quadratic,
  customExpression: null,
}

export function competitionConfigFields(mode: GameModeValue): ConfigFieldDef[] {
  switch (mode) {
    case 'Ctf':
      return [
        { key: 'defaultScoreCurve', label: translate("ui.defaultScoreCurve"), type: 'pointsCurve', defaultValue: POINTS_CURVE_DEFAULT, description: translate("ui.challengesWithoutAnOverrideInheritThisDecayCurve") },
        { key: 'bloodRewards', label: translate("ui.bloodListReward"), type: 'bloodRewards', defaultValue: [], description: translate("ui.additionalRewardsForTheFirstThreeProblemSolvingTeamsUp") },
        { key: 'wrongSubmissionPenalty', label: translate("ui.pointsDeductedForIncorrectSubmission"), type: 'int', min: 0, defaultValue: 0 },
        { key: 'flagTemplate', label: translate("ui.dynamicFlagTemplate"), type: 'flagTemplate', description: translate("ui.onlyUsedForEachFleetOfContainerFlagsGeneratedBy") },
      ]
    case 'Awd':
      return [
        { key: 'hardeningDurationSeconds', label: translate("ui.reinforcementPhaseDurationSeconds"), type: 'int', min: 0, defaultValue: 0, description: translate("ui.reinforcementTimeAfterTheStartOfTheGameDuringWhich") },
        { key: 'roundDurationSeconds', label: translate("ui.roundDurationSeconds"), type: 'int', min: 1, defaultValue: 300 },
        { key: 'attackRewardMode', label: translate("ui.attackScoringMethod"), type: 'select', options: ATTACK_REWARD_MODES, defaultValue: 'FixedPerAttack' },
        { key: 'attackPoints', label: translate("ui.scorePerAttack"), type: 'int', min: 0, defaultValue: 50 },
        { key: 'victimDefensePoolPoints', label: translate("ui.victimDefensePool"), type: 'int', min: 0, defaultValue: 100 },
        { key: 'checkerIntervalSeconds', label: translate("ui.checkIntervalSeconds"), type: 'int', min: 1, defaultValue: 30 },
        { key: 'serviceHealthyPoints', label: translate("ui.serviceNormalScore"), type: 'int', min: 0, defaultValue: 100 },
        { key: 'serviceUnhealthyPenalty', label: translate("ui.pointsDeductedForAbnormalService"), type: 'int', min: 0, defaultValue: 50 },
        { key: 'flagTemplate', label: translate("ui.teamFlagTemplate"), type: 'flagTemplate', description: translate("ui.optionalUsedToGenerateFlagsForEachTeamForEach") },
      ]
    case 'Awdp':
      return [
        { key: 'roundDurationSeconds', label: translate("ui.roundDurationSeconds"), type: 'int', min: 1, defaultValue: 300 },
        { key: 'break', label: translate("ui.breakScoreCurve"), type: 'pointsCurve', defaultValue: POINTS_CURVE_DEFAULT, description: translate("ui.settleEachRoundIndependentlyFromThatRoundSSuccessfulAttacking") },
        { key: 'fix', label: translate("ui.fixScoreCurve"), type: 'pointsCurve', defaultValue: POINTS_CURVE_DEFAULT, description: translate("ui.settleEachRoundIndependentlyFromThatRoundSSuccessfulFixing") },
        { key: 'requireBreakBeforeFix', label: translate("ui.requireBreakBeforeFix"), type: 'bool', defaultValue: false },
        { key: 'maxBreakSubmissions', label: translate("ui.breakMaximumNumberOfSubmissions"), type: 'int', min: 1, defaultValue: 10 },
        { key: 'maxFixSubmissions', label: translate("ui.fixMaximumNumberOfSubmissions"), type: 'int', min: 1, defaultValue: 10 },
        { key: 'flagWrongPenalty', label: translate("ui.wrongFlagPenalty"), type: 'int', min: 0, defaultValue: 0 },
        { key: 'exploitSucceededPenalty', label: translate("ui.exploitSuccessPenalty"), type: 'int', min: 0, defaultValue: 0 },
        { key: 'serviceAbnormalPenalty', label: translate("ui.pointsDeductedForAbnormalService"), type: 'int', min: 0, defaultValue: 0 },
        { key: 'evaluationDispatchMode', label: translate("ui.evaluationSchedulingMethod"), type: 'select', options: EVALUATION_DISPATCH_MODES, defaultValue: EvaluationDispatch.Automatic },
        { key: 'flagTemplate', label: translate("ui.breakFlagTemplate"), type: 'flagTemplate', description: translate("ui.optionalUsedToGenerateDynamicFlagsForEachTeamS") },
      ]
    case 'Koh':
      return [
        { key: 'pollIntervalSeconds', label: translate("ui.controlCheckIntervalSeconds"), type: 'int', min: 1, defaultValue: 5 },
        { key: 'controlPointsPerInterval', label: translate("ui.controlScorePerInterval"), type: 'int', min: 0, defaultValue: 10 },
      ]
  }
}

/** 题目规则(rulesJson):全部为可空,null = 继承竞赛默认。 */
export function challengeRuleFields(mode: GameModeValue): ConfigFieldDef[] {
  switch (mode) {
    case 'Ctf':
      return [
        { key: 'scoreCurve', label: translate("ui.scoreCurve"), type: 'pointsCurve' },
        { key: 'bloodRewards', label: translate("ui.bloodListReward"), type: 'bloodRewards', description: translate("ui.upTo3Items") },
        { key: 'maxFlagAttempts', label: translate("ui.flagMaximumNumberOfSubmissions"), type: 'int', min: 1 },
        { key: 'wrongSubmissionPenalty', label: translate("ui.pointsDeductedForIncorrectSubmission"), type: 'int', min: 0 },
        { key: 'flagTemplate', label: translate("ui.dynamicFlagTemplate"), type: 'flagTemplate', description: translate("ui.usedOnlyForFuturePerTeamRuntimeFlagsGeneratedFor") },
      ]
    case 'Awd':
      return [
        { key: 'attackRewardMode', label: translate("ui.attackScoringMethod"), type: 'select', options: ATTACK_REWARD_MODES },
        { key: 'attackPoints', label: translate("ui.scorePerAttack"), type: 'int', min: 0 },
        { key: 'victimDefensePoolPoints', label: translate("ui.victimDefensePool"), type: 'int', min: 0 },
        { key: 'checkerIntervalSeconds', label: translate("ui.checkIntervalSeconds"), type: 'int', min: 1 },
        { key: 'serviceHealthyPoints', label: translate("ui.serviceNormalScore"), type: 'int', min: 0 },
        { key: 'serviceUnhealthyPenalty', label: translate("ui.pointsDeductedForAbnormalService"), type: 'int', min: 0 },
        { key: 'flagTemplate', label: translate("ui.teamFlagTemplate"), type: 'flagTemplate', description: translate("ui.usedOnlyForFuturePerTeamPerRoundFlagsGenerated") },
      ]
    case 'Awdp':
      return [
        { key: 'break', label: translate("ui.breakScoreCurve"), type: 'pointsCurve' },
        { key: 'fix', label: translate("ui.fixScoreCurve"), type: 'pointsCurve' },
        { key: 'requireBreakBeforeFix', label: translate("ui.requireBreakBeforeFix"), type: 'bool' },
        { key: 'maxBreakSubmissions', label: translate("ui.breakMaximumNumberOfSubmissions"), type: 'int', min: 1 },
        { key: 'maxFixSubmissions', label: translate("ui.fixMaximumNumberOfSubmissions"), type: 'int', min: 1 },
        { key: 'flagWrongPenalty', label: translate("ui.wrongFlagPenalty"), type: 'int', min: 0 },
        { key: 'exploitSucceededPenalty', label: translate("ui.exploitSuccessPenalty"), type: 'int', min: 0 },
        { key: 'serviceAbnormalPenalty', label: translate("ui.pointsDeductedForAbnormalService"), type: 'int', min: 0 },
        { key: 'evaluationDispatchMode', label: translate("ui.evaluationSchedulingMethod"), type: 'select', options: EVALUATION_DISPATCH_MODES },
        { key: 'flagTemplate', label: translate("ui.breakFlagTemplate"), type: 'flagTemplate', description: translate("ui.usedOnlyForFuturePerTeamAttackInstanceFlagsGenerated") },
      ]
    case 'Koh':
      return [
        { key: 'pollIntervalSeconds', label: translate("ui.controlCheckIntervalSeconds"), type: 'int', min: 1 },
        { key: 'controlPointsPerInterval', label: translate("ui.controlScorePerInterval"), type: 'int', min: 0 },
      ]
  }
}

function parsePointsCurve(raw: unknown): PointsCurveValue {
  const obj = asObject(raw) ?? {}
  return {
    initialPoints: asNumber(obj.initialPoints),
    minimumPoints: asNumber(obj.minimumPoints),
    decayTeamCount: asNumber(obj.decayTeamCount),
    decayMode: asNumber(obj.decayMode) ?? ScoreDecayMode.Quadratic,
    customExpression: asString(obj.customExpression),
  }
}

function parseBloodRewards(raw: unknown): BloodRewardValue[] {
  if (!Array.isArray(raw)) return []
  return raw.map((item) => {
    const obj = asObject(item) ?? {}
    return {
      policy: asNumber(obj.policy) ?? BloodRewardPolicy.FixedPoints,
      value: asNumber(obj.value),
    }
  })
}

/** 字段缺省值(字段未出现在 JSON 中时使用)。 */
export function fieldDefaultValue(field: ConfigFieldDef): unknown {
  if (field.defaultValue !== undefined) return structuredClone(field.defaultValue)
  switch (field.type) {
    case 'int':
    case 'decimal':
      return null
    case 'string':
    case 'text':
      return ''
    case 'bool':
      return false
    case 'select':
      return field.options?.[0]?.value ?? ''
    case 'pointsCurve':
      return {
        initialPoints: null,
        minimumPoints: null,
        decayTeamCount: null,
        decayMode: ScoreDecayMode.Quadratic,
        customExpression: null,
      }
    case 'bloodRewards':
      return []
    case 'flagTemplate':
      return emptyFlagTemplate()
  }
}

function readFieldValue(field: ConfigFieldDef, raw: unknown): unknown {
  switch (field.type) {
    case 'int':
    case 'decimal':
      return asNumber(raw)
    case 'string':
    case 'text':
      return asString(raw)
    case 'bool':
      return asBool(raw)
    case 'select':
      return typeof raw === 'string' || typeof raw === 'number' ? raw : (field.options?.[0]?.value ?? '')
    case 'pointsCurve':
      return parsePointsCurve(raw)
    case 'bloodRewards':
      return parseBloodRewards(raw)
    case 'flagTemplate':
      return parseFlagTemplate(raw)
  }
}

/**
 * 解析配置 JSON 为字段值表。
 * overridden[key] = 该字段在 JSON 中显式出现(题目规则里用于区分「继承」与「覆盖」)。
 */
export function parseConfigValues(
  json: string | null | undefined,
  fields: ConfigFieldDef[],
): { values: ConfigValues; overridden: Record<string, boolean> } | null {
  const obj = parseJsonObject(json)
  if (!obj) return null
  const values: ConfigValues = {}
  const overridden: Record<string, boolean> = {}
  for (const field of fields) {
    if (field.key in obj) {
      values[field.key] = readFieldValue(field, obj[field.key])
      overridden[field.key] = true
    }
    else {
      values[field.key] = fieldDefaultValue(field)
      overridden[field.key] = false
    }
  }
  return { values, overridden }
}

function writeFieldValue(out: JsonObject, field: ConfigFieldDef, value: unknown): void {
  switch (field.type) {
    case 'int':
    case 'decimal': {
      if (typeof value === 'number' && Number.isFinite(value)) out[field.key] = value
      return
    }
    case 'string':
    case 'text': {
      if (typeof value === 'string' && value.trim()) out[field.key] = value.trim()
      return
    }
    case 'bool': {
      out[field.key] = value === true
      return
    }
    case 'select': {
      if (typeof value === 'string' || typeof value === 'number') out[field.key] = value
      return
    }
    case 'pointsCurve': {
      const curve = value as PointsCurveValue
      const obj: JsonObject = {}
      putNumber(obj, 'initialPoints', curve?.initialPoints ?? null)
      putNumber(obj, 'minimumPoints', curve?.minimumPoints ?? null)
      putNumber(obj, 'decayTeamCount', curve?.decayTeamCount ?? null)
      obj.decayMode = curve?.decayMode ?? ScoreDecayMode.Quadratic
      if (curve?.decayMode === ScoreDecayMode.Custom && curve.customExpression?.trim())
        obj.customExpression = curve.customExpression.trim()
      if (Object.keys(obj).length > 0) out[field.key] = obj
      return
    }
    case 'bloodRewards': {
      const rewards = (value as BloodRewardValue[]) ?? []
      out[field.key] = rewards.map(r => ({ policy: r.policy, ...(r.value !== null ? { value: r.value } : {}) }))
      return
    }
    case 'flagTemplate': {
      const tpl = value as FlagTemplateModel
      if (!tpl) return
      out[field.key] = {
        header: tpl.header.trim() || 'flag',
        bodyTemplate: tpl.bodyTemplate.trim() || '[GUID]',
        leetLiteralText: tpl.leetLiteralText,
      }
      return
    }
  }
}

/**
 * 序列化字段值表为配置 JSON。
 * rules 模式下只写入 overridden 的字段(其余继承竞赛默认)。
 */
export function serializeConfigValues(
  mode: GameModeValue,
  fields: ConfigFieldDef[],
  values: ConfigValues,
  options: { rules: boolean; overridden?: Record<string, boolean> },
): string {
  const version = options.rules ? CHALLENGE_RULES_SCHEMA_VERSION[mode] : COMPETITION_CONFIG_SCHEMA_VERSION[mode]
  const out: JsonObject = { schemaVersion: version }
  for (const field of fields) {
    if (options.rules && !options.overridden?.[field.key]) continue
    writeFieldValue(out, field, values[field.key])
  }
  return JSON.stringify(out, null, 2)
}

// ---------- 编辑辅助 ----------

/** 将字节数转换为 MiB 显示值(供内存输入框使用)。 */
export function bytesToMib(bytes: number | null): number | null {
  return bytes === null ? null : Math.round(bytes / 1048576)
}

export function mibToBytes(mib: number | null): number | null {
  return mib === null ? null : Math.round(mib * 1048576)
}

/** 将 nanoCpus 转换为核数显示值。 */
export function nanoCpusToCores(nanoCpus: number | null): number | null {
  return nanoCpus === null ? null : nanoCpus / 1e9
}

export function coresToNanoCpus(cores: number | null): number | null {
  return cores === null ? null : Math.round(cores * 1e9)
}
