<script setup lang="ts">
import {
  getGameplayFactStatusEndpoint,
  listGameplayFactsEndpoint,
} from '~/api'
import type { NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse } from '~/api'

type FixAttempt = NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse

const props = defineProps<{
  competitionId: string
  competitionChallengeId: string
}>()

const { items, loading, error, hasMore, initialized, loadMore } =
  useCursorPagination<FixAttempt>(async (cursor) => {
    const { data, error: requestError } = await listGameplayFactsEndpoint({
      path: { competitionId: props.competitionId },
      query: {
        competitionChallengeId: props.competitionChallengeId,
        kind: 'FixAttempt',
        cursor,
        limit: 50,
      },
    })
    if (requestError || !data)
      throw parseApiError(requestError, translate('加载 Fix 历史失败'))
    return { items: data.items ?? [], nextCursor: data.nextCursor ?? null }
  })

async function refreshPending(): Promise<void> {
  await Promise.all(items.value
    .filter(item => isGameplayFactPending(item.state))
    .map(async (item) => {
      const { data } = await getGameplayFactStatusEndpoint({
        path: {
          competitionId: props.competitionId,
          gameplayFactId: item.id!,
        },
      })
      if (!data) return
      item.state = data.state
      item.result = data.result
      item.failureCode = data.failureCode
      item.updatedAt = data.updatedAt
    }))
}

const { start: startPolling, stop: stopPolling } = usePolling(
  async () => {
    await refreshPending()
    return !items.value.some(item => isGameplayFactPending(item.state))
  },
  { interval: 2000, timeout: 300_000 },
)

watch(
  () => items.value.some(item => isGameplayFactPending(item.state)),
  pending => pending ? startPolling() : stopPolling(),
)

let unwatch: (() => void) | undefined
onMounted(async () => {
  await loadMore()
  unwatch = watchCompetition(props.competitionId, {
    gameplayFactStateChanged: () => void refreshPending(),
  })
})
onUnmounted(() => {
  unwatch?.()
  stopPolling()
})

function outcome(item: FixAttempt): string {
  if (isGameplayFactPending(item.state)) return gameplayFactStateLabel(item.state)
  if (item.result === 'Correct') return translate('防御成功')
  if (item.failureCode === 'AwdpExploitSucceeded') return translate('防御异常：EXP 利用成功')
  if (item.failureCode === 'AwdpServiceAbnormal') return translate('防御异常：服务异常')
  if (item.state === 'PlatformFailed') return translate('防御验证失败')
  return translate('防御异常：服务异常')
}

function outcomeVariant(item: FixAttempt) {
  if (isGameplayFactPending(item.state)) return 'secondary' as const
  return item.result === 'Correct' ? 'default' as const : 'destructive' as const
}
</script>

<template>
  <div class="min-w-0">
    <Alert v-if="error" variant="destructive" class="mb-4">
      <AlertDescription>{{ error.message }}</AlertDescription>
    </Alert>

    <div v-if="loading && !initialized" class="flex flex-col gap-2">
      <Skeleton v-for="index in 5" :key="index" class="h-14 w-full" />
    </div>

    <Empty v-else-if="initialized && !items.length" class="border py-14">
      <EmptyHeader>
        <EmptyTitle>{{ $t('暂无 Fix 记录') }}</EmptyTitle>
      </EmptyHeader>
    </Empty>

    <div v-else class="overflow-x-auto">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{{ $t('提交时间') }}</TableHead>
            <TableHead>{{ $t('状态') }}</TableHead>
            <TableHead>{{ $t('最终结果') }}</TableHead>
            <TableHead>{{ $t('完成时间') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="item in items" :key="item.id">
            <TableCell class="font-mono text-xs tabular-nums">{{ formatDateTime(item.occurredAt) }}</TableCell>
            <TableCell>
              <Badge variant="outline" class="gap-1">
                <Spinner v-if="isGameplayFactPending(item.state)" class="size-3" />
                {{ gameplayFactStateLabel(item.state) }}
              </Badge>
            </TableCell>
            <TableCell>
              <Badge :variant="outcomeVariant(item)">{{ outcome(item) }}</Badge>
            </TableCell>
            <TableCell class="font-mono text-xs text-muted-foreground tabular-nums">
              {{ isGameplayFactPending(item.state) ? '—' : formatDateTime(item.updatedAt) }}
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

    <div v-if="hasMore" class="mt-4 flex justify-center">
      <Button variant="outline" :disabled="loading" @click="loadMore">
        <Spinner v-if="loading" data-icon="inline-start" />
        {{ $t('加载更多') }}
      </Button>
    </div>
  </div>
</template>
