import { computed, ref, watch } from 'vue'
import { challengeDirectionsForType, normalizeDirection } from '@/lib/challengeDirections'
import { optionalPositiveNumber } from '../shared/number'

export interface CheckerConfigDto {
  image?: string
  command?: string
  timeoutSeconds?: number | null
  expImage?: string | null
  expCommand?: string | null
}

// Server payload as consumed by list/table views: only `id` is guaranteed,
// everything else is read defensively by both themes.
export interface ChallengeTemplateDto extends ChallengeTemplateInputDto {
  id: string
}

export interface ChallengeTemplateInputDto {
  title?: string
  description?: string
  typeId?: string
  direction?: string
  containerImage?: string
  containerMode?: 'SingleImage' | 'DockerCompose' | number
  composeYaml?: string
  composeProjectName?: string
  attachmentUrl?: string
  patchTemplateUrl?: string
  flagEnvironmentVariable?: string
  deploymentType?: 'NoAttachment' | 'StaticAttachment' | 'DynamicContainer' | 'StaticContainer' | number
  exposedPort?: number | null
  checkerConfig?: CheckerConfigDto
}

export interface ChallengeTemplateSubmit {
  payload: Record<string, unknown>
  attachmentFile: File | null
  patchTemplateFile: File | null
}

export const deploymentTypeKeys = ['NoAttachment', 'StaticAttachment', 'DynamicContainer', 'StaticContainer'] as const
export type DeploymentTypeKey = typeof deploymentTypeKeys[number]
export const ctfDeploymentTypes = ['StaticAttachment', 'DynamicContainer', 'StaticContainer'] as const
export const containerDeploymentTypes = ['DynamicContainer', 'StaticContainer'] as const

export type ChallengeTypeKey = 'Ctf' | 'Awd' | 'Awdp' | 'Koh'
export const challengeTypeOptions: { value: ChallengeTypeKey, label: string }[] = [
  { value: 'Ctf', label: 'CTF' },
  { value: 'Awd', label: 'AWD' },
  { value: 'Awdp', label: 'AWDP' },
  { value: 'Koh', label: 'KoH' },
]

// Display mapping used by list/table views: keeps 'NoAttachment' as-is.
export function deploymentTypeKey(value?: ChallengeTemplateInputDto['deploymentType']): DeploymentTypeKey {
  return typeof value === 'number' ? deploymentTypeKeys[value] ?? 'NoAttachment' : value ?? 'NoAttachment'
}

// Form mapping: the editor never offers 'NoAttachment' (it is derived from an
// empty attachment URL), so it falls back to 'StaticAttachment'.
export function formDeploymentTypeKey(value?: ChallengeTemplateInputDto['deploymentType']): DeploymentTypeKey {
  const resolved = typeof value === 'number' ? deploymentTypeKeys[value] ?? 'StaticAttachment' : value ?? 'StaticAttachment'
  return resolved === 'NoAttachment' ? 'StaticAttachment' : resolved
}

export function deploymentTypeValue(value: DeploymentTypeKey) {
  return deploymentTypeKeys.indexOf(value)
}

export function normalizeChallengeType(value?: string | null): ChallengeTypeKey {
  const key = (value ?? 'Ctf').trim().toLowerCase()
  if (key === 'awd')
    return 'Awd'
  if (key === 'awdp')
    return 'Awdp'
  if (key === 'koh')
    return 'Koh'
  return 'Ctf'
}

export interface ChallengeTemplateFormState {
  title: string
  description: string
  typeId: string
  direction: string
  attachmentUrl: string
  patchTemplateUrl: string
  deploymentType: string
  exposedPort: string
  flagSecret: string
  flagEnvironmentVariable: string
  containerImage: string
  containerMode: string
  composeYaml: string
  composeProjectName: string
  checkerImage: string
  checkerCommand: string
  checkerTimeoutSeconds: string
  expImage: string
  expCommand: string
}

export function defaultChallengeTemplateForm(): ChallengeTemplateFormState {
  return {
    title: '',
    description: '',
    typeId: 'Ctf',
    direction: 'WEB',
    attachmentUrl: '',
    patchTemplateUrl: '',
    deploymentType: 'StaticAttachment',
    exposedPort: '',
    flagSecret: '',
    flagEnvironmentVariable: 'NOCTF_FLAG_UUID',
    containerImage: '',
    containerMode: 'SingleImage',
    composeYaml: '',
    composeProjectName: '',
    checkerImage: '',
    checkerCommand: '',
    checkerTimeoutSeconds: '',
    expImage: '',
    expCommand: '',
  }
}

// Shared headless state for the challenge template editor. Both theme
// packages render their own form component on top of this hook.
export function useChallengeTemplateForm(template: () => ChallengeTemplateInputDto | null | undefined) {
  const attachmentFile = ref<File | null>(null)
  const patchTemplateFile = ref<File | null>(null)
  const form = ref<ChallengeTemplateFormState>(defaultChallengeTemplateForm())

  const selectedChallengeType = computed(() => normalizeChallengeType(form.value.typeId))
  const directionOptions = computed(() => challengeDirectionsForType(selectedChallengeType.value))
  const deploymentOptionKeys = computed(() => selectedChallengeType.value === 'Ctf'
    ? ctfDeploymentTypes
    : containerDeploymentTypes)
  const isContainerDeployment = computed(() =>
    containerDeploymentTypes.includes(form.value.deploymentType as typeof containerDeploymentTypes[number]))
  const usesRuntimeContainer = computed(() =>
    selectedChallengeType.value !== 'Ctf' || isContainerDeployment.value)
  const usesExpConfig = computed(() => selectedChallengeType.value === 'Awd')
  const usesPatchTemplate = computed(() => selectedChallengeType.value === 'Awdp')
  const isContainerImageMissing = computed(() => usesRuntimeContainer.value && !form.value.containerImage.trim())
  const canSave = computed(() => Boolean(form.value.title.trim()) && !isContainerImageMissing.value)

  watch(template, (value) => {
    attachmentFile.value = null
    patchTemplateFile.value = null
    if (!value) {
      form.value = defaultChallengeTemplateForm()
      return
    }

    form.value = {
      title: value.title ?? '',
      description: value.description ?? '',
      typeId: normalizeChallengeType(value.typeId),
      direction: normalizeDirection(value.direction),
      attachmentUrl: value.attachmentUrl ?? '',
      patchTemplateUrl: value.patchTemplateUrl ?? '',
      deploymentType: formDeploymentTypeKey(value.deploymentType),
      exposedPort: value.exposedPort === undefined || value.exposedPort === null ? '' : String(value.exposedPort),
      flagSecret: '',
      flagEnvironmentVariable: value.flagEnvironmentVariable ?? 'NOCTF_FLAG_UUID',
      containerImage: value.containerImage ?? '',
      containerMode: value.containerMode === 1 || value.containerMode === 'DockerCompose' ? 'DockerCompose' : 'SingleImage',
      composeYaml: value.composeYaml ?? '',
      composeProjectName: value.composeProjectName ?? '',
      checkerImage: value.checkerConfig?.image ?? '',
      checkerCommand: value.checkerConfig?.command ?? '',
      checkerTimeoutSeconds: value.checkerConfig?.timeoutSeconds === undefined || value.checkerConfig?.timeoutSeconds === null
        ? ''
        : String(value.checkerConfig.timeoutSeconds),
      expImage: value.checkerConfig?.expImage ?? '',
      expCommand: value.checkerConfig?.expCommand ?? '',
    }
  }, { immediate: true })

  watch(selectedChallengeType, (mode) => {
    if (mode !== 'Ctf' && !containerDeploymentTypes.includes(form.value.deploymentType as typeof containerDeploymentTypes[number]))
      form.value.deploymentType = 'DynamicContainer'
    if (mode === 'Ctf' && !ctfDeploymentTypes.includes(form.value.deploymentType as typeof ctfDeploymentTypes[number]))
      form.value.deploymentType = 'StaticAttachment'
    const availableDirections = challengeDirectionsForType(mode)
    if (!availableDirections.includes(normalizeDirection(form.value.direction)))
      form.value.direction = availableDirections[0]
  })

  function buildPayload(): Record<string, unknown> {
    const runtimeEnabled = usesRuntimeContainer.value
    const expEnabled = usesExpConfig.value
    const effectiveDeploymentType = runtimeEnabled
      ? form.value.deploymentType as DeploymentTypeKey
      : form.value.attachmentUrl.trim()
        ? 'StaticAttachment'
        : 'NoAttachment'
    const hasCheckerConfig = runtimeEnabled && (
      form.value.checkerImage.trim()
      || form.value.checkerCommand.trim()
      || (expEnabled && form.value.expImage.trim())
      || (expEnabled && form.value.expCommand.trim())
      || form.value.checkerTimeoutSeconds.trim()
    )

    return {
      title: form.value.title.trim(),
      description: form.value.description.trim() || undefined,
      typeId: normalizeChallengeType(form.value.typeId),
      direction: normalizeDirection(form.value.direction),
      attachmentUrl: form.value.attachmentUrl.trim() || undefined,
      patchTemplateUrl: usesPatchTemplate.value ? form.value.patchTemplateUrl.trim() || undefined : undefined,
      deploymentType: deploymentTypeValue(effectiveDeploymentType),
      exposedPort: runtimeEnabled ? optionalPositiveNumber(form.value.exposedPort) : undefined,
      flagSecret: selectedChallengeType.value === 'Ctf' ? form.value.flagSecret.trim() || undefined : undefined,
      flagEnvironmentVariable: form.value.flagEnvironmentVariable.trim() || undefined,
      containerImage: runtimeEnabled ? form.value.containerImage.trim() || undefined : undefined,
      containerMode: form.value.containerMode === 'DockerCompose' ? 1 : 0,
      composeYaml: runtimeEnabled ? form.value.composeYaml.trim() || undefined : undefined,
      composeProjectName: runtimeEnabled ? form.value.composeProjectName.trim() || undefined : undefined,
      checkerConfig: hasCheckerConfig
        ? {
            image: form.value.checkerImage.trim() || undefined,
            command: form.value.checkerCommand.trim() || undefined,
            timeoutSeconds: optionalPositiveNumber(form.value.checkerTimeoutSeconds),
            expImage: expEnabled ? form.value.expImage.trim() || undefined : undefined,
            expCommand: expEnabled ? form.value.expCommand.trim() || undefined : undefined,
          }
        : undefined,
    }
  }

  return {
    form,
    attachmentFile,
    patchTemplateFile,
    selectedChallengeType,
    directionOptions,
    deploymentOptionKeys,
    isContainerDeployment,
    usesRuntimeContainer,
    usesExpConfig,
    usesPatchTemplate,
    isContainerImageMissing,
    canSave,
    buildPayload,
  }
}
