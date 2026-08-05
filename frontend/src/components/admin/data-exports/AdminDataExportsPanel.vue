<script setup lang="ts">
import type { DataExportFailureCode, DataExportJob } from '@/api/dataExports'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  AlertTriangle,
  Archive,
  CheckCircle2,
  Download,
  FileClock,
  Loader2,
  RefreshCw,
  ShieldAlert,
} from 'lucide-vue-next'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { dataExportApi, DataExportStatus } from '@/api/dataExports'
import { queryKeys } from '@/api/queryKeys'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Panel } from '@/components/ui/panel'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Textarea } from '@/components/ui/textarea'

const props = withDefaults(defineProps<{
  scope: 'competition' | 'platform-audit'
  competitionId?: string
  allowProtectedFlags?: boolean
}>(), {
  competitionId: undefined,
  allowProtectedFlags: false,
})

const { locale, t } = useI18n()
const queryClient = useQueryClient()
const includeProtectedFlags = ref(false)
const reason = ref('')
const downloadingId = ref<string | null>(null)

const queryKey = computed(() => props.scope === 'competition'
  ? queryKeys.adminCompetitionDataExports(props.competitionId ?? '')
  : queryKeys.adminPlatformAuditDataExports)

const jobsQuery = useQuery({
  queryKey,
  queryFn: () => props.scope === 'competition'
    ? dataExportApi.listCompetition(props.competitionId ?? '')
    : dataExportApi.listPlatformAudit(),
  enabled: computed(() => props.scope === 'platform-audit' || Boolean(props.competitionId)),
  refetchInterval: query => (query.state.data ?? []).some(isActive) ? 3_000 : false,
})

const jobs = computed(() => jobsQuery.data.value ?? [])
const activeJob = computed(() => jobs.value.find(isActive) ?? null)
const reasonValid = computed(() => !includeProtectedFlags.value
  || reason.value.trim().length >= 8)

const createMutation = useMutation({
  mutationFn: () => props.scope === 'competition'
    ? dataExportApi.createCompetition(
        props.competitionId ?? '',
        includeProtectedFlags.value,
        includeProtectedFlags.value ? reason.value.trim() : null,
      )
    : dataExportApi.createPlatformAudit(),
  onSuccess: async (job) => {
    await queryClient.invalidateQueries({ queryKey: queryKey.value })
    toast.success(isActive(job)
      ? t('admin.dataExports.queued')
      : t('admin.dataExports.alreadyActive'))
  },
  onError: () => toast.error(t('admin.dataExports.createFailed')),
})

function isActive(job: DataExportJob) {
  return job.status === DataExportStatus.Queued || job.status === DataExportStatus.Processing
}

function statusKey(job: DataExportJob) {
  switch (job.status) {
    case DataExportStatus.Queued:
      return 'queued'
    case DataExportStatus.Processing:
      return 'processing'
    case DataExportStatus.Available:
      return 'available'
    case DataExportStatus.Failed:
      return 'failed'
    case DataExportStatus.Expired:
      return 'expired'
    default:
      return 'unknown'
  }
}

function statusVariant(job: DataExportJob) {
  if (job.status === DataExportStatus.Available)
    return 'secondary' as const
  if (job.status === DataExportStatus.Failed)
    return 'destructive' as const
  return 'outline' as const
}

function failureLabel(code: DataExportFailureCode | null | undefined) {
  switch (code) {
    case 0:
      return t('admin.dataExports.failures.subjectNotFound')
    case 1:
      return t('admin.dataExports.failures.sizeLimit')
    case 2:
      return t('admin.dataExports.failures.generation')
    case 3:
      return t('admin.dataExports.failures.storage')
    default:
      return t('admin.dataExports.failures.unknown')
  }
}

function formatDate(value?: string | null) {
  if (!value)
    return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime()))
    return '—'
  return new Intl.DateTimeFormat(locale.value, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date)
}

function formatBytes(value?: number | null) {
  if (typeof value !== 'number' || !Number.isFinite(value))
    return '—'
  if (value < 1024)
    return `${value} B`
  if (value < 1024 * 1024)
    return `${(value / 1024).toFixed(1)} KiB`
  if (value < 1024 * 1024 * 1024)
    return `${(value / 1024 / 1024).toFixed(1)} MiB`
  return `${(value / 1024 / 1024 / 1024).toFixed(2)} GiB`
}

async function downloadJob(job: DataExportJob) {
  if (!job.id || job.status !== DataExportStatus.Available)
    return
  downloadingId.value = job.id
  try {
    const result = await dataExportApi.download(job.id)
    const url = URL.createObjectURL(result.content)
    const link = document.createElement('a')
    link.href = url
    link.download = result.fileName
    link.click()
    URL.revokeObjectURL(url)
    toast.success(t('admin.dataExports.downloadStarted'))
  }
  catch {
    toast.error(t('admin.dataExports.downloadFailed'))
  }
  finally {
    downloadingId.value = null
  }
}
</script>

<template>
  <Card class="min-w-0 overflow-hidden rounded-none border-2 shadow-none">
    <div class="flex flex-col gap-4 border-b-2 border-border bg-muted/40 p-4 lg:flex-row lg:items-start lg:justify-between">
      <div class="max-w-3xl space-y-1">
        <div class="flex items-center gap-2">
          <Archive class="size-5" aria-hidden="true" />
          <h3 class="font-bold uppercase tracking-[0.08em]">
            {{ scope === 'competition' ? t('admin.dataExports.competitionTitle') : t('admin.dataExports.auditTitle') }}
          </h3>
        </div>
        <p class="text-sm leading-6 text-muted-foreground">
          {{ scope === 'competition' ? t('admin.dataExports.competitionDescription') : t('admin.dataExports.auditDescription') }}
        </p>
      </div>
      <div class="flex shrink-0 gap-2">
        <Button
          variant="outline"
          size="sm"
          :disabled="jobsQuery.isFetching.value"
          :aria-label="t('common.refresh')"
          @click="jobsQuery.refetch()"
        >
          <RefreshCw class="size-4" :class="{ 'animate-spin': jobsQuery.isFetching.value }" />
          {{ t('common.refresh') }}
        </Button>
        <Button
          size="sm"
          :disabled="Boolean(activeJob) || createMutation.isPending.value || !reasonValid"
          @click="createMutation.mutate()"
        >
          <Loader2 v-if="createMutation.isPending.value" class="size-4 animate-spin" />
          <Archive v-else class="size-4" />
          {{ activeJob ? t('admin.dataExports.inProgress') : t('admin.dataExports.create') }}
        </Button>
      </div>
    </div>

    <div v-if="scope === 'competition'" class="grid gap-4 border-b-2 border-border p-4 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,0.8fr)]">
      <Alert class="rounded-none border-2">
        <ShieldAlert class="size-4" />
        <AlertTitle>{{ t('admin.dataExports.protectionTitle') }}</AlertTitle>
        <AlertDescription>{{ t('admin.dataExports.protectionDescription') }}</AlertDescription>
      </Alert>
      <Panel v-if="allowProtectedFlags" border="default">
        <label class="flex cursor-pointer items-start gap-3 px-3 py-3 text-sm">
          <input v-model="includeProtectedFlags" type="checkbox" class="mt-0.5 size-4 shrink-0">
          <span>
            <span class="block font-bold">{{ t('admin.dataExports.includeProtectedFlags') }}</span>
            <span class="mt-1 block text-xs leading-5 text-muted-foreground">{{ t('admin.dataExports.includeProtectedFlagsHint') }}</span>
          </span>
        </label>
        <div v-if="includeProtectedFlags" class="grid gap-2 border-t-2 border-border px-3 py-3">
          <Label for="data-export-reason">{{ t('admin.dataExports.reason') }}</Label>
          <Textarea
            id="data-export-reason"
            v-model="reason"
            rows="3"
            maxlength="512"
            :placeholder="t('admin.dataExports.reasonPlaceholder')"
          />
          <p class="text-xs" :class="reasonValid ? 'text-muted-foreground' : 'text-destructive'">
            {{ t('admin.dataExports.reasonHint') }}
          </p>
        </div>
      </Panel>
    </div>

    <div aria-live="polite">
      <div v-if="jobsQuery.isLoading.value" class="flex items-center justify-center gap-2 px-4 py-14 text-sm text-muted-foreground">
        <Loader2 class="size-4 animate-spin" />
        {{ t('admin.dataExports.loading') }}
      </div>
      <Alert v-else-if="jobsQuery.isError.value" variant="destructive" class="m-4 rounded-none border-2">
        <AlertTriangle class="size-4" />
        <AlertTitle>{{ t('admin.dataExports.loadFailed') }}</AlertTitle>
        <AlertDescription>{{ t('admin.dataExports.loadFailedDescription') }}</AlertDescription>
      </Alert>
      <div v-else-if="jobs.length === 0" class="px-4 py-14 text-center">
        <FileClock class="mx-auto mb-3 size-6 text-muted-foreground" />
        <p class="font-bold">
          {{ t('admin.dataExports.empty') }}
        </p>
        <p class="mt-1 text-sm text-muted-foreground">
          {{ t('admin.dataExports.emptyDescription') }}
        </p>
      </div>
      <div v-else class="overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow class="border-b-2">
              <TableHead>{{ t('admin.dataExports.requestedAt') }}</TableHead>
              <TableHead>{{ t('admin.dataExports.status') }}</TableHead>
              <TableHead>{{ t('admin.dataExports.file') }}</TableHead>
              <TableHead>{{ t('admin.dataExports.retention') }}</TableHead>
              <TableHead class="text-right">
                {{ t('common.actions') }}
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="job in jobs" :key="job.id" class="align-top">
              <TableCell class="whitespace-nowrap font-mono text-xs tabular-nums">
                {{ formatDate(job.requestedAt) }}
                <span v-if="job.includeProtectedFlags" class="mt-1 block text-destructive">
                  {{ t('admin.dataExports.protected') }}
                </span>
              </TableCell>
              <TableCell class="min-w-40">
                <Badge :variant="statusVariant(job)" class="gap-1 rounded-none">
                  <Loader2 v-if="isActive(job)" class="size-3 animate-spin" />
                  <CheckCircle2 v-else-if="job.status === DataExportStatus.Available" class="size-3" />
                  <AlertTriangle v-else-if="job.status === DataExportStatus.Failed" class="size-3" />
                  {{ t(`admin.dataExports.statuses.${statusKey(job)}`) }}
                </Badge>
                <p v-if="job.status === DataExportStatus.Failed" class="mt-2 max-w-sm text-xs leading-5 text-destructive">
                  {{ failureLabel(job.failureCode) }}<template v-if="job.failureDetail">
                    · {{ job.failureDetail }}
                  </template>
                </p>
              </TableCell>
              <TableCell class="min-w-52">
                <p class="max-w-sm break-all font-mono text-xs">
                  {{ job.fileName || '—' }}
                </p>
                <p class="mt-1 text-xs text-muted-foreground">
                  {{ formatBytes(job.length) }}
                </p>
              </TableCell>
              <TableCell class="whitespace-nowrap text-xs tabular-nums">
                <template v-if="job.status === DataExportStatus.Available">
                  {{ t('admin.dataExports.expiresAt', { time: formatDate(job.expiresAt) }) }}
                </template>
                <template v-else-if="job.status === DataExportStatus.Expired">
                  {{ t('admin.dataExports.metadataRetained') }}
                </template>
                <template v-else>
                  {{ t('admin.dataExports.metadataWindow') }}
                </template>
              </TableCell>
              <TableCell class="text-right">
                <Button
                  v-if="job.status === DataExportStatus.Available"
                  variant="outline"
                  size="sm"
                  :disabled="downloadingId === job.id"
                  @click="downloadJob(job)"
                >
                  <Loader2 v-if="downloadingId === job.id" class="size-4 animate-spin" />
                  <Download v-else class="size-4" />
                  {{ t('admin.dataExports.download') }}
                </Button>
                <span v-else class="text-xs text-muted-foreground">—</span>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </div>
  </Card>
</template>
