<script setup lang="ts">
import { ref, computed, onUnmounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError, competitionApi } from '@/api/noctf'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { Alert } from '@/components/ui/alert'
import { renderMarkdown } from '@/lib/markdown'
import { toast } from 'vue-sonner'
import {
  Activity,
  CheckCircle2,
  Copy,
  Crosshair,
  Download,
  ExternalLink,
  FileArchive,
  Loader2,
  Shield,
  ShieldCheck,
  Server,
  Timer,
  Trash2,
  Upload,
} from 'lucide-vue-next'
import PenetrationChallengePanel from './PenetrationChallengePanel.vue'

interface Challenge {
  id: string
  title: string
  typeId: string
  points: number
  solveCount: number
  deploymentType?: string | null
  description?: string | null
  descriptionFormat?: string | null
  hints?: string[]
  attachmentUrl?: string | null
  patchTemplateUrl?: string | null
}

interface SubmitResponse {
  correct: boolean
  alreadySolved?: boolean
  result?: string
  message?: string | null
}

interface PatchSubmissionStatus {
  id?: string
  challengeId: string
  status: string | number
  fixStatus?: string | number
  attemptNumber?: number
  fileName?: string
  fixEntry?: string
  submittedAt?: string
  validatedAt?: string | null
  validationDetail?: string | null
}

interface AwdpRoundState {
  roundNumber: number
  status: string
  startTime: string
  endTime?: string | null
}

interface AwdpChallengeState {
  challengeId: string
  instanceStatus: string
  breakStatus: string
  fixStatus: string
  serviceStatus: string
  currentRoundAttackScore: number
  currentRoundDefenseScore: number
  attackScorePerRound: number
  defenseScorePerRound: number
  attackAttempts: number
  defenseAttempts: number
  maxAttackAttempts: number
  maxDefenseAttempts: number
  remainingAttackAttempts: number
  remainingDefenseAttempts: number
  canSubmitFlag: boolean
  canRequestDefense: boolean
  allowAttackAfterBreakSuccess: boolean
  allowDefenseAfterFixSuccess: boolean
  fixEntry: string
  lastValidationDetail?: string | null
  cooldownUntil?: string | null
}

interface InstanceResponse {
  containerId?: string | null
  ports?: Record<string, number>
  addresses?: string[]
  address?: string | null
  entryUrl?: string | null
  accessHost?: string
  status?: string
  expiresAt?: string | null
  cooldownUntil?: string | null
  serverTime?: string
}

const props = defineProps<{
  open: boolean
  challenge: Challenge | null
  competitionId: string
  solved: boolean
  gameModeType?: string
  isAwdMode?: boolean
  isAwdpMode?: boolean
  instanceReady?: boolean
  defenseEnabled?: boolean
  patchSubmissions?: PatchSubmissionStatus[]
  awdpState?: AwdpChallengeState | null
  awdpCurrentRound?: AwdpRoundState | null
  canCreateInstance?: boolean
  canSubmitFlag?: boolean
  canRequestDefense?: boolean
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  'create-instance': []
  'request-defense': []
  'patch-uploaded': []
  solved: []
}>()

const { t } = useI18n()

const flagInput = ref('')
const submitting = ref(false)
const submitResult = ref<'correct' | 'incorrect' | null>(null)
const submitError = ref<string | null>(null)
const patchFile = ref<File | null>(null)
const patchUploading = ref(false)
const instanceCreating = ref(false)
const instanceDestroying = ref(false)
const instanceExtending = ref(false)
const instanceLoading = ref(false)
const instance = ref<InstanceResponse | null>(null)
const instanceError = ref<string | null>(null)
const nowMs = ref(Date.now())
let clockTimer: ReturnType<typeof setInterval> | null = null
let pollTimer: ReturnType<typeof setInterval> | null = null

const isOpen = computed({
  get: () => props.open,
  set: (v) => emit('update:open', v),
})

const renderedDescription = computed(() => {
  if (!props.challenge?.description) return ''
  return renderMarkdown(props.challenge.description)
})

const visibleHints = computed(() => props.challenge?.hints?.filter(Boolean) ?? [])
const patchStatuses = computed(() => props.patchSubmissions ?? [])
const isDynamicContainer = computed(
  () => props.challenge?.deploymentType?.toLowerCase() === 'dynamiccontainer',
)
const isPenetrationChallenge = computed(
  () => props.challenge?.typeId?.toLowerCase() === 'penetration',
)
const canCreateDynamicInstance = computed(() =>
  Boolean(isDynamicContainer.value && props.canCreateInstance !== false),
)
const canSubmitCurrentFlag = computed(() => props.canSubmitFlag !== false)
const canRequestCurrentDefense = computed(() => props.canRequestDefense !== false)
const runningInstance = computed(
  () => instance.value?.status === 'running' && Boolean(instance.value?.containerId),
)
const instanceAddress = computed(
  () => instance.value?.entryUrl ?? instance.value?.address ?? instance.value?.addresses?.[0] ?? '',
)
const isWebChallenge = computed(
  () => props.challenge?.typeId?.toLowerCase().includes('web') ?? false,
)
const instanceAccess = computed(() => {
  const rawAddress = instanceAddress.value.trim()
  if (!rawAddress) return { text: '', href: '', isWeb: false }

  if (isWebChallenge.value) {
    const href = isHttpUrl(rawAddress) ? rawAddress : `http://${rawAddress}`
    return { text: href, href, isWeb: true }
  }

  const endpoint = parseHostPort(rawAddress)
  const text = endpoint
    ? `nc ${endpoint.host} ${endpoint.port}`
    : `nc ${stripAddressScheme(rawAddress)}`
  return { text, href: '', isWeb: false }
})
const expiresAtMs = computed(() =>
  instance.value?.expiresAt ? new Date(instance.value.expiresAt).getTime() : null,
)
const cooldownUntilMs = computed(() =>
  instance.value?.cooldownUntil ? new Date(instance.value.cooldownUntil).getTime() : null,
)
const expiresInMs = computed(() =>
  expiresAtMs.value ? Math.max(0, expiresAtMs.value - nowMs.value) : 0,
)
const cooldownMs = computed(() =>
  cooldownUntilMs.value ? Math.max(0, cooldownUntilMs.value - nowMs.value) : 0,
)
const isCoolingDown = computed(() => cooldownMs.value > 0)
const canOperateInstance = computed(() =>
  Boolean(canCreateDynamicInstance.value && !isCoolingDown.value),
)
const hasInstanceOperation = computed(
  () =>
    instanceCreating.value ||
    instanceDestroying.value ||
    instanceExtending.value ||
    instanceLoading.value,
)
const canUploadPatch = computed(() =>
  Boolean(
    props.isAwdMode &&
    props.defenseEnabled &&
    patchFile.value &&
    props.challenge &&
    canRequestCurrentDefense.value,
  ),
)
const awdpState = computed(() => props.awdpState ?? null)
const awdpInstanceRunning = computed(() => {
  if (runningInstance.value) return true
  return awdpState.value?.instanceStatus === 'InstanceRunning'
})
const awdpRemainingAttackAttempts = computed(() => awdpState.value?.remainingAttackAttempts ?? 0)
const awdpRemainingDefenseAttempts = computed(() => awdpState.value?.remainingDefenseAttempts ?? 0)
const awdpAttackAttemptsLabel = computed(
  () => `${awdpRemainingAttackAttempts.value} / ${awdpState.value?.maxAttackAttempts ?? 0}`,
)
const awdpDefenseAttemptsLabel = computed(
  () => `${awdpRemainingDefenseAttempts.value} / ${awdpState.value?.maxDefenseAttempts ?? 0}`,
)
const awdpCanSubmitFlag = computed(() =>
  Boolean(
    props.challenge &&
    awdpInstanceRunning.value &&
    canSubmitCurrentFlag.value &&
    (awdpState.value?.canSubmitFlag ?? true),
  ),
)
const awdpCanRequestDefense = computed(() =>
  Boolean(
    props.challenge &&
    awdpInstanceRunning.value &&
    canRequestCurrentDefense.value &&
    (awdpState.value?.canRequestDefense ?? true),
  ),
)
const canUploadAwdpFix = computed(() =>
  Boolean(props.isAwdpMode && props.challenge && patchFile.value && awdpCanRequestDefense.value),
)
const awdpBlockedMessage = computed(() => {
  if (!awdpInstanceRunning.value) return t('awdp.instanceRequired')
  if (awdpState.value?.breakStatus === 'AttackAttemptsExhausted')
    return t('awdp.attackAttemptsExhausted')
  if (awdpState.value?.fixStatus === 'DefenseAttemptsExhausted')
    return t('awdp.defenseAttemptsExhausted')
  if (
    awdpState.value?.breakStatus === 'BreakSuccess' &&
    awdpState.value.allowAttackAfterBreakSuccess === false
  )
    return t('awdp.breakLocked')
  if (
    awdpState.value?.fixStatus === 'FixSuccess' &&
    awdpState.value.allowDefenseAfterFixSuccess === false
  )
    return t('awdp.fixLocked')
  return ''
})

function onOpenChange(v: boolean) {
  if (!v) {
    flagInput.value = ''
    submitResult.value = null
    submitError.value = null
    patchFile.value = null
    instanceError.value = null
    stopInstancePolling()
    stopClock()
  }
  emit('update:open', v)
}

watch(
  () =>
    [props.open, props.challenge?.id, isDynamicContainer.value, props.canCreateInstance] as const,
  ([open]) => {
    if (
      open &&
      isDynamicContainer.value &&
      !isPenetrationChallenge.value &&
      props.challenge &&
      props.canCreateInstance !== false
    ) {
      startClock()
      void refreshInstance()
      startInstancePolling()
    } else {
      stopInstancePolling()
      stopClock()
    }
  },
  { immediate: true },
)

onUnmounted(() => {
  stopInstancePolling()
  stopClock()
})

async function createInstance() {
  if (!props.challenge || !canOperateInstance.value) return
  instanceCreating.value = true
  instanceError.value = null
  try {
    const data = await competitionApi.createInstance<InstanceResponse>(
      props.competitionId,
      props.challenge.id,
    )
    instance.value = data
    toast.success(t('challenges.instanceReady'))
    emit('create-instance')
  } catch (error) {
    const detail = getApiErrorDetail(error)
    instanceError.value = detail
      ? `${t('challenges.instanceFailed')}: ${detail}`
      : t('challenges.instanceFailed')
    toast.error(t('challenges.instanceFailed'))
  } finally {
    instanceCreating.value = false
  }
}

async function refreshInstance() {
  if (
    !props.challenge ||
    !isDynamicContainer.value ||
    isPenetrationChallenge.value ||
    props.canCreateInstance === false
  )
    return
  instanceLoading.value = true
  try {
    instance.value = await competitionApi.getInstance<InstanceResponse>(
      props.competitionId,
      props.challenge.id,
    )
  } catch {
    instance.value = null
  } finally {
    instanceLoading.value = false
  }
}

async function destroyInstance() {
  if (!props.challenge || !runningInstance.value || !canOperateInstance.value) return
  instanceDestroying.value = true
  instanceError.value = null
  try {
    instance.value = await competitionApi.destroyInstance<InstanceResponse>(
      props.competitionId,
      props.challenge.id,
    )
    toast.success(t('challenges.instanceDestroyed'))
  } catch (error) {
    const detail = getApiErrorDetail(error)
    instanceError.value = detail
      ? `${t('challenges.instanceDestroyFailed')}: ${detail}`
      : t('challenges.instanceDestroyFailed')
    toast.error(t('challenges.instanceDestroyFailed'))
  } finally {
    instanceDestroying.value = false
  }
}

async function extendInstance() {
  if (!props.challenge || !runningInstance.value || !canOperateInstance.value) return
  instanceExtending.value = true
  instanceError.value = null
  try {
    instance.value = await competitionApi.extendInstance<InstanceResponse>(
      props.competitionId,
      props.challenge.id,
    )
    toast.success(t('challenges.instanceExtended'))
  } catch (error) {
    const detail = getApiErrorDetail(error)
    instanceError.value = detail
      ? `${t('challenges.instanceExtendFailed')}: ${detail}`
      : t('challenges.instanceExtendFailed')
    toast.error(t('challenges.instanceExtendFailed'))
  } finally {
    instanceExtending.value = false
  }
}

async function copyInstanceAccess() {
  if (!instanceAccess.value.text) return
  await navigator.clipboard.writeText(instanceAccess.value.text)
  toast.success(t('challenges.addressCopied'))
}

function onPatchFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  patchFile.value = input.files?.[0] ?? null
}

async function submitFlag() {
  if (!props.challenge || !canSubmitCurrentFlag.value || !flagInput.value.trim()) return
  submitting.value = true
  submitResult.value = null
  submitError.value = null

  try {
    const data = await competitionApi.submitFlag<SubmitResponse>(
      props.competitionId,
      props.challenge.id,
      flagInput.value.trim(),
    )

    if (data?.correct) {
      submitResult.value = 'correct'
      emit('solved')
    } else {
      if (data?.result === 'attempts_exhausted') {
        submitResult.value = null
        submitError.value = t('awdp.attackAttemptsExhausted')
      } else if (data?.result === 'instance_required') {
        submitResult.value = null
        submitError.value = t('awdp.instanceRequired')
      } else if (data?.result === 'instance_expired') {
        submitResult.value = null
        submitError.value = t('awdp.instanceExpired')
      } else {
        submitResult.value = 'incorrect'
      }
    }
  } catch {
    submitError.value = t('challenges.submissionFailed')
  } finally {
    submitting.value = false
  }
}

async function submitPatch() {
  if (!props.challenge || !patchFile.value) return
  if (props.isAwdpMode && !awdpCanRequestDefense.value) return
  if (!props.isAwdpMode && (!props.defenseEnabled || !canRequestCurrentDefense.value)) return
  patchUploading.value = true

  try {
    await competitionApi.submitPatch(props.competitionId, props.challenge.id, patchFile.value)
    toast.success(t('awd.patchSubmitted'))
    patchFile.value = null
    emit('patch-uploaded')
  } catch {
    toast.error(t('awd.patchUploadFailed'))
  } finally {
    patchUploading.value = false
  }
}

function formatDate(value?: string | null) {
  if (!value) return ''
  return new Date(value).toLocaleString()
}

function isHttpUrl(value?: string | null) {
  return Boolean(value && /^https?:\/\//i.test(value))
}

function stripAddressScheme(value: string) {
  return value.replace(/^[a-z][a-z\d+.-]*:\/\//i, '')
}

function parseHostPort(value: string) {
  const trimmed = value.trim()
  const normalized = /^[a-z][a-z\d+.-]*:\/\//i.test(trimmed) ? trimmed : `tcp://${trimmed}`

  try {
    const url = new URL(normalized)
    if (url.hostname && url.port) return { host: url.hostname, port: url.port }
  } catch {
    // Fall back to simple host:port parsing below.
  }

  const bracketedIpv6 = trimmed.match(/^\[([^\]]+)\]:(\d+)$/)
  if (bracketedIpv6) return { host: bracketedIpv6[1], port: bracketedIpv6[2] }

  const hostPort = stripAddressScheme(trimmed).match(/^([^:/\s]+):(\d+)$/)
  return hostPort ? { host: hostPort[1], port: hostPort[2] } : null
}

function startClock() {
  if (clockTimer) return
  nowMs.value = Date.now()
  clockTimer = setInterval(() => {
    nowMs.value = Date.now()
    if (runningInstance.value && expiresInMs.value <= 0) void refreshInstance()
  }, 1000)
}

function stopClock() {
  if (!clockTimer) return
  clearInterval(clockTimer)
  clockTimer = null
}

function startInstancePolling() {
  if (pollTimer) return
  pollTimer = setInterval(() => {
    if (props.open && isDynamicContainer.value && !isPenetrationChallenge.value)
      void refreshInstance()
  }, 10_000)
}

function stopInstancePolling() {
  if (!pollTimer) return
  clearInterval(pollTimer)
  pollTimer = null
}

function formatDuration(ms: number) {
  const totalSeconds = Math.max(0, Math.ceil(ms / 1000))
  const hours = Math.floor(totalSeconds / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60
  if (hours > 0)
    return `${hours}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
  return `${minutes}:${String(seconds).padStart(2, '0')}`
}

function formatStatus(value?: string | number | null) {
  if (value === null || value === undefined || value === '') return t('common.none')
  const key = String(value)
  const translated = t(`awdp.status.${key}`)
  if (translated !== `awdp.status.${key}`) return translated
  return key.replace(/([a-z])([A-Z])/g, '$1 $2')
}

function formatDefenseResult(value?: string | null) {
  if (!value) return ''
  const translated = t(`awdp.results.${value}`)
  if (translated !== `awdp.results.${value}`) return translated
  return value
}

function statusBadgeVariant(
  value?: string | number | null,
): 'default' | 'secondary' | 'destructive' | 'outline' {
  const key = String(value ?? '').toLowerCase()
  if (key.includes('success') || key.includes('ok') || key.includes('running')) return 'default'
  if (
    key.includes('error') ||
    key.includes('failed') ||
    key.includes('timeout') ||
    key.includes('exhausted')
  )
    return 'destructive'
  if (key.includes('notstarted') || key.includes('unknown') || key.includes('notcreated'))
    return 'outline'
  return 'secondary'
}

function getApiErrorDetail(error: unknown) {
  if (error instanceof ApiError) {
    if (typeof error.details === 'string') return error.details
    if (error.details && typeof error.details === 'object') return JSON.stringify(error.details)
    if (error.status) return `HTTP ${error.status}`
  }
  return error instanceof Error ? error.message : ''
}
</script>

<template>
  <Dialog :open="isOpen" @update:open="onOpenChange">
    <DialogContent class="noctf-scrollbar max-h-[90vh] overflow-y-auto sm:max-w-2xl">
      <DialogHeader>
        <div class="flex items-center gap-2">
          <DialogTitle>{{ challenge?.title }}</DialogTitle>
          <Badge v-if="solved" class="bg-green-600 text-white border-transparent shrink-0">
            {{ t('challenges.solved') }}
          </Badge>
        </div>
        <div class="mt-1 flex items-center gap-2">
          <span class="text-xs text-muted-foreground">{{ challenge?.typeId }}</span>
          <span class="text-xs text-muted-foreground">·</span>
          <span class="text-xs font-medium">{{ challenge?.points }} {{ $t('nav.score') }}</span>
          <span class="text-xs text-muted-foreground">·</span>
          <span class="text-xs text-muted-foreground"
            >{{ challenge?.solveCount }}
            {{ t('challenges.solves', { count: challenge?.solveCount ?? 0 }) }}</span
          >
        </div>
      </DialogHeader>

      <div class="space-y-4 py-2">
        <div
          v-if="renderedDescription"
          class="challenge-markdown text-sm text-foreground"
          v-html="renderedDescription"
        />
        <p v-else class="text-sm text-muted-foreground italic">
          {{ t('challenges.noDescription') }}
        </p>

        <PenetrationChallengePanel
          v-if="isPenetrationChallenge && challenge"
          :competition-id="competitionId"
          :challenge="challenge"
          :can-submit-flag="canSubmitCurrentFlag"
          @solved="emit('solved')"
        />

        <div
          v-if="!isPenetrationChallenge && ((isDynamicContainer && !isAwdpMode) || isAwdMode)"
          class="grid gap-3 rounded-xl border bg-muted/25 p-3 sm:grid-cols-2"
        >
          <div v-if="isDynamicContainer" class="space-y-3 sm:col-span-2">
            <div v-if="runningInstance" class="rounded-lg border bg-background/80 p-3">
              <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <div class="min-w-0 space-y-1">
                  <div class="flex flex-wrap items-center gap-2 text-sm font-semibold">
                    <Server class="size-4 text-primary" />
                    <span>{{ t('challenges.instanceReady') }}</span>
                    <Badge variant="secondary" class="font-mono">{{
                      formatDuration(expiresInMs)
                    }}</Badge>
                  </div>
                </div>
                <div class="flex flex-wrap gap-2">
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    :disabled="hasInstanceOperation || !canOperateInstance"
                    @click="extendInstance"
                  >
                    <Loader2 v-if="instanceExtending" class="size-4 animate-spin" />
                    <Timer v-else class="size-4" />
                    {{
                      isCoolingDown
                        ? t('challenges.cooldownSeconds', { seconds: Math.ceil(cooldownMs / 1000) })
                        : t('challenges.extendInstance')
                    }}
                  </Button>
                  <Button
                    type="button"
                    variant="destructive"
                    size="sm"
                    :disabled="hasInstanceOperation || !canOperateInstance"
                    @click="destroyInstance"
                  >
                    <Loader2 v-if="instanceDestroying" class="size-4 animate-spin" />
                    <Trash2 v-else class="size-4" />
                    {{ t('challenges.destroyInstance') }}
                  </Button>
                </div>
              </div>
            </div>
            <Button
              v-else
              type="button"
              variant="outline"
              class="justify-start"
              :disabled="hasInstanceOperation || !canCreateDynamicInstance || isCoolingDown"
              @click="createInstance"
            >
              <Loader2
                v-if="instanceCreating || instanceLoading"
                class="mr-2 size-4 animate-spin"
              />
              <Server v-else class="mr-2 size-4" />
              {{
                isCoolingDown
                  ? t('challenges.cooldownSeconds', { seconds: Math.ceil(cooldownMs / 1000) })
                  : t('challenges.createInstance')
              }}
            </Button>
          </div>
          <Button
            v-if="isAwdMode"
            type="button"
            variant="outline"
            class="justify-start"
            :disabled="defenseEnabled || !canRequestCurrentDefense"
            @click="emit('request-defense')"
          >
            <Shield class="mr-2 size-4" />
            {{ defenseEnabled ? t('challenges.defenseReady') : t('challenges.requestDefense') }}
          </Button>
        </div>
        <Alert
          v-if="
            !isPenetrationChallenge &&
            ((isDynamicContainer && !isAwdpMode) || isAwdMode) &&
            !canSubmitCurrentFlag
          "
        >
          {{ t('challenges.participantActionRequiresTeam') }}
        </Alert>
        <div
          v-if="runningInstance && instanceAccess.text"
          class="flex items-center justify-between gap-3 rounded-md border bg-muted/30 px-3 py-2 text-xs"
        >
          <code class="min-w-0 flex-1 truncate font-mono text-foreground">{{
            instanceAccess.text
          }}</code>
          <div class="flex shrink-0 items-center gap-1">
            <Button
              v-if="instanceAccess.isWeb"
              type="button"
              variant="ghost"
              size="icon-sm"
              :title="t('challenges.openAddress')"
              as-child
            >
              <a :href="instanceAccess.href" target="_blank" rel="noopener noreferrer">
                <ExternalLink class="size-4" />
              </a>
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="icon-sm"
              :title="t('challenges.copyAddress')"
              @click="copyInstanceAccess"
            >
              <Copy class="size-4" />
            </Button>
          </div>
        </div>
        <Alert v-if="instanceError" variant="destructive">
          {{ instanceError }}
        </Alert>

        <div
          v-if="!isPenetrationChallenge && isAwdpMode"
          class="space-y-4 rounded-xl border bg-card p-4"
        >
          <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div class="space-y-1">
              <div class="flex items-center gap-2 text-sm font-semibold">
                <ShieldCheck class="size-4 text-primary" />
                <span>{{ t('awdp.operationCard') }}</span>
              </div>
              <p class="text-xs text-muted-foreground">{{ t('awdp.roundScoringHint') }}</p>
            </div>
            <Badge variant="secondary" class="w-fit">
              {{
                awdpCurrentRound
                  ? t('awdp.roundLabel', {
                      round: awdpCurrentRound.roundNumber,
                      status: formatStatus(awdpCurrentRound.status),
                    })
                  : t('awdp.roundPending')
              }}
            </Badge>
          </div>

          <div class="grid gap-2 sm:grid-cols-4">
            <div class="rounded-lg border bg-muted/20 px-3 py-2">
              <div class="mb-1 flex items-center gap-1 text-xs text-muted-foreground">
                <Server class="size-3" />
                {{ t('awdp.instanceStatus') }}
              </div>
              <Badge
                :variant="
                  statusBadgeVariant(
                    awdpInstanceRunning ? 'InstanceRunning' : awdpState?.instanceStatus,
                  )
                "
              >
                {{
                  formatStatus(awdpInstanceRunning ? 'InstanceRunning' : awdpState?.instanceStatus)
                }}
              </Badge>
            </div>
            <div class="rounded-lg border bg-muted/20 px-3 py-2">
              <div class="mb-1 flex items-center gap-1 text-xs text-muted-foreground">
                <Crosshair class="size-3" />
                {{ t('awdp.breakStatus') }}
              </div>
              <Badge :variant="statusBadgeVariant(awdpState?.breakStatus)">
                {{ formatStatus(awdpState?.breakStatus ?? 'BreakNotStarted') }}
              </Badge>
            </div>
            <div class="rounded-lg border bg-muted/20 px-3 py-2">
              <div class="mb-1 flex items-center gap-1 text-xs text-muted-foreground">
                <Shield class="size-3" />
                {{ t('awdp.fixStatus') }}
              </div>
              <Badge :variant="statusBadgeVariant(awdpState?.fixStatus)">
                {{ formatStatus(awdpState?.fixStatus ?? 'FixNotStarted') }}
              </Badge>
            </div>
            <div class="rounded-lg border bg-muted/20 px-3 py-2">
              <div class="mb-1 flex items-center gap-1 text-xs text-muted-foreground">
                <Activity class="size-3" />
                {{ t('awdp.serviceStatus') }}
              </div>
              <Badge :variant="statusBadgeVariant(awdpState?.serviceStatus)">
                {{ formatStatus(awdpState?.serviceStatus ?? 'ServiceUnknown') }}
              </Badge>
            </div>
          </div>

          <div class="grid gap-2 sm:grid-cols-4">
            <div class="rounded-lg bg-muted/30 px-3 py-2">
              <div class="text-xs text-muted-foreground">{{ t('awdp.attackRemaining') }}</div>
              <div class="font-mono text-sm font-semibold">{{ awdpAttackAttemptsLabel }}</div>
            </div>
            <div class="rounded-lg bg-muted/30 px-3 py-2">
              <div class="text-xs text-muted-foreground">{{ t('awdp.defenseRemaining') }}</div>
              <div class="font-mono text-sm font-semibold">{{ awdpDefenseAttemptsLabel }}</div>
            </div>
            <div class="rounded-lg bg-muted/30 px-3 py-2">
              <div class="text-xs text-muted-foreground">{{ t('awdp.attackRoundScore') }}</div>
              <div class="font-mono text-sm font-semibold">
                +{{ awdpState?.currentRoundAttackScore ?? 0 }}
              </div>
            </div>
            <div class="rounded-lg bg-muted/30 px-3 py-2">
              <div class="text-xs text-muted-foreground">{{ t('awdp.defenseRoundScore') }}</div>
              <div class="font-mono text-sm font-semibold">
                +{{ awdpState?.currentRoundDefenseScore ?? 0 }}
              </div>
            </div>
          </div>

          <Alert v-if="awdpBlockedMessage" :variant="awdpInstanceRunning ? 'warning' : 'default'">
            {{ awdpBlockedMessage }}
          </Alert>

          <div class="grid gap-3">
            <div class="flex flex-col gap-2 sm:flex-row">
              <Button
                type="button"
                variant="outline"
                class="justify-start sm:w-44"
                :disabled="
                  hasInstanceOperation ||
                  awdpInstanceRunning ||
                  !canCreateDynamicInstance ||
                  isCoolingDown
                "
                @click="createInstance"
              >
                <Loader2 v-if="instanceCreating || instanceLoading" class="size-4 animate-spin" />
                <Server v-else class="size-4" />
                {{
                  isCoolingDown
                    ? t('challenges.cooldownSeconds', { seconds: Math.ceil(cooldownMs / 1000) })
                    : t('challenges.createInstance')
                }}
              </Button>
              <div class="flex min-w-0 flex-1 gap-2">
                <Input
                  v-model="flagInput"
                  :placeholder="t('challenges.flagPlaceholder')"
                  :disabled="submitting || !awdpCanSubmitFlag"
                  @keydown.enter="submitFlag"
                />
                <Button
                  type="button"
                  class="shrink-0"
                  :disabled="submitting || !awdpCanSubmitFlag || !flagInput.trim()"
                  @click="submitFlag"
                >
                  <Loader2 v-if="submitting" class="size-4 animate-spin" />
                  <Crosshair v-else class="size-4" />
                  {{ t('awd.submitFlag') }}
                </Button>
              </div>
            </div>

            <div class="flex flex-col gap-2 sm:flex-row">
              <Button
                type="button"
                variant="outline"
                class="justify-start sm:w-44"
                :disabled="!awdpCanRequestDefense"
                @click="emit('request-defense')"
              >
                <Shield class="size-4" />
                {{ defenseEnabled ? t('challenges.defenseReady') : t('challenges.requestDefense') }}
              </Button>
              <Input
                type="file"
                accept=".zip,.tar.gz,.tgz"
                :disabled="patchUploading || !awdpCanRequestDefense"
                @change="onPatchFileChange"
              />
              <Button
                class="shrink-0"
                :disabled="patchUploading || !canUploadAwdpFix"
                @click="submitPatch"
              >
                <Loader2 v-if="patchUploading" class="size-4 animate-spin" />
                <FileArchive v-else class="size-4" />
                {{ t('awdp.uploadFixScript') }}
              </Button>
            </div>

            <div class="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              <span>{{ t('awdp.fixEntry', { entry: awdpState?.fixEntry ?? 'fix.sh' }) }}</span>
              <span v-if="awdpState?.lastValidationDetail"
                >· {{ formatDefenseResult(awdpState.lastValidationDetail) }}</span
              >
            </div>
          </div>

          <div class="space-y-2 border-t pt-3">
            <div class="text-sm font-semibold">{{ t('awd.patchStatus') }}</div>
            <div v-if="patchStatuses.length" class="divide-y">
              <div
                v-for="patch in patchStatuses"
                :key="patch.id ?? `${patch.challengeId}-${patch.submittedAt}`"
                class="space-y-1 py-2"
              >
                <div class="flex items-center justify-between gap-3">
                  <span class="text-xs text-muted-foreground">
                    {{
                      patch.attemptNumber
                        ? t('awdp.attemptNumber', { attempt: patch.attemptNumber })
                        : ''
                    }}
                    {{ formatDate(patch.submittedAt) }}
                  </span>
                  <Badge :variant="statusBadgeVariant(patch.fixStatus ?? patch.status)">
                    {{ formatStatus(patch.fixStatus ?? patch.status) }}
                  </Badge>
                </div>
                <p
                  v-if="patch.validationDetail"
                  class="text-xs leading-relaxed text-muted-foreground"
                >
                  <CheckCircle2 class="mr-1 inline size-3" />
                  {{ formatDefenseResult(patch.validationDetail) }}
                </p>
              </div>
            </div>
            <p v-else class="text-xs text-muted-foreground">{{ t('challenges.noPatchRecords') }}</p>
          </div>
        </div>

        <div v-if="visibleHints.length" class="rounded-lg border bg-muted/30 p-3">
          <div class="mb-2 text-xs font-medium uppercase text-muted-foreground">
            {{ t('challenges.hints') }}
          </div>
          <ol class="space-y-1 pl-4 text-sm leading-relaxed list-decimal">
            <li v-for="(hint, index) in visibleHints" :key="`${index}-${hint}`">
              {{ hint }}
            </li>
          </ol>
        </div>

        <div v-if="challenge?.attachmentUrl" class="text-sm">
          <a
            :href="challenge.attachmentUrl"
            target="_blank"
            rel="noopener noreferrer"
            class="text-primary underline-offset-4 hover:underline"
          >
            <Download class="mr-1 inline size-4" />
            {{ t('challenges.downloadAttachment') }}
          </a>
        </div>

        <div v-if="isAwdpMode && challenge?.patchTemplateUrl" class="text-sm">
          <a
            :href="challenge.patchTemplateUrl"
            target="_blank"
            rel="noopener noreferrer"
            class="text-primary underline-offset-4 hover:underline"
          >
            <Download class="mr-1 inline size-4" />
            {{ t('awdp.downloadPatchTemplate') }}
          </a>
        </div>

        <div v-if="isAwdMode" class="space-y-3 rounded-xl border bg-muted/20 p-3">
          <div class="flex items-center justify-between gap-3">
            <div>
              <div class="text-sm font-semibold">{{ t('awd.uploadPatch') }}</div>
              <p class="text-xs text-muted-foreground">
                {{ defenseEnabled ? t('challenges.patchUnlocked') : t('challenges.patchLocked') }}
              </p>
            </div>
            <Badge :variant="defenseEnabled ? 'default' : 'outline'">
              {{ defenseEnabled ? t('challenges.defenseReady') : t('challenges.defenseRequired') }}
            </Badge>
          </div>
          <div class="flex flex-col gap-2 sm:flex-row">
            <Input
              type="file"
              accept=".tar.gz,.tgz"
              :disabled="!defenseEnabled || patchUploading"
              @change="onPatchFileChange"
            />
            <Button
              class="shrink-0"
              :disabled="patchUploading || !canUploadPatch"
              @click="submitPatch"
            >
              <Loader2 v-if="patchUploading" class="mr-2 size-4 animate-spin" />
              <Upload v-else class="mr-2 size-4" />
              {{ t('awd.submitPatch') }}
            </Button>
          </div>
        </div>

        <div v-if="!solved && !isAwdpMode && !isPenetrationChallenge" class="space-y-2">
          <Label for="flag-input">{{ t('awd.flag') }}</Label>
          <div class="flex gap-2">
            <Input
              id="flag-input"
              v-model="flagInput"
              :placeholder="t('challenges.flagPlaceholder')"
              :disabled="submitting || !canSubmitCurrentFlag"
              @keydown.enter="submitFlag"
            />
            <Button
              type="button"
              :disabled="submitting || !canSubmitCurrentFlag || !flagInput.trim()"
              @click="submitFlag"
            >
              {{ submitting ? t('common.submitting') : t('common.submit') }}
            </Button>
          </div>
        </div>

        <!-- Result feedback -->
        <Alert v-if="submitResult === 'correct'" variant="success">
          {{ t('challenges.correctFlag') }}
        </Alert>
        <Alert v-else-if="submitResult === 'incorrect'" variant="destructive">
          {{ t('challenges.incorrectFlag') }}
        </Alert>
        <Alert v-else-if="submitError" variant="destructive">
          {{ submitError }}
        </Alert>

        <div
          v-if="solved && submitResult !== 'correct' && !isPenetrationChallenge"
          class="text-sm text-muted-foreground italic"
        >
          {{ t('challenges.alreadySolved') }}
        </div>
      </div>

      <DialogFooter>
        <Button variant="outline" @click="onOpenChange(false)">{{ t('common.close') }}</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<style scoped>
.challenge-markdown :deep(p) {
  margin: 0 0 0.75rem;
  line-height: 1.65;
}

.challenge-markdown :deep(p:last-child) {
  margin-bottom: 0;
}

.challenge-markdown :deep(h3),
.challenge-markdown :deep(h4),
.challenge-markdown :deep(h5) {
  margin: 0.9rem 0 0.4rem;
  font-weight: 650;
  line-height: 1.35;
}

.challenge-markdown :deep(ul) {
  margin: 0.5rem 0 0.75rem;
  padding-left: 1.25rem;
  list-style: disc;
}

.challenge-markdown :deep(li) {
  margin: 0.25rem 0;
}

.challenge-markdown :deep(code) {
  border-radius: var(--radius-sm);
  background: var(--muted);
  padding: 0.1rem 0.3rem;
  font-size: 0.85em;
}

.challenge-markdown :deep(a) {
  color: var(--primary);
  text-underline-offset: 0.2rem;
}

.challenge-markdown :deep(a:hover) {
  text-decoration: underline;
}
</style>
