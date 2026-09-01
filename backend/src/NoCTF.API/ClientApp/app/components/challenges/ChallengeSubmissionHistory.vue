<script setup lang="ts">
import {
  getGameplayFactStatusEndpoint,
  getGameplayFactValueEndpoint,
  listGameplayFactsEndpoint,
} from '~/api'
import type {
  NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse,
  NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse,
} from '~/api'
import { createLatestPageRefresh } from '~/lib/latest-page-refresh'

type Submission = NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse

const props = withDefaults(defineProps<{
  competitionId: string
  competitionChallengeId: string
  refreshKey?: number
}>(), {
  refreshKey: 0,
})

const valueDialogOpen = ref(false)
const valueSubmission = ref<Submission | null>(null)
const submittedValue = ref<string | null>(null)
const valueLoading = ref(false)
const valueError = ref<string | null>(null)
let valueRequestGeneration = 0

const { items, loading, error, hasMore, initialized, loadMore, reset } =
  useCursorPagination<Submission>(async (cursor) => {
    const { data, error: requestError, response } = await listGameplayFactsEndpoint({
      path: { competitionId: props.competitionId },
      query: {
        competitionChallengeId: props.competitionChallengeId,
        cursor,
        limit: 20,
      },
    })
    if (response?.status === 404) return { items: [], nextCursor: null }
    if (requestError || !data) throw requestError ?? new Error(translate('加载提交记录失败'))
    return { items: data.items, nextCursor: data.nextCursor }
  })

const { loadNextPage, refreshLatest } = createLatestPageRefresh({
  loadMore,
  reset: () => reset({ preserveItems: true }),
})

function sameId(
  left: string | null | undefined,
  right: string | null | undefined,
): boolean {
  if (!left || !right) return false
  return left.replaceAll('-', '').toLowerCase() === right.replaceAll('-', '').toLowerCase()
}

function applyStatus(status: NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse): boolean {
  const submission = items.value.find(item => sameId(item.id, status.gameplayFactId))
  if (!submission) return false
  submission.state = status.state
  submission.result = status.result
  submission.failureCode = status.failureCode
  submission.updatedAt = status.updatedAt
  return true
}

async function refreshPending(): Promise<void> {
  const pending = items.value.filter(item => item.id && isGameplayFactPending(item.state))
  await Promise.all(pending.map(async (submission) => {
    const { data } = await getGameplayFactStatusEndpoint({
      path: {
        competitionId: props.competitionId,
        gameplayFactId: submission.id!,
      },
    })
    if (data) applyStatus(data)
  }))
}

const { start: startPendingPolling, stop: stopPendingPolling } = usePolling(
  async () => {
    await refreshPending()
    return !items.value.some(item => isGameplayFactPending(item.state))
  },
  { interval: 1500, timeout: 300_000 },
)

watch(
  () => items.value.some(item => isGameplayFactPending(item.state)),
  pending => pending ? startPendingPolling() : stopPendingPolling(),
)

watch(
  () => [props.competitionId, props.competitionChallengeId] as const,
  () => {
    reset()
    void loadNextPage()
  },
  { immediate: true },
)

watch(
  () => props.refreshKey,
  (_value, previous) => {
    if (previous !== undefined) void refreshLatest()
  },
)

let unwatch: (() => void) | undefined
onMounted(() => {
  unwatch = watchCompetition(props.competitionId, {
    gameplayFactStateChanged: payload => {
      if (!sameId(payload.competitionChallengeId, props.competitionChallengeId)) return
      if (!applyStatus(payload)) void refreshLatest()
    },
    onReconnected: () => void refreshLatest(),
  })
})
onUnmounted(() => {
  unwatch?.()
  stopPendingPolling()
})

function resultVariant(submission: Submission) {
  if (isGameplayFactPending(submission.state)) return 'secondary' as const
  return ['Correct', 'Applied', 'Unlocked', 'Controlled', 'ServiceUp'].includes(submission.result ?? '')
    ? ('default' as const)
    : ('destructive' as const)
}

function resultText(submission: Submission): string {
  if (isGameplayFactPending(submission.state)) return gameplayFactStateLabel(submission.state)
  const result = gameplayFactResultLabel(submission.result)
  return submission.failureCode
    ? `${result} · ${gameplayFactFailureCodeLabel(submission.failureCode)}`
    : result
}

function canReadSubmittedValue(submission: Submission): boolean {
  return submission.kind === 'FlagAttempt' || submission.kind === 'BreakAttempt'
}

function closeValueDialog(): void {
  valueRequestGeneration += 1
  valueDialogOpen.value = false
  valueSubmission.value = null
  submittedValue.value = null
  valueError.value = null
  valueLoading.value = false
}

async function openSubmittedValue(submission: Submission): Promise<void> {
  if (!submission.id || !canReadSubmittedValue(submission) || valueLoading.value) return
  const generation = ++valueRequestGeneration
  valueDialogOpen.value = true
  valueSubmission.value = submission
  submittedValue.value = null
  valueError.value = null
  valueLoading.value = true
  const { data, error: requestError } = await getGameplayFactValueEndpoint({
    path: {
      competitionId: props.competitionId,
      gameplayFactId: submission.id,
    },
  })
  if (generation !== valueRequestGeneration) return
  valueLoading.value = false
  if (requestError || !data) {
    valueError.value = parseApiError(requestError, translate('加载 Flag 原文失败')).message
    return
  }
  submittedValue.value = data.value ?? null
}

function setValueDialogOpen(open: boolean): void {
  if (!open) closeValueDialog()
}
</script>

<template>
  <section class="border-t pt-5" aria-labelledby="challenge-submission-history-title">
    <h3 id="challenge-submission-history-title" class="text-sm font-semibold">{{ $t('本题提交记录') }}</h3>

    <Alert v-if="error" variant="destructive" class="mt-3">
      <AlertDescription>{{ error.message }}</AlertDescription>
    </Alert>

    <div v-if="loading && !initialized" class="mt-3 flex flex-col gap-2">
      <Skeleton v-for="index in 3" :key="index" class="h-11 w-full" />
    </div>

    <p v-else-if="initialized && !items.length" class="mt-3 text-sm text-muted-foreground">
      {{ $t('暂无本题提交记录') }}
    </p>

    <Table v-else class="mt-3">
      <TableHeader>
        <TableRow>
          <TableHead class="w-36">{{ $t('类型') }}</TableHead>
          <TableHead>{{ $t('结果') }}</TableHead>
          <TableHead class="w-44 text-right">{{ $t('提交时间') }}</TableHead>
          <TableHead class="w-28 text-right">{{ $t('操作') }}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="submission in items" :key="submission.id">
          <TableCell><Badge variant="outline">{{ gameplayFactKindLabel(submission.kind) }}</Badge></TableCell>
          <TableCell>
            <Badge :variant="resultVariant(submission)" class="gap-1">
              <Spinner v-if="isGameplayFactPending(submission.state)" class="size-3" />
              {{ resultText(submission) }}
            </Badge>
          </TableCell>
          <TableCell class="text-right font-mono text-xs text-muted-foreground tabular-nums">
            {{ formatDateTime(submission.occurredAt) }}
          </TableCell>
          <TableCell class="text-right">
            <Button
              v-if="canReadSubmittedValue(submission)"
              variant="outline"
              size="sm"
              :disabled="valueLoading && valueSubmission?.id === submission.id"
              @click="openSubmittedValue(submission)"
            >
              <Spinner v-if="valueLoading && valueSubmission?.id === submission.id" data-icon="inline-start" />
              {{ $t('查看 Flag') }}
            </Button>
            <span v-else class="text-muted-foreground">-</span>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>

    <Button v-if="hasMore" variant="outline" size="sm" class="mt-3" :disabled="loading" @click="loadNextPage">
      <Spinner v-if="loading" data-icon="inline-start" />{{ $t('加载更多') }}
    </Button>

    <Dialog :open="valueDialogOpen" @update:open="setValueDialogOpen">
      <DialogContent class="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>{{ $t('提交的 Flag 原文') }}</DialogTitle>
          <DialogDescription>
            {{ $t('仅本队成员可以查看本队提交的 Flag 原文。') }}
          </DialogDescription>
        </DialogHeader>

        <div v-if="valueLoading" class="flex min-h-20 items-center justify-center">
          <Spinner class="size-5" />
        </div>
        <Alert v-else-if="valueError" variant="destructive">
          <AlertDescription>{{ valueError }}</AlertDescription>
        </Alert>
        <pre
          v-else-if="submittedValue"
          class="max-h-64 overflow-auto whitespace-pre-wrap break-all rounded-md border bg-muted/40 p-4 font-mono text-sm select-text"
        >{{ submittedValue }}</pre>
        <p v-else class="text-sm text-muted-foreground">{{ $t('暂无可显示的 Flag 原文') }}</p>

        <DialogFooter>
          <Button variant="outline" @click="closeValueDialog">{{ $t('关闭') }}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </section>
</template>
