<script setup lang="ts">
import type {
  CompetitionEvent,
  CompetitionEventKind,
  CompetitionEventLevel,
} from '@/api/competitionEventApi'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  Activity,
  ChevronLeft,
  ChevronRight,
  Download,
  Eye,
  Filter,
  Loader2,
  Radio,
  RefreshCw,
  ShieldAlert,
  WifiOff,
} from 'lucide-vue-next'
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { competitionEventApi } from '@/api/competitionEventApi'
import { queryKeys } from '@/api/queryKeys'
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
import { Textarea } from '@/components/ui/textarea'
import { COMPETITION_HUB_PATH, useSignalR } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'
import {
  competitionEventKindEntries,
  competitionEventKindKey,
} from './competitionEventPresentation'

const props = defineProps<{
  competitionId: string
}>()

const { t, locale } = useI18n()
const auth = useAuthStore()
const queryClient = useQueryClient()

const eventKinds = competitionEventKindEntries
const now = new Date()
const yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000)

function toLocalInput(value: Date) {
  const offset = value.getTimezoneOffset() * 60_000
  return new Date(value.getTime() - offset).toISOString().slice(0, 16)
}

function toIso(value: string) {
  return new Date(value).toISOString()
}

const draft = reactive({
  from: toLocalInput(yesterday),
  to: toLocalInput(now),
  kind: 'all',
  minimumLevel: 'all',
  teamId: '',
  userId: '',
  competitionChallengeId: '',
  runtimeInstanceId: '',
})

const applied = ref({ ...draft })
const cursor = ref<string | null>(null)
const cursorHistory = ref<Array<string | null>>([])
const flagDialogOpen = ref(false)
const selectedSubmissionId = ref<string | null>(null)
const flagReason = ref('')
const revealedFlag = ref<string | null>(null)
const revealingFlag = ref(false)
const exporting = ref(false)
let heartbeatInterval: ReturnType<typeof setInterval> | null = null

const query = computed(() => ({
  from: toIso(applied.value.from),
  to: toIso(applied.value.to),
  kind: applied.value.kind === 'all'
    ? undefined
    : Number(applied.value.kind) as CompetitionEventKind,
  minimumLevel: applied.value.minimumLevel === 'all'
    ? undefined
    : Number(applied.value.minimumLevel) as CompetitionEventLevel,
  teamId: applied.value.teamId.trim() || undefined,
  userId: applied.value.userId.trim() || undefined,
  competitionChallengeId: applied.value.competitionChallengeId.trim() || undefined,
  runtimeInstanceId: applied.value.runtimeInstanceId.trim() || undefined,
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
    ...queryKeys.competitionEvents(props.competitionId),
    query.value,
  ]),
  queryFn: () => competitionEventApi.list(props.competitionId, query.value),
  enabled: computed(() => Boolean(props.competitionId)),
  refetchInterval: 15_000,
})

const events = computed(() => page.value?.items ?? [])

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
      queryKey: queryKeys.competitionEvents(props.competitionId),
    })
  })
}, { immediate: true })

watch(flagDialogOpen, (open) => {
  if (open)
    return
  selectedSubmissionId.value = null
  flagReason.value = ''
  revealedFlag.value = null
})

function applyFilters() {
  applied.value = { ...draft }
  cursor.value = null
  cursorHistory.value = []
}

function resetFilters() {
  const resetNow = new Date()
  Object.assign(draft, {
    from: toLocalInput(new Date(resetNow.getTime() - 24 * 60 * 60 * 1000)),
    to: toLocalInput(resetNow),
    kind: 'all',
    minimumLevel: 'all',
    teamId: '',
    userId: '',
    competitionChallengeId: '',
    runtimeInstanceId: '',
  })
  applyFilters()
}

function nextPage() {
  if (!page.value?.nextCursor)
    return
  cursorHistory.value.push(cursor.value)
  cursor.value = page.value.nextCursor
}

function previousPage() {
  if (cursorHistory.value.length === 0)
    return
  cursor.value = cursorHistory.value.pop() ?? null
}

function kindLabel(kind?: CompetitionEventKind) {
  const key = competitionEventKindKey(kind)
  return key ? t(`competitionEvents.kinds.${key}`) : t('common.unknown')
}

function levelLabel(level?: CompetitionEventLevel) {
  if (level === 2)
    return t('competitionEvents.levels.error')
  if (level === 1)
    return t('competitionEvents.levels.warning')
  return t('competitionEvents.levels.information')
}

function levelVariant(level?: CompetitionEventLevel): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (level === 2)
    return 'destructive'
  if (level === 1)
    return 'default'
  return 'secondary'
}

function formatTime(value?: string) {
  if (!value)
    return t('common.unknown')
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'medium',
  }).format(new Date(value))
}

function shortId(value?: string | null) {
  return value ? value.slice(0, 8) : ''
}

function openFlagAccess(event: CompetitionEvent) {
  if (!event.submissionId)
    return
  selectedSubmissionId.value = event.submissionId
  flagDialogOpen.value = true
}

async function revealFlag() {
  if (!selectedSubmissionId.value || flagReason.value.trim().length < 8)
    return
  revealingFlag.value = true
  try {
    const result = await competitionEventApi.accessFlag(
      props.competitionId,
      selectedSubmissionId.value,
      flagReason.value.trim(),
    )
    revealedFlag.value = result.submittedFlag ?? null
    toast.success(t('competitionEvents.flagAccessed'))
    void queryClient.invalidateQueries({
      queryKey: queryKeys.competitionEvents(props.competitionId),
    })
  }
  catch {
    toast.error(t('competitionEvents.flagAccessFailed'))
  }
  finally {
    revealingFlag.value = false
  }
}

async function exportEvents() {
  exporting.value = true
  try {
    const result = await competitionEventApi.export(props.competitionId, {
      from: toIso(applied.value.from),
      to: toIso(applied.value.to),
      kind: query.value.kind,
      minimumLevel: query.value.minimumLevel,
      teamId: query.value.teamId,
      userId: query.value.userId,
      competitionChallengeId: query.value.competitionChallengeId,
      runtimeInstanceId: query.value.runtimeInstanceId,
    })
    const url = URL.createObjectURL(result.content)
    const link = document.createElement('a')
    link.href = url
    link.download = result.fileName
    link.click()
    URL.revokeObjectURL(url)
  }
  catch {
    toast.error(t('competitionEvents.exportFailed'))
  }
  finally {
    exporting.value = false
  }
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
    // The periodic SDK query remains the safe fallback.
  }
}

onMounted(() => void start())
onUnmounted(() => {
  if (heartbeatInterval)
    clearInterval(heartbeatInterval)
})
</script>

<template>
  <div class="space-y-5">
    <Card class="overflow-hidden">
      <CardHeader class="border-b bg-muted/20 pb-4">
        <div class="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <div class="mb-2 flex items-center gap-2">
              <Activity class="size-5" />
              <CardTitle>{{ t('competitionEvents.title') }}</CardTitle>
              <Badge :variant="isConnected ? 'default' : 'outline'" class="gap-1">
                <Radio v-if="isConnected" class="size-3" />
                <WifiOff v-else class="size-3" />
                {{ isConnected ? t('competitionEvents.live') : t('competitionEvents.polling') }}
              </Badge>
            </div>
            <p class="max-w-3xl text-sm text-muted-foreground">
              {{ t('competitionEvents.description') }}
            </p>
          </div>
          <Button
            v-if="page?.canExport"
            variant="outline"
            :disabled="exporting"
            @click="exportEvents"
          >
            <Loader2 v-if="exporting" class="size-4 animate-spin" />
            <Download v-else class="size-4" />
            {{ t('competitionEvents.exportJsonl') }}
          </Button>
        </div>
      </CardHeader>
      <CardContent class="p-5">
        <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          <div class="space-y-2">
            <Label for="events-from">{{ t('competitionEvents.from') }}</Label>
            <Input id="events-from" v-model="draft.from" type="datetime-local" />
          </div>
          <div class="space-y-2">
            <Label for="events-to">{{ t('competitionEvents.to') }}</Label>
            <Input id="events-to" v-model="draft.to" type="datetime-local" />
          </div>
          <div class="space-y-2">
            <Label>{{ t('competitionEvents.kind') }}</Label>
            <Select v-model="draft.kind">
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">
                  {{ t('competitionEvents.allKinds') }}
                </SelectItem>
                <SelectItem
                  v-for="[value, key] in eventKinds"
                  :key="value"
                  :value="String(value)"
                >
                  {{ t(`competitionEvents.kinds.${key}`) }}
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div class="space-y-2">
            <Label>{{ t('competitionEvents.minimumLevel') }}</Label>
            <Select v-model="draft.minimumLevel">
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">
                  {{ t('competitionEvents.allLevels') }}
                </SelectItem>
                <SelectItem value="0">
                  {{ t('competitionEvents.levels.information') }}
                </SelectItem>
                <SelectItem value="1">
                  {{ t('competitionEvents.levels.warning') }}
                </SelectItem>
                <SelectItem value="2">
                  {{ t('competitionEvents.levels.error') }}
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>

        <details class="mt-4 rounded-md border bg-muted/10 px-4 py-3">
          <summary class="cursor-pointer text-sm font-semibold">
            {{ t('competitionEvents.advancedFilters') }}
          </summary>
          <div class="mt-4 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <Input v-model="draft.teamId" :placeholder="t('competitionEvents.teamId')" />
            <Input v-model="draft.userId" :placeholder="t('competitionEvents.userId')" />
            <Input v-model="draft.competitionChallengeId" :placeholder="t('competitionEvents.challengeId')" />
            <Input v-model="draft.runtimeInstanceId" :placeholder="t('competitionEvents.runtimeId')" />
          </div>
        </details>

        <div class="mt-4 flex flex-wrap gap-2">
          <Button :disabled="isFetching" @click="applyFilters">
            <Filter class="size-4" />
            {{ t('competitionEvents.apply') }}
          </Button>
          <Button variant="outline" @click="resetFilters">
            {{ t('common.reset') }}
          </Button>
          <Button variant="ghost" :disabled="isFetching" @click="refetch()">
            <RefreshCw class="size-4" :class="isFetching && 'animate-spin'" />
            {{ t('common.refresh') }}
          </Button>
        </div>
      </CardContent>
    </Card>

    <Card v-if="isLoading" class="flex min-h-64 items-center justify-center">
      <Loader2 class="size-6 animate-spin text-muted-foreground" />
    </Card>

    <Card v-else-if="isError" class="flex min-h-64 flex-col items-center justify-center gap-3 border-destructive/40 text-center">
      <ShieldAlert class="size-8 text-destructive" />
      <p class="font-semibold">
        {{ t('competitionEvents.loadFailed') }}
      </p>
      <Button variant="outline" @click="refetch()">
        {{ t('common.retry') }}
      </Button>
    </Card>

    <Card v-else-if="events.length === 0" class="flex min-h-64 flex-col items-center justify-center border-dashed text-center">
      <Activity class="mb-3 size-8 text-muted-foreground" />
      <p class="font-semibold">
        {{ t('competitionEvents.empty') }}
      </p>
      <p class="mt-1 text-sm text-muted-foreground">
        {{ t('competitionEvents.emptyHint') }}
      </p>
    </Card>

    <div v-else class="relative space-y-3 before:absolute before:bottom-4 before:left-[1.15rem] before:top-4 before:w-px before:bg-border">
      <Card
        v-for="event in events"
        :key="event.id"
        class="relative ml-10 overflow-hidden"
      >
        <span
          class="absolute -left-[2.05rem] top-6 size-3 rounded-full border-2 border-background bg-foreground ring-4 ring-background"
        />
        <CardContent class="p-4">
          <div class="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div class="min-w-0 space-y-2">
              <div class="flex flex-wrap items-center gap-2">
                <span class="font-bold">{{ kindLabel(event.kind) }}</span>
                <Badge :variant="levelVariant(event.level)">
                  {{ levelLabel(event.level) }}
                </Badge>
                <Badge variant="outline">
                  {{ t(`competitionEvents.visibility.${event.visibility ?? 0}`) }}
                </Badge>
              </div>
              <div class="flex flex-wrap gap-x-4 gap-y-1 text-sm text-muted-foreground">
                <span v-if="event.actorDisplayName">{{ t('competitionEvents.actor') }}: {{ event.actorDisplayName }}</span>
                <span v-if="event.relatedUserDisplayName">{{ t('competitionEvents.relatedUser') }}: {{ event.relatedUserDisplayName }}</span>
                <span v-if="event.teamDisplayName">{{ t('competitionEvents.team') }}: {{ event.teamDisplayName }}</span>
                <span v-if="event.challengeTitle">{{ t('competitionEvents.challenge') }}: {{ event.challengeTitle }}</span>
                <span v-if="event.runtimeInstanceId">Runtime: {{ shortId(event.runtimeInstanceId) }}</span>
                <span v-if="event.submissionId">Submission: {{ shortId(event.submissionId) }}</span>
                <span v-if="event.hostPort">{{ t('competitionEvents.hostPort') }}: {{ event.hostPort }}</span>
              </div>
              <p v-if="event.reason" class="rounded-sm border-l-2 border-foreground/40 bg-muted/40 px-3 py-2 text-sm">
                {{ event.reason }}
              </p>
            </div>
            <div class="flex shrink-0 items-center gap-2 lg:flex-col lg:items-end">
              <time class="text-xs tabular-nums text-muted-foreground">{{ formatTime(event.occurredAt) }}</time>
              <Button
                v-if="page?.canAccessSubmissionFlags && event.submissionId"
                size="sm"
                variant="outline"
                @click="openFlagAccess(event)"
              >
                <Eye class="size-4" />
                {{ t('competitionEvents.viewFlag') }}
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>

    <div class="flex items-center justify-end gap-2">
      <Button
        variant="outline"
        :disabled="cursorHistory.length === 0 || isFetching"
        @click="previousPage"
      >
        <ChevronLeft class="size-4" />
        {{ t('common.previous') }}
      </Button>
      <Button
        variant="outline"
        :disabled="!page?.nextCursor || isFetching"
        @click="nextPage"
      >
        {{ t('common.next') }}
        <ChevronRight class="size-4" />
      </Button>
    </div>

    <Dialog v-model:open="flagDialogOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ t('competitionEvents.flagAccessTitle') }}</DialogTitle>
          <DialogDescription>{{ t('competitionEvents.flagAccessDescription') }}</DialogDescription>
        </DialogHeader>
        <div v-if="!revealedFlag" class="space-y-3">
          <Label for="flag-access-reason">{{ t('competitionEvents.accessReason') }}</Label>
          <Textarea
            id="flag-access-reason"
            v-model="flagReason"
            :placeholder="t('competitionEvents.accessReasonPlaceholder')"
            :maxlength="512"
          />
          <p class="text-xs text-muted-foreground">
            {{ t('competitionEvents.accessReasonHint') }}
          </p>
        </div>
        <div v-else class="space-y-2">
          <Label>{{ t('competitionEvents.submittedFlag') }}</Label>
          <pre class="overflow-x-auto rounded-md border bg-muted p-4 text-sm"><code>{{ revealedFlag }}</code></pre>
          <p class="text-xs text-destructive">
            {{ t('competitionEvents.transientFlagWarning') }}
          </p>
        </div>
        <DialogFooter>
          <Button
            v-if="!revealedFlag"
            :disabled="flagReason.trim().length < 8 || revealingFlag"
            @click="revealFlag"
          >
            <Loader2 v-if="revealingFlag" class="size-4 animate-spin" />
            <Eye v-else class="size-4" />
            {{ t('competitionEvents.confirmAccess') }}
          </Button>
          <Button v-else variant="outline" @click="flagDialogOpen = false">
            {{ t('common.close') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
