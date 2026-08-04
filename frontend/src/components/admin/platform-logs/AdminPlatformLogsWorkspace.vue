<script setup lang="ts">
import type { PlatformLogFilter } from './platformLogPresentation'
import type {
  PlatformAuditLog,
  PlatformDeadLetter,
  PlatformLog,
  PlatformLogLevel,
  PlatformLogService,
} from '@/api/platformLogs'
import {
  AlertTriangle,
  Check,
  FileClock,
  Loader2,
  Pause,
  Play,
  Radio,
  RefreshCw,
  RotateCcw,
  ScrollText,
  Search,
  ServerCrash,
  ShieldCheck,
} from 'lucide-vue-next'
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { platformLogsApi } from '@/api/platformLogs'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useSignalR } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'
import {
  DEFAULT_PLATFORM_LOG_LEVEL,
  matchesPlatformLog,
  PLATFORM_LOG_HUB_PATH,
  platformAuditActionName,
  platformAuditKindName,

  platformLogLevelName,
  platformLogServiceName,
} from './platformLogPresentation'

const { locale, t } = useI18n()
const auth = useAuthStore()
const activeTab = ref('live')
const paused = ref(false)
const connecting = ref(false)
const historyLoading = ref(false)
const historyLoadingMore = ref(false)
const historyError = ref(false)
const auditLoading = ref(false)
const auditError = ref(false)
const deadLetterLoading = ref(false)
const deadLetterError = ref(false)
const pendingRequeueId = ref<string | null>(null)
const requeueingId = ref<string | null>(null)
const historyEntries = ref<PlatformLog[]>([])
const liveEntries = ref<PlatformLog[]>([])
const nextCursor = ref<string | null>(null)
const auditEntries = ref<PlatformAuditLog[]>([])
const deadLetters = ref<PlatformDeadLetter[]>([])
const deadLetterSearch = ref('')

const draft = reactive({
  minimumLevel: String(DEFAULT_PLATFORM_LOG_LEVEL),
  service: 'all',
  from: '',
  to: '',
  competitionId: '',
  runtimeInstanceId: '',
})

const auditDraft = reactive({
  kind: 'all',
  from: '',
  to: '',
  competitionId: '',
  actorId: '',
})

const appliedFilter = ref<PlatformLogFilter>({
  minimumLevel: DEFAULT_PLATFORM_LOG_LEVEL,
  service: null,
  from: null,
  to: null,
  competitionId: null,
  runtimeInstanceId: null,
})

const {
  connection,
  error: realtimeError,
  isConnected,
  start,
} = useSignalR({
  hubUrl: PLATFORM_LOG_HUB_PATH,
  accessToken: () => auth.accessToken,
})

function toIso(value: string) {
  return value ? new Date(value).toISOString() : null
}

function normalized(value: string) {
  const trimmed = value.trim()
  return trimmed || null
}

function buildAppliedFilter(): PlatformLogFilter {
  return {
    minimumLevel: Number(draft.minimumLevel) as PlatformLogLevel,
    service: draft.service === 'all' ? null : Number(draft.service) as PlatformLogService,
    from: toIso(draft.from),
    to: toIso(draft.to),
    competitionId: normalized(draft.competitionId),
    runtimeInstanceId: normalized(draft.runtimeInstanceId),
  }
}

async function fetchLogs(reset = true) {
  if (reset) {
    historyLoading.value = true
    historyError.value = false
    nextCursor.value = null
  }
  else {
    historyLoadingMore.value = true
  }

  try {
    const filter = appliedFilter.value
    const response = await platformLogsApi.list({
      minimumLevel: filter.minimumLevel,
      service: filter.service,
      from: filter.from,
      to: filter.to,
      competitionId: filter.competitionId,
      runtimeInstanceId: filter.runtimeInstanceId,
      cursor: reset ? null : nextCursor.value,
      limit: 100,
    })
    historyEntries.value = reset
      ? response.items ?? []
      : [...historyEntries.value, ...(response.items ?? [])]
    nextCursor.value = response.nextCursor ?? null
  }
  catch {
    historyError.value = true
    toast.error(t('admin.platformLogs.errors.history'))
  }
  finally {
    historyLoading.value = false
    historyLoadingMore.value = false
  }
}

async function applyLogFilters() {
  appliedFilter.value = buildAppliedFilter()
  liveEntries.value = []
  await fetchLogs()
}

async function resetLogFilters() {
  draft.minimumLevel = String(DEFAULT_PLATFORM_LOG_LEVEL)
  draft.service = 'all'
  draft.from = ''
  draft.to = ''
  draft.competitionId = ''
  draft.runtimeInstanceId = ''
  await applyLogFilters()
}

async function fetchAudits() {
  auditLoading.value = true
  auditError.value = false
  try {
    const response = await platformLogsApi.audits({
      kind: auditDraft.kind === 'all' ? null : Number(auditDraft.kind) as 0 | 1,
      from: toIso(auditDraft.from),
      to: toIso(auditDraft.to),
      competitionId: normalized(auditDraft.competitionId),
      actorId: normalized(auditDraft.actorId),
      limit: 200,
    })
    auditEntries.value = response.items ?? []
  }
  catch {
    auditError.value = true
    toast.error(t('admin.platformLogs.errors.audit'))
  }
  finally {
    auditLoading.value = false
  }
}

async function fetchDeadLetters() {
  deadLetterLoading.value = true
  deadLetterError.value = false
  pendingRequeueId.value = null
  try {
    deadLetters.value = await platformLogsApi.deadLetters(200)
  }
  catch {
    deadLetterError.value = true
    toast.error(t('admin.platformLogs.errors.deadLetters'))
  }
  finally {
    deadLetterLoading.value = false
  }
}

async function requestRequeue(messageId: string) {
  if (pendingRequeueId.value !== messageId) {
    pendingRequeueId.value = messageId
    return
  }

  requeueingId.value = messageId
  try {
    await platformLogsApi.requeueDeadLetter(messageId)
    toast.success(t('admin.platformLogs.deadLetters.requeued'))
    await fetchDeadLetters()
  }
  catch {
    toast.error(t('admin.platformLogs.deadLetters.requeueFailed'))
  }
  finally {
    requeueingId.value = null
  }
}

async function connectRealtime() {
  if (isConnected.value || connecting.value)
    return
  connecting.value = true
  try {
    await start()
  }
  finally {
    connecting.value = false
  }
}

function receiveRealtimeLog(entry: PlatformLog) {
  if (paused.value || !matchesPlatformLog(entry, appliedFilter.value))
    return
  liveEntries.value = [entry, ...liveEntries.value.filter(item => item.cursor !== entry.cursor)]
    .slice(0, 500)
}

const visibleLogs = computed(() => {
  const cursors = new Set<string>()
  return [...liveEntries.value, ...historyEntries.value].filter((entry) => {
    if (!entry.cursor || cursors.has(entry.cursor))
      return false
    cursors.add(entry.cursor)
    return true
  })
})

const filteredDeadLetters = computed(() => {
  const search = deadLetterSearch.value.trim().toLocaleLowerCase()
  if (!search)
    return deadLetters.value
  return deadLetters.value.filter(item => [
    item.messageId,
    item.messageType,
    item.source,
    item.exceptionType,
  ].some(value => value?.toLocaleLowerCase().includes(search)))
})

function formatTime(value?: string) {
  if (!value)
    return '—'
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'medium',
  }).format(new Date(value))
}

function levelClass(level?: number) {
  if ((level ?? 0) >= 5)
    return 'border-fuchsia-500/40 bg-fuchsia-500/10 text-fuchsia-700'
  if ((level ?? 0) >= 4)
    return 'border-red-500/40 bg-red-500/10 text-red-700'
  if ((level ?? 0) >= 3)
    return 'border-amber-500/40 bg-amber-500/10 text-amber-800'
  return 'border-sky-500/40 bg-sky-500/10 text-sky-700'
}

function refreshActiveTab() {
  if (activeTab.value === 'audit')
    return fetchAudits()
  if (activeTab.value === 'dead-letters')
    return fetchDeadLetters()
  return fetchLogs()
}

let reconnectTimer: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  connection.value?.on('platformLogReceived', receiveRealtimeLog)
  void Promise.allSettled([fetchLogs(), fetchAudits(), fetchDeadLetters(), connectRealtime()])
  reconnectTimer = setInterval(connectRealtime, 15_000)
})

onUnmounted(() => {
  connection.value?.off('platformLogReceived', receiveRealtimeLog)
  if (reconnectTimer)
    clearInterval(reconnectTimer)
})
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col justify-between gap-4 xl:flex-row xl:items-start">
      <div class="space-y-2">
        <div class="flex flex-wrap items-center gap-2">
          <h2 class="text-2xl font-black tracking-tight">
            {{ t('admin.platformLogs.title') }}
          </h2>
          <Badge variant="outline" class="rounded-none border-2 font-mono">
            <ShieldCheck class="size-3" />
            {{ t('admin.platformLogs.adminOnly') }}
          </Badge>
        </div>
        <p class="max-w-3xl text-sm leading-6 text-muted-foreground">
          {{ t('admin.platformLogs.subtitle') }}
        </p>
        <p class="max-w-3xl text-xs text-muted-foreground">
          {{ t('admin.platformLogs.redaction') }}
        </p>
      </div>
      <div class="flex items-center gap-2">
        <div
          class="inline-flex h-9 items-center gap-2 border-2 px-3 text-xs font-bold"
          :class="isConnected ? 'border-emerald-500/40 bg-emerald-500/10 text-emerald-700' : 'border-amber-500/40 bg-amber-500/10 text-amber-800'"
        >
          <Radio class="size-3.5" :class="{ 'animate-pulse': isConnected }" />
          {{ isConnected ? t('admin.platformLogs.connected') : t('admin.platformLogs.reconnecting') }}
        </div>
        <Button variant="outline" :disabled="historyLoading || auditLoading || deadLetterLoading" @click="refreshActiveTab">
          <RefreshCw class="size-4" :class="{ 'animate-spin': historyLoading || auditLoading || deadLetterLoading }" />
          {{ t('common.refresh') }}
        </Button>
      </div>
    </div>

    <Alert v-if="realtimeError" class="rounded-none border-2 border-amber-500/40 bg-amber-500/5">
      <AlertTriangle class="size-4" />
      <AlertTitle>{{ t('admin.platformLogs.realtimeUnavailable') }}</AlertTitle>
      <AlertDescription>{{ t('admin.platformLogs.realtimeFallback') }}</AlertDescription>
    </Alert>

    <Tabs v-model="activeTab" class="space-y-5">
      <TabsList class="h-auto w-full justify-start rounded-none border-2 bg-muted/30 p-1 sm:w-auto">
        <TabsTrigger value="live" class="gap-2 rounded-none px-4 py-2.5">
          <ScrollText class="size-4" />
          {{ t('admin.platformLogs.tabs.live') }}
          <span class="font-mono text-xs text-muted-foreground">{{ visibleLogs.length }}</span>
        </TabsTrigger>
        <TabsTrigger value="audit" class="gap-2 rounded-none px-4 py-2.5">
          <FileClock class="size-4" />
          {{ t('admin.platformLogs.tabs.audit') }}
          <span class="font-mono text-xs text-muted-foreground">{{ auditEntries.length }}</span>
        </TabsTrigger>
        <TabsTrigger value="dead-letters" class="gap-2 rounded-none px-4 py-2.5">
          <ServerCrash class="size-4" />
          {{ t('admin.platformLogs.tabs.deadLetters') }}
          <span class="font-mono text-xs text-muted-foreground">{{ deadLetters.length }}</span>
        </TabsTrigger>
      </TabsList>

      <TabsContent value="live" class="space-y-4">
        <Card class="rounded-none border-2 shadow-none">
          <CardHeader class="border-b-2 border-border py-4">
            <CardTitle class="text-base">
              {{ t('admin.platformLogs.filters.title') }}
            </CardTitle>
          </CardHeader>
          <CardContent class="grid gap-3 pt-4 md:grid-cols-2 xl:grid-cols-4">
            <label class="space-y-1 text-xs font-bold">
              <span>{{ t('admin.platformLogs.filters.level') }}</span>
              <Select v-model="draft.minimumLevel">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="0">Trace</SelectItem>
                  <SelectItem value="1">Debug</SelectItem>
                  <SelectItem value="2">Information</SelectItem>
                  <SelectItem value="3">Warning</SelectItem>
                  <SelectItem value="4">Error</SelectItem>
                  <SelectItem value="5">Critical</SelectItem>
                </SelectContent>
              </Select>
            </label>
            <label class="space-y-1 text-xs font-bold">
              <span>{{ t('admin.platformLogs.filters.service') }}</span>
              <Select v-model="draft.service">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">{{ t('common.all') }}</SelectItem>
                  <SelectItem value="0">API</SelectItem>
                  <SelectItem value="1">Worker</SelectItem>
                  <SelectItem value="2">Runner</SelectItem>
                </SelectContent>
              </Select>
            </label>
            <label class="space-y-1 text-xs font-bold">
              <span>{{ t('admin.platformLogs.filters.from') }}</span>
              <Input v-model="draft.from" type="datetime-local" />
            </label>
            <label class="space-y-1 text-xs font-bold">
              <span>{{ t('admin.platformLogs.filters.to') }}</span>
              <Input v-model="draft.to" type="datetime-local" />
            </label>
            <label class="space-y-1 text-xs font-bold md:col-span-1 xl:col-span-2">
              <span>{{ t('admin.platformLogs.filters.competition') }}</span>
              <Input v-model="draft.competitionId" spellcheck="false" placeholder="UUID" class="font-mono" />
            </label>
            <label class="space-y-1 text-xs font-bold md:col-span-1 xl:col-span-2">
              <span>{{ t('admin.platformLogs.filters.runtime') }}</span>
              <Input v-model="draft.runtimeInstanceId" spellcheck="false" placeholder="UUID" class="font-mono" />
            </label>
            <div class="flex flex-wrap gap-2 md:col-span-2 xl:col-span-4">
              <Button :disabled="historyLoading" @click="applyLogFilters">
                <Search class="size-4" />
                {{ t('admin.platformLogs.filters.apply') }}
              </Button>
              <Button variant="outline" :disabled="historyLoading" @click="resetLogFilters">
                <RotateCcw class="size-4" />
                {{ t('admin.platformLogs.filters.reset') }}
              </Button>
              <Button variant="outline" @click="paused = !paused">
                <component :is="paused ? Play : Pause" class="size-4" />
                {{ paused ? t('admin.platformLogs.resume') : t('admin.platformLogs.pause') }}
              </Button>
            </div>
          </CardContent>
        </Card>

        <div v-if="historyLoading" class="space-y-2">
          <Skeleton v-for="index in 5" :key="index" class="h-24 rounded-none" />
        </div>
        <Alert v-else-if="historyError" variant="destructive" class="rounded-none border-2">
          <AlertTriangle class="size-4" />
          <AlertTitle>{{ t('admin.platformLogs.errors.history') }}</AlertTitle>
          <AlertDescription class="mt-3">
            <Button variant="outline" size="sm" @click="fetchLogs()">
              {{ t('common.retry') }}
            </Button>
          </AlertDescription>
        </Alert>
        <Card v-else-if="visibleLogs.length === 0" class="rounded-none border-2 border-dashed py-16 text-center shadow-none">
          <ScrollText class="mx-auto size-7 text-muted-foreground" />
          <p class="mt-3 font-bold">
            {{ t('admin.platformLogs.empty.live') }}
          </p>
        </Card>
        <div v-else class="overflow-hidden border-2 bg-card">
          <div
            v-for="entry in visibleLogs"
            :key="entry.cursor"
            class="grid gap-3 border-b border-border p-4 last:border-b-0 xl:grid-cols-[10rem_6rem_minmax(0,1fr)]"
          >
            <div class="space-y-1 font-mono text-xs text-muted-foreground">
              <p>{{ formatTime(entry.timestamp) }}</p>
              <p class="break-all">
                {{ entry.cursor }}
              </p>
            </div>
            <div class="flex flex-wrap content-start gap-1.5 xl:flex-col">
              <Badge variant="outline" class="rounded-none font-mono">
                {{ platformLogServiceName(entry.service) }}
              </Badge>
              <span class="inline-flex w-fit border px-2 py-0.5 font-mono text-xs font-bold" :class="levelClass(entry.level)">
                {{ platformLogLevelName(entry.level) }}
              </span>
            </div>
            <div class="min-w-0 space-y-2">
              <div class="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
                <span class="break-all font-mono">{{ entry.category || '—' }}</span>
                <span v-if="entry.eventName" class="font-mono">{{ entry.eventName }} · {{ entry.eventId }}</span>
              </div>
              <pre class="whitespace-pre-wrap break-words font-mono text-xs leading-5 text-foreground">{{ entry.message || '—' }}</pre>
              <div v-if="entry.competitionId || entry.runtimeInstanceId" class="flex flex-wrap gap-2 text-[11px] text-muted-foreground">
                <code v-if="entry.competitionId">competition={{ entry.competitionId }}</code>
                <code v-if="entry.runtimeInstanceId">runtime={{ entry.runtimeInstanceId }}</code>
              </div>
              <details v-if="entry.exceptionMessage" class="border-l-2 border-red-500 pl-3 text-xs">
                <summary class="cursor-pointer font-bold text-red-700">
                  {{ entry.exceptionType || t('admin.platformLogs.exception') }}
                </summary>
                <pre class="mt-2 whitespace-pre-wrap break-words font-mono leading-5">{{ entry.exceptionMessage }}</pre>
              </details>
            </div>
          </div>
        </div>
        <div v-if="nextCursor" class="flex justify-center">
          <Button variant="outline" :disabled="historyLoadingMore" @click="fetchLogs(false)">
            <Loader2 v-if="historyLoadingMore" class="size-4 animate-spin" />
            {{ t('admin.platformLogs.loadOlder') }}
          </Button>
        </div>
      </TabsContent>

      <TabsContent value="audit" class="space-y-4">
        <Card class="rounded-none border-2 shadow-none">
          <CardContent class="grid gap-3 pt-5 md:grid-cols-2 xl:grid-cols-5">
            <label class="space-y-1 text-xs font-bold">
              <span>{{ t('admin.platformLogs.audit.kind') }}</span>
              <Select v-model="auditDraft.kind">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">{{ t('common.all') }}</SelectItem>
                  <SelectItem value="0">{{ t('admin.platformLogs.audit.competitionLifecycle') }}</SelectItem>
                  <SelectItem value="1">{{ t('admin.platformLogs.audit.accountLifecycle') }}</SelectItem>
                </SelectContent>
              </Select>
            </label>
            <label class="space-y-1 text-xs font-bold">
              <span>{{ t('admin.platformLogs.filters.from') }}</span>
              <Input v-model="auditDraft.from" type="datetime-local" />
            </label>
            <label class="space-y-1 text-xs font-bold">
              <span>{{ t('admin.platformLogs.filters.to') }}</span>
              <Input v-model="auditDraft.to" type="datetime-local" />
            </label>
            <label class="space-y-1 text-xs font-bold">
              <span>{{ t('admin.platformLogs.filters.competition') }}</span>
              <Input v-model="auditDraft.competitionId" placeholder="UUID" class="font-mono" />
            </label>
            <label class="space-y-1 text-xs font-bold">
              <span>{{ t('admin.platformLogs.audit.actor') }}</span>
              <Input v-model="auditDraft.actorId" placeholder="UUID" class="font-mono" />
            </label>
            <Button class="md:col-span-2 xl:col-span-1" :disabled="auditLoading" @click="fetchAudits">
              <Search class="size-4" />
              {{ t('admin.platformLogs.filters.apply') }}
            </Button>
          </CardContent>
        </Card>

        <div v-if="auditLoading" class="space-y-2">
          <Skeleton v-for="index in 4" :key="index" class="h-20 rounded-none" />
        </div>
        <Alert v-else-if="auditError" variant="destructive" class="rounded-none border-2">
          <AlertTriangle class="size-4" />
          <AlertTitle>{{ t('admin.platformLogs.errors.audit') }}</AlertTitle>
        </Alert>
        <Card v-else-if="auditEntries.length === 0" class="rounded-none border-2 border-dashed py-16 text-center shadow-none">
          <FileClock class="mx-auto size-7 text-muted-foreground" />
          <p class="mt-3 font-bold">
            {{ t('admin.platformLogs.empty.audit') }}
          </p>
        </Card>
        <div v-else class="overflow-x-auto border-2 bg-card">
          <table class="w-full min-w-[900px] text-left text-sm">
            <thead class="border-b-2 bg-muted/40 text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th class="px-4 py-3">
                  {{ t('admin.platformLogs.audit.time') }}
                </th>
                <th class="px-4 py-3">
                  {{ t('admin.platformLogs.audit.kind') }}
                </th>
                <th class="px-4 py-3">
                  {{ t('admin.platformLogs.audit.action') }}
                </th>
                <th class="px-4 py-3">
                  {{ t('admin.platformLogs.audit.subject') }}
                </th>
                <th class="px-4 py-3">
                  {{ t('admin.platformLogs.audit.actor') }}
                </th>
                <th class="px-4 py-3">
                  {{ t('admin.platformLogs.audit.reason') }}
                </th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="entry in auditEntries" :key="entry.id" class="border-b last:border-b-0">
                <td class="whitespace-nowrap px-4 py-3 font-mono text-xs">
                  {{ formatTime(entry.occurredAt) }}
                </td>
                <td class="px-4 py-3">
                  <Badge variant="outline" class="rounded-none">
                    {{ platformAuditKindName(entry) }}
                  </Badge>
                </td>
                <td class="px-4 py-3 font-bold">
                  {{ platformAuditActionName(entry) }}
                </td>
                <td class="max-w-60 px-4 py-3">
                  <p class="truncate font-bold">
                    {{ entry.subjectDisplayName || entry.subjectId || '—' }}
                  </p>
                  <code v-if="entry.competitionId" class="text-[11px] text-muted-foreground">{{ entry.competitionId }}</code>
                </td>
                <td class="px-4 py-3 font-mono text-xs">
                  {{ entry.actorId || '—' }}
                </td>
                <td class="max-w-80 px-4 py-3 text-muted-foreground">
                  {{ entry.reason || (entry.automatic ? t('admin.platformLogs.audit.automatic') : '—') }}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </TabsContent>

      <TabsContent value="dead-letters" class="space-y-4">
        <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div class="relative w-full sm:max-w-md">
            <Search class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input v-model="deadLetterSearch" class="pl-9" :placeholder="t('admin.platformLogs.deadLetters.search')" />
          </div>
          <p class="text-xs text-muted-foreground">
            {{ t('admin.platformLogs.deadLetters.metadataOnly') }}
          </p>
        </div>

        <div v-if="deadLetterLoading" class="space-y-2">
          <Skeleton v-for="index in 4" :key="index" class="h-20 rounded-none" />
        </div>
        <Alert v-else-if="deadLetterError" variant="destructive" class="rounded-none border-2">
          <AlertTriangle class="size-4" />
          <AlertTitle>{{ t('admin.platformLogs.errors.deadLetters') }}</AlertTitle>
        </Alert>
        <Card v-else-if="filteredDeadLetters.length === 0" class="rounded-none border-2 border-dashed py-16 text-center shadow-none">
          <Check class="mx-auto size-7 text-emerald-600" />
          <p class="mt-3 font-bold">
            {{ t('admin.platformLogs.empty.deadLetters') }}
          </p>
        </Card>
        <div v-else class="overflow-x-auto border-2 bg-card">
          <table class="w-full min-w-[900px] text-left text-sm">
            <thead class="border-b-2 bg-muted/40 text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th class="px-4 py-3">
                  {{ t('admin.platformLogs.deadLetters.time') }}
                </th>
                <th class="px-4 py-3">
                  {{ t('admin.platformLogs.deadLetters.message') }}
                </th>
                <th class="px-4 py-3">
                  {{ t('admin.platformLogs.deadLetters.source') }}
                </th>
                <th class="px-4 py-3">
                  {{ t('admin.platformLogs.deadLetters.exception') }}
                </th>
                <th class="px-4 py-3 text-right">
                  {{ t('admin.platformLogs.deadLetters.action') }}
                </th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="entry in filteredDeadLetters" :key="entry.messageId" class="border-b last:border-b-0">
                <td class="whitespace-nowrap px-4 py-3 font-mono text-xs">
                  {{ formatTime(entry.sentAt) }}
                </td>
                <td class="max-w-80 px-4 py-3">
                  <p class="truncate font-bold">
                    {{ entry.messageType || '—' }}
                  </p>
                  <code class="text-[11px] text-muted-foreground">{{ entry.messageId }}</code>
                </td>
                <td class="px-4 py-3 font-mono text-xs">
                  {{ entry.source || '—' }}
                </td>
                <td class="px-4 py-3 font-mono text-xs text-red-700">
                  {{ entry.exceptionType || '—' }}
                </td>
                <td class="px-4 py-3 text-right">
                  <Button
                    v-if="entry.replayable"
                    :variant="pendingRequeueId === entry.messageId ? 'destructive' : 'outline'"
                    size="sm"
                    :disabled="requeueingId === entry.messageId"
                    @click="entry.messageId && requestRequeue(entry.messageId)"
                  >
                    <Loader2 v-if="requeueingId === entry.messageId" class="size-4 animate-spin" />
                    <RotateCcw v-else class="size-4" />
                    {{ pendingRequeueId === entry.messageId ? t('admin.platformLogs.deadLetters.confirmRequeue') : t('admin.platformLogs.deadLetters.requeue') }}
                  </Button>
                  <span v-else class="text-xs text-muted-foreground">{{ t('admin.platformLogs.deadLetters.notReplayable') }}</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </TabsContent>
    </Tabs>
  </div>
</template>
