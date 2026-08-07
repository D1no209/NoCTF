<script setup lang="ts">
import { listChallengesEndpoint } from '~/api'
import type { NoCtfapiEndpointsChallengesChallengeResponse } from '~/api'

type Challenge = NoCtfapiEndpointsChallengesChallengeResponse

const route = useRoute()
const competitionId = route.params.id as string

const items = ref<Challenge[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const dataScope = ref<number>(LeaderboardDataScope.Live)

onMounted(async () => {
  const { data, error: err } = await listChallengesEndpoint({ path: { competitionId } })
  loading.value = false
  if (err || !data) {
    error.value = parseApiError(err, '加载题目失败').message
    return
  }
  items.value = (data.items ?? []).filter((c) => c.isPublished)
  dataScope.value = data.dataScope ?? LeaderboardDataScope.Live
})

const groups = computed(() => {
  const map = new Map<string, Challenge[]>()
  for (const item of items.value) {
    const direction = item.direction || '未分类'
    const list = map.get(direction) ?? []
    list.push(item)
    map.set(direction, list)
  }
  return [...map.entries()].map(([direction, challenges]) => ({
    direction,
    challenges: challenges.sort((a, b) => (a.order ?? 0) - (b.order ?? 0)),
  }))
})
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <Alert v-else-if="dataScope === LeaderboardDataScope.Frozen">
      <AlertDescription>排行榜已冻结,题目分数显示为冻结时快照。</AlertDescription>
    </Alert>

    <div v-if="loading" class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <Skeleton v-for="i in 6" :key="i" class="h-28 w-full" />
    </div>

    <Empty v-else-if="!items.length" class="border py-12">
      <EmptyHeader>
        <EmptyTitle>暂无已发布的题目</EmptyTitle>
        <EmptyDescription>题目发布后会在「动态」中通知,请稍后再来</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <div v-for="group in groups" v-else :key="group.direction" class="flex flex-col gap-3">
      <h2 class="flex items-center gap-2 text-lg font-semibold">
        {{ group.direction }}
        <Badge variant="secondary">{{ group.challenges.length }}</Badge>
      </h2>
      <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <NuxtLink
          v-for="challenge in group.challenges"
          :key="challenge.id"
          :to="`/competitions/${competitionId}/challenges/${challenge.id}`"
        >
          <Card class="h-full transition-colors hover:border-primary/50">
            <CardHeader>
              <div class="flex items-start justify-between gap-2">
                <CardTitle class="text-base">{{ challenge.title }}</CardTitle>
                <Badge variant="outline">{{ challenge.direction }}</Badge>
              </div>
            </CardHeader>
            <CardContent>
              <Badge v-if="challenge.baseScore === null || challenge.baseScore === undefined" variant="secondary">
                分数隐藏
              </Badge>
              <span v-else class="text-lg font-semibold text-primary">{{ challenge.baseScore }} 分</span>
            </CardContent>
          </Card>
        </NuxtLink>
      </div>
    </div>
  </div>
</template>
