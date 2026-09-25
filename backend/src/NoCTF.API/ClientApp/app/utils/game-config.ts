import type {
  NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeRuntimeContract,
  NoCtfapiEndpointsAdministrationCompetitionsBloodRewardPolicyProtocol,
  NoCtfapiEndpointsAdministrationCompetitionsEvaluationDispatchModeProtocol,
  NoCtfapiEndpointsAdministrationCompetitionsScoreDecayModeProtocol,
  NoCtfapiEndpointsCompetitionsGameModeProtocol,
} from '../api'
import { translate } from './i18n'

/** Strongly typed editor models for the OpenAPI game-mode contracts. */

export type GameModeValue = NoCtfapiEndpointsCompetitionsGameModeProtocol

// ---------- Editor enums ----------

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
export const CtfInteraction = { FlagSubmission: 0, PatchVerification: 1 } as const

const scoreDecayProtocols = [
  'Fixed', 'Linear', 'Quadratic', 'Exponential', 'Logarithmic', 'Custom',
] as const satisfies readonly NoCtfapiEndpointsAdministrationCompetitionsScoreDecayModeProtocol[]
const bloodRewardProtocols = [
  'FixedPoints', 'InitialPointsPercentage', 'SolveTimePointsPercentage', 'CurrentPointsPercentage',
] as const satisfies readonly NoCtfapiEndpointsAdministrationCompetitionsBloodRewardPolicyProtocol[]
const evaluationDispatchProtocols = [
  'Automatic', 'Manual',
] as const satisfies readonly NoCtfapiEndpointsAdministrationCompetitionsEvaluationDispatchModeProtocol[]

function protocolIndex(value: unknown, names: readonly string[], fallback: number): number {
  const index = names.indexOf(value as string)
  return index < 0 ? fallback : index
}

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

export interface OvaDefinitionModel {
  kind: 'ova'
  sourceUrl: string
  sha256: string
}

export type RuntimeDefinitionModel = ContainerDefinitionModel | ComposeDefinitionModel | OvaDefinitionModel

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
  interactionKind: number
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
export const DEFAULT_CTF_PATCH_UPLOAD_BYTES = 64 * 1024 * 1024
export const HARD_MAXIMUM_PATCH_UPLOAD_BYTES = 1024 * 1024 * 1024
export const DEFAULT_RUNTIME_MEMORY_BYTES = 256 * 1024 * 1024
export const DEFAULT_RUNTIME_NANO_CPUS = 500_000_000
export const DEFAULT_RUNTIME_PIDS_LIMIT = 128
export const DEFAULT_RUNTIME_TTL_SECONDS = 3600
export const DEFAULT_RUNTIME_OPERATION_TIMEOUT_SECONDS = 60

export function defaultContainerSecurity(): SecurityModel {
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
    containerPorts: [null],
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
  const accessBinding = emptyUrlBinding()
  accessBinding.exposure = mode === 'Ctf' || mode === 'Awdp'
    ? UrlExposure.OwnerOnly
    : UrlExposure.Participants
  return {
    allocation: mode === 'Koh' ? RuntimeAllocation.Shared : RuntimeAllocation.PerTeam,
    definition: emptyContainerDefinition(mode === 'Ctf' || mode === 'Awdp'),
    limits: defaultRuntimeLimits(),
    ttlSeconds: DEFAULT_RUNTIME_TTL_SECONDS,
    operationTimeoutSeconds: DEFAULT_RUNTIME_OPERATION_TIMEOUT_SECONDS,
    urlBindings: [accessBinding],
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
    interactionKind: CtfInteraction.FlagSubmission,
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

// ---------- Definition mapping ----------

function parseUrlBinding(raw: unknown): UrlBindingModel {
  const obj = asObject(raw) ?? {}
  return {
    urlTemplate: asString(obj.urlTemplate),
    exposure: obj.exposure === 'OwnerOnly'
      ? UrlExposure.OwnerOnly
      : UrlExposure.Participants,
    containerPort: asNumber(obj.containerPort),
    serviceName: asString(obj.serviceName),
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

// ---------- Definition mapping ----------

function putNumber(obj: JsonObject, key: string, value: number | null): void {
  if (value !== null && value !== undefined) obj[key] = value
}


export function emptyOvaDefinition(): OvaDefinitionModel {
  return { kind: 'ova', sourceUrl: '', sha256: '' }
}

function protocolAllocation(value: unknown): number {
  return value === 'Shared' ? RuntimeAllocation.Shared : RuntimeAllocation.PerTeam
}

function protocolFlagSource(value: unknown): number {
  return value === 'PerTeam'
    ? FlagSource.PerTeam
    : value === 'AwdRotation'
      ? FlagSource.AwdRotation
      : FlagSource.Static
}

function definitionContractRuntime(
  runtime: NoCtfapiEndpointsAdministrationChallengeBankChallengeRuntimeContract,
): RuntimeTemplateModel {
  const value = runtime as Record<string, unknown>
  let definition: RuntimeDefinitionModel
  if (runtime.kind === 'Compose') {
    const compose = asObject(runtime.compose)
    if (!compose || runtime.container || runtime.ova) throw new Error('Invalid Compose Runtime contract.')
    definition = {
      kind: 'compose',
      composeYaml: asString(compose.composeYaml),
      serviceResources: Array.isArray(compose.serviceResources)
        ? compose.serviceResources.map((entry) => {
            const item = asObject(entry) ?? {}
            const limits = asObject(item.limits) ?? {}
            return {
              service: asString(item.serviceName),
              memoryBytes: asNumber(limits.memoryBytes),
              nanoCpus: asNumber(limits.nanoCpus),
              pidsLimit: asNumber(limits.pidsLimit),
            }
          })
        : [],
      environment: asStringMap(compose.environment),
      labels: asStringMap(compose.labels),
      flagEnvironmentVariables: asStringMap(compose.flagEnvironmentVariables),
    }
  }
  else if (runtime.kind === 'Ova') {
    const ova = asObject(runtime.ova)
    if (!ova || runtime.container || runtime.compose) throw new Error('Invalid Ova Runtime contract.')
    definition = {
      kind: 'ova',
      sourceUrl: asString(ova.sourceUrl),
      sha256: asString(ova.sha256),
    }
  }
  else if (runtime.kind === 'Container') {
    const container = asObject(runtime.container)
    if (!container || runtime.compose || runtime.ova) throw new Error('Invalid Container Runtime contract.')
    const security = asObject(container.security) ?? {}
    definition = {
      kind: 'container',
      image: asString(container.image),
      command: asStringArray(container.command),
      environment: asStringMap(container.environment),
      labels: asStringMap(container.labels),
      containerPorts: Array.isArray(container.portMappings)
        ? container.portMappings.map(entry => asNumber(asObject(entry)?.containerPort))
        : [],
      security: {
        noNewPrivileges: asBool(security.noNewPrivileges),
        readonlyRootfs: asBool(security.readonlyRootfs),
        runAsNonRoot: asBool(security.runAsNonRoot),
        capDrop: asStringArray(security.capDrop),
        capAdd: asStringArray(security.capAdd),
      },
      flagEnvironmentVariableName: asString(container.flagEnvironmentVariableName),
      internalPorts: asNumberArray(container.internalPorts),
    }
  }
  else throw new Error('Unknown Runtime kind.')
  const limits = asObject(value.limits) ?? {}
  const bindings = Array.isArray(value.urlBindings) ? value.urlBindings : []
  return {
    allocation: protocolAllocation(value.allocation),
    definition,
    limits: {
      memoryBytes: asNumber(limits.memoryBytes),
      nanoCpus: asNumber(limits.nanoCpus),
      pidsLimit: asNumber(limits.pidsLimit),
    },
    ttlSeconds: asNumber(value.ttlSeconds),
    operationTimeoutSeconds: asNumber(value.operationTimeoutSeconds),
    urlBindings: bindings.filter(item => asObject(item)?.isControlCheck !== true).map(parseUrlBinding),
    flagSource: protocolFlagSource(value.flagSource),
    controlCheckUrlBinding: bindings.find(item => asObject(item)?.isControlCheck === true)
      ? parseUrlBinding(bindings.find(item => asObject(item)?.isControlCheck === true))
      : null,
  }
}

export function definitionContractToModel(
  contract: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract,
  mode: GameModeValue,
): DefinitionModel {
  if (contract.mode !== mode) throw new Error('Challenge definition mode does not match the editor.')
  const selected = mode === 'Ctf' ? contract.ctf : mode === 'Awd' ? contract.awd : mode === 'Awdp' ? contract.awdp : contract.koh
  if (!selected || [contract.ctf, contract.awd, contract.awdp, contract.koh].filter(Boolean).length !== 1)
    throw new Error('Invalid challenge definition branch.')
  const model = emptyDefinition(mode)
  model.flagTemplate = contract.flagTemplate ? parseFlagTemplate(contract.flagTemplate) : null
  model.interactionKind = contract.ctf?.interactionKind === 'PatchVerification'
    ? CtfInteraction.PatchVerification
    : CtfInteraction.FlagSubmission
  model.runtime = contract.runtime ? definitionContractRuntime(contract.runtime) : null
  const checker = asObject(contract.checker)
  if (checker) {
    const job = {
      image: asString(checker.image),
      command: asStringArray(checker.command),
      environment: asStringMap(checker.environment),
      timeoutSeconds: asNumber(checker.timeoutSeconds),
    }
    if (mode === 'Awd') {
      model.checker = {
        job,
        targetServiceName: asString(checker.targetServiceName),
      }
    }
    else {
      model.checkerJob = job
    }
  }
  const injection = asObject(contract.awd?.flagInjection)
  if (mode === 'Awd' && injection) {
    model.flagInjection = {
      command: asString(injection.command),
      timeoutSeconds: asNumber(injection.timeoutSeconds),
      serviceName: asString(injection.serviceName),
    }
  }
  model.patchEntrypoint = asString(contract.patchEntrypoint)
  model.patchCommand = asStringArray(contract.patchCommand)
  model.patchTimeoutSeconds = asNumber(contract.patchTimeoutSeconds)
  model.readyTimeoutSeconds = asNumber(contract.readyTimeoutSeconds)
  model.maximumPatchUploadBytes = asNumber(contract.maximumPatchUploadBytes)
  model.checkerFixInput = contract.checkerFixInput === true
  model.checkerAllowRoot = contract.checkerAllowRoot === true
  return model
}

export function applyCtfInteraction(
  model: DefinitionModel,
  interactionKind: number,
): void {
  model.interactionKind = interactionKind
  if (interactionKind === CtfInteraction.PatchVerification) {
    model.runtime ??= emptyRuntimeTemplate('Ctf')
    if (model.runtime.definition.kind !== 'container')
      model.runtime.definition = emptyContainerDefinition(false)
    model.runtime.flagSource = FlagSource.Static
    model.runtime.definition.flagEnvironmentVariableName = ''
    model.checkerJob ??= emptyRunnerJob()
    model.maximumPatchUploadBytes ??= DEFAULT_CTF_PATCH_UPLOAD_BYTES
    model.flagTemplate = null
    return
  }
  model.patchEntrypoint = ''
  model.patchCommand = []
  model.patchTimeoutSeconds = null
  model.readyTimeoutSeconds = null
  model.maximumPatchUploadBytes = null
  model.checkerJob = null
  model.checkerFixInput = false
  model.checkerAllowRoot = false
  if (model.runtime) {
    model.runtime.flagSource = FlagSource.PerTeam
    if (model.runtime.definition.kind === 'container')
      model.runtime.definition.flagEnvironmentVariableName = 'FLAG'
  }
}

// ---------- Field-driven competition and challenge configuration ----------

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
        { key: 'breakScoreCurve', label: translate("ui.breakScoreCurve"), type: 'pointsCurve', defaultValue: POINTS_CURVE_DEFAULT, description: translate("ui.settleEachRoundIndependentlyFromThatRoundSSuccessfulAttacking") },
        { key: 'fixScoreCurve', label: translate("ui.fixScoreCurve"), type: 'pointsCurve', defaultValue: POINTS_CURVE_DEFAULT, description: translate("ui.settleEachRoundIndependentlyFromThatRoundSSuccessfulFixing") },
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

/** Challenge rule fields are nullable; null means inheriting the competition value. */
export function challengeRuleFields(mode: GameModeValue): ConfigFieldDef[] {
  switch (mode) {
    case 'Ctf':
      return [
        { key: 'scoreCurve', label: translate("ui.scoreCurve"), type: 'pointsCurve' },
        { key: 'bloodRewards', label: translate("ui.bloodListReward"), type: 'bloodRewards', description: translate("ui.upTo3Items") },
        { key: 'maxFlagAttempts', label: translate("ui.flagMaximumNumberOfSubmissions"), type: 'int', min: 1 },
        { key: 'maxPatchAttempts', label: translate("ui.patchMaximumNumberOfSubmissions"), type: 'int', min: 1, defaultValue: 10 },
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
        { key: 'breakScoreCurve', label: translate("ui.breakScoreCurve"), type: 'pointsCurve' },
        { key: 'fixScoreCurve', label: translate("ui.fixScoreCurve"), type: 'pointsCurve' },
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
    decayMode: protocolIndex(obj.decayMode, scoreDecayProtocols, ScoreDecayMode.Quadratic),
    customExpression: asString(obj.customExpression),
  }
}

function parseBloodRewards(raw: unknown): BloodRewardValue[] {
  if (!Array.isArray(raw)) return []
  return raw.map((item) => {
    const obj = asObject(item) ?? {}
    return {
      policy: protocolIndex(obj.policy, bloodRewardProtocols, BloodRewardPolicy.FixedPoints),
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
      if (field.key === 'evaluationDispatchMode')
        return protocolIndex(raw, evaluationDispatchProtocols, EvaluationDispatch.Automatic)
      return typeof raw === 'string' || typeof raw === 'number' ? raw : (field.options?.[0]?.value ?? '')
    case 'pointsCurve':
      return parsePointsCurve(raw)
    case 'bloodRewards':
      return parseBloodRewards(raw)
    case 'flagTemplate':
      return parseFlagTemplate(raw)
  }
}

function runtimeModelToContract(
  runtime: RuntimeTemplateModel,
): NoCtfapiEndpointsAdministrationChallengeBankChallengeRuntimeContract {
  const common = {
    allocation: runtime.allocation === RuntimeAllocation.Shared ? 'Shared' as const : 'PerTeam' as const,
    limits: {
      memoryBytes: runtime.limits.memoryBytes ?? 0,
      nanoCpus: runtime.limits.nanoCpus ?? 0,
      pidsLimit: runtime.limits.pidsLimit ?? 0,
    },
    ttlSeconds: runtime.ttlSeconds,
    operationTimeoutSeconds: runtime.operationTimeoutSeconds,
    flagSource: runtime.flagSource === FlagSource.PerTeam
      ? 'PerTeam' as const
      : runtime.flagSource === FlagSource.AwdRotation
        ? 'AwdRotation' as const
        : 'Static' as const,
    egressPolicy: 'Isolated' as const,
    urlBindings: [
      ...runtime.urlBindings.map(binding => ({
        urlTemplate: binding.urlTemplate,
        exposure: binding.exposure === UrlExposure.OwnerOnly ? 'OwnerOnly' as const : 'Participants' as const,
        containerPort: binding.containerPort,
        serviceName: binding.serviceName || null,
        vmId: null,
        guestPort: null,
        isControlCheck: false,
      })),
      ...(runtime.controlCheckUrlBinding
        ? [{
            urlTemplate: runtime.controlCheckUrlBinding.urlTemplate,
            exposure: runtime.controlCheckUrlBinding.exposure === UrlExposure.OwnerOnly
              ? 'OwnerOnly' as const
              : 'Participants' as const,
            containerPort: runtime.controlCheckUrlBinding.containerPort,
            serviceName: runtime.controlCheckUrlBinding.serviceName || null,
            vmId: null,
            guestPort: null,
            isControlCheck: true,
          }]
        : []),
    ],
  }
  if (runtime.definition.kind === 'compose') {
    return {
      kind: 'Compose',
      ...common,
      compose: {
        composeYaml: runtime.definition.composeYaml,
        environment: runtime.definition.environment,
        labels: runtime.definition.labels,
        flagEnvironmentVariables: runtime.definition.flagEnvironmentVariables,
        serviceResources: runtime.definition.serviceResources.map(item => ({
          serviceName: item.service,
          limits: {
            memoryBytes: item.memoryBytes ?? 0,
            nanoCpus: item.nanoCpus ?? 0,
            pidsLimit: item.pidsLimit ?? 0,
          },
        })),
      },
    }
  }
  if (runtime.definition.kind === 'ova') {
    return {
      kind: 'Ova',
      ...common,
      ova: { sourceUrl: runtime.definition.sourceUrl, sha256: runtime.definition.sha256 },
    }
  }
  return {
    kind: 'Container',
    ...common,
    container: {
      image: runtime.definition.image,
      command: runtime.definition.command,
      environment: runtime.definition.environment,
      labels: runtime.definition.labels,
      portMappings: runtime.definition.containerPorts
        .filter((port): port is number => port !== null)
        .map(port => ({ containerPort: port, hostPort: 0 })),
      security: {
        noNewPrivileges: runtime.definition.security.noNewPrivileges,
        readonlyRootfs: runtime.definition.security.readonlyRootfs,
        runAsNonRoot: runtime.definition.security.runAsNonRoot,
        capDrop: runtime.definition.security.capDrop,
        capAdd: runtime.definition.security.capAdd,
      },
      flagEnvironmentVariableName: runtime.definition.flagEnvironmentVariableName || null,
      internalPorts: runtime.definition.internalPorts
        .filter((port): port is number => port !== null),
    },
  }
}

function runnerJobContract(job: RunnerJobModel, targetServiceName: string | null = null) {
  return {
    image: job.image,
    command: job.command,
    environment: job.environment,
    timeoutSeconds: job.timeoutSeconds ?? 60,
    targetServiceName,
  }
}

export function definitionModelToContract(
  mode: GameModeValue,
  model: DefinitionModel,
): NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract {
  const common = {
    flagTemplate: model.flagTemplate ? {
      header: model.flagTemplate.header,
      bodyTemplate: model.flagTemplate.bodyTemplate,
      leetLiteralText: model.flagTemplate.leetLiteralText,
    } : null,
    runtime: model.runtime ? runtimeModelToContract(model.runtime) : null,
    checker: mode === 'Awd' && model.checker
      ? runnerJobContract(model.checker.job, model.checker.targetServiceName || null)
      : model.checkerJob
        ? runnerJobContract(model.checkerJob)
        : null,
    patchEntrypoint: model.patchEntrypoint || null,
    patchCommand: model.patchCommand,
    patchTimeoutSeconds: model.patchTimeoutSeconds,
    readyTimeoutSeconds: model.readyTimeoutSeconds,
    maximumPatchUploadBytes: model.maximumPatchUploadBytes,
    checkerFixInput: model.checkerFixInput,
    checkerAllowRoot: model.checkerAllowRoot,
  }
  switch (mode) {
    case 'Ctf':
      return {
        mode,
        ...common,
        ctf: { interactionKind: model.interactionKind === CtfInteraction.PatchVerification
          ? 'PatchVerification'
          : 'FlagSubmission' },
      }
    case 'Awd':
      return {
        mode,
        ...common,
        awd: { flagInjection: model.flagInjection
          ? {
              command: model.flagInjection.command,
              timeoutSeconds: model.flagInjection.timeoutSeconds ?? 30,
              serviceName: model.flagInjection.serviceName || null,
            }
          : null },
      }
    case 'Awdp':
      return { mode, ...common, awdp: {} }
    case 'Koh':
      return { mode, ...common, koh: {} }
  }
}

export function defaultDefinition(mode: GameModeValue): NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract {
  return definitionModelToContract(mode, emptyDefinition(mode))
}

export function readConfigValues(
  configuration: Record<string, unknown> | null | undefined,
  fields: ConfigFieldDef[],
  options: { rules: boolean },
): { values: ConfigValues; overridden: Record<string, boolean> } | null {
  if (!configuration) return null
  const mode = configuration.mode
  if (mode !== 'Ctf' && mode !== 'Awd' && mode !== 'Awdp' && mode !== 'Koh') return null
  if (!options.rules && !asObject(configuration.flagTemplate)) return null
  const branchName = mode.toLowerCase()
  const branch = asObject(configuration[branchName])
  if (!branch || ['ctf', 'awd', 'awdp', 'koh'].filter(key => configuration[key] != null).length !== 1)
    return null
  const values: ConfigValues = {}
  const overridden: Record<string, boolean> = {}
  for (const field of fields) {
    const source = !options.rules && field.key === 'flagTemplate' ? configuration : branch
    const key = !options.rules && field.key === 'scoreCurve' ? 'defaultScoreCurve' : field.key
    if (key in source) {
      values[field.key] = readFieldValue(field, source[key])
      overridden[field.key] = source[key] !== null
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
      if (field.key === 'evaluationDispatchMode') {
        out[field.key] = evaluationDispatchProtocols[value as number] ?? evaluationDispatchProtocols[EvaluationDispatch.Automatic]
        return
      }
      if (typeof value === 'string' || typeof value === 'number') out[field.key] = value
      return
    }
    case 'pointsCurve': {
      const curve = value as PointsCurveValue
      const obj: JsonObject = {}
      putNumber(obj, 'initialPoints', curve?.initialPoints ?? null)
      putNumber(obj, 'minimumPoints', curve?.minimumPoints ?? null)
      putNumber(obj, 'decayTeamCount', curve?.decayTeamCount ?? null)
      obj.decayMode = scoreDecayProtocols[curve?.decayMode ?? ScoreDecayMode.Quadratic]
        ?? scoreDecayProtocols[ScoreDecayMode.Quadratic]
      if (curve?.decayMode === ScoreDecayMode.Custom && curve.customExpression?.trim())
        obj.customExpression = curve.customExpression.trim()
      if (Object.keys(obj).length > 0) out[field.key] = obj
      return
    }
    case 'bloodRewards': {
      const rewards = (value as BloodRewardValue[]) ?? []
      out[field.key] = rewards.map(r => ({
        policy: bloodRewardProtocols[r.policy] ?? bloodRewardProtocols[BloodRewardPolicy.FixedPoints],
        ...(r.value !== null ? { value: r.value } : {}),
      }))
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

export function buildConfigValues(
  mode: GameModeValue,
  fields: ConfigFieldDef[],
  values: ConfigValues,
  options: { rules: boolean; overridden?: Record<string, boolean> },
): Record<string, unknown> {
  const branch: JsonObject = {}
  const out: JsonObject = { mode, [mode.toLowerCase()]: branch }
  for (const field of fields) {
    if (options.rules && !options.overridden?.[field.key]) continue
    writeFieldValue(!options.rules && field.key === 'flagTemplate' ? out : branch, field, values[field.key])
  }
  if (!options.rules && !out.flagTemplate)
    writeFieldValue(out, { key: 'flagTemplate', label: '', type: 'flagTemplate' }, emptyFlagTemplate())
  return out
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
