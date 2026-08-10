<script setup lang="ts">
import {
  getGameplayFactStatusEndpoint,
  listChallengesEndpoint,
  listGameplayFactsEndpoint,
} from '~/api'
import type { NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse } from '~/api'

definePageMeta({ middleware: 'auth' })

type Submission = NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse

const route = useRoute()
const competitionId = route.params.id as string

const challengeTitles = ref<Record<string, string>>({})

const { items, loading, error, hasMore, initialized, loadMore } =
  useCursorPagination<Submission>(async (cursor) => {
    const { data, error: err } = await listGameplayFactsEndpoint({
      path: { competitionId },
      query: { cursor, limit: 50 },
    })
    // 未加入队伍时后端返回 404:视为没有提交记录,展示空态而不是报错。
    if ((err as { status?: number } | undefined)?.status === 404) {
      return { items: [], nextCursor: null }
    }
    if (err || !data) throw err ?? new Error('加载失败')
    return { items: data.items, nextCursor: data.nextCursor }
  })

onMounted(async () => {
  const { data } = await listChallengesEndpoint({ path: { competitionId } })
  challengeTitles.value = Object.fromEntries(
    (data?.items ?? []).map((c) => [c.id!, c.title ?? '']),
  )
  await loadMore()
})

// 待评测提交轮询刷新
async function refreshPending() {
  const pending = items.value.filter((s) => isGameplayFactPending(s.state))
  await Promise.all(
    pending.map(async (submission) => {
      const { data } = await getGameplayFactStatusEndpoint({
        path: { competitionId, gameplayFactId: submission.id! },
      })
      if (!data) return
      submission.state = data.state
      submission.result = data.result
      submission.failureCode = data.failureCode
    }),
  )
}

const { start: startPolling } = usePolling(
  async () => {
    await refreshPending()
    return !items.value.some((s) => isGameplayFactPending(s.state))
  },
  { interval: 3000, timeout: 300_000 },
)

watch(
  () => items.value.some((s) => isGameplayFactPending(s.state)),
  (hasPending) => {
    if (hasPending) startPolling()
  },
)

// 实时:提交结果推送 → 刷新对应提交
let unwatch: (() => void) | undefined
onMounted(() => {
  unwatch = watchCompetition(competitionId, {
    gameplayFactStateChanged: () => void refreshPending(),
  })
})
onUnmounted(() => unwatch?.())

function resultVariant(submission: Submission) {
  if (isGameplayFactPending(submission.state)) return 'secondary' as const
  return submission.result === GameplayFactResult.Correct ? ('default' as const) : ('destructive' as const)
}

function resultText(submission: Submission) {
  if (isGameplayFactPending(submission.state)) {
    return gameplayFactStateLabel(submission.state)
  }
  return gameplayFactResultLabel(submission.result)
}
</script>

<template>
  <div class="flex flex-col gap-4">
    <h2 class="text-lg font-semibold">我的提交</h2>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error.message }}</AlertDescription>
    </Alert>

    <div v-if="loading && !initialized" class="flex flex-col gap-2">
      <Skeleton v-for="i in 5" :key="i" class="h-12 w-full" />
    </div>

    <Empty v-else-if="initialized && !items.length" class="border py-12">
      <EmptyHeader>
        <EmptyTitle>暂无提交记录</EmptyTitle>
        <EmptyDescription>到题目区解题并提交 flag 吧</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Table v-else>
      <TableHeader>
        <TableRow>
          <TableHead>题目</TableHead>
          <TableHead>类型</TableHead>
          <TableHead>状态</TableHead>
          <TableHead>提交时间</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="submission in items" :key="submission.id">
          <TableCell>
            <NuxtLink
              :to="`/competitions/${competitionId}/challenges/${submission.competitionChallengeId}`"
              class="font-medium hover:underline"
            >
              {{ challengeTitles[submission.competitionChallengeId!] ?? '未知题目' }}
            </NuxtLink>
          </TableCell>
          <TableCell>
            <Badge variant="outline">{{ gameplayFactKindLabel(submission.kind) }}</Badge>
          </TableCell>
          <TableCell>
            <Badge :variant="resultVariant(submission)" class="gap-1">
              <Spinner v-if="isGameplayFactPending(submission.state)" class="size-3" />
              {{ resultText(submission) }}
            </Badge>
          </TableCell>
          <TableCell class="text-muted-foreground">{{ formatDateTime(submission.occurredAt) }}</TableCell>
        </TableRow>
      </TableBody>
    </Table>

    <div v-if="hasMore" class="flex justify-center">
      <Button variant="outline" :disabled="loading" @click="loadMore">
        <Spinner v-if="loading" data-icon="inline-start" />
        加载更多
      </Button>
    </div>
  </div>
</template>
