<script setup lang="ts">
import { listGameplayFactsEndpoint } from '~/api'
import type { NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse } from '~/api'
import { createLatestPageRefresh } from '~/lib/latest-page-refresh'

type Submission = NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse

const props = withDefaults(defineProps<{
  competitionId: string
  competitionChallengeId: string
  refreshKey?: number
}>(), {
  refreshKey: 0,
})

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

const { refreshLatest } = createLatestPageRefresh({
  loadMore,
  reset: () => reset({ preserveItems: true }),
})

function sameId(left: string | null | undefined, right: string): boolean {
  return left?.replaceAll('-', '').toLowerCase() === right.replaceAll('-', '').toLowerCase()
}

watch(
  () => [props.competitionId, props.competitionChallengeId] as const,
  () => {
    reset()
    void loadMore()
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
      if (sameId(payload.competitionChallengeId, props.competitionChallengeId))
        void refreshLatest()
    },
    onReconnected: () => void refreshLatest(),
  })
})
onUnmounted(() => unwatch?.())

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
        </TableRow>
      </TableBody>
    </Table>

    <Button v-if="hasMore" variant="outline" size="sm" class="mt-3" :disabled="loading" @click="loadMore">
      <Spinner v-if="loading" data-icon="inline-start" />{{ $t('加载更多') }}
    </Button>
  </section>
</template>
