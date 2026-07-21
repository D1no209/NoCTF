<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/ui-v1/components/ui/dialog'
import { Button } from '@/ui-v1/components/ui/button'
import { Card } from '@/ui-v1/components/ui/card'
import { Input } from '@/ui-v1/components/ui/input'
import { Label } from '@/ui-v1/components/ui/label'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Alert } from '@/ui-v1/components/ui/alert'
import { Panel } from '@/ui-v1/components/ui/panel'
import { renderMarkdown } from '@/lib/markdown'
import { challengeTypeLabel } from '@/lib/challengeLabels'
import { toast } from 'vue-sonner'
import { Activity, CheckCircle2, Copy, Crosshair, Download, FileArchive, Loader2, Shield, ShieldCheck, Server, Timer, Trash2, Upload } from 'lucide-vue-next'
import {
  useChallengeConsole,
  type ChallengeConsoleAwdpStateDto,
  type ChallengeConsoleChallengeDto,
} from '@/features/game/useChallengeConsole'

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

const props = defineProps<{
  open: boolean
  challenge: ChallengeConsoleChallengeDto | null
  competitionId: string
  solved: boolean
  gameModeType?: string
  isAwdMode?: boolean
  isAwdpMode?: boolean
  instanceReady?: boolean
  defenseEnabled?: boolean
  patchSubmissions?: PatchSubmissionStatus[]
  awdpState?: ChallengeConsoleAwdpStateDto | null
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

const {
  flagInput,
  submitting,
  submitResult,
  submitFailureKind,
  patchFile,
  patchUploading,
  instance,
  instanceCreating,
  instanceDestroying,
  instanceExtending,
  instanceLoading,
  instanceFailureKind,
  instanceFailureDetail,
  isDynamicContainer,
  showContainerControls,
  canCreateDynamicInstance,
  canSubmitCurrentFlag,
  canRequestCurrentDefense,
  runningInstance,
  instanceAddress,
  expiresInMs,
  cooldownMs,
  isCoolingDown,
  canOperateInstance,
  hasInstanceOperation,
  canUploadPatch,
  awdpState,
  awdpInstanceRunning,
  awdpAttackAttemptsLabel,
  awdpDefenseAttemptsLabel,
  awdpCanSubmitFlag,
  awdpCanRequestDefense,
  canUploadAwdpFix,
  awdpBlockedReason,
  resetConsole,
  createInstance: createInstanceAction,
  destroyInstance: destroyInstanceAction,
  extendInstance: extendInstanceAction,
  copyInstanceAddress: copyInstanceAddressAction,
  submitFlag: submitFlagAction,
  submitPatch: submitPatchAction,
} = useChallengeConsole({
  competitionId: () => props.competitionId,
  challenge: () => props.challenge,
  open: () => props.open,
  isAwdMode: () => props.isAwdMode,
  isAwdpMode: () => props.isAwdpMode,
  defenseEnabled: () => props.defenseEnabled,
  canCreateInstance: () => props.canCreateInstance,
  canSubmitFlag: () => props.canSubmitFlag,
  canRequestDefense: () => props.canRequestDefense,
  awdpState: () => props.awdpState,
})

const isOpen = computed({
  get: () => props.open,
  set: (v) => emit('update:open', v),
})

const renderedDescription = computed(() => {
  if (!props.challenge?.description) return ''
  return renderMarkdown(props.challenge.description)
})
const challengeType = computed(() => challengeTypeLabel(props.challenge?.typeId))

const visibleHints = computed(() => props.challenge?.hints?.filter(Boolean) ?? [])
const patchStatuses = computed(() => props.patchSubmissions ?? [])

const instanceStatus = computed(() => {
  if (!runningInstance.value)
    return null
  return instanceAddress.value
    ? `${instance.value?.status ?? 'running'} ${instanceAddress.value}`
    : instance.value?.status ?? 'running'
})

const instanceError = computed(() => {
  if (!instanceFailureKind.value)
    return null
  const key = instanceFailureKind.value === 'create'
    ? 'challenges.instanceFailed'
    : instanceFailureKind.value === 'destroy'
      ? 'challenges.instanceDestroyFailed'
      : 'challenges.instanceExtendFailed'
  const detail = instanceFailureDetail.value
  return detail ? `${t(key)}: ${detail}` : t(key)
})

const submitError = computed(() => {
  switch (submitFailureKind.value) {
    case 'attempts_exhausted': return t('awdp.attackAttemptsExhausted')
    case 'instance_required': return t('awdp.instanceRequired')
    case 'instance_expired': return t('awdp.instanceExpired')
    case 'submission_failed': return t('challenges.submissionFailed')
    default: return null
  }
})

const awdpBlockedMessage = computed(() => {
  switch (awdpBlockedReason.value) {
    case 'instance_required': return t('awdp.instanceRequired')
    case 'attack_exhausted': return t('awdp.attackAttemptsExhausted')
    case 'defense_exhausted': return t('awdp.defenseAttemptsExhausted')
    case 'break_locked': return t('awdp.breakLocked')
    case 'fix_locked': return t('awdp.fixLocked')
    default: return ''
  }
})

function onOpenChange(v: boolean) {
  if (!v)
    resetConsole()
  emit('update:open', v)
}

async function createInstance() {
  const outcome = await createInstanceAction()
  if (outcome.kind === 'ok') {
    toast.success(t('challenges.instanceReady'))
    emit('create-instance')
  }
  else if (outcome.kind === 'failed') {
    toast.error(t('challenges.instanceFailed'))
  }
}

async function destroyInstance() {
  const outcome = await destroyInstanceAction()
  if (outcome.kind === 'ok')
    toast.success(t('challenges.instanceDestroyed'))
  else if (outcome.kind === 'failed')
    toast.error(t('challenges.instanceDestroyFailed'))
}

async function extendInstance() {
  const outcome = await extendInstanceAction()
  if (outcome.kind === 'ok')
    toast.success(t('challenges.instanceExtended'))
  else if (outcome.kind === 'failed')
    toast.error(t('challenges.instanceExtendFailed'))
}

async function copyInstanceAddress() {
  if (await copyInstanceAddressAction())
    toast.success(t('challenges.addressCopied'))
}

function onPatchFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  patchFile.value = input.files?.[0] ?? null
}

async function submitFlag() {
  const outcome = await submitFlagAction()
  if (outcome.kind === 'correct')
    emit('solved')
}

async function submitPatch() {
  const outcome = await submitPatchAction()
  if (outcome.kind === 'ok') {
    toast.success(t('awd.patchSubmitted'))
    emit('patch-uploaded')
  }
  else if (outcome.kind === 'failed') {
    toast.error(t('awd.patchUploadFailed'))
  }
}

function formatDate(value?: string | null) {
  if (!value) return ''
  return new Date(value).toLocaleString()
}

function formatDuration(ms: number) {
  const totalSeconds = Math.max(0, Math.ceil(ms / 1000))
  const hours = Math.floor(totalSeconds / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60
  if (hours > 0) return `${hours}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
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

function statusBadgeVariant(value?: string | number | null): 'default' | 'secondary' | 'destructive' | 'outline' {
  const key = String(value ?? '').toLowerCase()
  if (key.includes('success') || key.includes('ok') || key.includes('running')) return 'default'
  if (key.includes('error') || key.includes('failed') || key.includes('timeout') || key.includes('exhausted')) return 'destructive'
  if (key.includes('notstarted') || key.includes('unknown') || key.includes('notcreated')) return 'outline'
  return 'secondary'
}
</script>


<template>
  <Dialog :open="isOpen" @update:open="onOpenChange">
    <DialogContent
      class="rounded-none border-card-border bg-card shadow-float border-t-4 max-h-[90vh] overflow-y-auto sm:max-w-2xl"
      :class="challengeType.modalClassName"
    >
      <!-- Folder tab -->
      <div
        class="absolute -top-5 left-6 z-20 rounded-t-md px-4 py-1.5 text-xs font-black uppercase tracking-wider text-[var(--neon-foreground)] shadow-sm"
        :class="challengeType.pinClassName"
      >
        CASE FILE
      </div>

      <!-- Case closed stamp -->
      <div
        v-if="solved"
        class="absolute right-6 top-4 z-10 rounded-full border-2 border-dashed border-destructive px-3 py-1 text-xs font-black uppercase tracking-wider text-destructive rotate-12"
      >
        CASE CLOSED
      </div>

      <DialogHeader class="border-b border-border pb-3 pt-2">
        <div class="flex items-start gap-3">
          <Badge :class="challengeType.className">
            {{ challengeType.text }}
          </Badge>
          <div class="min-w-0">
            <DialogTitle class="text-xl leading-tight">{{ challenge?.title }}</DialogTitle>
            <div class="mt-1 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              <span class="font-medium text-foreground">{{ challenge?.points }}pt</span>
              <span>·</span>
              <span>{{ challenge?.solveCount }} {{ t('challenges.solves', { count: challenge?.solveCount ?? 0 }) }}</span>
            </div>
          </div>
        </div>
      </DialogHeader>

      <div class="space-y-4 py-2">
        <!-- Case notes -->
        <Card class="p-1">
          <Panel variant="default" class="p-4">
            <div class="mb-2 text-[10px] font-black uppercase tracking-wider text-muted-foreground">
              CASE NOTES
            </div>
            <div
              v-if="renderedDescription"
              class="challenge-markdown text-sm text-foreground"
              v-html="renderedDescription"
            />
            <p v-else class="text-sm italic text-muted-foreground">{{ t('challenges.noDescription') }}</p>
          </Panel>
        </Card>

        <Card v-if="(showContainerControls && !isAwdpMode) || isAwdMode" class="p-1">
          <Panel class="grid gap-3 p-3 sm:grid-cols-2">
            <div v-if="isDynamicContainer" class="space-y-3 sm:col-span-2">
              <div v-if="runningInstance" class="rounded-lg border bg-background/80 p-3">
                <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                  <div class="min-w-0 space-y-1">
                    <div class="flex flex-wrap items-center gap-2 text-sm font-semibold">
                      <Server class="size-4 text-primary" />
                      <span>{{ t('challenges.instanceReady') }}</span>
                      <Badge variant="secondary" class="font-mono">{{ formatDuration(expiresInMs) }}</Badge>
                    </div>
                    <div class="flex min-w-0 flex-wrap items-center gap-2 text-xs text-muted-foreground">
                      <span class="font-mono text-foreground">{{ instanceAddress }}</span>
                      <Button type="button" variant="ghost" size="icon-sm" :title="t('challenges.copyAddress')" @click="copyInstanceAddress">
                        <Copy class="size-4" />
                      </Button>
                      <span class="inline-flex items-center gap-1">
                        <Timer class="size-3" />
                        {{ t('challenges.expiresIn') }}
                      </span>
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
                      {{ isCoolingDown ? t('challenges.cooldownSeconds', { seconds: Math.ceil(cooldownMs / 1000) }) : t('challenges.extendInstance') }}
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
                <Loader2 v-if="instanceCreating || instanceLoading" class="mr-2 size-4 animate-spin" />
                <Server v-else class="mr-2 size-4" />
                {{ isCoolingDown ? t('challenges.cooldownSeconds', { seconds: Math.ceil(cooldownMs / 1000) }) : t('challenges.createInstance') }}
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
          </Panel>
        </Card>
        <Alert v-if="((showContainerControls && !isAwdpMode) || isAwdMode) && !canSubmitCurrentFlag">
          {{ t('challenges.participantActionRequiresTeam') }}
        </Alert>
        <div v-if="instanceStatus" class="rounded-md border bg-muted/30 px-3 py-2 text-xs text-muted-foreground">{{ instanceStatus }}</div>
        <Alert v-if="instanceError" variant="destructive">
          {{ instanceError }}
        </Alert>

        <Card v-if="isAwdpMode" class="p-1">
          <Panel class="gap-4 p-4">
            <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div class="space-y-1">
                <div class="flex items-center gap-2 text-sm font-semibold">
                  <ShieldCheck class="size-4 text-primary" />
                  <span>{{ t('awdp.operationCard') }}</span>
                </div>
                <p class="text-xs text-muted-foreground">{{ t('awdp.roundScoringHint') }}</p>
              </div>
              <Badge variant="secondary" class="w-fit">
                {{ awdpCurrentRound ? t('awdp.roundLabel', { round: awdpCurrentRound.roundNumber, status: formatStatus(awdpCurrentRound.status) }) : t('awdp.roundPending') }}
              </Badge>
            </div>

            <div class="grid gap-2 sm:grid-cols-4">
              <div class="rounded-lg border bg-muted/20 px-3 py-2">
                <div class="mb-1 flex items-center gap-1 text-xs text-muted-foreground">
                  <Server class="size-3" />
                  {{ t('awdp.instanceStatus') }}
                </div>
                <Badge :variant="statusBadgeVariant(awdpInstanceRunning ? 'InstanceRunning' : awdpState?.instanceStatus)">
                  {{ formatStatus(awdpInstanceRunning ? 'InstanceRunning' : awdpState?.instanceStatus) }}
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
                <div class="font-mono text-sm font-semibold">+{{ awdpState?.currentRoundAttackScore ?? 0 }}</div>
              </div>
              <div class="rounded-lg bg-muted/30 px-3 py-2">
                <div class="text-xs text-muted-foreground">{{ t('awdp.defenseRoundScore') }}</div>
                <div class="font-mono text-sm font-semibold">+{{ awdpState?.currentRoundDefenseScore ?? 0 }}</div>
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
                  :disabled="hasInstanceOperation || awdpInstanceRunning || !canCreateDynamicInstance || isCoolingDown"
                  @click="createInstance"
                >
                  <Loader2 v-if="instanceCreating || instanceLoading" class="size-4 animate-spin" />
                  <Server v-else class="size-4" />
                  {{ isCoolingDown ? t('challenges.cooldownSeconds', { seconds: Math.ceil(cooldownMs / 1000) }) : t('challenges.createInstance') }}
                </Button>
                <div class="flex min-w-0 flex-1 gap-2">
                  <Input
                    v-model="flagInput"
                    :placeholder="t('challenges.flagPlaceholder')"
                    :disabled="submitting || !awdpCanSubmitFlag"
                    @keydown.enter="submitFlag"
                  />
                  <Button type="button" class="shrink-0" :disabled="submitting || !awdpCanSubmitFlag || !flagInput.trim()" @click="submitFlag">
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
                <Button class="shrink-0" :disabled="patchUploading || !canUploadAwdpFix" @click="submitPatch">
                  <Loader2 v-if="patchUploading" class="size-4 animate-spin" />
                  <FileArchive v-else class="size-4" />
                  {{ t('awdp.uploadFixScript') }}
                </Button>
              </div>

              <div class="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                <span>{{ t('awdp.fixEntry', { entry: awdpState?.fixEntry ?? 'fix.sh' }) }}</span>
                <span v-if="awdpState?.lastValidationDetail">· {{ formatDefenseResult(awdpState.lastValidationDetail) }}</span>
              </div>
            </div>

            <div class="space-y-2 border-t pt-3">
              <div class="text-sm font-semibold">{{ t('awd.patchStatus') }}</div>
              <div v-if="patchStatuses.length" class="divide-y">
                <div v-for="patch in patchStatuses" :key="patch.id ?? `${patch.challengeId}-${patch.submittedAt}`" class="space-y-1 py-2">
                  <div class="flex items-center justify-between gap-3">
                    <span class="text-xs text-muted-foreground">
                      {{ patch.attemptNumber ? t('awdp.attemptNumber', { attempt: patch.attemptNumber }) : '' }}
                      {{ formatDate(patch.submittedAt) }}
                    </span>
                    <Badge :variant="statusBadgeVariant(patch.fixStatus ?? patch.status)">
                      {{ formatStatus(patch.fixStatus ?? patch.status) }}
                    </Badge>
                  </div>
                  <p v-if="patch.validationDetail" class="text-xs leading-relaxed text-muted-foreground">
                    <CheckCircle2 class="mr-1 inline size-3" />
                    {{ formatDefenseResult(patch.validationDetail) }}
                  </p>
                </div>
              </div>
              <p v-else class="text-xs text-muted-foreground">{{ t('challenges.noPatchRecords') }}</p>
            </div>
          </Panel>
        </Card>

        <Card v-if="visibleHints.length" class="p-1">
          <Panel class="p-3">
            <div class="mb-2 text-xs font-medium uppercase text-muted-foreground">{{ t('challenges.hints') }}</div>
            <ol class="list-decimal space-y-1 pl-4 text-sm leading-relaxed">
              <li v-for="(hint, index) in visibleHints" :key="`${index}-${hint}`">
                {{ hint }}
              </li>
            </ol>
          </Panel>
        </Card>

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

        <Card v-if="isAwdMode" class="p-1">
          <Panel class="gap-3 p-3">
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
              <Input type="file" accept=".tar.gz,.tgz" :disabled="!defenseEnabled || patchUploading" @change="onPatchFileChange" />
              <Button class="shrink-0" :disabled="patchUploading || !canUploadPatch" @click="submitPatch">
                <Loader2 v-if="patchUploading" class="mr-2 size-4 animate-spin" />
                <Upload v-else class="mr-2 size-4" />
                {{ t('awd.submitPatch') }}
              </Button>
            </div>
          </Panel>
        </Card>

        <div v-if="!solved && !isAwdpMode" class="space-y-2">
          <Label for="flag-input">{{ t('awd.flag') }}</Label>
          <div class="flex gap-2">
            <Input
              id="flag-input"
              v-model="flagInput"
              :placeholder="t('challenges.flagPlaceholder')"
              :disabled="submitting || !canSubmitCurrentFlag"
              @keydown.enter="submitFlag"
            />
            <Button type="button" :disabled="submitting || !canSubmitCurrentFlag || !flagInput.trim()" @click="submitFlag">
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

        <div v-if="solved && submitResult !== 'correct'" class="text-sm italic text-muted-foreground">
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
  border-radius: 0.25rem;
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
