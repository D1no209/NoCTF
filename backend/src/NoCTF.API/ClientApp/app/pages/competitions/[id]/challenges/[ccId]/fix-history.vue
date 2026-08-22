<script setup lang="ts">
import { ArrowLeft, ShieldCheck } from '@lucide/vue'
import {
  getChallengeEndpoint,
  getGameplayFactStatusEndpoint,
  listGameplayFactsEndpoint,
} from '~/api'
import type {
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse,
} from '~/api'

definePageMeta({ middleware: 'auth' })

type FixAttempt = NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse

const route = useRoute()
const competitionId = route.params.id as string
const competitionChallengeId = route.params.ccId as string
const challenge = ref<NoCtfapiEndpointsChallengesChallengeResponse | null>(null)

const { items, loading, error, hasMore, initialized, loadMore } =
  useCursorPagination<FixAttempt>(async (cursor) => {
    const { data, error: requestError } = await listGameplayFactsEndpoint({
      path: { competitionId },
      query: {
        competitionChallengeId,
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
        path: { competitionId, gameplayFactId: item.id! },
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
  const [challengeResult] = await Promise.all([
    getChallengeEndpoint({
      path: { competitionId, competitionChallengeId },
    }),
    loadMore(),
  ])
  challenge.value = challengeResult.data ?? null
  unwatch = watchCompetition(competitionId, {
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
  <div class="flex flex-col gap-5">
    <div class="flex flex-wrap items-start justify-between gap-4">
      <div>
        <Button as-child variant="ghost" size="sm" class="mb-2 -ml-3">
          <NuxtLink :to="`/competitions/${competitionId}/challenges?challenge=${competitionChallengeId}`">
            <ArrowLeft data-icon="inline-start" />
            {{ $t('返回题目') }}
          </NuxtLink>
        </Button>
        <div class="flex items-center gap-2">
          <ShieldCheck class="size-5 text-primary" aria-hidden="true" />
          <h2 class="text-display text-xl">{{ $t('Fix 历史') }}</h2>
        </div>
        <p class="mt-1 text-sm text-muted-foreground">
          {{ challenge?.title ?? $t('当前题目') }}
        </p>
      </div>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error.message }}</AlertDescription>
    </Alert>

    <div v-if="loading && !initialized" class="flex flex-col gap-2">
      <Skeleton v-for="index in 5" :key="index" class="h-14 w-full" />
    </div>

    <Empty v-else-if="initialized && !items.length" class="border py-14">
      <EmptyHeader>
        <EmptyTitle>{{ $t('暂无 Fix 记录') }}</EmptyTitle>
        <EmptyDescription>{{ $t('每次防御验证的最终结果会显示在这里。') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Table v-else>
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

    <div v-if="hasMore" class="flex justify-center">
      <Button variant="outline" :disabled="loading" @click="loadMore">
        <Spinner v-if="loading" data-icon="inline-start" />
        {{ $t('加载更多') }}
      </Button>
    </div>
  </div>
</template>
