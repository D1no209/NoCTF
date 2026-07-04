<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Box, Loader2 } from 'lucide-vue-next'
import { Button } from '@/components/ui/button'
import { adminApi } from '@/api/noctf'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'

interface CheckerConfigDto {
  image?: string
  command?: string
  timeoutSeconds?: number | null
  expImage?: string | null
  expCommand?: string | null
}

interface ChallengeTemplateDto {
  id?: string
  title?: string
  description?: string
  typeId?: string
  containerImage?: string
  containerMode?: 'SingleImage' | 'DockerCompose' | number
  composeYaml?: string
  composeProjectName?: string
  orchestrationJson?: string
  attachmentUrl?: string
  patchTemplateUrl?: string
  flagEnvironmentVariable?: string
  deploymentType?: 'NoAttachment' | 'StaticAttachment' | 'DynamicContainer' | 'StaticContainer' | number
  exposedPort?: number | null
  checkerConfig?: CheckerConfigDto
}

const props = withDefaults(defineProps<{
  template?: ChallengeTemplateDto | null
  saving?: boolean
  submitText?: string
  cancelText?: string
}>(), {
  template: null,
  saving: false,
  submitText: '',
  cancelText: '',
})

const emit = defineEmits<{
  submit: [value: { payload: Record<string, unknown>; attachmentFile: File | null; patchTemplateFile: File | null; penetrationTopology: Record<string, unknown> | null }]
  cancel: []
}>()

const { t } = useI18n()
const attachmentFile = ref<File | null>(null)
const patchTemplateFile = ref<File | null>(null)
const topologyError = ref('')
const orchestrationError = ref('')
const topologyLoading = ref(false)
let topologyLoadId = 0

const defaultOrchestrationJson = () => JSON.stringify({
  provider: 'Docker',
  runtime: 'SingleContainer',
  kubernetes: {
    exposure: 'NodePort',
    networkMode: 'Isolated',
    imagePullPolicy: 'IfNotPresent',
    imagePullSecrets: [],
    resources: {
      cpuRequest: '50m',
      memoryRequest: '64Mi',
      cpuLimit: '500m',
      memoryLimit: '256Mi',
      ephemeralStorageLimit: '1Gi',
    },
    security: {
      allowPrivilegeEscalation: false,
      automountServiceAccountToken: false,
      runAsNonRoot: null,
      readOnlyRootFilesystem: null,
      capabilitiesDrop: ['ALL'],
      capabilitiesAdd: [],
    },
    ingress: {
      baseDomain: '',
      className: '',
      tlsSecretName: '',
      path: '/',
    },
    nodeSelector: {},
    tolerations: [],
    annotations: {},
    labels: {},
    volumes: [],
  },
}, null, 2)

const defaultForm = () => ({
  title: '',
  description: '',
  typeId: 'Ctf',
  attachmentUrl: '',
  patchTemplateUrl: '',
  deploymentType: 'StaticAttachment',
  exposedPort: undefined as number | undefined,
  flagSecret: '',
  flagEnvironmentVariable: 'NOCTF_FLAG_UUID',
  containerImage: '',
  containerMode: 'SingleImage',
  composeYaml: '',
  composeProjectName: '',
  orchestrationJson: defaultOrchestrationJson(),
  checkerImage: '',
  checkerCommand: '',
  checkerTimeoutSeconds: undefined as number | undefined,
  expImage: '',
  expCommand: '',
  penetrationTopologyJson: JSON.stringify({
    name: 'Penetration Range',
    description: '',
    entryConfig: { scheme: 'http' },
    config: {
      allowReset: true,
      instanceMode: 'team',
      maxResetCount: 3,
      visibleEntryAfterStart: true,
      instanceTtlSeconds: 7200,
      actionCooldownSeconds: 5,
    },
    nodes: [
      {
        name: 'web',
        role: 'entry',
        image: 'nginx:alpine',
        ports: [80],
        isEntry: true,
        isInternal: false,
      },
    ],
    flags: [
      {
        stage: 1,
        name: 'Initial Access',
        score: 100,
        isDynamic: true,
        nodeName: 'web',
        injectionKey: 'NOCTF_FLAG_STAGE1',
        visible: true,
      },
    ],
  }, null, 2),
})

const form = ref(defaultForm())
const deploymentTypeKeys = ['NoAttachment', 'StaticAttachment', 'DynamicContainer', 'StaticContainer'] as const
type DeploymentTypeKey = typeof deploymentTypeKeys[number]
const ctfDeploymentTypes = ['StaticAttachment', 'DynamicContainer', 'StaticContainer'] as const
const containerDeploymentTypes = ['DynamicContainer', 'StaticContainer'] as const
const challengeTypeOptions = [
  { value: 'Ctf', label: 'CTF' },
  { value: 'Awd', label: 'AWD' },
  { value: 'Awdp', label: 'AWDP' },
  { value: 'Koh', label: 'KoH' },
  { value: 'Penetration', label: 'Penetration' },
] as const
type ChallengeTypeKey = typeof challengeTypeOptions[number]['value']

function deploymentTypeKey(value?: ChallengeTemplateDto['deploymentType']) {
  const resolved = typeof value === 'number' ? deploymentTypeKeys[value] ?? 'StaticAttachment' : value ?? 'StaticAttachment'
  return resolved === 'NoAttachment' ? 'StaticAttachment' : resolved
}

function deploymentTypeValue(value: DeploymentTypeKey) {
  return deploymentTypeKeys.indexOf(value)
}

function normalizeChallengeType(value?: string | null): ChallengeTypeKey {
  const key = (value ?? 'Ctf').trim().toLowerCase()
  if (key === 'awd') return 'Awd'
  if (key === 'awdp') return 'Awdp'
  if (key === 'koh') return 'Koh'
  if (key === 'penetration') return 'Penetration'
  return 'Ctf'
}

const selectedChallengeType = computed(() => normalizeChallengeType(form.value.typeId))
const deploymentOptions = computed(() => selectedChallengeType.value === 'Ctf'
  ? ctfDeploymentTypes
  : containerDeploymentTypes)
const isContainerDeployment = computed(() =>
  containerDeploymentTypes.includes(form.value.deploymentType as typeof containerDeploymentTypes[number]))
const usesRuntimeContainer = computed(() =>
  selectedChallengeType.value !== 'Ctf' || isContainerDeployment.value)
const usesExpConfig = computed(() => selectedChallengeType.value === 'Awd')
const usesPatchTemplate = computed(() => selectedChallengeType.value === 'Awdp')
const usesPenetrationTopology = computed(() => selectedChallengeType.value === 'Penetration')
const checkerImageLabel = computed(() => selectedChallengeType.value === 'Awdp'
  ? t('admin.challenges.awdpCheckImage')
  : t('admin.challenges.checkerImage'))
const checkerCommandLabel = computed(() => selectedChallengeType.value === 'Awdp'
  ? t('admin.challenges.awdpCheckCommand')
  : t('admin.challenges.checkerCommand'))
const checkerTimeoutLabel = computed(() => selectedChallengeType.value === 'Awdp'
  ? t('admin.challenges.awdpCheckTimeout')
  : t('admin.challenges.checkerTimeout'))
const orchestrationImage = computed(() => {
  try {
    const parsed = JSON.parse(form.value.orchestrationJson || '{}') as { image?: unknown }
    return typeof parsed.image === 'string' ? parsed.image.trim() : ''
  } catch {
    return ''
  }
})
const isContainerImageMissing = computed(() => usesRuntimeContainer.value && !usesPenetrationTopology.value && !form.value.containerImage.trim() && !orchestrationImage.value)
const canSave = computed(() => Boolean(form.value.title.trim()) && !isContainerImageMissing.value && !topologyLoading.value)

watch(() => props.template, (template) => {
  attachmentFile.value = null
  patchTemplateFile.value = null
  topologyError.value = ''
  orchestrationError.value = ''
  topologyLoadId += 1
  topologyLoading.value = false
  if (!template) {
    form.value = defaultForm()
    return
  }

  const defaults = defaultForm()
  form.value = {
    title: template.title ?? '',
    description: template.description ?? '',
    typeId: normalizeChallengeType(template.typeId),
    attachmentUrl: template.attachmentUrl ?? '',
    patchTemplateUrl: template.patchTemplateUrl ?? '',
    deploymentType: deploymentTypeKey(template.deploymentType),
    exposedPort: template.exposedPort ?? undefined,
    flagSecret: '',
    flagEnvironmentVariable: template.flagEnvironmentVariable ?? 'NOCTF_FLAG_UUID',
    containerImage: template.containerImage ?? '',
    containerMode: template.containerMode === 1 || template.containerMode === 'DockerCompose' ? 'DockerCompose' : 'SingleImage',
    composeYaml: template.composeYaml ?? '',
    composeProjectName: template.composeProjectName ?? '',
    orchestrationJson: template.orchestrationJson || defaultOrchestrationJson(),
    checkerImage: template.checkerConfig?.image ?? '',
    checkerCommand: template.checkerConfig?.command ?? '',
    checkerTimeoutSeconds: template.checkerConfig?.timeoutSeconds ?? undefined,
    expImage: template.checkerConfig?.expImage ?? '',
    expCommand: template.checkerConfig?.expCommand ?? '',
    penetrationTopologyJson: defaults.penetrationTopologyJson,
  }
  if (normalizeChallengeType(template.typeId) === 'Penetration' && template.id) {
    void loadPenetrationTopology(template.id)
  }
}, { immediate: true })

watch(selectedChallengeType, (mode) => {
  if (mode !== 'Ctf' && !containerDeploymentTypes.includes(form.value.deploymentType as typeof containerDeploymentTypes[number])) {
    form.value.deploymentType = 'DynamicContainer'
  }
  if (mode === 'Penetration') {
    form.value.deploymentType = 'DynamicContainer'
    form.value.containerMode = 'DockerCompose'
  }
  if (mode === 'Ctf' && !ctfDeploymentTypes.includes(form.value.deploymentType as typeof ctfDeploymentTypes[number])) {
    form.value.deploymentType = 'StaticAttachment'
  }
})

function optionalPositiveNumber(value: number | undefined) {
  return value ? Number(value) : undefined
}

function buildPayload() {
  const runtimeEnabled = usesRuntimeContainer.value
  const expEnabled = usesExpConfig.value
  const effectiveDeploymentType = runtimeEnabled
    ? form.value.deploymentType as DeploymentTypeKey
    : form.value.attachmentUrl.trim()
      ? 'StaticAttachment'
      : 'NoAttachment'
  const hasCheckerConfig = runtimeEnabled && (
    form.value.checkerImage.trim() ||
    form.value.checkerCommand.trim() ||
    (expEnabled && form.value.expImage.trim()) ||
    (expEnabled && form.value.expCommand.trim()) ||
    form.value.checkerTimeoutSeconds
  )

  return {
    title: form.value.title.trim(),
    description: form.value.description.trim() || undefined,
    typeId: normalizeChallengeType(form.value.typeId),
    attachmentUrl: form.value.attachmentUrl.trim() || undefined,
    patchTemplateUrl: usesPatchTemplate.value ? form.value.patchTemplateUrl.trim() || undefined : undefined,
    deploymentType: deploymentTypeValue(usesPenetrationTopology.value ? 'DynamicContainer' : effectiveDeploymentType),
    exposedPort: runtimeEnabled ? optionalPositiveNumber(form.value.exposedPort) : undefined,
    flagSecret: selectedChallengeType.value === 'Ctf' ? form.value.flagSecret.trim() || undefined : undefined,
    flagEnvironmentVariable: form.value.flagEnvironmentVariable.trim() || undefined,
    containerImage: runtimeEnabled && !usesPenetrationTopology.value ? form.value.containerImage.trim() || undefined : undefined,
    containerMode: usesPenetrationTopology.value ? 1 : form.value.containerMode === 'DockerCompose' ? 1 : 0,
    composeYaml: runtimeEnabled ? form.value.composeYaml.trim() || undefined : undefined,
    composeProjectName: runtimeEnabled ? form.value.composeProjectName.trim() || undefined : undefined,
    orchestrationJson: runtimeEnabled || usesPenetrationTopology.value ? form.value.orchestrationJson.trim() || undefined : undefined,
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

function parsePenetrationTopology() {
  if (!usesPenetrationTopology.value) return null
  topologyError.value = ''
  try {
    return JSON.parse(form.value.penetrationTopologyJson) as Record<string, unknown>
  } catch {
    topologyError.value = t('penetration.invalidTopology')
    return null
  }
}

async function loadPenetrationTopology(templateId: string) {
  const loadId = ++topologyLoadId
  topologyLoading.value = true
  try {
    const topology = await adminApi.penetrationTemplateTopology<Record<string, unknown>>(templateId)
    if (loadId !== topologyLoadId) return
    if ((topology as { code?: string }).code !== 'not_configured') {
      form.value.penetrationTopologyJson = JSON.stringify(topology, null, 2)
    }
  } catch {
    if (loadId === topologyLoadId)
      topologyError.value = t('penetration.invalidTopology')
  } finally {
    if (loadId === topologyLoadId)
      topologyLoading.value = false
  }
}

function submit() {
  if (!canSave.value || props.saving) return
  orchestrationError.value = ''
  if (usesRuntimeContainer.value || usesPenetrationTopology.value) {
    try {
      const parsed = JSON.parse(form.value.orchestrationJson || '{}')
      if (!parsed || Array.isArray(parsed) || typeof parsed !== 'object')
        throw new Error('invalid')
    } catch {
      orchestrationError.value = t('admin.challenges.invalidOrchestration')
      return
    }
  }
  const penetrationTopology = parsePenetrationTopology()
  if (usesPenetrationTopology.value && !penetrationTopology) return
  emit('submit', {
    payload: buildPayload(),
    attachmentFile: attachmentFile.value,
    patchTemplateFile: patchTemplateFile.value,
    penetrationTopology,
  })
}
</script>

<template>
  <form class="grid gap-5" @submit.prevent="submit">
    <div class="grid gap-2">
      <Label>{{ t('admin.challenges.titleColumn') }}</Label>
      <Input v-model="form.title" />
    </div>
    <div class="grid gap-2">
      <Label>{{ t('admin.challenges.description') }}</Label>
      <Textarea v-model="form.description" rows="4" />
    </div>

    <div class="grid gap-4 sm:grid-cols-2">
      <div class="grid gap-2">
        <Label>{{ t('admin.challenges.challengeMode') }}</Label>
        <Select v-model="form.typeId">
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in challengeTypeOptions" :key="option.value" :value="option.value">
              {{ option.label }}
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div class="grid gap-2">
        <Label>{{ t('admin.challenges.attachmentUrl') }}</Label>
        <Input v-model="form.attachmentUrl" placeholder="/api/files/challenge.zip" />
      </div>
      <div class="grid gap-2 sm:col-span-2">
        <Label>{{ t('admin.challenges.attachmentUpload') }}</Label>
        <Input type="file" @change="attachmentFile = ($event.target as HTMLInputElement).files?.[0] ?? null" />
        <p class="text-xs text-muted-foreground">{{ t('admin.challenges.attachmentEmptyHint') }}</p>
      </div>
      <div v-if="usesPatchTemplate" class="grid gap-2">
        <Label>{{ t('admin.challenges.patchTemplateUrl') }}</Label>
        <Input v-model="form.patchTemplateUrl" placeholder="/api/files/patch-template.zip" />
      </div>
      <div v-if="usesPatchTemplate" class="grid gap-2">
        <Label>{{ t('admin.challenges.patchTemplateUpload') }}</Label>
        <Input type="file" accept=".zip,.tar.gz,.tgz" @change="patchTemplateFile = ($event.target as HTMLInputElement).files?.[0] ?? null" />
        <p class="text-xs text-muted-foreground">{{ t('admin.challenges.patchTemplateHint') }}</p>
      </div>
    </div>

    <div class="grid gap-4 sm:grid-cols-2">
      <div class="grid gap-2">
        <Label>{{ t('admin.challenges.deploymentType') }}</Label>
        <Select v-model="form.deploymentType">
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="deployment in deploymentOptions" :key="deployment" :value="deployment">
              {{ t(`admin.challenges.deploymentTypes.${deployment}`) }}
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div v-if="usesRuntimeContainer && !usesPenetrationTopology" class="grid gap-2">
        <Label>{{ t('admin.challenges.exposedPort') }}</Label>
        <Input v-model.number="form.exposedPort" type="number" min="1" max="65535" :placeholder="t('admin.challenges.exposedPortPlaceholder')" />
      </div>
    </div>

    <div v-if="usesPenetrationTopology" class="space-y-3 rounded-lg border bg-muted/20 p-4">
      <div>
        <div class="text-sm font-semibold">{{ t('penetration.topology') }}</div>
        <p class="text-xs text-muted-foreground">{{ t('penetration.topologyHint') }}</p>
      </div>
      <Textarea v-model="form.penetrationTopologyJson" class="min-h-80 font-mono text-xs" />
      <p v-if="topologyError" class="text-xs text-destructive">{{ topologyError }}</p>
      <p v-else-if="topologyLoading" class="text-xs text-muted-foreground">{{ t('common.loading') }}</p>
    </div>

    <div v-if="!usesPenetrationTopology" class="grid gap-4 sm:grid-cols-2">
      <div class="grid gap-2">
        <Label>{{ t('admin.challenges.flagSecret') }}</Label>
        <Input
          v-model="form.flagSecret"
          :disabled="selectedChallengeType !== 'Ctf'"
          :placeholder="selectedChallengeType === 'Ctf' ? t('admin.challenges.flagSecretPlaceholder') : t('admin.challenges.dynamicFlagManagedByCompetition')"
        />
      </div>
      <div class="grid gap-2">
        <Label>{{ t('admin.challenges.flagEnvironmentVariable') }}</Label>
        <Input v-model="form.flagEnvironmentVariable" placeholder="NOCTF_FLAG_UUID" />
      </div>
    </div>

    <div v-if="usesRuntimeContainer && !usesPenetrationTopology" class="space-y-4 rounded-lg border bg-muted/30 p-4">
      <div class="flex items-center gap-2 text-sm font-bold uppercase tracking-wider">
        <Box class="size-4" />
        {{ t('admin.challenges.runtimeContainers') }}
      </div>
      <div class="grid gap-4 sm:grid-cols-2">
        <div class="grid gap-2">
          <Label>{{ t('admin.challenges.challengeContainerMode') }}</Label>
          <Select v-model="form.containerMode">
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="SingleImage">{{ t('admin.challenges.singleImage') }}</SelectItem>
              <SelectItem value="DockerCompose">{{ t('admin.challenges.dockerCompose') }}</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div class="grid gap-2">
          <Label>
            {{ t('admin.challenges.challengeImage') }}
            <span class="text-destructive">*</span>
          </Label>
          <Input v-model="form.containerImage" required :aria-invalid="isContainerImageMissing" />
          <p v-if="isContainerImageMissing" class="text-xs text-destructive">
            {{ t('admin.challenges.challengeImageRequired') }}
          </p>
        </div>
        <div v-if="form.containerMode === 'DockerCompose'" class="grid gap-2">
          <Label>{{ t('admin.challenges.composeProjectName') }}</Label>
          <Input v-model="form.composeProjectName" />
        </div>
      </div>
      <div v-if="form.containerMode === 'DockerCompose'" class="grid gap-2">
        <Label>{{ t('admin.challenges.composeYaml') }}</Label>
        <Textarea v-model="form.composeYaml" class="font-mono text-xs" rows="7" />
      </div>
      <div class="grid gap-4 sm:grid-cols-2">
        <div class="grid gap-2">
          <Label>{{ checkerImageLabel }}</Label>
          <Input v-model="form.checkerImage" :placeholder="t('admin.challenges.optional')" />
        </div>
        <div class="grid gap-2">
          <Label>{{ checkerCommandLabel }}</Label>
          <Input v-model="form.checkerCommand" :placeholder="t('admin.challenges.optional')" />
        </div>
        <div class="grid gap-2">
          <Label>{{ checkerTimeoutLabel }}</Label>
          <Input v-model.number="form.checkerTimeoutSeconds" type="number" min="1" :placeholder="t('admin.challenges.optional')" />
        </div>
        <div v-if="usesExpConfig" class="grid gap-2">
          <Label>{{ t('admin.challenges.expImage') }}</Label>
          <Input v-model="form.expImage" :placeholder="t('admin.challenges.optional')" />
        </div>
        <div v-if="usesExpConfig" class="grid gap-2 sm:col-span-2">
          <Label>{{ t('admin.challenges.expCommand') }}</Label>
          <Input v-model="form.expCommand" :placeholder="t('admin.challenges.optional')" />
        </div>
      </div>
    </div>

    <div v-if="usesRuntimeContainer || usesPenetrationTopology" class="space-y-3 rounded-lg border bg-muted/20 p-4">
      <div>
        <div class="text-sm font-semibold">{{ t('admin.challenges.orchestration') }}</div>
        <p class="text-xs text-muted-foreground">{{ t('admin.challenges.orchestrationHint') }}</p>
      </div>
      <Textarea v-model="form.orchestrationJson" class="min-h-72 font-mono text-xs" />
      <p v-if="orchestrationError" class="text-xs text-destructive">{{ orchestrationError }}</p>
    </div>

    <div class="flex flex-col-reverse gap-2 border-t pt-5 sm:flex-row sm:justify-end">
      <Button type="button" variant="outline" :disabled="saving" @click="emit('cancel')">
        {{ cancelText || t('common.cancel') }}
      </Button>
      <Button type="submit" :disabled="saving || !canSave">
        <Loader2 v-if="saving" class="mr-2 size-4 animate-spin" />
        {{ submitText || t('common.save') }}
      </Button>
    </div>
  </form>
</template>
