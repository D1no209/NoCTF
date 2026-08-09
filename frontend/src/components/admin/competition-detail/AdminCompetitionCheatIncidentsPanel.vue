<script setup lang="ts">
import type { CheatIncidentStatus } from '@/api/cheatIncidentApi'
import type {
  NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentListItemResponse,
  NoCtfapiEndpointsTeamsTeamResponse,
} from '@/api/generated/types.gen'
import type { CheatIncidentResolutionAction } from '@/composables/useCheatIncidentResolution'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  AlertTriangle,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  Clipboard,
  Eye,
  Filter,
  Loader2,
  Radio,
  RefreshCw,
  RotateCcw,
  ShieldX,
  WifiOff,
} from 'lucide-vue-next'
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import {
  cheatIncidentApi,
  readCheatIncidentResolutionError,
} from '@/api/cheatIncidentApi'
import { queryKeys } from '@/api/queryKeys'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Textarea } from '@/components/ui/textarea'
import {
  invalidateCheatIncidentResolutionQueries,
  useCheatIncidentResolution,
} from '@/composables/useCheatIncidentResolution'
import { COMPETITION_HUB_PATH, useSignalR } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'

const props = defineProps<{
  competitionId: string
  competitionTeams?: NoCtfapiEndpointsTeamsTeamResponse[]
}>()

const { t, locale } = useI18n()
const auth = useAuthStore()
const queryClient = useQueryClient()
const statuses = [
  [0, 'pending'],
  [1, 'confirmed'],
  [2, 'dismissed'],
  [3, 'superseded'],
  [4, 'corrected'],
] as const satisfies ReadonlyArray<readonly [CheatIncidentStatus, string]>
const statusKeys = new Map<CheatIncidentStatus, string>(statuses)
const now = new Date()
const weekAgo = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000)

function toLocalInput(value: Date) {
  const offset = value.getTimezoneOffset() * 60_000
  return new Date(value.getTime() - offset).toISOString().slice(0, 16)
}

function toIso(value: string) {
  return new Date(value).toISOString()
}

const draft = reactive({
  from: toLocalInput(weekAgo),
  to: toLocalInput(now),
  sourceTeamId: 'all',
  ownerTeamId: 'all',
  userId: '',
  competitionChallengeId: '',
  status: 'all',
})
const applied = ref({ ...draft })
const cursor = ref<string | null>(null)
const cursorHistory = ref<Array<string | null>>([])
const selectedScoringEventId = ref<string | null>(null)
let heartbeatInterval: ReturnType<typeof setInterval> | null = null

const query = computed(() => ({
  from: toIso(applied.value.from),
  to: toIso(applied.value.to),
  sourceTeamId: applied.value.sourceTeamId === 'all'
    ? undefined
    : applied.value.sourceTeamId,
  ownerTeamId: applied.value.ownerTeamId === 'all'
    ? undefined
    : applied.value.ownerTeamId,
  userId: applied.value.userId.trim() || undefined,
  competitionChallengeId: applied.value.competitionChallengeId.trim() || undefined,
  status: applied.value.status === 'all'
    ? undefined
    : Number(applied.value.status) as CheatIncidentStatus,
  cursor: cursor.value ?? undefined,
  limit: 50,
}))

const {
  data: page,
  isLoading,
  isFetching,
  isError,
  refetch,
} = useQuery({
  queryKey: computed(() => [
    ...queryKeys.adminCompetitionCheatIncidents(props.competitionId),
    query.value,
  ]),
  queryFn: () => cheatIncidentApi.list(props.competitionId, query.value),
  enabled: computed(() => Boolean(props.competitionId)),
  refetchInterval: 15_000,
})

const incidents = computed(() => page.value?.items ?? [])
const selectableTeams = computed(() => (props.competitionTeams ?? [])
  .filter((team): team is NoCtfapiEndpointsTeamsTeamResponse & { id: string } =>
    typeof team.id === 'string' && team.id.length > 0))

const {
  data: detail,
  isError: isEvidenceError,
  isFetching: isEvidenceFetching,
  isPending: isEvidencePending,
} = useQuery({
  queryKey: computed(() => queryKeys.adminCompetitionCheatIncident(
    props.competitionId,
    selectedScoringEventId.value ?? '',
  )),
  queryFn: () => cheatIncidentApi.get(
    props.competitionId,
    selectedScoringEventId.value!,
  ),
  enabled: computed(() => Boolean(props.competitionId && selectedScoringEventId.value)),
  retry: false,
})

watch(isEvidenceError, (hasError) => {
  if (hasError)
    toast.error(t('admin.competitionDetail.cheatEvidenceLoadError'))
})

const {
  action: resolutionAction,
  begin: openResolution,
  canSubmit: resolutionCanSubmit,
  cancel: cancelResolution,
  error: resolutionError,
  isOpen: resolutionOpen,
  isSubmitting: resolutionIsSubmitting,
  reason: resolutionReason,
  remainingCharacters: resolutionRemainingCharacters,
  setOpen: setResolutionOpen,
  submit: submitResolutionRequest,
  target: resolutionTarget,
} = useCheatIncidentResolution({
  execute: async ({ action, scoringEventId, reason }) => {
    if (action === 'dismiss')
      return cheatIncidentApi.dismiss(props.competitionId, scoringEventId, reason)
    if (action === 'confirm')
      return cheatIncidentApi.confirm(props.competitionId, scoringEventId, reason)
    return cheatIncidentApi.correct(props.competitionId, scoringEventId, reason)
  },
  onSuccess: ({ action, scoringEventId }) => {
    toast.success(t(`admin.competitionDetail.cheatActionSuccess.${action}`))
    void invalidateCheatIncidentResolutionQueries(
      queryClient,
      props.competitionId,
      scoringEventId,
    )
  },
  readError: error => readCheatIncidentResolutionError(
    error,
    t('admin.competitionDetail.cheatActionError'),
  ),
})

const { connection, isConnected, start } = useSignalR({
  hubUrl: COMPETITION_HUB_PATH,
  accessToken: () => auth.accessToken,
  onConnected: () => void joinCompetition(),
  onReconnected: () => void joinCompetition(),
})

watch(connection, (current) => {
  if (!current)
    return
  current.on('competitionEventChanged', (notification: { competitionId?: string }) => {
    if (notification.competitionId !== props.competitionId)
      return
    void queryClient.invalidateQueries({
      queryKey: queryKeys.adminCompetitionCheatIncidents(props.competitionId),
    })
  })
}, { immediate: true })

function applyFilters() {
  const from = new Date(draft.from)
  const to = new Date(draft.to)
  const range = to.getTime() - from.getTime()
  if (!Number.isFinite(range) || range < 0 || range > 31 * 24 * 60 * 60 * 1000) {
    toast.error(t('admin.competitionDetail.cheatDateRangeError'))
    return
  }
  applied.value = { ...draft }
  cursor.value = null
  cursorHistory.value = []
  selectedScoringEventId.value = null
}

function resetFilters() {
  const resetNow = new Date()
  Object.assign(draft, {
    from: toLocalInput(new Date(resetNow.getTime() - 7 * 24 * 60 * 60 * 1000)),
    to: toLocalInput(resetNow),
    sourceTeamId: 'all',
    ownerTeamId: 'all',
    userId: '',
    competitionChallengeId: '',
    status: 'all',
  })
  applyFilters()
}

function nextPage() {
  if (!page.value?.nextCursor)
    return
  cursorHistory.value.push(cursor.value)
  cursor.value = page.value.nextCursor
  selectedScoringEventId.value = null
}

function previousPage() {
  if (cursorHistory.value.length === 0)
    return
  cursor.value = cursorHistory.value.pop() ?? null
  selectedScoringEventId.value = null
}

function statusLabel(status?: CheatIncidentStatus) {
  const key = status === undefined ? undefined : statusKeys.get(status)
  return key ? t(`admin.competitionDetail.cheatStatuses.${key}`) : t('common.unknown')
}

function statusVariant(status?: CheatIncidentStatus): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (status === 0)
    return 'destructive'
  if (status === 1)
    return 'default'
  if (status === 4)
    return 'outline'
  return 'secondary'
}

function formatTime(value?: string | null) {
  if (!value)
    return t('common.unknown')
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

function openEvidence(incident: NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentListItemResponse) {
  if (!incident.scoringEventId)
    return
  selectedScoringEventId.value = incident.scoringEventId
}

async function copyFlag() {
  if (!detail.value?.submittedFlag)
    return
  try {
    await navigator.clipboard.writeText(detail.value.submittedFlag)
    toast.success(t('admin.competitionDetail.cheatFlagCopied'))
  }
  catch {
    toast.error(t('admin.competitionDetail.cheatFlagCopyError'))
  }
}

function beginResolution(action: CheatIncidentResolutionAction) {
  if (!detail.value?.scoringEventId)
    return

  openResolution(action, {
    scoringEventId: detail.value.scoringEventId,
    sourceTeamId: detail.value.sourceTeamId,
    sourceTeamName: detail.value.sourceTeamName,
  })
}

function submitResolution() {
  void submitResolutionRequest()
}

function actionTitle(action: CheatIncidentResolutionAction | null) {
  return action
    ? t(`admin.competitionDetail.cheatActions.${action}.title`)
    : ''
}

function actionDescription(action: CheatIncidentResolutionAction | null) {
  return action
    ? t(`admin.competitionDetail.cheatActions.${action}.description`)
    : ''
}

async function joinCompetition() {
  try {
    await connection.value?.invoke('JoinCompetition', props.competitionId)
    if (!heartbeatInterval) {
      heartbeatInterval = setInterval(() => {
        void connection.value?.invoke('HeartbeatCompetition', props.competitionId)
      }, 30_000)
    }
  }
  catch {
    // The periodic strongly typed query remains the fallback.
  }
}

onMounted(() => void start())
onUnmounted(() => {
  if (heartbeatInterval)
    clearInterval(heartbeatInterval)
})
</script>

<template>
  <div class="min-w-0 space-y-5">
    <Card class="min-w-0 overflow-hidden">
      <CardHeader class="border-b bg-muted/20 pb-4">
        <div class="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <div class="mb-2 flex items-center gap-2">
              <AlertTriangle class="size-5 text-destructive" />
              <CardTitle>{{ t('admin.competitionDetail.cheatTitle') }}</CardTitle>
              <Badge v-if="page?.pendingCount" variant="destructive">
                {{ t('admin.competitionDetail.cheatPendingCount', { count: page.pendingCount }) }}
              </Badge>
            </div>
            <p class="max-w-3xl text-sm text-muted-foreground">
              {{ t('admin.competitionDetail.cheatDescription') }}
            </p>
          </div>
          <div class="flex items-center gap-2">
            <Badge :variant="isConnected ? 'default' : 'outline'" class="gap-1">
              <Radio v-if="isConnected" class="size-3" />
              <WifiOff v-else class="size-3" />
              {{ isConnected ? t('admin.competitionDetail.cheatLive') : t('admin.competitionDetail.cheatPolling') }}
            </Badge>
            <Button variant="outline" size="sm" :disabled="isFetching" @click="refetch()">
              <RefreshCw class="mr-2 size-4" :class="isFetching && 'animate-spin'" />
              {{ t('common.refresh') }}
            </Button>
          </div>
        </div>
      </CardHeader>

      <CardContent class="space-y-5 pt-5">
        <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <div class="space-y-1.5">
            <Label>{{ t('admin.competitionDetail.cheatFrom') }}</Label>
            <Input v-model="draft.from" type="datetime-local" />
          </div>
          <div class="space-y-1.5">
            <Label>{{ t('admin.competitionDetail.cheatTo') }}</Label>
            <Input v-model="draft.to" type="datetime-local" />
          </div>
          <div class="space-y-1.5">
            <Label>{{ t('admin.competitionDetail.cheatSourceTeam') }}</Label>
            <Select v-model="draft.sourceTeamId">
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">
                  {{ t('common.all') }}
                </SelectItem>
                <SelectItem v-for="team in selectableTeams" :key="team.id" :value="team.id">
                  {{ team.name ?? team.id }}
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div class="space-y-1.5">
            <Label>{{ t('admin.competitionDetail.cheatOwnerTeam') }}</Label>
            <Select v-model="draft.ownerTeamId">
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">
                  {{ t('common.all') }}
                </SelectItem>
                <SelectItem v-for="team in selectableTeams" :key="team.id" :value="team.id">
                  {{ team.name ?? team.id }}
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div class="space-y-1.5">
            <Label>{{ t('admin.competitionDetail.cheatSubmitterId') }}</Label>
            <Input v-model="draft.userId" :placeholder="t('admin.competitionDetail.cheatOptionalUuid')" />
          </div>
          <div class="space-y-1.5">
            <Label>{{ t('admin.competitionDetail.cheatChallengeId') }}</Label>
            <Input v-model="draft.competitionChallengeId" :placeholder="t('admin.competitionDetail.cheatOptionalUuid')" />
          </div>
          <div class="space-y-1.5">
            <Label>{{ t('common.status') }}</Label>
            <Select v-model="draft.status">
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">
                  {{ t('common.all') }}
                </SelectItem>
                <SelectItem v-for="[status, key] in statuses" :key="status" :value="String(status)">
                  {{ t(`admin.competitionDetail.cheatStatuses.${key}`) }}
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div class="flex items-end gap-2">
            <Button class="flex-1" @click="applyFilters">
              <Filter class="mr-2 size-4" />{{ t('admin.competitionDetail.cheatApplyFilters') }}
            </Button>
            <Button variant="outline" size="icon" :title="t('common.reset')" @click="resetFilters">
              <RotateCcw class="size-4" />
            </Button>
          </div>
        </div>

        <Alert v-if="isError" variant="destructive">
          {{ t('admin.competitionDetail.cheatLoadError') }}
        </Alert>

        <div class="max-w-full overflow-x-auto border-2 border-border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{{ t('admin.competitionDetail.cheatSourceTeam') }}</TableHead>
                <TableHead>{{ t('admin.competitionDetail.cheatOwnerTeam') }}</TableHead>
                <TableHead>{{ t('admin.competitionDetail.cheatSubmitter') }}</TableHead>
                <TableHead>{{ t('admin.competitionDetail.cheatChallenge') }}</TableHead>
                <TableHead>{{ t('admin.competitionDetail.cheatDetectedAt') }}</TableHead>
                <TableHead>{{ t('common.status') }}</TableHead>
                <TableHead class="text-right">
                  {{ t('common.actions') }}
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-if="isLoading">
                <TableCell colspan="7" class="h-24 text-center text-muted-foreground">
                  <Loader2 class="mr-2 inline size-4 animate-spin" />{{ t('common.loading') }}
                </TableCell>
              </TableRow>
              <TableRow v-else-if="!incidents.length">
                <TableCell colspan="7" class="h-24 text-center text-muted-foreground">
                  {{ t('admin.competitionDetail.noCheatIncidents') }}
                </TableCell>
              </TableRow>
              <TableRow v-for="incident in incidents" v-else :key="incident.scoringEventId">
                <TableCell>
                  <div class="flex flex-wrap items-center gap-2 font-medium">
                    <span>{{ incident.sourceTeamName ?? '-' }}</span>
                    <Badge v-if="incident.sourceTeamIsBanned" variant="destructive">
                      {{ t('admin.competitionDetail.cheatSourceTeamBanned') }}
                    </Badge>
                  </div>
                  <code class="text-[10px] text-muted-foreground">{{ incident.sourceTeamId }}</code>
                </TableCell>
                <TableCell>
                  <div class="font-medium">
                    {{ incident.ownerTeamName ?? '-' }}
                  </div>
                  <code class="text-[10px] text-muted-foreground">{{ incident.ownerTeamId }}</code>
                </TableCell>
                <TableCell>
                  <div>{{ incident.submittedByUserName ?? '-' }}</div>
                  <code class="text-[10px] text-muted-foreground">{{ incident.submittedByUserId }}</code>
                </TableCell>
                <TableCell>
                  <div>{{ incident.challengeTitle ?? '-' }}</div>
                  <code class="text-[10px] text-muted-foreground">{{ incident.competitionChallengeId }}</code>
                </TableCell>
                <TableCell class="whitespace-nowrap">
                  {{ formatTime(incident.detectedAt) }}
                </TableCell>
                <TableCell>
                  <Badge :variant="statusVariant(incident.status)">
                    {{ statusLabel(incident.status) }}
                  </Badge>
                </TableCell>
                <TableCell class="text-right">
                  <Button
                    variant="ghost"
                    size="icon"
                    class="size-8"
                    :disabled="isEvidenceFetching"
                    :title="t('admin.competitionDetail.cheatReviewEvidence')"
                    @click="openEvidence(incident)"
                  >
                    <Eye class="size-4" />
                  </Button>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>

        <div class="flex items-center justify-between">
          <span class="text-xs text-muted-foreground">{{ t('admin.competitionDetail.cheatPageCount', { count: incidents.length }) }}</span>
          <div class="flex gap-2">
            <Button variant="outline" size="sm" :disabled="!cursorHistory.length" @click="previousPage">
              <ChevronLeft class="mr-1 size-4" />{{ t('common.previous') }}
            </Button>
            <Button variant="outline" size="sm" :disabled="!page?.nextCursor" @click="nextPage">
              {{ t('common.next') }}<ChevronRight class="ml-1 size-4" />
            </Button>
          </div>
        </div>
      </CardContent>
    </Card>

    <Card v-if="selectedScoringEventId" class="min-w-0 border-2 border-destructive/30">
      <CardHeader class="flex-row items-center justify-between gap-3 border-b bg-destructive/5">
        <CardTitle class="flex items-center gap-2 text-base">
          <Eye class="size-4" />{{ t('admin.competitionDetail.cheatEvidenceTitle') }}
        </CardTitle>
        <Button variant="ghost" size="sm" @click="selectedScoringEventId = null">
          {{ t('common.close') }}
        </Button>
      </CardHeader>
      <CardContent class="space-y-5 pt-5">
        <div v-if="isEvidencePending" class="py-10 text-center text-sm text-muted-foreground">
          <Loader2 class="mr-2 inline size-4 animate-spin" />{{ t('admin.competitionDetail.cheatEvidenceLoading') }}
        </div>
        <Alert v-else-if="isEvidenceError" variant="destructive">
          {{ t('admin.competitionDetail.cheatEvidenceLoadError') }}
        </Alert>
        <template v-else-if="detail">
          <Alert variant="warning">
            {{ t('admin.competitionDetail.cheatEvidenceAuditHint') }}
          </Alert>
          <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <div>
              <div class="text-xs uppercase text-muted-foreground">
                {{ t('admin.competitionDetail.cheatSourceTeam') }}
              </div><div class="flex flex-wrap items-center gap-2 font-medium">
                <span>{{ detail.sourceTeamName }}</span>
                <Badge v-if="detail.sourceTeamIsBanned" variant="destructive">
                  {{ t('admin.competitionDetail.cheatSourceTeamBanned') }}
                </Badge>
              </div>
            </div>
            <div>
              <div class="text-xs uppercase text-muted-foreground">
                {{ t('admin.competitionDetail.cheatOwnerTeam') }}
              </div><div class="font-medium">
                {{ detail.ownerTeamName }}
              </div>
            </div>
            <div>
              <div class="text-xs uppercase text-muted-foreground">
                {{ t('admin.competitionDetail.cheatSubmitter') }}
              </div><div class="font-medium">
                {{ detail.submittedByUserName }}
              </div>
            </div>
            <div>
              <div class="text-xs uppercase text-muted-foreground">
                {{ t('admin.competitionDetail.cheatChallenge') }}
              </div><div class="font-medium">
                {{ detail.challengeTitle }}
              </div>
            </div>
          </div>
          <div class="border-2 border-border bg-muted/30 p-4">
            <div class="mb-2 flex items-center justify-between gap-3">
              <Label>{{ t('admin.competitionDetail.submittedFlag') }}</Label>
              <Button variant="outline" size="sm" @click="copyFlag">
                <Clipboard class="mr-2 size-4" />{{ t('admin.competitionDetail.cheatCopyFlag') }}
              </Button>
            </div>
            <code class="block break-all text-sm">{{ detail.submittedFlag }}</code>
          </div>
          <div v-if="detail.resolutionReason" class="grid gap-2 border-l-4 border-muted-foreground/40 pl-4 text-sm">
            <span class="font-medium">{{ t('admin.competitionDetail.cheatResolution') }}</span>
            <span>{{ detail.resolutionReason }}</span>
            <span class="text-xs text-muted-foreground">
              {{ detail.resolvedByUserName ?? t('common.unknown') }} · {{ formatTime(detail.resolvedAt) }}
            </span>
          </div>
          <div class="flex flex-wrap justify-end gap-2 border-t pt-4">
            <Button v-if="detail.canDismiss" variant="outline" @click="beginResolution('dismiss')">
              <ShieldX class="mr-2 size-4" />{{ t('admin.competitionDetail.cheatActions.dismiss.button') }}
            </Button>
            <Button v-if="detail.canConfirm" variant="destructive" @click="beginResolution('confirm')">
              <CheckCircle2 class="mr-2 size-4" />{{ t('admin.competitionDetail.cheatActions.confirm.button') }}
            </Button>
            <Button v-if="detail.canCorrect" variant="outline" @click="beginResolution('correct')">
              <RotateCcw class="mr-2 size-4" />{{ t('admin.competitionDetail.cheatActions.correct.button') }}
            </Button>
          </div>
        </template>
      </CardContent>
    </Card>

    <Dialog :open="resolutionOpen" @update:open="setResolutionOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ actionTitle(resolutionAction) }}</DialogTitle>
          <DialogDescription>{{ actionDescription(resolutionAction) }}</DialogDescription>
        </DialogHeader>
        <div class="grid gap-3 border-2 border-border bg-muted/30 p-3 text-sm sm:grid-cols-2">
          <div>
            <div class="text-xs font-medium uppercase tracking-wide text-muted-foreground">
              {{ t('admin.competitionDetail.cheatResolutionAction') }}
            </div>
            <div class="mt-1 font-semibold">
              {{ actionTitle(resolutionAction) }}
            </div>
          </div>
          <div>
            <div class="text-xs font-medium uppercase tracking-wide text-muted-foreground">
              {{ t('admin.competitionDetail.cheatResolutionTarget') }}
            </div>
            <div class="mt-1 break-all font-semibold">
              {{ resolutionTarget?.sourceTeamName || resolutionTarget?.sourceTeamId || t('common.unknown') }}
            </div>
          </div>
        </div>
        <Alert v-if="resolutionError" variant="destructive" role="alert">
          {{ resolutionError }}
        </Alert>
        <div class="space-y-2">
          <Label for="cheat-resolution-reason">{{ t('admin.competitionDetail.cheatReason') }}</Label>
          <Textarea
            id="cheat-resolution-reason"
            v-model="resolutionReason"
            :maxlength="512"
            :placeholder="t('admin.competitionDetail.cheatReasonPlaceholder')"
            :aria-invalid="resolutionRemainingCharacters > 0"
            aria-describedby="cheat-resolution-reason-validation cheat-resolution-reason-hint"
          />
          <p
            v-if="resolutionRemainingCharacters > 0"
            id="cheat-resolution-reason-validation"
            class="text-xs font-medium text-destructive"
            role="alert"
          >
            {{ t('admin.competitionDetail.cheatReasonTooShort', { count: resolutionRemainingCharacters }) }}
          </p>
          <p id="cheat-resolution-reason-hint" class="text-xs text-muted-foreground">
            {{ t('admin.competitionDetail.cheatReasonHint') }}
          </p>
        </div>
        <DialogFooter>
          <Button variant="outline" :disabled="resolutionIsSubmitting" @click="cancelResolution">
            {{ t('common.cancel') }}
          </Button>
          <Button
            :variant="resolutionAction === 'confirm' ? 'destructive' : 'default'"
            :disabled="!resolutionCanSubmit"
            @click="submitResolution"
          >
            <Loader2 v-if="resolutionIsSubmitting" class="mr-2 size-4 animate-spin" />
            {{ resolutionIsSubmitting ? t('common.submitting') : t('common.confirm') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
