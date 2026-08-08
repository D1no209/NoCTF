<script setup lang="ts">
import { listChallengesEndpoint } from '~/api'
import type { NoCtfapiEndpointsChallengesChallengeResponse } from '~/api'

type Challenge = NoCtfapiEndpointsChallengesChallengeResponse

const route = useRoute()
const competitionId = route.params.id as string

const items = ref<Challenge[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const dataScope = ref<string>(LeaderboardDataScope.Live)

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
  <div class="flex flex-col gap-8">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <Alert v-else-if="dataScope === LeaderboardDataScope.Frozen">
      <AlertDescription>排行榜已冻结,题目分数显示为冻结时快照。</AlertDescription>
    </Alert>

    <div v-if="loading" class="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
      <Skeleton v-for="i in 6" :key="i" class="h-28 w-full" />
    </div>

    <Empty v-else-if="!items.length" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>暂无已发布的题目</EmptyTitle>
        <EmptyDescription>题目发布后会在「动态」中通知,请稍后再来</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <div v-for="group in groups" v-else :key="group.direction" class="flex flex-col gap-4">
      <h2 class="flex items-center gap-2.5 text-lg font-semibold">
        <component
          :is="directionIcon(group.direction)"
          class="size-5"
          :class="directionTextClass(group.direction)"
        />
        {{ group.direction }}
        <Badge variant="secondary">{{ group.challenges.length }}</Badge>
      </h2>
      <div class="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
        <NuxtLink
          v-for="challenge in group.challenges"
          :key="challenge.id"
          :to="`/competitions/${competitionId}/challenges/${challenge.id}`"
          class="group block rounded-xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
        >
          <Card class="h-full transition-all duration-300 group-hover:-translate-y-1 group-hover:border-primary/50 group-hover:shadow-lg">
            <CardHeader>
              <div class="flex items-start justify-between gap-2">
                <CardTitle class="text-base leading-snug group-hover:text-primary">
                  {{ challenge.title }}
                </CardTitle>
                <Badge variant="outline" :class="directionBadgeClass(challenge.direction)">
                  {{ challenge.direction }}
                </Badge>
              </div>
            </CardHeader>
            <CardContent>
              <Badge v-if="challenge.baseScore === null || challenge.baseScore === undefined" variant="secondary">
                分数隐藏
              </Badge>
              <span v-else class="font-mono text-lg font-bold text-primary tabular-nums">
                {{ challenge.baseScore }}<span class="ml-1 text-xs font-medium text-muted-foreground">pts</span>
              </span>
            </CardContent>
          </Card>
        </NuxtLink>
      </div>
    </div>
  </div>
</template>
